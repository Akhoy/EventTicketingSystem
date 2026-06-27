namespace Booking.API;

public class Booking
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string SeatId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
}
