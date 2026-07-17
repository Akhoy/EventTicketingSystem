using Microsoft.EntityFrameworkCore;

namespace RepositoryPattern.Example.Hydrate;

// ════════════════════════════════════════════════════════════════════════
//  Runnable example for: docs/system-design-concepts.html §14
//  "Why the Aggregate Is the Ideal Unit for a Repository"
//
//  Demonstrates the DDD principle:
//     "use Repositories strictly to HYDRATE domain entities for state
//      changes (WRITES); separate out READS (CQRS)."
//
//  HYDRATE       = take a lifeless database row and pour it into a live C# object
//  DOMAIN ENTITY = that live object, which carries RULES (Booking.Confirm())
//  FOR WRITES    = you only bother doing this when you intend to CHANGE the data
//
//  Two paths on the same data:
//    PATH 1 (WRITE) — hydrate through the repository, change it, save it.
//    PATH 2 (READ)  — just look at data. NO repository. Direct query (CQRS read side).
// ════════════════════════════════════════════════════════════════════════

public static class HydrateDemo
{
    public static async Task RunAsync()
    {
        await SeedTwoBookings();

        Console.WriteLine("\n══════════ PATH 1 — WRITE (hydrate a domain entity to change it) ══════════");
        await RunWritePath();

        Console.WriteLine("\n══════════ PATH 2 — READ (just looking — no repository) ══════════");
        await RunReadPath();
    }

    // PATH 1 — WRITE: hydrate the entity BECAUSE we want to change it
    static async Task RunWritePath()
    {
        using var db = new AppDb();
        IBookingRepository repo = new BookingRepository(db);

        Console.WriteLine("In the DB, booking bbb-1 is just a lifeless row: (Id=bbb-1, Status='Pending').");

        // HYDRATE: load the row and fill a live Booking object that has behaviour.
        var booking = await repo.GetByIdAsync("bbb-1");
        Console.WriteLine($"HYDRATED into a live object. It now has behaviour, e.g. Confirm(). Status={booking!.Status}");

        // Change it through a guarded method — the rule is enforced right here.
        booking.Confirm();
        Console.WriteLine($"Changed it via booking.Confirm() — rule enforced. Status={booking.Status}");

        // Persist the change. This is the whole reason we hydrated it.
        await repo.SaveAsync();
        Console.WriteLine("Saved the change back to the DB. THIS is 'hydrate for a write'.");

        // Proof the rule is real: hydrate the EXPIRED one and try to confirm it.
        var expired = await repo.GetByIdAsync("bbb-2");
        Console.WriteLine($"\nHydrated bbb-2 (Status={expired!.Status}) and calling Confirm()...");
        try { expired.Confirm(); }
        catch (Exception e) { Console.WriteLine($"BLOCKED by the domain rule: {e.Message}"); }
    }

    // PATH 2 — READ: just looking. No change, no rule, so NO repository.
    static async Task RunReadPath()
    {
        using var db = new AppDb();

        // Only DISPLAY data → don't hydrate a domain entity, don't use the repository.
        // Query straight into a flat view model (the CQRS read side).
        var list = await db.Bookings
            .AsNoTracking()
            .Select(b => new BookingRow(b.Id, b.Status))
            .ToListAsync();

        Console.WriteLine("Queried the DB directly into a flat list — no repository, no domain entity:");
        foreach (var row in list)
            Console.WriteLine($"   {row.Id}: {row.Status}");
        Console.WriteLine("Nothing to protect when you're only looking, so the repository would be pointless ceremony.");
    }

    static async Task SeedTwoBookings()
    {
        using var db = new AppDb();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        db.Bookings.Add(Booking.Rehydrate("bbb-1", "Pending"));   // will be confirmed
        db.Bookings.Add(Booking.Rehydrate("bbb-2", "Expired"));   // will be blocked
        await db.SaveChangesAsync();
    }
}


// The DOMAIN ENTITY — a rich Booking with rules (private setters + methods)
public class Booking
{
    public string Id { get; private set; } = "";
    public string Status { get; private set; } = "Pending";

    private Booking() { }   // EF uses this (via reflection) to hydrate from a row

    public static Booking Rehydrate(string id, string status) =>
        new() { Id = id, Status = status };

    public void Confirm()
    {
        if (Status == "Expired")
            throw new InvalidOperationException("Cannot confirm an expired booking.");
        Status = "Confirmed";
    }
}

// A flat read-model for the READ path — no behaviour, just data to display.
public record BookingRow(string Id, string Status);


// The REPOSITORY — used ONLY on the write path: hydrate + save
public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(string id);   // hydrate one entity
    Task SaveAsync();                          // persist changes (Unit of Work)
}
public class BookingRepository(AppDb db) : IBookingRepository
{
    public async Task<Booking?> GetByIdAsync(string id) =>
        await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);

    public Task SaveAsync() => db.SaveChangesAsync();
}


// EF plumbing
public class AppDb : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
        => o.UseInMemoryDatabase("hydrate-demo");

    protected override void OnModelCreating(ModelBuilder m)
        => m.Entity<Booking>().HasKey(b => b.Id);
}
