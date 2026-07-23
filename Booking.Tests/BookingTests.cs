using Booking.Domain;

namespace Booking.Tests;

// Tests the Booking aggregate in isolation — no database, no DI container, no ASP.NET Core
// host. This is only possible because Booking.Domain has zero outward dependencies: it doesn't
// reference EF Core, Redis, or RabbitMQ, so a plain `new`/method-call test is enough to verify
// every business rule. That's the payoff of Clean Architecture that Day 5 was about.
//
// Each test follows AAA: Arrange (build the object), Act (call the one thing under test),
// Assert (check the outcome). Keeping one behaviour per test means a failure tells you exactly
// which rule broke, instead of one giant test that could fail for five different reasons.
public class BookingTests
{
    [Fact]
    public void Create_SetsInitialStateToPending()
    {
        // Arrange + Act: Create() is a static factory — it's the *only* way to construct a
        // Booking (the constructor is private). That's a deliberate domain rule: you can't
        // accidentally build a Booking in a half-formed state, because there's only one path in.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");

        // Assert: every new booking must start life as Pending — nothing should ever be able
        // to construct a booking that's already Confirmed or Expired.
        Assert.Equal("Pending", booking.Status);
        Assert.Equal("evt-1", booking.EventId.Value);
        Assert.Equal("A1", booking.SeatId.Value);
        Assert.NotEqual(Guid.Empty, booking.Id); // Id must be generated, never left default
    }

    [Fact]
    public void Confirm_FromPending_SetsStatusToConfirmed()
    {
        // This is the "happy path" for payment confirmation: a booking sitting in Pending
        // (seat held, payment not yet done) transitions to Confirmed once payment succeeds.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");

        booking.Confirm();

        Assert.Equal("Confirmed", booking.Status);
    }

    [Fact]
    public void Confirm_FromPending_RaisesBookingConfirmedEvent()
    {
        // Domain Events let other parts of the system react to "a booking was confirmed"
        // (e.g. OutboxRelayWorker later publishes this to RabbitMQ) without Booking itself
        // knowing anything about RabbitMQ. Confirm() must record the event on the aggregate
        // so infrastructure code can pick it up after SaveAsync().
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");

        booking.Confirm();

        var domainEvent = Assert.Single(booking.DomainEvents); // exactly one event, no more
        // BookingConfirmed is a positional record: public record BookingConfirmed(Guid BookingId);
        // — the constructor parameter name becomes a generated property automatically.
        var confirmed = Assert.IsType<BookingConfirmed>(domainEvent);
        Assert.Equal(booking.Id, confirmed.BookingId); // event must reference the right booking
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_IsIdempotentAndDoesNotRaiseSecondEvent()
    {
        // Payment webhooks/retries can call /confirm more than once for the same booking
        // (e.g. the payment provider retries a webhook after a slow response). Confirm() must
        // treat a second call as a harmless no-op rather than crashing or double-firing events.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        booking.Confirm();

        booking.Confirm(); // second call — should do nothing

        Assert.Equal("Confirmed", booking.Status);
        Assert.Single(booking.DomainEvents); // still just the one event from the first call
    }

    [Fact]
    public void Confirm_WhenExpired_Throws()
    {
        // If the seat hold already expired (customer took too long to pay), confirming payment
        // afterwards is a genuine problem — the seat may have been given to someone else.
        // This must throw loudly instead of silently marking a stale booking as paid.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        booking.Expire();

        var ex = Assert.Throws<InvalidOperationException>(() => booking.Confirm());
        Assert.Contains("Expired", ex.Message);
    }

    [Fact]
    public void Expire_FromPending_SetsStatusToExpired()
    {
        // This is what BookingExpiryWorker does to an abandoned checkout: no payment arrived
        // within the hold window, so the booking is expired and the seat is released elsewhere.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");

        booking.Expire();

        Assert.Equal("Expired", booking.Status);
    }

    [Fact]
    public void Expire_WhenAlreadyConfirmed_IsSilentNoOp()
    {
        // BookingExpiryWorker sweeps rows by a time cutoff, not by re-checking status first,
        // so it's possible for it to encounter a booking that was confirmed a split second
        // before the sweep ran. Expire() must never downgrade a paid booking back to Expired —
        // that would be a serious bug (a customer who paid loses their seat).
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");
        booking.Confirm();

        booking.Expire();

        Assert.Equal("Confirmed", booking.Status); // unchanged — Expire() silently did nothing
    }

    [Fact]
    public void MarkPublished_SetsPublishedAtTimestamp()
    {
        // OutboxRelayWorker calls this after it successfully publishes the booking to RabbitMQ.
        // PublishedAt is how the relay knows not to publish the same booking twice on its next
        // 5-second poll — it's the "outbox" pattern's dedupe marker.
        var booking = Domain.Booking.Create(new EventId("evt-1"), new SeatId("A1"), "user-1");

        booking.MarkPublished();

        Assert.NotNull(booking.PublishedAt);
    }
}
