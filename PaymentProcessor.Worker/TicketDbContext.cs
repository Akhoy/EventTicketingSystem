using Microsoft.EntityFrameworkCore;

namespace PaymentProcessor.Worker;

// DbContext: This class represents the database context for our ticket orders. It inherits from Entity Framework Core's DbContext, which provides the necessary functionality to interact with a database. The TicketDbContext class defines a DbSet<TicketOrder> property, which represents the collection of ticket orders in the database. This allows us to perform CRUD (Create, Read, Update, Delete) operations on ticket orders using Entity Framework Core's LINQ queries and other features.
public class TicketDbContext : DbContext
{
    // Dependency Injection: The constructor of the TicketDbContext class takes a DbContextOptions<TicketDbContext> parameter, which is used to configure the database connection and other options. This allows us to inject the necessary configuration when we create an instance of the TicketDbContext, making it flexible and easy to use in different environments (e.g., development, production).
    // What is dependency injection? Dependency injection is a design pattern that allows us to decouple the creation of an object from its dependencies. Instead of creating dependencies directly within a class, we can inject them from the outside, typically through constructors or properties. This promotes loose coupling and makes our code more modular and testable. In the case of the TicketDbContext, we are injecting the DbContextOptions, which contains the configuration for the database connection, allowing us to easily switch between different databases or configurations without changing the code inside the TicketDbContext class.
    public TicketDbContext(DbContextOptions<TicketDbContext> options) : base(options) { }

    // DbSet<TicketOrder>: This property represents the collection of ticket orders in the database. It allows us to perform various operations on the ticket orders, such as querying, adding new orders, updating existing orders, and deleting orders. By using Entity Framework Core's DbSet, we can leverage its powerful features for working with data, such as LINQ queries, change tracking, and migrations. It represents a table in the database where each record corresponds to a TicketOrder entity. When we interact with this DbSet, Entity Framework Core translates our operations into SQL queries that are executed against the underlying database, allowing us to manage our ticket orders efficiently.
    public DbSet<TicketOrder> TicketOrders { get; set; }
}