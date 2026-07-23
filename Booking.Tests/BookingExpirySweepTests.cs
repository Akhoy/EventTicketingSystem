using Booking.Domain;

namespace Booking.Tests;

// BookingExpiryWorker itself is a BackgroundService that also talks to Redis and needs a
// IServiceScopeFactory — wiring all of that up just to test "does it expire stale bookings" is
// heavy infrastructure ceremony for what is really a domain-level question. So instead, these
// tests exercise the exact repository interaction the worker performs — GetPendingBookingsOlderThan,
// then Expire(), then SaveAsync() — directly against the fake repo. That proves the *logic* is
// correct; testing the worker's Redis side-effects (releasing the seat lock, incrementing the
// counter) would be an integration test, not a unit test, since it needs a real Redis instance.
//
// Important constraint these tests work around: Booking.CreatedAt is set once, to
// DateTime.UtcNow, inside Create() — there's no setter, so a test can't backdate a booking to
// simulate "created 10 minutes ago". That means every booking in these tests has (essentially)
// the same CreatedAt. So instead of trying to mix "stale" and "fresh" bookings in one list and
// filtering by time, each test varies exactly one thing at a time:
//   - one test fixes the cutoff and varies Status (Pending vs Confirmed)
//   - another test fixes Status=Pending and varies the cutoff (future vs past)
public class BookingExpirySweepTests
{
    [Fact]
    public async Task GetPendingBookingsOlderThan_ExcludesConfirmedBookingsEvenIfCutoffMatches()
    {
        // Arrange: both bookings are created "now", and the cutoff is in the future, so both
        // would satisfy the time check — the only thing that should tell them apart is Status.
        var repository = new FakeBookingRepository();

        var pending = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        var confirmed = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A2"), "user-2");
        confirmed.Confirm();

        repository.Seed(pending);
        repository.Seed(confirmed);

        var futureCutoff = DateTime.UtcNow.AddMinutes(5);

        // Act
        var result = await repository.GetPendingBookingsOlderThan(futureCutoff);

        // Assert: only the Pending booking comes back — the expiry sweep must never touch a
        // booking that's already been paid for, no matter how old it is.
        var found = Assert.Single(result);
        Assert.Equal(pending.Id, found.Id);
    }

    [Fact]
    public async Task GetPendingBookingsOlderThan_WithFutureCutoff_ReturnsPendingBooking()
    {
        // A cutoff in the future — the booking was created "now", which is earlier than the
        // cutoff, so CreatedAt < cutoff holds and it counts as stale. This is what happens in
        // production once real time passes 5 minutes past CreatedAt.
        var repository = new FakeBookingRepository();
        var pending = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        repository.Seed(pending);

        var futureCutoff = DateTime.UtcNow.AddMinutes(5);

        var result = await repository.GetPendingBookingsOlderThan(futureCutoff);

        var found = Assert.Single(result);
        Assert.Equal(pending.Id, found.Id);
    }

    [Fact]
    public async Task GetPendingBookingsOlderThan_WithPastCutoff_ReturnsNothing()
    {
        var repository = new FakeBookingRepository();
        var pending = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        repository.Seed(pending);

        // A cutoff in the past — the booking was created "now", which is more recent than the
        // cutoff, so CreatedAt < cutoff is false. Mirrors the real worker leaving a booking
        // alone while it's still inside its 5-minute hold window.
        var pastCutoff = DateTime.UtcNow.AddMinutes(-5);

        var result = await repository.GetPendingBookingsOlderThan(pastCutoff);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExpirySweep_ExpiresStaleBookingsAndPersists()
    {
        // This reproduces the exact three-step sequence BookingExpiryWorker runs each cycle:
        // 1. Find stale Pending bookings.
        // 2. Call Expire() on each (the domain rule).
        // 3. SaveAsync() once, after the loop.
        var repository = new FakeBookingRepository();
        var stalePending = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        repository.Seed(stalePending);

        var cutoff = DateTime.UtcNow.AddMinutes(5);
        var expired = await repository.GetPendingBookingsOlderThan(cutoff);

        foreach (var booking in expired)
            booking.Expire();

        await repository.SaveAsync();

        Assert.Equal("Expired", stalePending.Status);
    }

    [Fact]
    public async Task GetConfirmedUnpublishedBookings_ReturnsOnlyConfirmedAndNotYetPublished()
    {
        // Mirrors what OutboxRelayWorker looks for: bookings that are paid (Confirmed) but
        // haven't been relayed to RabbitMQ yet (PublishedAt is still null).
        var repository = new FakeBookingRepository();

        var confirmedUnpublished = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        confirmedUnpublished.Confirm();

        var confirmedAndPublished = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A2"), "user-2");
        confirmedAndPublished.Confirm();
        confirmedAndPublished.MarkPublished();

        var stillPending = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A3"), "user-3");

        repository.Seed(confirmedUnpublished);
        repository.Seed(confirmedAndPublished);
        repository.Seed(stillPending);

        var result = await repository.GetConfirmedUnpublishedBookings();

        var found = Assert.Single(result);
        Assert.Equal(confirmedUnpublished.Id, found.Id);
    }
}
