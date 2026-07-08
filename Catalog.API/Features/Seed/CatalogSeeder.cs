using Catalog.Shared;
using StackExchange.Redis;

namespace Catalog.Features.Seed;

public static class CatalogSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        //var catalogRepository = app.Services.GetRequiredService<ICatalogRepository>();
        //var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();

        var count = await catalogRepository.GetCountAsync();
        if (count > 0) return;

        var seedData = new List<CatalogItem>
        {
            new() { EventId = "evt-taylor-swift", EventName = "Taylor Swift - Eras Tour", Price = 250.00m, AvailableSeats = 5000 },
            new() { EventId = "evt-coldplay", EventName = "Coldplay - Spheres", Price = 120.00m, AvailableSeats = 2000 },
            new() { EventId = "evt-tech-conf", EventName = "Tech Conference 2026", Price = 50.00m, AvailableSeats = 300 }
        };

        Console.WriteLine("Seeding initial catalog data...");
        await catalogRepository.SeedDataAsync(seedData);

        var db = redis.GetDatabase();
        foreach (var item in seedData)
            await db.StringSetAsync($"event:{item.EventId}:seats", item.AvailableSeats);
    }
}
