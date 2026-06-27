using MongoDB.Driver;

namespace Catalog.API.Repositories;
public class CatalogRepository: ICatalogRepository
{
    private readonly IMongoCollection<CatalogItem> _ticketsCollection;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(IMongoDatabase database, ILogger<CatalogRepository> logger)
    {
        _ticketsCollection = database.GetCollection<CatalogItem>("Tickets");
        _logger = logger;
        _logger.LogInformation("📦 CatalogRepository has been initialized and connected to MongoDB!");
    }
    public async Task DecrementAvailableSeatsAsync(string eventId)
    {
        // Target the specific event, not "the first document". This is the read-model
        // (display) count that follows confirmed bookings — Redis is the authoritative gate.
        var filter = Builders<CatalogItem>.Filter.Eq(x => x.EventId, eventId);
        var update = Builders<CatalogItem>.Update.Inc(x => x.AvailableSeats, -1);
        await _ticketsCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("✅ Decremented available seats for event {EventId} in MongoDB!", eventId);
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
