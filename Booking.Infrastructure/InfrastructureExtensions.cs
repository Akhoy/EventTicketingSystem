using Booking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Infrastructure;

public static class InfrastructureExtensions
{
    // This file exists so Program.cs calls one line to wire up all infrastructure dependencies
    // without ever referencing EF Core, BookingDbContext, or BookingRepository directly.
    // Those are Infrastructure details — the API layer shouldn't know about them.
    //
    // IServiceCollection is the registry — you tell it "when someone asks for X, give them Y."
    // After builder.Build(), it becomes IServiceProvider — the resolver that handles constructor
    // injection automatically. You almost never call IServiceProvider yourself; ASP.NET Core does it.
    public static IServiceCollection AddBookingInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // Scoped lifetime: one BookingDbContext per HTTP request, or per manually created scope.
        // Scoped ensures that within one request, every class that needs DbContext gets the SAME
        // instance — so they share the same EF Core change tracker and SaveChanges covers all changes.
        services.AddDbContext<BookingDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Scoped lifetime matches DbContext above — BookingRepository and BookingDbContext live
        // and die together within the same scope. This is intentional: the repository wraps the
        // context, so they must share the same lifetime.
        //
        // Workers are Singleton (one instance for the app's lifetime). A Singleton cannot take
        // a Scoped dependency in its constructor — DI throws at startup. Workers get around this
        // by using IServiceScopeFactory to create a new scope manually each cycle.
        services.AddScoped<IBookingRepository, BookingRepository>();

        return services;
    }
}
