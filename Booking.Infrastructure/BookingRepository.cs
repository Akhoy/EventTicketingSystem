using Booking.Domain;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure;

// Implements IBookingRepository using EF Core.
// This is the only class in the codebase that calls SaveChangesAsync, FindAsync, or LINQ on Bookings.
// Program.cs and workers never touch DbContext directly — they only know about IBookingRepository.
public class BookingRepository : IBookingRepository
{
    private readonly BookingDbContext _context;

    public BookingRepository(BookingDbContext context) => _context = context;

    public async Task AddAsync(Booking.Domain.Booking booking)
    {
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();
    }

    // Saves whatever state changes were already made on any tracked booking by its domain methods.
    // The caller (endpoint or worker) calls booking.Confirm() / booking.Expire() first, then this.
    public async Task SaveAsync() =>
        await _context.SaveChangesAsync();

    public async Task<Booking.Domain.Booking?> GetByIdAsync(Guid id) =>
        await _context.Bookings.FindAsync(id);

    public async Task<List<Booking.Domain.Booking>> GetPendingBookingsOlderThan(DateTime cutoff) =>
        await _context.Bookings
            .Where(b => b.Status == "Pending" && b.CreatedAt < cutoff)
            .ToListAsync();

    public async Task<List<Booking.Domain.Booking>> GetConfirmedUnpublishedBookings() =>
        await _context.Bookings
            .Where(b => b.Status == "Confirmed" && b.PublishedAt == null)
            .ToListAsync();
}
