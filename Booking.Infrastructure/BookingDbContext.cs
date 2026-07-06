using Booking.Domain;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure;

// Moved from Booking.API to Booking.Infrastructure — EF Core is an infrastructure concern.
// Booking.Domain must not reference EF Core. Keeping DbContext here enforces that boundary.
public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public DbSet<Booking.Domain.Booking> Bookings => Set<Booking.Domain.Booking>();
}
