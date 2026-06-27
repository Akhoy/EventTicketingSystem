using Microsoft.EntityFrameworkCore;

namespace PaymentProcessor.Worker;
// EF
public class TicketDbContext : DbContext
{
    public TicketDbContext(DbContextOptions<TicketDbContext> options) : base(options) { }
    public DbSet<TicketOrder> TicketOrders { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}