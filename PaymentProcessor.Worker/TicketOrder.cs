namespace PaymentProcessor.Worker;

public class TicketOrder
{
    public Guid Id { get; set; }
    public string SeatId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public string Status { get; set; } = "Completed";
}