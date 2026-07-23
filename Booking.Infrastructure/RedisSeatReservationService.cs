using Booking.Application;
using StackExchange.Redis;

namespace Booking.Infrastructure;

// Implements the ISeatReservationService port (defined in Booking.Application) using Redis.
// This is the exact same Redis logic that used to live inline inside Program.cs's /book lambda —
// only the "where it lives" changed, not the behaviour.
//
// Why it moved from Program.cs into Booking.Infrastructure: Booking.Application's
// CreateBookingHandler needs to lock/decrement/release seats, but Application must not know
// about StackExchange.Redis directly — Application only depends on Domain, never on a specific
// technology (same rule that already kept EF Core out of Program.cs, now applied to Redis too).
// So Application declares the interface (ISeatReservationService — "I need seat reservation,
// I don't care how"), and Infrastructure — the layer that's allowed to know about concrete
// technologies — provides the Redis answer here. Program.cs no longer touches Redis command
// calls at all; it just registers this class against the interface in DI.
public class RedisSeatReservationService : ISeatReservationService
{
    // IConnectionMultiplexer.GetDatabase() is cheap and stateless — it doesn't open a new
    // connection, just returns a lightweight handle to send commands through the existing
    // multiplexed connection. It's safe to fetch once here in the constructor and reuse for
    // every call, instead of calling GetDatabase() again inside each method below.
    private readonly IDatabase _cache;

    public RedisSeatReservationService(IConnectionMultiplexer redis)
    {
        _cache = redis.GetDatabase();
    }

    public Task<bool> TryLockSeatAsync(string eventId, string seatId, string holderId, TimeSpan ttl)
    {
        var lockKey = $"seat:lock:{eventId}:{seatId}";

        // When.NotExists = Redis's SETNX ("set if not exists") — this single call is atomic,
        // so two simultaneous requests for the same seat can never both succeed.
        return _cache.StringSetAsync(lockKey, holderId, ttl, When.NotExists);
    }

    public Task<long> DecrementAvailableSeatsAsync(string eventId) =>
        _cache.StringDecrementAsync($"event:{eventId}:seats");

    public Task IncrementAvailableSeatsAsync(string eventId) =>
        _cache.StringIncrementAsync($"event:{eventId}:seats");

    public Task ReleaseSeatLockAsync(string eventId, string seatId) =>
        _cache.KeyDeleteAsync($"seat:lock:{eventId}:{seatId}");
}
