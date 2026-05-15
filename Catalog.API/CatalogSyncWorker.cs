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
        // Implementation for background task
        // 1. Connect to RabbitMQ
        var rabbitMqConnectionString = _configuration.GetConnectionString("RabbitMQ");
        var factory = new ConnectionFactory() { Uri = new Uri(rabbitMqConnectionString!) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // 2. Declare the Exchange (Just in case Catalog boots before the Payment Worker)
        await _channel.ExchangeDeclareAsync(exchange: "ticket_events", type: ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

        // 3. Create a private Queue strictly for Catalog API and bind it to the Exchange
        var queueName = "catalog_sync_queue";
        await _channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(queue: queueName, exchange: "ticket_events", routingKey: string.Empty, cancellationToken: stoppingToken);

        _logger.LogInformation("🎧 Catalog API is successfully bound to the Event Bus!");

        // 4. Start consuming messages - Listen for events and update MongoDB accordingly
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            // Handle the received message and update MongoDB
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            _logger.LogInformation("📩 Received event: {Message}", message);
            // Here, you would deserialize the message and perform the necessary database operations to sync the catalog data.
            // For example, if the message indicates that a ticket was purchased, you might want to decrease the available seats for that ticket in MongoDB.
            // 6. Update the NoSQL Database
            await UpdateMongoDatabaseAsync();          
            // 7. Tell RabbitMQ the message was processed successfully
            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);  
        };

        await _channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        
        // Keep the background service alive forever
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task UpdateMongoDatabaseAsync()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        await catalogRepository.DecrementAvailableSeatsAsync();
        _logger.LogInformation("✅ MongoDB has been updated based on the received event!");        
    }
}