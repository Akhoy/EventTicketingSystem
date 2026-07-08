using MongoDB.Driver;

namespace Catalog.Shared;

public class CatalogRepository : ICatalogRepository
{
    private readonly IMongoCollection<CatalogItem> _ticketsCollection;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(IMongoDatabase database, ILogger<CatalogRepository> logger)
    {
        _ticketsCollection = database.GetCollection<CatalogItem>("Tickets");
        _logger = logger;
        _logger.LogInformation("CatalogRepository initialized and connected to MongoDB");
    }

    public async Task DecrementAvailableSeatsAsync(string eventId)
    {
        var filter = Builders<CatalogItem>.Filter.Eq(x => x.EventId, eventId);
        var update = Builders<CatalogItem>.Update.Inc(x => x.AvailableSeats, -1);
        await _ticketsCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("Decremented available seats for event {EventId} in MongoDB", eventId);
    }

    public Task<List<CatalogItem>> GetAllAsync()
    {
        return _ticketsCollection.Find(_ => true).ToListAsync();
    }

    public Task<long> GetCountAsync()
    {
        return _ticketsCollection.CountDocumentsAsync(FilterDefinition<CatalogItem>.Empty);
    }

    public Task SeedDataAsync(IEnumerable<CatalogItem> items)
    {
        return _ticketsCollection.InsertManyAsync(items);
    }
}
