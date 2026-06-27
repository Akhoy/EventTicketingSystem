// 4. The NoSQL Document Model
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class CatalogItem
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Stable business identifier shared across services (Booking, Redis counter).
    // Separate from the Mongo-assigned _id above, which is an internal storage detail.
    public string EventId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int AvailableSeats { get; set; }
}