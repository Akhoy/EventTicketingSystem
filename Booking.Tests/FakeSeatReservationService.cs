using Booking.Application;

namespace Booking.Tests;

// Same idea as FakeBookingRepository: a hand-written in-memory stand-in for
// ISeatReservationService, so CreateBookingHandlerTests can exercise the lock/decrement/
// rollback logic without a real Redis instance. Backed by plain dictionaries instead of
// Redis's SETNX/DECRBY commands.
public class FakeSeatReservationService : ISeatReservationService
{
    private readonly Dictionary<string, string> _locks = new();
    private readonly Dictionary<string, long> _availableSeats = new();

    // Lets a test pre-set an event's remaining-seat count before exercising the handler,
    // mirroring what CatalogSeeder would have set in real Redis.
    public void SeedAvailableSeats(string eventId, long count) => _availableSeats[eventId] = count;

    // Lets a test simulate a seat that's already locked by someone else.
    public void SeedLock(string eventId, string seatId, string holderId) =>
        _locks[$"{eventId}:{seatId}"] = holderId;

    public Task<bool> TryLockSeatAsync(string eventId, string seatId, string holderId, TimeSpan ttl)
    {
        var key = $"{eventId}:{seatId}";
        if (_locks.ContainsKey(key))
            return Task.FromResult(false); // mirrors Redis SETNX failing when the key exists

        _locks[key] = holderId;
        return Task.FromResult(true);
    }

    public Task<long> DecrementAvailableSeatsAsync(string eventId)
    {
        var current = _availableSeats.GetValueOrDefault(eventId, 0);
        var updated = current - 1;
        _availableSeats[eventId] = updated;
        return Task.FromResult(updated);
    }

    public Task IncrementAvailableSeatsAsync(string eventId)
    {
        _availableSeats[eventId] = _availableSeats.GetValueOrDefault(eventId, 0) + 1;
        return Task.CompletedTask;
    }

    public Task ReleaseSeatLockAsync(string eventId, string seatId)
    {
        _locks.Remove($"{eventId}:{seatId}");
        return Task.CompletedTask;
    }
}
