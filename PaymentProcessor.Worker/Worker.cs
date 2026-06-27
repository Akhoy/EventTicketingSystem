using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentProcessor.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, IConfiguration config, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _config = config;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Processor Worker starting up...");

        var factory = new ConnectionFactory { 
            Uri = new Uri(_config.GetConnectionString("RabbitMQ")!) 
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // bind channel to the same queue as Booking API
        await _channel.QueueDeclareAsync(
            queue: "ticket_orders", 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var orderData = System.Text.Json.JsonSerializer.Deserialize<TicketOrder>(message);
            var seatId = orderData?.SeatId ?? "Unknown";
            var userId = orderData?.UserId ?? "Unknown";
            _logger.LogInformation("Processing payment for Seat: {SeatId}, User: {UserId}", seatId, userId);
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            using var transaction = await dbContext.Database.BeginTransactionAsync();
            try
            {
                var ticketOrder = new TicketOrder
                {
                    Id = Guid.NewGuid(),
                    SeatId = seatId,
                    UserId = userId,
                    ProcessedAt = DateTime.UtcNow
                };
                dbContext.TicketOrders.Add(ticketOrder);
                var outboxMessage = new OutboxMessage {
                    EventType = "TicketPaid",
                    Payload = System.Text.Json.JsonSerializer.Serialize(new { ticketOrder.SeatId }) 
                };
                dbContext.OutboxMessages.Add(outboxMessage);

                await dbContext.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync();
                _logger.LogInformation("Payment processed and order saved to database for Seat: {SeatId}, User: {UserId}", seatId, userId);            
                
                // send an acknowledgment to RabbitMQ after successful processing after which the message will be removed from the queue
                await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment for Seat: {SeatId}, User: {UserId}", seatId, userId);
                return;
            }            
        };

        await _channel.BasicConsumeAsync(
            queue: "ticket_orders", 
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker stopping. Cleaning up RabbitMQ connections.");
        if (_channel != null) await _channel.CloseAsync(cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}