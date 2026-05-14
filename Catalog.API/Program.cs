using Catalog.API;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// 1. Connect to MongoDB
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoClient = new MongoClient(mongoConnectionString);
var database = mongoClient.GetDatabase("CatalogDb");
var ticketsCollection = database.GetCollection<CatalogItem>("Tickets");

// Add the RabbitMQ listener as a background service
builder.Services.AddHostedService<CatalogSyncWorker>();
var app = builder.Build();

// 2. Seed Data (Run once on startup if the DB is empty)
var ticketCount = await ticketsCollection.CountDocumentsAsync(FilterDefinition<CatalogItem>.Empty);
if (ticketCount == 0)
{
    Console.WriteLine("Seeding Catalog Database...");
    var seedData = new List<CatalogItem>
    {
        new() { EventName = "Taylor Swift - Eras Tour", Price = 250.00m, AvailableSeats = 5000 },
        new() { EventName = "Coldplay - Spheres", Price = 120.00m, AvailableSeats = 2000 },
        new() { EventName = "Tech Conference 2026", Price = 50.00m, AvailableSeats = 300 }
    };
    await ticketsCollection.InsertManyAsync(seedData);
}
// 3. The CQRS "Query" Endpoint - Optimized for blazing fast reads
// The endpoint to call this: http://localhost:5050/catalog/items. Why is it /catalog/items? Because we're following RESTful conventions where "catalog" is the resource and "items" are the specific entities we're fetching. This makes it clear that we're retrieving items from the catalog, and it also allows for future expansion (e.g., /catalog/items/{id} for specific item details). This gets pointed to /items in the API gateway due to the transform configuration we set up in the API gateway, which helps to keep our internal API structure clean and consistent while providing a user-friendly endpoint for clients.
// for e.g. "Transforms": [{ "PathPattern": "{**catch-all}" }]
app.MapGet("/items", async () =>
{
    // Fetches all tickets from NoSQL instantly
    // In a real app, you'd add pagination, filtering, etc. here
    // MongoDB's Find with an empty filter returns all documents
    // _ => true means "match all documents". It's a Func that takes a CatalogItem and returns true for all items, effectively selecting everything in the collection.
    var tickets = await ticketsCollection.Find(_ => true).ToListAsync();
    return Results.Ok(tickets);
});
app.Run();