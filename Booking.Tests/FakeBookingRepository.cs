using Booking.Domain;

namespace Booking.Tests;

// A hand-written "fake" implementation of IBookingRepository, backed by a plain in-memory list
// instead of EF Core + SQL Server. This is the "fake" mentioned in the Day 5 study plan note:
// "Mocking IBookingRepository with a fake to test worker logic without EF Core."
//
// Why a hand-written fake instead of a mocking library (e.g. Moq/NSubstitute)? Because
// IBookingRepository's methods aren't simple value returns — GetPendingBookingsOlderThan
// needs real filtering logic, and SaveAsync needs to actually reflect Expire()/Confirm() calls
// made directly on the Booking objects (since EF Core just tracks in-place mutations, a fake
// that holds the same object references behaves the same way for free). A mocking library
// would need cumbersome setup to replicate that; a small real class is simpler and clearer.
//
// This class only exists in the test project — production code always uses the real
// EF-Core-backed BookingRepository in Booking.Infrastructure.
public class FakeBookingRepository : IBookingRepository
{
    private readonly List<Domain.Booking> _bookings = new();

    // Lets a test seed the fake with bookings before exercising the worker logic.
    public void Seed(Domain.Booking booking) => _bookings.Add(booking);

    public Task AddAsync(Domain.Booking booking)
    {
        _bookings.Add(booking);
        return Task.CompletedTask;
    }

    // Real EF Core's SaveChangesAsync() flushes whatever mutations were already made on tracked
    // entities. Because this fake stores the same object references that the test/worker holds,
    // there's nothing to "flush" — Expire()/Confirm() already mutated the object in place. This
    // method is a no-op, but it's still called so tests exercise the same call pattern as
    // production code (repository.SaveAsync() after mutating a booking).
    public Task SaveAsync() => Task.CompletedTask;

    public Task<Domain.Booking?> GetByIdAsync(Guid id)
    {
        return Task.FromResult(_bookings.FirstOrDefault(b => b.Id == id));
    }

    public Task<List<Domain.Booking>> GetPendingBookingsOlderThan(DateTime cutoff)
    {
        var result = _bookings
            .Where(b => b.Status == "Pending" && b.CreatedAt < cutoff)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<List<Domain.Booking>> GetConfirmedUnpublishedBookings()
    {
        var result = _bookings
            .Where(b => b.Status == "Confirmed" && b.PublishedAt is null)
            .ToList();
        return Task.FromResult(result);
    }
}
