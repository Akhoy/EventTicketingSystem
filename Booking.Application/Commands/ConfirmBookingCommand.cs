using MediatR;

namespace Booking.Application.Commands;

// RequestingUserId is the caller's identity, taken from their verified JWT (the "sub" claim) —
// never from a URL parameter or request body the caller could freely edit. Comparing this
// against the booking's own UserId is the IDOR fix: it proves the caller who is confirming
// payment is the same person who created the booking, not just someone who guessed a GUID.
public record ConfirmBookingCommand(Guid BookingId, string RequestingUserId)
    : IRequest<ConfirmBookingResult>;

public abstract record ConfirmBookingResult;
public record ConfirmBookingSucceeded(string Message) : ConfirmBookingResult;
public record ConfirmBookingNotFound(string Message) : ConfirmBookingResult;
public record ConfirmBookingForbidden(string Message) : ConfirmBookingResult;
public record ConfirmBookingConflict(string Message) : ConfirmBookingResult;
