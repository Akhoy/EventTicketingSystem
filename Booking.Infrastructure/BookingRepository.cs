using Booking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure;

// Implements IBookingRepository using EF Core.
// This is the only class in the codebase that calls SaveChangesAsync, FindAsync, or LINQ on Bookings.
// Program.cs and workers never touch DbContext directly — they only know about IBookingRepository.
public class BookingRepository : IBookingRepository
{
    private readonly BookingDbContext _context;
    private readonly ILogger<BookingRepository> _logger;

    public BookingRepository(BookingDbContext context, ILogger<BookingRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task AddAsync(Booking.Domain.Booking booking)
    {
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();
    }

    // Saves whatever state changes were already made on any tracked booking by its domain methods.
    // The caller (endpoint or worker) calls booking.Confirm() / booking.Expire() first, then this.
    public async Task SaveAsync()
    {
        await _context.SaveChangesAsync();

        // Only reached if the save succeeded. "Raising" a domain event (adding it to
        // _domainEvents) already happened earlier, inside Booking.Confirm() — that's
        // just recording that something happened, nothing sent anywhere yet.
        // "Dispatching" is this step: reading what was raised and acting on it, and
        // it only runs here, after the database save is confirmed durable, so nothing
        // is ever announced for a change that didn't actually persist.
        //
        // Logging here is just the simplest possible example of "dispatch." Common
        // real dispatch actions for a domain event: publish to a message broker
        // (RabbitMQ/Kafka) for other services to react to, send a notification
        // (email/SMS/push), update a read model / materialized view, trigger a
        // MediatR INotification so in-process handlers can each react independently,
        // or write an outbox row for reliable async delivery (as OutboxRelayWorker
        // already does for confirmed bookings, separately from this).
        var bookingsWithEvents = _context.ChangeTracker.Entries<Booking.Domain.Booking>()
            .Select(e => e.Entity)
            .Where(b => b.DomainEvents.Count > 0);

        foreach (var booking in bookingsWithEvents)
        {
            foreach (var domainEvent in booking.DomainEvents)
                _logger.LogInformation("Domain event dispatched: {DomainEvent}", domainEvent);

            // DbContext is Scoped — the same tracked Booking instance can still be
            // around if SaveAsync() is called again later in this same request. Without
            // clearing, the same BookingConfirmed would be found and dispatched twice.
            booking.ClearDomainEvents();
        }
    }

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
