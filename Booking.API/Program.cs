using Booking.API;
using Booking.Domain;
using Booking.Infrastructure;
using StackExchange.Redis;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    var seqUrl = builder.Configuration["SeqUrl"];
    configuration
        .MinimumLevel.Information()
        .WriteTo.Console();
    if (!string.IsNullOrEmpty(seqUrl))
        configuration.WriteTo.Seq(seqUrl);
});

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var redis = ConnectionMultiplexer.Connect(redisConnectionString!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Single call wires up BookingDbContext + IBookingRepository.
// Program.cs no longer references EF Core or DbContext directly — Infrastructure owns those details.
builder.Services.AddBookingInfrastructure(
    builder.Configuration.GetConnectionString("SqlServer")!);

builder.Services.AddHostedService<OutboxRelayWorker>();
builder.Services.AddHostedService<BookingExpiryWorker>();

var app = builder.Build();

app.MapPost("/book/{eventId}/{seatId}", async (
    string eventId,
    string seatId,
    IConnectionMultiplexer redis,
    IBookingRepository repository,
    ILogger<Program> logger) =>
{
    var cache = redis.GetDatabase();
    var userId = Guid.NewGuid().ToString();

    var lockKey = $"seat:lock:{eventId}:{seatId}";
    var seatsKey = $"event:{eventId}:seats";

    bool locked = await cache.StringSetAsync(lockKey, userId, TimeSpan.FromMinutes(5), When.NotExists);
    if (!locked)
    {
        logger.LogWarning("Seat {SeatId} for event {EventId} is already reserved", seatId, eventId);
        return Results.Conflict(new { message = $"Seat {seatId} is currently reserved by someone else." });
    }

    long remaining = await cache.StringDecrementAsync(seatsKey);
    if (remaining < 0)
    {
        await cache.StringIncrementAsync(seatsKey);
        await cache.KeyDeleteAsync(lockKey);
        logger.LogWarning("Event {EventId} is sold out", eventId);
        return Results.Conflict(new { message = $"Event {eventId} is sold out." });
    }

    var booking = Booking.Domain.Booking.Create(eventId, seatId, userId);

    try
    {
        // AddAsync persists the booking — if SQL fails here, we roll back both Redis guards
        // so the seat counter and lock are restored and the seat isn't permanently lost.
        await repository.AddAsync(booking);
    }
    catch (Exception ex)
    {
        await cache.StringIncrementAsync(seatsKey);
        await cache.KeyDeleteAsync(lockKey);
        logger.LogError(ex, "Failed to persist booking — rolled back Redis counter and seat lock. Event: {EventId}, Seat: {SeatId}", eventId, seatId);
        return Results.Problem("Booking could not be saved. Please try again.");
    }

    logger.LogInformation("Booking created (Pending) — Event: {EventId}, Seat: {SeatId}, BookingId: {BookingId}", eventId, seatId, booking.Id);
    return Results.Ok(new { bookingId = booking.Id, userId, message = $"Seat {seatId} reserved. Proceed to payment." });
});

app.MapPost("/payments/{bookingId}/confirm", async (
    Guid bookingId,
    IBookingRepository repository,
    ILogger<Program> logger) =>
{
    var booking = await repository.GetByIdAsync(bookingId);
    if (booking is null)
        return Results.NotFound(new { message = "Booking not found." });

    try
    {
        // Confirm() enforces the business rule — throws if booking is Expired.
        // SaveAsync persists the state change. SaveChangesAsync wraps in an implicit transaction,
        // so a failure here leaves the booking unchanged in the database.
        booking.Confirm();
        await repository.SaveAsync();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }

    logger.LogInformation("Payment confirmed — Seat: {SeatId}, BookingId: {BookingId}", booking.SeatId, bookingId);
    return Results.Ok(new { message = "Payment confirmed. Your booking is being processed." });
});

app.Run();
