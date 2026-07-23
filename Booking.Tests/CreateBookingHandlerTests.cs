using Booking.Application.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.Tests;

// Tests CreateBookingHandler (Booking.Application) using the fake seat-reservation service and
// fake repository instead of real Redis/SQL Server — this is the MediatR handler that used to
// be inline lambda code in Program.cs before the Day 6 CQRS refactor.
public class CreateBookingHandlerTests
{
    // NullLogger<T> is a do-nothing ILogger implementation from
    // Microsoft.Extensions.Logging.Abstractions — used here because these tests only care about
    // the handler's return value, not what it logs.
    private static CreateBookingHandler CreateHandler(FakeSeatReservationService seats, FakeBookingRepository repository) =>
        new(seats, repository, NullLogger<CreateBookingHandler>.Instance);

    [Fact]
    public async Task Handle_WithAvailableSeat_ReturnsSucceeded()
    {
        var seats = new FakeSeatReservationService();
        seats.SeedAvailableSeats("evt-1", 10);
        var repository = new FakeBookingRepository();
        var handler = CreateHandler(seats, repository);

        var result = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "alice"), CancellationToken.None);

        var succeeded = Assert.IsType<CreateBookingSucceeded>(result);
        Assert.Equal("alice", succeeded.UserId);
    }

    [Fact]
    public async Task Handle_WhenSeatAlreadyLocked_ReturnsSeatTaken()
    {
        var seats = new FakeSeatReservationService();
        seats.SeedAvailableSeats("evt-1", 10);
        seats.SeedLock("evt-1", "A1", "someone-else"); // simulates a concurrent request winning the lock first
        var repository = new FakeBookingRepository();
        var handler = CreateHandler(seats, repository);

        var result = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "alice"), CancellationToken.None);

        Assert.IsType<CreateBookingSeatTaken>(result);
    }

    [Fact]
    public async Task Handle_WhenEventSoldOut_ReturnsSoldOutAndRollsBackLock()
    {
        var seats = new FakeSeatReservationService();
        seats.SeedAvailableSeats("evt-1", 0); // no seats left — the decrement will go negative
        var repository = new FakeBookingRepository();
        var handler = CreateHandler(seats, repository);

        var result = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "alice"), CancellationToken.None);

        Assert.IsType<CreateBookingSoldOut>(result);

        // Compensating transaction check: the lock this handler took before discovering the
        // event was sold out must be released, otherwise seat A1 would be stuck locked forever
        // even though no booking was actually created for it.
        var secondAttempt = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "bob"), CancellationToken.None);
        Assert.IsType<CreateBookingSoldOut>(secondAttempt); // still sold out (by seat count), but NOT "seat taken"
    }

    [Fact]
    public async Task Handle_WhenPersistFails_ReturnsPersistFailedAndRollsBackRedis()
    {
        var seats = new FakeSeatReservationService();
        seats.SeedAvailableSeats("evt-1", 10);
        var repository = new FakeBookingRepository { ThrowOnAdd = true }; // simulates a SQL Server failure
        var handler = CreateHandler(seats, repository);

        var result = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "alice"), CancellationToken.None);

        Assert.IsType<CreateBookingPersistFailed>(result);

        // Compensating transaction check: since no booking was ever created, the seat lock must
        // have been released — a fresh attempt should be able to take it again (not "seat taken").
        repository.ThrowOnAdd = false;
        var retryResult = await handler.Handle(new CreateBookingCommand("evt-1", "A1", "bob"), CancellationToken.None);
        Assert.IsType<CreateBookingSucceeded>(retryResult);
    }
}
