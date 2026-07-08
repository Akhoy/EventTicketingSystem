namespace Catalog.Shared;

public interface ICatalogRepository
{
    Task<long> GetCountAsync();
    Task SeedDataAsync(IEnumerable<CatalogItem> items);
    Task<List<CatalogItem>> GetAllAsync();
    Task DecrementAvailableSeatsAsync(string eventId);
}
