using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Infrastructure;

// Design-time only — lets `dotnet ef migrations add` construct BookingDbContext
// without needing Booking.API's DI container. Never used at runtime.
public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookingDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=BookingDb;User Id=sa;Password=SuperSecretPassword123!;TrustServerCertificate=True;");
        return new BookingDbContext(optionsBuilder.Options);
    }
}
