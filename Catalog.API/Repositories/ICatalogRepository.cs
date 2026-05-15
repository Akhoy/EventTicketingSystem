public interface ICatalogRepository
{
    Task<long> GetCountAsync();
    // to seed data at the start of the application
    Task SeedDataAsync(IEnumerable<CatalogItem> items);
    // get all items from the catalog, this will be used in the GET /items endpoint in Program.cs to fetch all the tickets from MongoDB and return them to the client. In a real application, you would likely want to add pagination, filtering, and sorting capabilities to this method to handle larger datasets and provide a better user experience. For example, you might want to allow clients to request a specific page of results with a certain number of items per page, or filter the results based on criteria such as event name or price range.
     Task<List<CatalogItem>> GetAllAsync();
    // This method would contain the logic to connect to MongoDB and update the catalog data based on the received event (catalogsyncworker.cs)
    // For example, you might connect to MongoDB, find the relevant ticket document, and update the available seats or other details as needed.
    // The actual implementation would depend on the structure of your MongoDB documents and the events you're receiving from RabbitMQ.
    Task DecrementAvailableSeatsAsync();    
}