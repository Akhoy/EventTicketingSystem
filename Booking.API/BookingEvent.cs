namespace Booking.API;

// DTO (Data Transfer Object) — the message contract published to the "ticket_events" fanout.
// Carries only the fields that cross the wire to other services, NOT the full Booking entity
// (no Status/CreatedAt/PublishedAt). Catalog.API holds a matching copy and deserializes into it.
// The two copies are kept in sync by hand: if you change a field here, change it there too,
// otherwise JSON deserialization on the consumer binds the renamed field to null.
public record BookingEvent(string EventId, string SeatId, string UserId);
