namespace Booking.Domain;

// Domain Event — a record of something that already happened. Past tense,
// named after the fact. Raised by Booking.Confirm(), read and dispatched by
// BookingRepository.SaveAsync() only after SaveChangesAsync() succeeds.
public record BookingConfirmed(Guid BookingId);
