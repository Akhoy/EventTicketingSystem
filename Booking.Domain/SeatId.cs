namespace Booking.Domain;

// Value Object: a type defined by its value, not an identity. Two instances
// with the same Value are interchangeable — there's no "which one is which."
// record struct gives us value equality + ToString for free, backed by a
// struct so copies are just values, not heap objects.
public readonly record struct SeatId
{
    public string Value { get; }

    public SeatId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SeatId cannot be null or empty.", nameof(value));

        Value = value;
    }

    public override string ToString() => Value;
}
