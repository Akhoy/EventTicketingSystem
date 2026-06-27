using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Catalog.API;

public class CatalogSyncWorker: BackgroundService
{
    // This worker listens to RabbitMQ for events and syncs them to MongoDB
    private readonly IConfiguration _configuration;
    private readonly ILogger<CatalogSyncWorker> _logger;
    private IConnection? _connection;
    private IChannel? _channel;  

    private IServiceScopeFactory _serviceScopeFactory;
    public CatalogSyncWorker(IConfiguration configuration, ILogger<CatalogSyncWorker> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitMqConnectionString = _configuration.GetConnectionString("RabbitMQ");
        var factory = new ConnectionFactory() { Uri = new Uri(rabbitMqConnectionString!) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync(exchange: "ticket_events", type: ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

        var queueName = "catalog_sync_queue";
        await _channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(queue: queueName, exchange: "ticket_events", routingKey: string.Empty, cancellationToken: stoppingToken);

        _logger.LogInformation("🎧 Catalog API is successfully bound to the Event Bus!");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            _logger.LogInformation("📩 Received event: {Message}", message);

            // Parse the EventId so we decrement the right event, not a blind "first document".
            var bookingEvent = JsonSerializer.Deserialize<BookingEvent>(message);
            if (bookingEvent is not null && !string.IsNullOrEmpty(bookingEvent.EventId))
                await UpdateMongoDatabaseAsync(bookingEvent.EventId);
            else
                _logger.LogWarning("⚠️ Event message had no EventId, skipping: {Message}", message);

            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await _channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task UpdateMongoDatabaseAsync(string eventId)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        await catalogRepository.DecrementAvailableSeatsAsync(eventId);
        _logger.LogInformation("✅ MongoDB has been updated based on the received event!");
    }
}

// DTO (Data Transfer Object) — the message contract consumed from the "ticket_events" fanout.
// This is the consumer's copy of the shape Booking.API publishes; it carries only the wire
// fields, not Catalog's own CatalogItem entity. Kept in sync by hand with Booking.API's copy:
// if a field is renamed there, rename it here too or deserialization binds it to null.
public record BookingEvent(string EventId, string SeatId, string UserId);