using Booking.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Booking.Application.Commands;

// IRequestHandler<TCommand, TResult> is MediatR's contract: "I know how to handle a
// CreateBookingCommand and produce a CreateBookingResult." MediatR finds this class via DI
// (registered by AddMediatR(...) in Program.cs) purely by matching the generic types — nothing
// in Program.cs references CreateBookingHandler by name.
public class CreateBookingHandler : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private readonly ISeatReservationService _seatReservation;
    private readonly IBookingRepository _repository;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        ISeatReservationService seatReservation,
        IBookingRepository repository,
        ILogger<CreateBookingHandler> logger)
    {
        _seatReservation = seatReservation;
        _repository = repository;
        _logger = logger;
    }

    // Same 4-step sequence that used to live inline in Program.cs's /book lambda — moved here
    // unchanged, just wrapped as a MediatR handler instead of a lambda.
    //
    // Why decrement the Redis counter BEFORE persisting to SQL, not after: the Redis decrement
    // is atomic — if many requests race for the last seat, Redis guarantees exactly one of them
    // takes the counter to 0 and every other one goes negative and is rejected. That's the only
    // thing standing between this system and overselling a seat. If we persisted first and
    // decremented second, "check availability" and "save the booking" would be two separate,
    // non-atomic steps — two concurrent requests could both pass a check and both save,
    // overselling the seat. So Redis has to be the single atomic gatekeeper, checked first,
    // before the (slower, per-request) SQL write is even attempted.
    //
    // That means if the SQL write then fails, we've already decremented a counter and locked a
    // seat that no booking actually exists for — so we must undo both. This "if step 2 fails,
    // reverse step 1" is a tiny compensating transaction: the same idea behind the Saga pattern
    // (Day W1) — BookingExpiryWorker is a bigger example of the same idea, compensating for an
    // abandoned checkout instead of a failed database write.
    public async Task<CreateBookingResult> Handle(CreateBookingCommand command, CancellationToken cancellationToken)
    {
        bool locked = await _seatReservation.TryLockSeatAsync(
            command.EventId, command.SeatId, command.UserId, TimeSpan.FromMinutes(5));

        if (!locked)
        {
            _logger.LogWarning("Seat {SeatId} for event {EventId} is already reserved", command.SeatId, command.EventId);
            return new CreateBookingSeatTaken($"Seat {command.SeatId} is currently reserved by someone else.");
        }

        long remaining = await _seatReservation.DecrementAvailableSeatsAsync(command.EventId);
        if (remaining < 0)
        {
            // Compensating step: undo the decrement, release the lock — no booking was ever
            // persisted for this attempt, so there's nothing else to clean up.
            await _seatReservation.IncrementAvailableSeatsAsync(command.EventId);
            await _seatReservation.ReleaseSeatLockAsync(command.EventId, command.SeatId);
            _logger.LogWarning("Event {EventId} is sold out", command.EventId);
            return new CreateBookingSoldOut($"Event {command.EventId} is sold out.");
        }

        // EventId must be qualified here as Domain.EventId — Microsoft.Extensions.Logging (used
        // for _logger above) has its own unrelated EventId type (for tagging log entries), so
        // the compiler can't tell which one "EventId" means without the namespace prefix.
        var booking = Domain.Booking.Create(new Domain.EventId(command.EventId), new SeatId(command.SeatId), command.UserId);

        try
        {
            await _repository.AddAsync(booking);
        }
        catch (Exception ex)
        {
            // Compensating step: the counter was already decremented and the seat already
            // locked, but the booking never made it into SQL — reverse both so the seat isn't
            // silently lost forever.
            await _seatReservation.IncrementAvailableSeatsAsync(command.EventId);
            await _seatReservation.ReleaseSeatLockAsync(command.EventId, command.SeatId);
            _logger.LogError(ex, "Failed to persist booking — rolled back Redis counter and seat lock. Event: {EventId}, Seat: {SeatId}", command.EventId, command.SeatId);
            return new CreateBookingPersistFailed("Booking could not be saved. Please try again.");
        }

        _logger.LogInformation("Booking created (Pending) — Event: {EventId}, Seat: {SeatId}, BookingId: {BookingId}", command.EventId, command.SeatId, booking.Id);
        return new CreateBookingSucceeded(booking.Id, command.UserId, $"Seat {command.SeatId} reserved. Proceed to payment.");
    }
}
