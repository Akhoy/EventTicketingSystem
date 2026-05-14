// 4. The NoSQL Document Model
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class CatalogItem
{
    // MongoDB requires an Id field, which is typically of type ObjectId.
    // The BsonId attribute indicates that this property is the document's primary key. What is more, the BsonRepresentation attribute allows us to use a string representation of the ObjectId, making it easier to work with in our application code. 
    // What is Bson? Bson stands for Binary JSON, and it is a binary-encoded serialization of JSON-like documents. It is the data format used by MongoDB to store and transmit data. Bson allows for efficient storage and retrieval of data, as well as support for a wide range of data types, including embedded documents and arrays. In our CatalogItem class, we use Bson attributes to define how the properties of the class should be serialized and deserialized when interacting with MongoDB.
    // The Id property is decorated with the BsonId attribute, which indicates that it is the primary key for the document in MongoDB. The BsonRepresentation attribute specifies that the Id should be represented as a string when serialized to BSON, which allows us to work with it as a string in our application code while still being stored as an ObjectId in MongoDB. This is a common practice when working with MongoDB in .NET applications, as it provides a more convenient way to handle the Id property without having to deal with the ObjectId type directly.
    // What is BsonType.ObjectId? BsonType.ObjectId is a specific data type in MongoDB that represents a unique identifier for documents. It is a 12-byte binary value that is typically used as the primary key for documents in MongoDB collections. The ObjectId is generated automatically by MongoDB when a new document is inserted into a collection, and it contains information about the timestamp of creation, the machine identifier, the process identifier, and a counter. In our CatalogItem class, we use the BsonRepresentation attribute to specify that the Id property should be represented as a string when serialized to BSON, while still being stored as an ObjectId in MongoDB. This allows us to work with the Id as a string in our application code while maintaining its unique identifier functionality in MongoDB.
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int AvailableSeats { get; set; }
}