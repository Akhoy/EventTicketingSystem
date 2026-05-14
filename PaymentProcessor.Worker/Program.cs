using Microsoft.EntityFrameworkCore;
using PaymentProcessor.Worker;

var builder = Host.CreateApplicationBuilder(args);
// 1. Register the DbContext using the Docker connection string
// Grab the connection string from appsettings.json or Environment Variables (injected by Docker Compose)
var connectionString = builder.Configuration.GetConnectionString("SqlServer");

// Configure the DbContext to use SQL Server with the provided connection string. This allows our application to connect to the SQL Server database running in a Docker container, enabling us to perform database operations such as querying and saving ticket orders. By using the connection string from the configuration, we can easily switch between different databases or configurations without changing the code, making our application more flexible and adaptable to different environments (e.g., development, production).
// by default has a scoped lifetime, which means a new instance is created for each request. In a worker service, this is typically sufficient, as the DbContext will be used within the scope of a single operation (e.g., processing a message from RabbitMQ). However, if you need to share the DbContext across multiple operations or threads, you may want to consider using a different lifetime (e.g., singleton) and managing the DbContext's lifecycle manually to ensure thread safety and proper disposal.
builder.Services.AddDbContext<TicketDbContext>(options =>
    options.UseSqlServer(connectionString));
// what is a hosted service? A hosted service is a background task that runs alongside the main application. It is typically used for tasks that need to run continuously or on a schedule, such as processing messages from a queue, performing background maintenance, or running periodic jobs. In this case, we are adding a hosted service called Worker, which will handle the background processing of ticket orders. By registering it with the dependency injection container, we ensure that it will be started automatically when the application runs and can take advantage of the services and configurations defined in the application.
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// 2. Automatically apply database migrations on startup
// EnsureCreated vs Migrations: EnsureCreated is a method that checks if the database exists and creates it if it doesn't. However, it does not handle schema changes or updates to the database structure. Migrations, on the other hand, are a more robust way to manage database schema changes over time. They allow you to define incremental changes to the database schema and apply them in a controlled manner. In this case, we are using EnsureCreated for simplicity, but in a production application, you would typically use migrations to manage your database schema more effectively. By calling EnsureCreated on startup, we ensure that the database and the necessary tables are created if they don't already exist, allowing our application to function properly without manual database setup.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
    db.Database.EnsureCreated(); // Creates the DB and tables if they don't exist
}
// Get the logger directly from the DI container
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 THIS IS A PROPER LOG: Worker is about to start!");
host.Run();