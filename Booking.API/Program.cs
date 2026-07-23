using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Booking.API;
using Booking.Application.Commands;
using Booking.Domain;
using Booking.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// JWT settings — same three values used both here (to check tokens) and in the /auth/token
// endpoint below (to create them). In a real system SigningKey would come from a secrets
// manager, not appsettings.json; it's plaintext here only because this is a study project.
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core silently renames the standard "sub" claim to the older
        // ClaimTypes.NameIdentifier URI claim (legacy WIF/WS-Federation compatibility) when it
        // builds the ClaimsPrincipal — so looking up JwtRegisteredClaimNames.Sub later (in
        // /book and /confirm) would find nothing and silently return null, even though the
        // token genuinely contains a "sub" claim. false keeps claim names as they are in the token.
        options.MapInboundClaims = false;

        // TokenValidationParameters is the rulebook the middleware checks every incoming
        // token against: was it signed with our key (not forged), does it claim to be from
        // our Issuer, is it meant for our Audience, and has it not expired yet.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Stand-in for a real login system — there's no password check here at all. Its only job is
// to prove the rest of the pipeline (issue a token -> caller sends it -> middleware validates
// it -> endpoint reads the caller's identity from it) actually works end to end. A real system
// would replace this with a proper identity provider (its own service, or something like
// Auth0/Keycloak/Entra ID) that verifies credentials before ever handing out a token.
app.MapPost("/auth/token", (string userId) =>
{
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) };

    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: credentials);

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
});

// Endpoints are now thin: build a command describing the intent, hand it to IMediator, and
// translate whichever typed result comes back into the matching HTTP response. All the actual
// logic (Redis locking, counter math, persistence, rollback) lives in CreateBookingHandler,
// inside Booking.Application — the endpoint itself no longer knows Redis or EF Core exist.
app.MapPost("/book/{eventId}/{seatId}", async (
    string eventId,
    string seatId,
    ClaimsPrincipal caller,
    IMediator mediator) =>
{
    // Minimal APIs bind ClaimsPrincipal straight from HttpContext.User — this is the caller's
    // identity as proven by their JWT, not something the caller can put in a URL or body
    // themselves. "Sub" (subject) is the standard JWT claim for "who this token is about".
    // RequireAuthorization() below guarantees this endpoint never runs without a valid token,
    // so caller.FindFirstValue(...) is never null here.
    var userId = caller.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    var result = await mediator.Send(new CreateBookingCommand(eventId, seatId, userId));

    return result switch
    {
        CreateBookingSucceeded s => Results.Ok(new { bookingId = s.BookingId, userId = s.UserId, message = s.Message }),
        CreateBookingSeatTaken s => Results.Conflict(new { message = s.Message }),
        CreateBookingSoldOut s => Results.Conflict(new { message = s.Message }),
        CreateBookingPersistFailed s => Results.Problem(s.Message),
        _ => Results.Problem("Unexpected result.")
    };
}).RequireAuthorization();

app.MapPost("/payments/{bookingId}/confirm", async (
    Guid bookingId,
    ClaimsPrincipal caller,
    IMediator mediator) =>
{
    // This is the IDOR fix in action: requestingUserId now comes from the caller's verified
    // token, not a value they could type into a URL/body themselves. ConfirmBookingHandler
    // compares this against the booking's actual owner before allowing the confirm.
    var requestingUserId = caller.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    var result = await mediator.Send(new ConfirmBookingCommand(bookingId, requestingUserId));

    return result switch
    {
        ConfirmBookingSucceeded s => Results.Ok(new { message = s.Message }),
        ConfirmBookingNotFound s => Results.NotFound(new { message = s.Message }),
        ConfirmBookingForbidden s => Results.Json(new { message = s.Message }, statusCode: StatusCodes.Status403Forbidden),
        ConfirmBookingConflict s => Results.Conflict(new { message = s.Message }),
        _ => Results.Problem("Unexpected result.")
    };
}).RequireAuthorization();

app.Run();
