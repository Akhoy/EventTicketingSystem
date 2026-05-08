using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentProcessor.Worker;

public class Worker : BackgroundService
{
    // In a real application, you'd likely inject a service to handle the actual payment processing logic (e.g., StripeService).
    // For this demo, we'll just simulate the work with a delay.
    // The ILogger is used for logging messages to the console or a file, and IConfiguration is used to read settings (like the RabbitMQ connection string).
    // The IConnection and IChannel are RabbitMQ client interfaces for managing the connection and communication with the RabbitMQ server.
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private IConnection? _connection;
    private IChannel? _channel;
    // IServiceScopeFactory is used to create scopes for dependency injection, which allows us to create scoped services (like DbContext) within our background service. What does that mean? It means that we can create a new scope for each message we process, allowing us to use scoped services like DbContext without running into issues of shared state or concurrency. By using the scope factory, we can ensure that each message is processed with its own instance of the DbContext, which is important for thread safety and proper resource management.
    private readonly IServiceScopeFactory _scopeFactory;

    // The constructor takes in the logger and configuration via dependency injection, which is a common pattern in .NET applications. The connection and channel are initialized later when the worker starts.
    public Worker(ILogger<Worker> logger, IConfiguration config, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _config = config;
        _scopeFactory = scopeFactory; // inject the scope factory to create scopes for DbContext when processing messages
    }

    // The ExecuteAsync method is the main entry point for the background service. It sets up the RabbitMQ connection, declares the queue, and starts consuming messages. The while loop at the end keeps the service running until it's stopped.
    // how to handle cancellation: The stoppingToken is a CancellationToken that signals when the service should stop. We pass this token to all asynchronous operations so that they can be cancelled gracefully when the service is shutting down. Where to call? We call this method when the worker starts, and it will run until the worker is stopped. The cancellation token allows us to break out of the infinite loop and clean up resources when the service is stopping.
    // cancellationToken: auto or manually passed? The cancellation token is automatically passed to the ExecuteAsync method by the framework when the background service starts. We also pass it to all asynchronous operations within the method to ensure that they can be cancelled if the service is stopping. This allows for graceful shutdowns and resource cleanup.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Processor Worker starting up...");

        var factory = new ConnectionFactory { 
            Uri = new Uri(_config.GetConnectionString("RabbitMQ")!) 
        };

        // 1. Establish the connection asynchronously
        // _connection: This is the connection to the RabbitMQ server. It's like opening a channel to talk to the server.
        // _channel: This is the channel through which we will send and receive messages. Think of it as a virtual connection that allows us to interact with the queues.
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // 2. Ensure the queue exists (in case the worker boots up before the API)
        await _channel.QueueDeclareAsync(
            queue: "ticket_orders", 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            arguments: null,
            cancellationToken: stoppingToken);

        // 3. Set up the Consumer to listen for messages
        // AsyncEventingBasicConsumer is a RabbitMQ client class that allows us to consume messages asynchronously. It raises an event every time a new message is received.
        var consumer = new AsyncEventingBasicConsumer(_channel);
        
        // This event handler is called every time a new message arrives in the "ticket_orders" queue. We read the message, simulate processing it (e.g., calling a payment API), and then acknowledge the message if processing is successful. model: This is the consumer instance that received the message. ea: This is the event arguments that contain details about the received message, including the body, delivery tag, etc.
        consumer.ReceivedAsync += async (model, ea) =>
        {
            // 1. Read the message body (the order details)
            var body = ea.Body.ToArray();
            // 2. Convert the byte array to a string (assuming UTF-8 encoding). UTF-8: This is a common character encoding that can represent all characters in the Unicode standard. It's widely used for text data.
            // Byte array: RabbitMQ messages are sent as byte arrays. We need to convert them back to a string to read the order details.
            var message = Encoding.UTF8.GetString(body);

            // 1. Deserialize the incoming RabbitMQ message
            var orderData = System.Text.Json.JsonSerializer.Deserialize<TicketOrder>(message);
            var seatId = orderData?.SeatId ?? "Unknown";
            var userId = orderData?.UserId ?? "Unknown";

            _logger.LogInformation("Processing payment for Seat: {SeatId}, User: {UserId}", seatId, userId);

            // Open a new scope for dependency injection to get a new instance of the DbContext for this message processing. This ensures that we have a fresh DbContext for each message, which is important for thread safety and proper resource management.
            // which scope is it? It's a new scope created for each message processing. By calling CreateScope(), we are creating a new scope that will be disposed of after the message is processed, ensuring that any scoped services (like DbContext) are properly managed and do not interfere with other message processing.
            using var scope = _scopeFactory.CreateScope();
            // Using the above scope, we can now get an instance of the TicketDbContext that is configured for this scope. This allows us to interact with the database (e.g., save the processed order) as part of our message processing logic.
            // How is it configured for this scope? The TicketDbContext is registered in the dependency injection container with a scoped lifetime - example: services.AddDbContext<TicketDbContext>(options => options.UseSqlServer(connectionString));. When we call GetRequiredService<TicketDbContext>(), the service provider will give us the instance of TicketDbContext that is associated with the current scope, allowing us to safely interact with the database without worrying about concurrency issues or shared state between different message processing tasks.
            // Get the DbContext instance from the service provider within the scope. This allows us to interact with the database (e.g., save the processed order) as part of our message processing logic. What is a service provider? It's an object that manages the creation and lifetime of services in a dependency injection system. By calling GetRequiredService<TicketDbContext>(), we are asking the service provider to give us an instance of the TicketDbContext that we can use to interact with the database. What is a service? A service is a reusable component that provides specific functionality, such as data access, logging, or business logic. In this case, the TicketDbContext is a service that allows us to interact with the database to manage ticket orders.
            var dbContext = scope.ServiceProvider.            GetRequiredService<TicketDbContext>();

            // Create the entity and save it to the database
            var ticketOrder = new TicketOrder
            {
                Id = Guid.NewGuid(),
                SeatId = seatId,
                UserId = userId,
                ProcessedAt = DateTime.UtcNow
            };
            dbContext.TicketOrders.Add(ticketOrder);
            await dbContext.SaveChangesAsync(stoppingToken);

            _logger.LogInformation("Payment processed and order saved to database for Seat: {SeatId}, User: {UserId}", seatId, userId);
            
            // _logger.LogInformation($"[RECEIVED] New order to process: {message}");

            // // SIMULATE HEAVY WORK (e.g., calling Stripe or a bank API)
            // await Task.Delay(3000, stoppingToken); 

            // _logger.LogInformation($"[SUCCESS] Payment processed for order. Acknowledging message.");

            // 4. Manual Acknowledgment
            // This tells RabbitMQ: "I successfully processed this, you can delete it from the queue now."
            // If the worker crashes BEFORE this line runs, RabbitMQ puts the message back in the queue!
            // deliveryTag: This is a unique identifier for the message that RabbitMQ uses to track which messages have been acknowledged. multiple: false means we are only acknowledging this single message, not multiple messages at once.
            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        // 5. Start consuming!
        // autoAck: false means we will manually acknowledge messages after processing. If it were true, RabbitMQ would automatically consider messages as "handled" as soon as they are delivered to the consumer, which is not what we want for payment processing.
        // cancellationToken: This allows us to gracefully stop consuming messages when the worker is shutting down.
        // This line tells RabbitMQ to start sending messages from the "ticket_orders" queue to our consumer. The worker will keep running and processing messages until it's stopped. BasicConsumeAsync: This is the asynchronous version of the method to start consuming messages from a queue. It allows our worker to handle messages without blocking the main thread, which is important for scalability and responsiveness.
        await _channel.BasicConsumeAsync(
            queue: "ticket_orders", 
            autoAck: false, // Must be false so we can manually acknowledge AFTER payment succeeds
            consumer: consumer,
            cancellationToken: stoppingToken);

        // Keep the background service running infinitely
        // why is the below line required? The while loop with Task.Delay is a common pattern to keep the background service running. It allows the worker to continue processing messages as they arrive while also checking for cancellation requests. If we didn't have this loop, the ExecuteAsync method would exit immediately after setting up the consumer, which would stop the worker from running and processing messages.
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    // The StopAsync method is called when the worker is stopping. We use it to clean up the RabbitMQ connections gracefully. We check if the channel and connection are not null before attempting to close them, and we pass the cancellation token to ensure that the shutdown process can be cancelled if needed.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker stopping. Cleaning up RabbitMQ connections.");
        if (_channel != null) await _channel.CloseAsync(cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}