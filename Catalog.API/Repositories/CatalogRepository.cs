using MongoDB.Driver;

namespace Catalog.API.Repositories;
public class CatalogRepository: ICatalogRepository
{
    // This class would contain the actual implementation of the ICatalogRepository interface, connecting to MongoDB and performing the necessary operations to update the catalog data based on the events received from RabbitMQ.
    // For example, in the DecrementAvailableSeatsAsync method, you would connect to MongoDB, find the relevant ticket document based on the event data (e.g., ticket ID), and decrement the available seats accordingly. You would also need to handle any potential concurrency issues that may arise when multiple events are processed simultaneously.    
    private readonly IMongoCollection<CatalogItem> _ticketsCollection;
    private readonly ILogger<CatalogRepository> _logger;

    // Inject the database, do not create it here.
    public CatalogRepository(IMongoDatabase database, ILogger<CatalogRepository> logger)
    {
        _ticketsCollection = database.GetCollection<CatalogItem>("Tickets");
        _logger = logger;
        _logger.LogInformation("📦 CatalogRepository has been initialized and connected to MongoDB!");
    }
    public async Task DecrementAvailableSeatsAsync()
    {        
        var filter = Builders<CatalogItem>.Filter.Empty; // Example filter, you would use actual data from the event. No filter so it will take the first document it finds. In a real implementation, you would likely have a more specific filter to target the correct ticket document based on the event data (e.g., using a ticket ID).
        var update = Builders<CatalogItem>.Update.Inc(x => x.AvailableSeats, -1); // Example update, you would adjust the available seats based on the event data. What will the example update do? The example update uses the MongoDB driver to create an update definition that decrements the AvailableSeats field by 1 for the matched document(s). In a real implementation, you would likely have a more specific filter to target the correct ticket document based on the event data (e.g., using a ticket ID), and the update might adjust the available seats based on the number of seats purchased rather than just decrementing by 1. This is just a simplified example to illustrate how you might perform an update operation in MongoDB based on an event received from RabbitMQ.
        await _ticketsCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("✅ Available seats have been decremented in MongoDB based on the received event!");
    }

    public Task<List<CatalogItem>> GetAllAsync()
    {
        _logger.LogInformation("✅ Fetching all catalog items from MongoDB from repository!");
        return _ticketsCollection.Find(_ => true).ToListAsync();        
    }

    public Task<long> GetCountAsync()
    {
        _logger.LogInformation("✅ Counting documents in MongoDB! from repository");
        return _ticketsCollection.CountDocumentsAsync(FilterDefinition<CatalogItem>.Empty);
            
    }

    public Task SeedDataAsync(IEnumerable<CatalogItem> items)
    {
        _logger.LogInformation("✅ Seeding initial catalog data into MongoDB! from repository");
        return _ticketsCollection.InsertManyAsync(items);
    }
        
}
