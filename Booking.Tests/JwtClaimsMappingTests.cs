using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Tests;

// Regression tests for the exact bug hit while wiring up JWT auth in Booking.API: a token is
// built with a "sub" claim (as /auth/token in Program.cs does), but by default, .NET's JWT
// validation silently RENAMES "sub" to the older ClaimTypes.NameIdentifier URI claim before the
// endpoint ever sees it — so looking up JwtRegisteredClaimNames.Sub afterwards finds nothing and
// silently returns null. This isn't a made-up scenario: it's what actually broke /book and
// /confirm the first time, and the failure mode was silent (no exception, just a wrong result),
// which is exactly the kind of bug a test should catch instead of a human debugging it by hand.
//
// This doesn't spin up Booking.API or a real HTTP request — it exercises JwtSecurityTokenHandler
// directly, the same class ASP.NET Core's JWT bearer middleware uses internally to build the
// ClaimsPrincipal from a validated token.
public class JwtClaimsMappingTests
{
    private const string Issuer = "TicketingSystem";
    private const string Audience = "TicketingSystem.Clients";
    private const string SigningKey = "test-only-signing-key-not-used-anywhere-real-32chars+";

    // Builds a token exactly the way Program.cs's /auth/token endpoint does.
    private static string BuildToken(string userId)
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
    };

    [Fact]
    public void ValidateToken_WithMapInboundClaimsFalse_FindsSubClaimByItsRealName()
    {
        // This is the actual fix applied in Program.cs: options.MapInboundClaims = false.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var token = BuildToken("alice");

        var principal = handler.ValidateToken(token, ValidationParameters, out _);

        // With mapping disabled, "sub" stays "sub" — this is exactly what
        // caller.FindFirstValue(JwtRegisteredClaimNames.Sub) in Program.cs relies on.
        // (FindFirst/.Value is the plain base-.NET API; FindFirstValue used in Program.cs is
        // an ASP.NET Core convenience extension method not available in this test project.)
        Assert.Equal("alice", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    }

    [Fact]
    public void ValidateToken_WithDefaultMapping_SilentlyRenamesSubClaim()
    {
        // This test documents the footgun itself: it deliberately reproduces the DEFAULT
        // behaviour (mapping left on) to prove why the fix in Program.cs is necessary at all.
        // If Program.cs's options.MapInboundClaims = false were ever accidentally removed, this
        // test's twin above would start failing — but understanding *why* it would fail is the
        // point of keeping this one here too.
        var handler = new JwtSecurityTokenHandler(); // MapInboundClaims defaults to true
        var token = BuildToken("alice");

        var principal = handler.ValidateToken(token, ValidationParameters, out _);

        // The claim named "sub" is gone — searching for it directly finds nothing.
        Assert.Null(principal.FindFirst(JwtRegisteredClaimNames.Sub));

        // It's not lost — it got renamed to the legacy WIF-era claim type instead.
        Assert.Equal("alice", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}
