using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Booking.API;

// Runs in the background continuously, polling the Bookings table every 5 seconds.
// Its only job: find confirmed-but-not-yet-published bookings and push them to RabbitMQ.
// This decouples the HTTP response from the RabbitMQ publish — if RabbitMQ is down,
// the booking is already safely in SQL and the relay will retry until it succeeds.
public class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayWorker> _logger;
    private readonly IConfiguration _configuration;

    public OutboxRelayWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

                // Status == "Confirmed" — payment is done, safe to hand off to the worker.
                // PublishedAt == null  — not yet pushed to RabbitMQ, so the relay still has work to do.
                // "Pending" bookings are intentionally excluded — seat is reserved but user hasn't paid yet.
                var unpublished = await db.Bookings
                    .Where(b => b.Status == "Confirmed" && b.PublishedAt == null)
                    .ToListAsync(stoppingToken);

                if (unpublished.Count > 0)
                {
                    var factory = new ConnectionFactory { Uri = new Uri(_configuration.GetConnectionString("RabbitMQ")!) };
                    using var connection = await factory.CreateConnectionAsync(stoppingToken);
                    using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                    // Fanout exchange — RabbitMQ copies the message to every bound queue.
                    // New consumers (notifications, analytics) just bind their own queue here
                    // without any change to this publisher.
                    await channel.ExchangeDeclareAsync("ticket_events", ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

                    foreach (var booking in unpublished)
                    {
                        // Serialize the typed DTO (not an anonymous object) so the wire contract is explicit.
                        var payload = JsonSerializer.Serialize(new BookingEvent(booking.EventId, booking.SeatId, booking.UserId));

                        // Persistent = true ensures the message survives a RabbitMQ broker restart.
                        var props = new BasicProperties { Persistent = true };
                        await channel.BasicPublishAsync(
                            exchange: "ticket_events",
                            routingKey: string.Empty, // ignored by fanout exchanges
                            mandatory: false,
                            basicProperties: props,
                            body: Encoding.UTF8.GetBytes(payload),
                            cancellationToken: stoppingToken);

                        // Mark published so the next poll cycle skips this booking.
                        booking.MarkPublished();
                        _logger.LogInformation("Relayed booking to ticket_events — Seat: {SeatId}, User: {UserId}", booking.SeatId, booking.UserId);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // RabbitMQ down or SQL error — log and retry next cycle. Nothing is lost.
                _logger.LogError(ex, "Outbox relay failed, will retry in 5s");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
