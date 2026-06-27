using Catalog.API;
using Catalog.API.Repositories;
using Microsoft.VisualBasic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoClient = new MongoClient(mongoConnectionString);
var database = mongoClient.GetDatabase("CatalogDb");
builder.Services.AddSingleton<IMongoDatabase>(database);

// Redis — Catalog.API owns the seat data, so it primes the availability counter
// that Booking.API later decrements. abortConnect=false so startup doesn't throw if Redis lags.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var redis = ConnectionMultiplexer.Connect(redisConnectionString!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddHostedService<CatalogSyncWorker>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var catalogRepository = services.GetRequiredService<ICatalogRepository>();
    var count = await catalogRepository.GetCountAsync();
    var seedData = new List<CatalogItem>
    {
        new() { EventId = "evt-taylor-swift", EventName = "Taylor Swift - Eras Tour", Price = 250.00m, AvailableSeats = 5000 },
        new() { EventId = "evt-coldplay", EventName = "Coldplay - Spheres", Price = 120.00m, AvailableSeats = 2000 },
        new() { EventId = "evt-tech-conf", EventName = "Tech Conference 2026", Price = 50.00m, AvailableSeats = 300 }
    };

    if (count == 0)
    {
        Console.WriteLine("Seeding initial catalog data...");
        await catalogRepository.SeedDataAsync(seedData);

        // Prime the Redis availability gate from the SAME list that seeded Mongo,
        // so the two stores can't drift. Booking.API decrements these counters.
        var db = redis.GetDatabase();
        foreach (var item in seedData)
            await db.StringSetAsync($"event:{item.EventId}:seats", item.AvailableSeats);
    }
}

app.MapGet("/items", async (ICatalogRepository catalogRepository) =>
{
    var tickets = await catalogRepository.GetAllAsync();
    return Results.Ok(tickets);
});
app.Run();