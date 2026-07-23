using MediatR;

namespace Booking.Application.Commands;

// IRequest<CreateBookingResult> tells MediatR: "sending this returns a CreateBookingResult".
// It's a record (immutable data holder) because a command is just a description of intent —
// "create a booking for this seat/event/user" — carrying no behaviour of its own.
public record CreateBookingCommand(string EventId, string SeatId, string UserId)
    : IRequest<CreateBookingResult>;

// A small result hierarchy instead of throwing exceptions for expected outcomes (seat taken,
// event sold out). Exceptions should be reserved for genuinely unexpected failures — "the seat
// is already booked" is a normal, everyday outcome the endpoint needs to turn into a 409, not a
// 500. The `abstract record` + subtypes pattern lets the endpoint pattern-match on which
// outcome happened and pick the right HTTP response for each.
public abstract record CreateBookingResult;
public record CreateBookingSucceeded(Guid BookingId, string UserId, string Message) : CreateBookingResult;
public record CreateBookingSeatTaken(string Message) : CreateBookingResult;
public record CreateBookingSoldOut(string Message) : CreateBookingResult;
public record CreateBookingPersistFailed(string Message) : CreateBookingResult;
