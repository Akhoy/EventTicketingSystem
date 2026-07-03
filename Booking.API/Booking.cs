namespace Booking.API;

// Aggregate root for a single seat booking. All business rules live here, not in Program.cs.
public class Booking
{
    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string SeatId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    // EF Core uses reflection to call this when reconstructing objects from DB rows.
    // Private so all application code must go through Create() instead.
    private Booking() { }

    // The only way to create a Booking — enforces every booking starts as Pending.
    public static Booking Create(string eventId, string seatId, string userId)
    {
        return new Booking
        {
            Id        = Guid.NewGuid(),
            EventId   = eventId,
            SeatId    = seatId,
            UserId    = userId,
            Status    = "Pending",
            CreatedAt = DateTime.UtcNow
        };
    }

    // Idempotent — confirming an already-confirmed booking is safe to ignore.
    // Throws only if the booking is Expired, which indicates a genuine problem.
    public void Confirm()
    {
        if (Status == "Confirmed") return;
        if (Status != "Pending")
            throw new InvalidOperationException($"Cannot confirm a {Status} booking.");
        Status = "Confirmed";
    }

    // Silent no-op if already expired or confirmed — expiry worker must never crash on a stale row.
    public void Expire()
    {
        if (Status != "Pending") return;
        Status = "Expired";
    }

    // Called by OutboxRelayWorker after successfully publishing to RabbitMQ.
    public void MarkPublished()
    {
        PublishedAt = DateTime.UtcNow;
    }
}
