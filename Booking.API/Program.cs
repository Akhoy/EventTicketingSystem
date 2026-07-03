using Booking.API;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    var seqUrl = builder.Configuration["SeqUrl"];
    configuration
        .MinimumLevel.Information()
        .WriteTo.Console();
    // SeqUrl is only present when running in Docker (set via docker-compose env var).
    // Guard here so the app still starts locally or during EF CLI design-time bootstrap.
    if (!string.IsNullOrEmpty(seqUrl))
        configuration.WriteTo.Seq(seqUrl);
});

// Redis — fast concurrency guard to prevent two users booking the same seat simultaneously.
// abortConnect=false in the connection string in docker-composemeans failed connections retry in the background
// instead of throwing on startup — important for local dev and EF CLI design-time bootstrap
// where Redis may not be running.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var redis = ConnectionMultiplexer.Connect(redisConnectionString!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// BookingDb — owned exclusively by this service, no other service touches these tables
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));

// Relay runs in background, publishing confirmed bookings to RabbitMQ every 5 seconds
builder.Services.AddHostedService<OutboxRelayWorker>();

// Sweeps abandoned Pending bookings and returns their held seats to the pool
builder.Services.AddHostedService<BookingExpiryWorker>();

var app = builder.Build();

// Step 1: User selects a specific seat for a specific event.
// Two independent Redis guards, then writes Booking(Pending) to SQL. No RabbitMQ publish yet.
//   1. Seat lock  (SETNX) — is THIS seat already held? Prevents double-booking one seat.
//   2. Capacity   (DECR)  — are there ANY seats left for the event? Prevents overselling.
// Returns bookingId so the client can reference it after payment completes.
app.MapPost("/book/{eventId}/{seatId}", async (string eventId, string seatId, IConnectionMultiplexer redis, BookingDbContext db, ILogger<Program> logger) =>
{
    var cache = redis.GetDatabase();
    var userId = Guid.NewGuid().ToString();

    // Lock key is namespaced by event — seat "A1" exists in many events, so the key must include both.
    var lockKey = $"seat:lock:{eventId}:{seatId}";
    var seatsKey = $"event:{eventId}:seats";

    // Guard 1 — hold this specific seat. TTL auto-releases the hold if checkout is abandoned.
    bool locked = await cache.StringSetAsync(lockKey, userId, TimeSpan.FromMinutes(5), When.NotExists);
    if (!locked)
    {
        logger.LogWarning("Seat {SeatId} for event {EventId} is already reserved", seatId, eventId);
        return Results.Conflict(new { message = $"Seat {seatId} is currently reserved by someone else." });
    }

    // Guard 2 — atomically claim one unit of capacity. DECR returns the new value;
    // if it dropped below 0 the event is sold out, so put the unit back (INCR) and
    // release the seat lock we just took, then reject.
    long remaining = await cache.StringDecrementAsync(seatsKey);
    if (remaining < 0)
    {
        await cache.StringIncrementAsync(seatsKey);
        await cache.KeyDeleteAsync(lockKey);
        logger.LogWarning("Event {EventId} is sold out", eventId);
        return Results.Conflict(new { message = $"Event {eventId} is sold out." });
    }

    var booking = Booking.API.Booking.Create(eventId, seatId, userId);

    try
    {
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        // SQL failed after Redis DECR succeeded — roll back both guards so the seat isn't permanently lost.
        await cache.StringIncrementAsync(seatsKey);
        await cache.KeyDeleteAsync(lockKey);
        logger.LogError(ex, "Failed to persist booking — rolled back Redis counter and seat lock. Event: {EventId}, Seat: {SeatId}", eventId, seatId);
        return Results.Problem("Booking could not be saved. Please try again.");
    }

    logger.LogInformation("Booking created (Pending) — Event: {EventId}, Seat: {SeatId}, BookingId: {BookingId}", eventId, seatId, booking.Id);
    return Results.Ok(new { bookingId = booking.Id, userId, message = $"Seat {seatId} reserved. Proceed to payment." });
});

// Step 2: Payment complete (simulates a Stripe webhook in production).
// Flips booking to Confirmed inside a transaction. The OutboxRelayWorker detects this
// on its next poll and publishes to ticket_orders — no direct RabbitMQ call here,
// so a broker outage cannot silently lose a confirmed booking.
app.MapPost("/payments/{bookingId}/confirm", async (Guid bookingId, BookingDbContext db, ILogger<Program> logger) =>
{
    var booking = await db.Bookings.FindAsync(bookingId);
    if (booking is null)
        return Results.NotFound(new { message = "Booking not found." });
    try
    {
        using var transaction = await db.Database.BeginTransactionAsync();
        booking.Confirm();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }

    logger.LogInformation("Payment confirmed — Seat: {SeatId}, BookingId: {BookingId}", booking.SeatId, bookingId);
    return Results.Ok(new { message = "Payment confirmed. Your booking is being processed." });
});

app.Run();
