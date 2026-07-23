namespace Booking.Application;

// A "port" (in ports-and-adapters/hexagonal terms) — Application defines WHAT it needs to
// reserve a seat, without knowing HOW that's implemented. Booking.Infrastructure provides the
// real answer (Redis), and Booking.Tests could provide a fake in-memory answer, without either
// of them changing this interface. Same Dependency Inversion idea as IBookingRepository —
// applied here to Redis instead of SQL Server.
public interface ISeatReservationService
{
    // Atomically claims a seat for `holderId` for `ttl`. Returns false if someone already holds
    // it. Backed by Redis SETNX (SET ... NX) in the real implementation, which is atomic —
    // two concurrent requests can never both succeed for the same seat.
    Task<bool> TryLockSeatAsync(string eventId, string seatId, string holderId, TimeSpan ttl);

    // Atomically decrements the event's remaining-seats counter and returns the new value.
    // A negative result means the event is sold out and the caller must roll back.
    Task<long> DecrementAvailableSeatsAsync(string eventId);

    // Undoes DecrementAvailableSeatsAsync — used when a booking attempt fails after the
    // counter was already decremented (e.g. the database write failed).
    Task IncrementAvailableSeatsAsync(string eventId);

    // Releases a seat lock early (e.g. on rollback), instead of waiting for its TTL to expire.
    Task ReleaseSeatLockAsync(string eventId, string seatId);
}
