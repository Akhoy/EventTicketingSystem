namespace Catalog.Features.SyncBooking;

// DTO (Data Transfer Object) — the message contract consumed from the "ticket_events" fanout.
// This is the consumer's copy of the shape Booking.API publishes; it carries only the wire
// fields, not Catalog's own CatalogItem entity. Kept in sync by hand with Booking.API's copy:
// if a field is renamed there, rename it here too or deserialization binds it to null.
public record BookingEvent(string EventId, string SeatId, string UserId);
