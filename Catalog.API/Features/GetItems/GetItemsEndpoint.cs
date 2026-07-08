using Catalog.Shared;

namespace Catalog.Features.GetItems;

public static class GetItemsEndpoint
{
    public static void Register(WebApplication app)
    {
        app.MapGet("/items", async (ICatalogRepository catalogRepository) =>
        {
            var tickets = await catalogRepository.GetAllAsync();
            return Results.Ok(tickets);
        });
    }
}
