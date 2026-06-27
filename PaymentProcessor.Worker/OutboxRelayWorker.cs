using RabbitMQ.Client;
using System.Text;

namespace PaymentProcessor.Worker;

class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayWorker> _logger;
    private readonly IConfiguration _configuration;
    public OutboxRelayWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 OutboxRelayWorker is starting...");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
                if(db.OutboxMessages == null)
                {
                    _logger.LogWarning("OutboxMessages table is not available in the database.");
                    continue;
                }
                else
                {
                    _logger.LogInformation("Checking for unprocessed outbox messages...");
                }
                var messagesToProcess = db.OutboxMessages
                    .Where(m => m.ProcessedOnUtc == null);

                foreach (var message in messagesToProcess)
                {
                    await SendMessageToRabbitMQ(message, stoppingToken);
                    message.ProcessedOnUtc = DateTime.UtcNow;
                }

                if (messagesToProcess.Any())
                {
                    _logger.LogInformation("Processed {Count} outbox messages.", messagesToProcess.Count());
                    await db.SaveChangesAsync(stoppingToken);
                }                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages. Rabbit MQ is down. Will retry in the next cycle.");                
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task SendMessageToRabbitMQ(OutboxMessage message, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory()
        {
            Uri = new Uri(_configuration.GetConnectionString("RabbitMQ")!)
        };
        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();
        var eventBody = Encoding.UTF8.GetBytes(message.Payload);
        await channel.ExchangeDeclareAsync(
                exchange: "ticket_events", 
                type: ExchangeType.Fanout, 
                durable: true, 
                autoDelete: false, 
                arguments: null,
                cancellationToken: cancellationToken);
        await channel.BasicPublishAsync(
            exchange: "ticket_events", 
            routingKey: string.Empty, 
            body: eventBody);
        _logger.LogInformation("Published TicketPurchased Integration Event to the Event Bus!");
        
    }
}