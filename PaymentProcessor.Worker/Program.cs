using Microsoft.EntityFrameworkCore;
using PaymentProcessor.Worker;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("SqlServer");
builder.Services.AddDbContext<TicketDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<OutboxRelayWorker>();

var app = builder.Build();
// using (var scope = app.Services.CreateScope())
// {
//     var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
//     db.Database.Migrate(); 
// }
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 THIS IS A PROPER LOG: Worker is about to start!");
app.Run();