using Booking.Application.Commands;
using Booking.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.Tests;

// Tests ConfirmBookingHandler — specifically the IDOR (Insecure Direct Object Reference) fix.
// Before this check existed, /payments/{bookingId}/confirm would confirm any bookingId handed
// to it, regardless of who was asking. These tests prove the ownership check actually blocks
// a mismatched caller and actually allows a matching one. Note: RequestingUserId here is just a
// plain string — these tests don't touch JWT at all, they only prove the ownership-comparison
// logic itself is correct. Program.cs is what's responsible for making sure that string is a
// real, verified identity (from the caller's JWT) rather than something forgeable.
public class ConfirmBookingHandlerTests
{
    private static ConfirmBookingHandler CreateHandler(FakeBookingRepository repository) =>
        new(repository, NullLogger<ConfirmBookingHandler>.Instance);

    [Fact]
    public async Task Handle_WhenRequestingUserIsTheOwner_Succeeds()
    {
        var repository = new FakeBookingRepository();
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "alice");
        repository.Seed(booking);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ConfirmBookingCommand(booking.Id, "alice"), CancellationToken.None);

        Assert.IsType<ConfirmBookingSucceeded>(result);
        Assert.Equal("Confirmed", booking.Status);
    }

    [Fact]
    public async Task Handle_WhenRequestingUserIsNotTheOwner_ReturnsForbiddenAndDoesNotConfirm()
    {
        // This is the actual IDOR attack scenario: "mallory" knows/guesses alice's bookingId
        // (they're plain GUIDs that appear in URLs/logs) and tries to confirm it as if it were
        // her own booking.
        var repository = new FakeBookingRepository();
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "alice");
        repository.Seed(booking);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ConfirmBookingCommand(booking.Id, "mallory"), CancellationToken.None);

        Assert.IsType<ConfirmBookingForbidden>(result);
        // Just as important as returning Forbidden: the booking's state must be untouched —
        // an attacker who gets rejected must not have caused any side effect at all.
        Assert.Equal("Pending", booking.Status);
    }

    [Fact]
    public async Task Handle_WhenBookingDoesNotExist_ReturnsNotFound()
    {
        var repository = new FakeBookingRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ConfirmBookingCommand(Guid.NewGuid(), "alice"), CancellationToken.None);

        Assert.IsType<ConfirmBookingNotFound>(result);
    }

    [Fact]
    public async Task Handle_WhenBookingIsExpired_ReturnsConflictEvenForTheRealOwner()
    {
        // The IDOR check passes here (alice IS the owner) — this proves ownership and business
        // rules are two independent checks: passing one doesn't bypass the other.
        var repository = new FakeBookingRepository();
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "alice");
        booking.Expire();
        repository.Seed(booking);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ConfirmBookingCommand(booking.Id, "alice"), CancellationToken.None);

        Assert.IsType<ConfirmBookingConflict>(result);
    }
}
