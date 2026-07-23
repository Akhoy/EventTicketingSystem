using Booking.Domain;

namespace Booking.Tests;

// SeatId and EventId are Value Objects — thin wrappers around a string instead of passing raw
// strings around everywhere. The whole point of wrapping a string is that the wrapper's
// constructor becomes the one and only place validation happens, so an invalid EventId/SeatId
// can never exist anywhere else in the system. These tests exist to prove that guarantee holds.
public class ValueObjectTests
{
    // [Fact] marks a single, fixed test case with no parameters — xUnit runs this method body
    // exactly once. Use [Fact] whenever there's just one scenario to check (as opposed to
    // [Theory] below, which runs the same body multiple times with different inputs).
    [Fact]
    public void EventId_WithValidValue_StoresValue()
    {
        var eventId = new EventId("evt-42");

        Assert.Equal("evt-42", eventId.Value);
    }

    // [Theory] is xUnit's version of a "parameterized test" — instead of one fixed test body
    // like [Fact], it's a template method that runs once per [InlineData(...)] attribute stacked
    // above it. Each [InlineData(x)] supplies the value(s) passed into the method's parameter(s),
    // in order. Below, xUnit runs this exact method body 3 separate times:
    //   run 1: invalidValue = null
    //   run 2: invalidValue = ""
    //   run 3: invalidValue = "   "
    // Each run is reported as its own pass/fail in the test output, so if only the whitespace
    // case broke, you'd see that specific run fail — not a vague single failure. This is purely
    // to avoid writing three near-identical [Fact] methods that differ by one input value.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EventId_WithNullEmptyOrWhitespace_Throws(string? invalidValue)
    {
        // Act + Assert combined: Assert.Throws runs the lambda and fails the test if the
        // expected exception type is NOT thrown (e.g. if validation were accidentally removed).
        Assert.Throws<ArgumentException>(() => new EventId(invalidValue!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SeatId_WithNullEmptyOrWhitespace_Throws(string? invalidValue)
    {
        Assert.Throws<ArgumentException>(() => new SeatId(invalidValue!));
    }

    [Fact]
    public void EventId_TwoInstancesWithSameValue_AreEqual()
    {
        // EventId is declared as `readonly record struct` in Booking.Domain. The "record" part
        // gives value equality for free: two EventId instances wrapping the same underlying
        // string are considered equal, even though they're two separate objects in memory.
        // (Contrast with a plain class, where two instances are only equal if they're the exact
        // same object reference.) This matters because EF Core and dictionary/collection lookups
        // rely on this value-based equality to treat two EventId("evt-1") as "the same event".
        var a = new EventId("evt-1");
        var b = new EventId("evt-1");

        Assert.Equal(a, b);
    }
}
