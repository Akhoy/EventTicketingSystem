using RabbitMQ.Client;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using Serilog; // 1. Add this at the top for Serilog


var builder = WebApplication.CreateBuilder(args);

// 2. Add this block to hijack the logging pipeline
// ILogger now goes through Serilog, which will write to both the console and Seq. This allows us to have structured logging that can be easily analyzed in Seq, while still keeping logs visible in the terminal for local development and debugging.
// What is Serilog? Serilog is a popular structured logging library for .NET applications. It provides a simple and flexible API for logging messages with rich contextual information. With Serilog, you can easily capture structured data in your logs, which makes it easier to analyze and search through log entries. By configuring Serilog to write to both the console and Seq, we can take advantage of its powerful features for managing and analyzing logs in our booking system, while still maintaining visibility of logs in the terminal during development.
// Are Serilog and Seq the same thing? No, they are not the same thing. Serilog is a logging library that allows you to create structured logs in your application, while Seq is a log server that collects, stores, and analyzes those logs. Serilog can be configured to send logs to various destinations (called "sinks"), including the console, files, and log servers like Seq. In our case, we are using Serilog to generate structured logs in our application and then sending those logs to Seq for centralized storage and analysis. This combination allows us to have a powerful logging solution that can help us monitor and troubleshoot our booking system effectively.
// What is Seq? Seq is a structured log server that allows you to collect, search, and analyze logs from your applications. It provides a user-friendly interface for exploring log data, creating dashboards, and setting up alerts based on specific log events. By sending logs to Seq, you can gain insights into the behavior of your application, identify issues, and monitor performance in a more efficient way than traditional text-based logging. In our case, we are configuring Serilog to send logs to Seq running in a Docker container, which allows us to centralize our logging and take advantage of Seq's powerful features for analyzing our booking system's logs.
builder.Host.UseSerilog((context, configuration) =>
{
    // Grab the URL dynamically from appsettings.json or Environment Variables!
    var seqUrl = builder.Configuration["SeqUrl"];
    configuration
        .MinimumLevel.Information()
        .WriteTo.Console() // Keep writing to the terminal
        .WriteTo.Seq(seqUrl!); // Send logs over the Docker network to the Seq container
});

// 1. Grab the connection string Docker Compose is injecting
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

// 2. Create a ConnectionMultiplexer and register it as a singleton
// what is a ConnectionMultiplexer? It's the main object used to interact with Redis. It manages the connection and provides methods to perform operations on Redis. By registering it as a singleton, we ensure that the same instance is used throughout the application, which is important for performance and resource management.
// what will redisConnectionString look like? It will be something like "redis:6379" where "redis" is the hostname of the Redis container defined in our Docker Compose file, and "6379" is the default port that Redis listens on. This connection string allows our application to connect to the Redis instance running in the Docker container. By using this connection string, we can easily access Redis from our application without having to worry about the underlying network configuration, as Docker Compose will handle the networking between our application and the Redis container.
// also, what will redisConnectionString look like in development vs production? In development, it might be "localhost:6379" if you're running Redis locally, or "redis:6379" if you're using Docker Compose. In production, it could be something like "redis-prod:6379" or a connection string provided by a managed Redis service, such as "my-redis-instance.redis.cache.windows.net:6380,password=yourpassword". The key point is that the connection string can vary based on the environment, and by using configuration files or environment variables, we can easily manage these differences without changing our code. This allows for greater flexibility and easier deployment across different environments.
// redisConnectionString! is a null-forgiving operator in C#. It tells the compiler that we are confident that redisConnectionString will not be null at runtime, even though it may be defined as a nullable type. In this context, we are using it because we expect that the connection string will always be provided through configuration, and if it's not, it would indicate a misconfiguration that should be addressed. By using the null-forgiving operator, we can avoid compiler warnings about potential null reference exceptions while still ensuring that our code is robust and handles the case where the connection string might be missing appropriately (e.g., by throwing an exception or logging an error).
var redis = ConnectionMultiplexer.Connect(redisConnectionString!);

// 3. Register the ConnectionMultiplexer as a singleton in the dependency injection container. This allows us to inject it into our services and controllers wherever we need to interact with Redis. 
// By doing this, we can easily access Redis from any part of our application without having to create multiple connections, which can be resource-intensive and lead to performance issues. Why use IConnectionMultiplexer instead of ConnectionMultiplexer? Using the interface IConnectionMultiplexer allows for better abstraction and flexibility. It enables us to easily swap out the implementation of the connection multiplexer if needed, such as for testing purposes or if we want to use a different Redis client in the future. By programming against the interface, we can decouple our code from the specific implementation, making it more maintainable and adaptable to changes.
// by different implementations, we can also mock the IConnectionMultiplexer in unit tests, allowing us to test our code without needing a real Redis instance. This promotes better testing practices and helps ensure that our code is robust and reliable.  
// by different redis client, what if we want to switch to a different Redis client library in the future? By using the IConnectionMultiplexer interface, we can easily swap out the implementation without having to change our code that interacts with Redis. This provides greater flexibility and allows us to adapt to changes in our technology stack without significant refactoring. IConnectionMultiplexer is an interface that defines the contract for the connection multiplexer, while ConnectionMultiplexer is a concrete implementation of that interface. By registering the IConnectionMultiplexer interface in our dependency injection container, we can ensure that our code is not tightly coupled to a specific implementation, making it easier to maintain and test. IConnectionMultiplexer is used by StackExchange.Redis to provide a consistent API for interacting with Redis, regardless of the underlying implementation. By using the interface, we can take advantage of features like dependency injection and mocking, which can improve the maintainability and testability of our code. Additionally, using the interface allows us to easily switch to a different Redis client library in the future if needed, without having to change our code that interacts with Redis. This promotes better flexibility and adaptability in our application design.
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// 1. Connect to RabbitMQ using the connection string from docker-compose
//  ConnectionFactory is a class from the RabbitMQ.Client library that is used to create connections to a RabbitMQ server. It allows us to specify the connection parameters, such as the URI, username, password, and other settings needed to establish a connection. In this case, we are setting the Uri property of the ConnectionFactory to the connection string we retrieved from our configuration, which points to our RabbitMQ instance running in Docker. By using the ConnectionFactory, we can easily create connections and channels to interact with RabbitMQ for sending messages related to ticket bookings and payments.
var factory = new ConnectionFactory { 
    //Uri = new Uri(config.GetConnectionString("RabbitMQ")!) 
    Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMQ")!) 
};
        
// Use 'await using' to ensure the connection closes cleanly when the request ends
// This is important for resource management, especially under high load, as it prevents connection leaks and ensures that connections are properly disposed of after use. By using 'await using', we can take advantage of asynchronous disposal, which allows the application to efficiently manage resources without blocking threads while waiting for the connection to close. This is particularly beneficial in a high-traffic scenario where multiple requests may be creating connections to RabbitMQ simultaneously.
// connection and channel are both IAsyncDisposable, which means they can be used with 'await using' to ensure they are disposed of properly. This is crucial for maintaining the health of our application and preventing resource leaks, especially when dealing with external services like RabbitMQ.
// connection is the main object that represents a connection to the RabbitMQ server. It manages the underlying TCP connection and provides methods for creating channels, which are used to perform operations like declaring queues and publishing messages. The channel is a virtual connection within the main connection that allows us to interact with RabbitMQ without having to manage multiple TCP connections. By using 'await using', we ensure that both the connection and channel are properly disposed of when we're done with them, which helps maintain the stability and performance of our application.  
// channel is the object that we use to interact with RabbitMQ. It allows us to declare queues, publish messages, and perform other operations related to messaging. By using 'await using' for the channel, we ensure that it is properly disposed of after we're done with it, which helps prevent resource leaks and ensures that our application can handle high traffic without running into issues related to too many open channels or connections.
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

// 2. Ensure a queue named "ticket_orders" exists
// Await the queue declaration
// QueueDeclareAsync is an asynchronous method that declares a queue in RabbitMQ. It takes parameters such as the queue name, durability (durability is a boolean indicating whether the queue should survive broker restarts), exclusivity (whether the queue can only be accessed by the current connection), auto-delete behavior (whether the queue should be automatically deleted when the last consumer unsubscribes), and any additional arguments. By awaiting this method, we ensure that the queue is declared before we attempt to publish messages to it. This is important because if the queue does not exist when we try to publish a message, it will result in an error. By declaring the queue asynchronously, we can efficiently manage our resources and ensure that our application remains responsive, even under high load.
// broker restarts refer to situations where the RabbitMQ server is restarted, either intentionally (for maintenance) or unintentionally (due to crashes). If a queue is declared as durable, it will survive these restarts, meaning that the queue and its messages will still be available when the server comes back online. This is important for ensuring that our application can continue to function smoothly even in the face of unexpected server issues. By declaring our "ticket_orders" queue as durable, we can ensure that any messages related to ticket bookings and payments are not lost if the RabbitMQ server experiences downtime.
// ensure queue exists at startup
await channel.QueueDeclareAsync(
    queue: "ticket_orders", 
    durable: true, 
    exclusive: false, 
    autoDelete: false, 
    arguments: null);
// the rabbit mq initialisation code (connecting to the server, creating a channel, and declaring the queue) is placed at the application startup level. This means that it will be executed once when the application starts, ensuring that the connection to RabbitMQ is established and the necessary queue is declared before any requests are processed. By doing this, we can avoid the overhead of establishing a connection and declaring the queue for each incoming request, which can improve performance and reduce latency in our booking endpoint. Additionally, by using 'await using' for the connection and channel, we ensure that they are properly disposed of when the application shuts down, which helps maintain resource management and prevents potential issues with lingering connections.

var app = builder.Build();


// 3. The Booking Endpoint
// app.MapPost("/book/{seatId}", async (string seatId, IConnectionMultiplexer redis) =>
// {
//     var db = redis.GetDatabase();
//     // Perform booking logic here
//     // For example, we can use a Redis hash to store the booking status of each seat
//     var bookingKey = $"booking:{seatId}";
//     // Check if the seat is already booked
//     if (await db.HashExistsAsync(bookingKey, "status"))
//     {
//         return Results.BadRequest("Seat is already booked.");
//     }
//     // If not booked, set the booking status to "booked"
//     await db.HashSetAsync(bookingKey, "status", "booked");
//     return Results.Ok($"Seat {seatId} has been booked successfully.");    
// });

// another way of doing the above
// for a high traffic event ticketing system, we want to ensure that only one user can book a seat at a time.
// above code has issues since it is not atomic. If two users try to book the same seat at the same time, they could both pass the HashExistsAsync check before either of them sets the status to "booked". This could lead to a race condition where both users end up booking the same seat, which is not what we want. To solve this problem, we can use Redis' atomic operations to ensure that only one user can book a seat at a time. We can use the StringSetAsync method with the "When.NotExists" option to atomically set a lock on the seat. This way, if two users try to book the same seat at the same time, only one of them will succeed in acquiring the lock, while the other will receive a conflict response indicating that the seat is already reserved. This approach ensures that our booking system is robust and can handle high traffic without running into race conditions.

// 3. The Booking Endpoint
app.MapPost("/book/{seatId}", async (string seatId, IConnectionMultiplexer redis, IConfiguration config, ILogger<Program> logger) =>
{
    var db = redis.GetDatabase();
    var lockKey = $"seat:lock:{seatId}";
    var userId = Guid.NewGuid().ToString(); // Simulating a logged-in user

    // THE SENIOR MOVE: 
    // StringSetAsync with "When.NotExists" is an atomic operation. 
    // Even if 10,000 requests hit this line at the exact same millisecond, 
    // Redis guarantees that ONLY ONE of them will return true.
    bool acquiredLock = await db.StringSetAsync(
        lockKey, 
        userId, 
        TimeSpan.FromMinutes(5), // Lock expires in 5 mins if they don't pay
        When.NotExists
    );

    if (acquiredLock)
    {
        // Success! They got the lock. Now we can proceed with the booking and payment process.
        // --- NEW: SEND MESSAGE TO RABBITMQ ---       

        // 3. Create the event payload (what happened?)
        var ticketEvent = new { SeatId = seatId, UserId = userId, Timestamp = DateTime.UtcNow };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticketEvent));

        // 4. Publish it to the queue!
        // Await the publish action
        // BasicPublishAsync is an asynchronous method that publishes a message to a specified exchange with a routing key in RabbitMQ. It takes parameters such as the exchange name, routing key, and the message body (which is typically a byte array). By awaiting this method, we ensure that the message is successfully published to the queue before we proceed with any further actions. This is important for maintaining the integrity of our messaging system, especially in scenarios where we need to guarantee that messages are delivered reliably. By using BasicPublishAsync, we can efficiently manage our resources and ensure that our application remains responsive while interacting with RabbitMQ. In our case, we are publishing the ticket event to the default exchange (indicated by an empty string) with the routing key "ticket_orders", which will route the message to the "ticket_orders" queue we declared earlier. This allows us to decouple the booking logic from the payment processing logic, as the payment service can consume messages from the "ticket_orders" queue to handle payment processing asynchronously. This design promotes better scalability and responsiveness in our application, as the booking endpoint can quickly respond to the user while the payment processing happens in the background.
        await channel.BasicPublishAsync(
            exchange: string.Empty, 
            routingKey: "ticket_orders", 
            body: body);
        // --- NEW: Structured Log for Success --- 
        // logger is an instance of ILogger<Program> that is injected into the endpoint. It allows us to log structured information about the booking event. By using logger.LogInformation, we can log a message that includes the seat ID and user ID in a structured format. This is beneficial for monitoring and debugging purposes, as it allows us to easily search and filter logs based on specific properties (like SeatId and UserId) when analyzing our logs in Seq or any other logging platform. Structured logging provides more context and makes it easier to understand the flow of events in our application, especially when dealing with high traffic scenarios where multiple booking events may be occurring simultaneously. By logging this information, we can gain insights into user behavior, identify potential issues, and ensure that our booking system is functioning as expected.
        logger.LogInformation("Successfully published booking event for Seat {SeatId} by User {UserId}", seatId, userId);
        return Results.Ok(new { 
            message = $"Seat {seatId} locked successfully for 5 minutes.", 
            user = userId 
        });        
    }
    else
    {
        // Failed. Someone else beat them to it.
        logger.LogWarning("Booking failed. Seat {SeatId} was already locked.", seatId);
        return Results.Conflict(new { 
            message = $"Seat {seatId} is currently reserved by someone else." 
        });
    }
});

app.Run();