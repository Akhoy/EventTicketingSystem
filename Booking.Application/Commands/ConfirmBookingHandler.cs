using Booking.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Booking.Application.Commands;

public class ConfirmBookingHandler : IRequestHandler<ConfirmBookingCommand, ConfirmBookingResult>
{
    private readonly IBookingRepository _repository;
    private readonly ILogger<ConfirmBookingHandler> _logger;

    public ConfirmBookingHandler(IBookingRepository repository, ILogger<ConfirmBookingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ConfirmBookingResult> Handle(ConfirmBookingCommand command, CancellationToken cancellationToken)
    {
        var booking = await _repository.GetByIdAsync(command.BookingId);
        if (booking is null)
            return new ConfirmBookingNotFound("Booking not found.");

        // THE IDOR FIX: before this existed, /payments/{bookingId}/confirm would confirm
        // *any* bookingId handed to it — no check that the caller was the person who made the
        // booking. Anyone who could see or guess a GUID (they're logged, they appear in URLs,
        // they leak in referrer headers, etc.) could confirm someone else's booking. This
        // ownership check is the entire fix: reject with 403 Forbidden if the authenticated
        // caller's identity doesn't match the booking's owner, before any state changes.
        if (booking.UserId != command.RequestingUserId)
        {
            _logger.LogWarning(
                "IDOR attempt blocked — user {RequestingUserId} tried to confirm booking {BookingId} owned by {OwnerUserId}",
                command.RequestingUserId, command.BookingId, booking.UserId);
            return new ConfirmBookingForbidden("You do not have permission to confirm this booking.");
        }

        try
        {
            // Confirm() enforces the business rule — throws if booking is Expired.
            // SaveAsync persists the state change. SaveChangesAsync wraps in an implicit
            // transaction, so a failure here leaves the booking unchanged in the database.
            booking.Confirm();
            await _repository.SaveAsync();
        }
        catch (InvalidOperationException ex)
        {
            return new ConfirmBookingConflict(ex.Message);
        }

        _logger.LogInformation("Payment confirmed — Seat: {SeatId}, BookingId: {BookingId}", booking.SeatId, command.BookingId);
        return new ConfirmBookingSucceeded("Payment confirmed. Your booking is being processed.");
    }
}
