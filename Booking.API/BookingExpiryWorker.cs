using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Booking.API;

// Compensation for abandoned checkouts.
// /book decrements the Redis capacity counter when a booking is created (Pending).
// If the user never pays, that seat would be lost forever — the counter has no TTL
// like the seat lock does. This worker polls for Pending bookings older than the hold
// window and releases them: gives the capacity unit back, frees the seat lock, and
// marks the booking Expired so it is never published or released twice.
public class BookingExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BookingExpiryWorker> _logger;

    // Must match the seat-lock TTL in Program.cs — the seat is held for this long.
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(5);

    public BookingExpiryWorker(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis, ILogger<BookingExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
                var cache = _redis.GetDatabase();

                var cutoff = DateTime.UtcNow - HoldWindow;
                var expired = await db.Bookings
                    .Where(b => b.Status == "Pending" && b.CreatedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var booking in expired)
                {
                    // Give the capacity unit back and release the specific seat hold.
                    // KeyDelete is idempotent — the lock's TTL may have already expired it.
                    await cache.StringIncrementAsync($"event:{booking.EventId}:seats");
                    await cache.KeyDeleteAsync($"seat:lock:{booking.EventId}:{booking.SeatId}");

                    booking.Status = "Expired";
                    _logger.LogInformation("Expired abandoned booking — Event: {EventId}, Seat: {SeatId}, BookingId: {BookingId}",
                        booking.EventId, booking.SeatId, booking.Id);
                }

                if (expired.Count > 0)
                    await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Booking expiry sweep failed, will retry");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
