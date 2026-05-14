using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Catalog.API;

public class CatalogSyncWorker: BackgroundService
{
    // This worker listens to RabbitMQ for events and syncs them to MongoDB
    // In a real application, you'd want to handle retries, dead-lettering, and other edge cases for robustness
    // For simplicity, this example focuses on the core concept of consuming messages and updating the database.
    // IConnection and IChannel are RabbitMQ client interfaces for managing connections and channels to the RabbitMQ server. They allow us to establish a connection, create channels for communication, and manage the lifecycle of these resources.
    // IConfiguration is used to access configuration settings, such as connection strings for MongoDB and RabbitMQ, which are typically stored in appsettings.json or environment variables. ILogger is used for logging information, warnings, and errors during the execution of the background service, which is crucial for monitoring and debugging.
    private readonly IConfiguration _configuration;
    private readonly ILogger<CatalogSyncWorker> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    // The constructor initializes the configuration and logger, which are essential for the worker's operation. The configuration is used to retrieve connection strings and other settings, while the logger is used to log important information and errors that occur during the execution of the background service. Why are we defining a constructor here? Because we need to inject the IConfiguration and ILogger dependencies into our CatalogSyncWorker class. This allows us to access configuration settings (like connection strings) and log information or errors during the execution of the background service. Dependency injection is a common pattern in .NET applications that promotes loose coupling and makes it easier to manage dependencies.
    public CatalogSyncWorker(IConfiguration configuration, ILogger<CatalogSyncWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
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
        //var queueName = await _channel.QueueDeclareAsync(queue: "", exclusive: true, cancellationToken: stoppingToken);
        //await _channel.QueueBindAsync(queue: queueName.QueueName, exchange: "ticket_events", routingKey: string.Empty, cancellationToken: stoppingToken);

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

        // the below code starts consuming messages from the specified queue using the defined consumer. It listens for incoming messages and processes them asynchronously. The autoAck parameter is set to false, which means that the worker will manually acknowledge the message after processing it successfully. This allows for better control over message processing and ensures that messages are not lost if there is an error during processing. The cancellationToken is passed to allow for graceful shutdown of the background service when needed.
        await _channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        
        // Keep the background service alive forever
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task UpdateMongoDatabaseAsync()
    {
        // This method would contain the logic to connect to MongoDB and update the catalog data based on the received event.
        // For example, you might connect to MongoDB, find the relevant ticket document, and update the available seats or other details as needed.
        // The actual implementation would depend on the structure of your MongoDB documents and the events you're receiving from RabbitMQ.
        // 1. Connect to MongoDB
        var mongoConnectionString = _configuration.GetConnectionString("MongoDb");
        var mongoClient = new MongoClient(mongoConnectionString);
        var database = mongoClient.GetDatabase("CatalogDb");
        var ticketsCollection = database.GetCollection<CatalogItem>("Tickets");
        // 2. Perform the necessary update operations based on the event data
        // For example, if the event indicates a ticket purchase, you might want to decrease the available seats for that ticket in MongoDB. You would need to deserialize the event message to get the relevant information (e.g., ticket ID, number of seats purchased) and then execute an update operation on the MongoDB collection to reflect the changes in the catalog.
        var filter = Builders<CatalogItem>.Filter.Empty; // Example filter, you would use actual data from the event. No filter so it will take the first document it finds. In a real implementation, you would likely have a more specific filter to target the correct ticket document based on the event data (e.g., using a ticket ID).
        var update = Builders<CatalogItem>.Update.Inc(x => x.AvailableSeats, -1); // Example update, you would adjust the available seats based on the event data. What will the example update do? The example update uses the MongoDB driver to create an update definition that decrements the AvailableSeats field by 1 for the matched document(s). In a real implementation, you would likely have a more specific filter to target the correct ticket document based on the event data (e.g., using a ticket ID), and the update might adjust the available seats based on the number of seats purchased rather than just decrementing by 1. This is just a simplified example to illustrate how you might perform an update operation in MongoDB based on an event received from RabbitMQ.
        await ticketsCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("✅ MongoDB has been updated based on the received event!");        
    }
}