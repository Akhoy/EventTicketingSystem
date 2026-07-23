using Booking.API;
using Booking.Application.Commands;
using Booking.Domain;
using Booking.Infrastructure;
using MediatR;
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

// Scans the Booking.Application assembly for every class implementing IRequestHandler<T,TResult>
// (CreateBookingHandler, ConfirmBookingHandler, ...) and registers each one in DI automatically.
// After this, IMediator.Send(command) finds the matching handler without any manual wiring here.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateBookingCommand).Assembly));

builder.Services.AddHostedService<OutboxRelayWorker>();
builder.Services.AddHostedService<BookingExpiryWorker>();

var app = builder.Build();

// Endpoints are now thin: build a command describing the intent, hand it to IMediator, and
// translate whichever typed result comes back into the matching HTTP response. All the actual
// logic (Redis locking, counter math, persistence, rollback) lives in CreateBookingHandler,
// inside Booking.Application — the endpoint itself no longer knows Redis or EF Core exist.
app.MapPost("/book/{eventId}/{seatId}", async (
    string eventId,
    string seatId,
    IMediator mediator) =>
{
    // A real userId will come from the caller's JWT once auth is added — for now this keeps
    // the same "no login yet" placeholder behaviour the endpoint always had.
    var userId = Guid.NewGuid().ToString();

    var result = await mediator.Send(new CreateBookingCommand(eventId, seatId, userId));

    return result switch
    {
        CreateBookingSucceeded s => Results.Ok(new { bookingId = s.BookingId, userId = s.UserId, message = s.Message }),
        CreateBookingSeatTaken s => Results.Conflict(new { message = s.Message }),
        CreateBookingSoldOut s => Results.Conflict(new { message = s.Message }),
        CreateBookingPersistFailed s => Results.Problem(s.Message),
        _ => Results.Problem("Unexpected result.")
    };
});

app.MapPost("/payments/{bookingId}/confirm", async (
    Guid bookingId,
    IMediator mediator) =>
{
    // Same placeholder as above until JWT auth is wired in — at that point this becomes the
    // authenticated caller's "sub" claim instead of a query-string/header value the caller
    // could just as easily fake, which is what makes the IDOR check in ConfirmBookingHandler
    // actually mean something.
    var requestingUserId = Guid.NewGuid().ToString();

    var result = await mediator.Send(new ConfirmBookingCommand(bookingId, requestingUserId));

    return result switch
    {
        ConfirmBookingSucceeded s => Results.Ok(new { message = s.Message }),
        ConfirmBookingNotFound s => Results.NotFound(new { message = s.Message }),
        ConfirmBookingForbidden s => Results.Json(new { message = s.Message }, statusCode: StatusCodes.Status403Forbidden),
        ConfirmBookingConflict s => Results.Conflict(new { message = s.Message }),
        _ => Results.Problem("Unexpected result.")
    };
});

app.Run();
