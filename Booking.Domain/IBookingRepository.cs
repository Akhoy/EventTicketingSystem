namespace Booking.Domain;

// Lives in Domain, not Infrastructure — the domain defines WHAT it needs (a way to save/retrieve bookings).
// It doesn't know HOW it's stored. EF Core is the Infrastructure's concern, not the domain's.
// Dependency direction: Infrastructure depends on Domain, never the other way around.
public interface IBookingRepository
{
    // Used by /book endpoint — creates a new booking record.
    Task AddAsync(Booking booking);

    // Used by workers and /confirm endpoint — persists state changes already made on the booking object.
    // The booking's business method (Confirm/Expire/MarkPublished) is called first, then SaveAsync persists it.
    // No parameter — EF Core tracks all changes on the DbContext; SaveChangesAsync saves everything at once.
    Task SaveAsync();

    // Used by /confirm endpoint — looks up a booking by its ID before confirming payment.
    Task<Booking?> GetByIdAsync(Guid id);

    // Used by BookingExpiryWorker — finds Pending bookings older than the hold window.
    // Cutoff is passed in because the worker decides the hold duration, not the repository.
    Task<List<Booking>> GetPendingBookingsOlderThan(DateTime cutoff);

    // Used by OutboxRelayWorker — finds bookings that are Confirmed but not yet published to RabbitMQ.
    Task<List<Booking>> GetConfirmedUnpublishedBookings();
}
