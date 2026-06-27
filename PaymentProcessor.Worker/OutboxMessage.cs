namespace PaymentProcessor.Worker;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty; 
    public string Payload { get; set; } = string.Empty;   
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedOnUtc { get; set; } 
}