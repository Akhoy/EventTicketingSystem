using Microsoft.EntityFrameworkCore;

namespace RepositoryPattern.Example.Ladder;

// ════════════════════════════════════════════════════════════════════════
//  Runnable example for: docs/system-design-concepts.html §12
//  "The Repository Ladder — 7 Patterns in One Debate"
//
//  The SAME task done every way the famous r/dotnet thread describes:
//     Task: "get all CONFIRMED bookings for the taylor-swift event"
//     (correct answer is always 2 rows — but HOW each gets there is the argument)
//
//  Uses EF Core In-Memory, so no real database is needed.
// ════════════════════════════════════════════════════════════════════════

public static class LadderDemo
{
    public static async Task RunAsync()
    {
        await Way1_DirectEf();
        await Way2_GenericRepo_Naive();
        await Way2b_GenericRepo_Leaky();
        await Way3_SpecificRepo();
        await Way4_DddAggregate();
        await Way5_CqrsReadSide();
        await Way6_FuncOfIQueryable();
        await Way7_FilterObject();
    }

    static void Header(string t) => Console.WriteLine($"\n══════════ {t} ══════════");

    // WAY 1 — DIRECT EF ("EF *is* the repository"). No abstraction. Query runs in the DB.
    static async Task Way1_DirectEf()
    {
        Header("WAY 1: Direct EF (no repository)");
        using var db = Seed.Fresh();
        var result = await db.Bookings
            .Where(b => b.EventId == "taylor-swift" && b.Status == "Confirmed")
            .ToListAsync();
        Console.WriteLine($"Got {result.Count} rows. Query ran IN THE DATABASE.");
        Console.WriteLine("Verdict: simplest thing that works. Great for small apps.");
    }

    // WAY 2 — GENERIC REPO, NAIVE. Only has GetAll → forced to filter in memory.
    static async Task Way2_GenericRepo_Naive()
    {
        Header("WAY 2: Generic repo, naive  (IRepository<T>)");
        using var db = Seed.Fresh();
        IRepository<Booking> repo = new GenericRepository<Booking>(db);
        var all = await repo.GetAllAsync();                 // pulls ALL rows
        var result = all
            .Where(b => b.EventId == "taylor-swift" && b.Status == "Confirmed")
            .ToList();                                       // filter in memory
        Console.WriteLine($"Loaded ALL {all.Count} rows into memory, then filtered to {result.Count} in C#.");
        Console.WriteLine("Verdict: with 5 rows fine. With 5 MILLION rows this melts your server.");
    }

    // WAY 2b — GENERIC REPO, LEAKY. Adds Expression<Func<>> → caller writes the LINQ.
    static async Task Way2b_GenericRepo_Leaky()
    {
        Header("WAY 2b: Generic repo, leaky  (Expression<Func<T,bool>>)");
        using var db = Seed.Fresh();
        ILeakyRepository<Booking> repo = new LeakyRepository<Booking>(db);
        var result = await repo.FindAsync(
            b => b.EventId == "taylor-swift" && b.Status == "Confirmed");
        Console.WriteLine($"Got {result.Count} rows. Query ran in the DB this time...");
        Console.WriteLine("...BUT the caller wrote the LINQ. The repo abstracts NOTHING.");
        Console.WriteLine("Verdict: OP's 'you poorly re-implemented LINQ'. Boo generic repos.");
    }

    // WAY 3 — SPECIFIC REPO. Named method, LINQ hidden inside. (What this codebase uses.)
    static async Task Way3_SpecificRepo()
    {
        Header("WAY 3: Specific repo  (IBookingRepository)");
        using var db = Seed.Fresh();
        IBookingRepository repo = new BookingRepository(db);
        var result = await repo.GetConfirmedForEventAsync("taylor-swift");
        Console.WriteLine($"Got {result.Count} rows. Query ran in the DB. Caller just asked for what it wanted.");
        Console.WriteLine("Verdict: the grown-up version. Named intent, no leak, no in-memory filtering.");
    }

    // WAY 4 — DDD AGGREGATE. Private setters; changes go through Confirm(); rule enforced.
    static Task Way4_DddAggregate()
    {
        Header("WAY 4: DDD aggregate + repo");
        var booking = BookingAggregate.Create("taylor-swift");
        // booking.Status = "Confirmed";  // ← won't compile: private setter
        booking.Confirm();
        try { booking.Confirm(); }
        catch (Exception e) { Console.WriteLine($"Blocked illegal action: {e.Message}"); }
        Console.WriteLine($"Status via domain method: {booking.Status}");
        Console.WriteLine($"Domain events raised: {string.Join(", ", booking.Events)}");
        Console.WriteLine("Verdict: repo exists to protect INVARIANTS, not to swap databases.");
        return Task.CompletedTask;
    }

    // WAY 5 — CQRS READ SIDE. Reads bypass the repo with a thin AsNoTracking query.
    static async Task Way5_CqrsReadSide()
    {
        Header("WAY 5: CQRS read side  (IQuery<T>, AsNoTracking)");
        using var db = Seed.Fresh();
        IQuery<Booking> query = new EfQuery<Booking>(db);
        var result = await query.Get()
            .Where(b => b.EventId == "taylor-swift" && b.Status == "Confirmed")
            .ToListAsync();
        Console.WriteLine($"Got {result.Count} rows, read-only (no change tracking overhead).");
        Console.WriteLine("Verdict: writes through repo+aggregate, reads through direct EF. That's CQRS.");
    }

    // WAY 6 — Func<IQueryable<T>>. Full LINQ behind one entry point, but leaks IQueryable.
    static async Task Way6_FuncOfIQueryable()
    {
        Header("WAY 6: Func<IQueryable<T>> composition");
        using var db = Seed.Fresh();
        var repo = new ComposableRepository<Booking>(db);
        var result = await repo.QueryAsync(q => q
            .Where(b => b.EventId == "taylor-swift" && b.Status == "Confirmed"));
        Console.WriteLine($"Got {result.Count} rows. Full LINQ power, query runs in DB, one entry point.");
        Console.WriteLine("Verdict: middle ground. Flexible, but still leaks IQueryable to the caller.");
    }

    // WAY 7 — FILTER OBJECT. Caller passes DATA, repo owns the LINQ. No leak.
    static async Task Way7_FilterObject()
    {
        Header("WAY 7: Filter object (no leak)");
        using var db = Seed.Fresh();
        var repo = new FilteredRepository(db);
        var result = await repo.FindAsync(new BookingFilter
        {
            EventId = "taylor-swift",
            Status = "Confirmed"
        });
        Console.WriteLine($"Got {result.Count} rows. Caller described WHAT (data), repo decided HOW (LINQ).");
        Console.WriteLine("Verdict: the disciplined generic repo. Contract stays clean, no EF leak.");
    }
}


// ════════════════════════════════════════════════════════════════════════
//  Shared model + seed
// ════════════════════════════════════════════════════════════════════════
public class Booking
{
    public int Id { get; set; }
    public string EventId { get; set; } = "";
    public string Status { get; set; } = "Pending"; // "Pending" | "Confirmed"
}

public class AppDb : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    protected override void OnConfiguring(DbContextOptionsBuilder o)
        => o.UseInMemoryDatabase("ladder-demo");
}

static class Seed
{
    public static AppDb Fresh()
    {
        var db = new AppDb();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        db.Bookings.AddRange(
            new Booking { EventId = "taylor-swift", Status = "Confirmed" },
            new Booking { EventId = "taylor-swift", Status = "Pending" },
            new Booking { EventId = "taylor-swift", Status = "Confirmed" },
            new Booking { EventId = "coldplay", Status = "Confirmed" },
            new Booking { EventId = "coldplay", Status = "Pending" });
        db.SaveChanges();
        return db;
    }
}


// ════════════════════════════════════════════════════════════════════════
//  Implementations for each way
// ════════════════════════════════════════════════════════════════════════

// WAY 2 — generic, naive
public interface IRepository<T> where T : class
{
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);
    Task AddAsync(T entity);
}
public class GenericRepository<T>(AppDb db) : IRepository<T> where T : class
{
    public async Task<List<T>> GetAllAsync() => await db.Set<T>().ToListAsync();
    public async Task<T?> GetByIdAsync(int id) => await db.Set<T>().FindAsync(id);
    public async Task AddAsync(T e) { db.Add(e); await db.SaveChangesAsync(); }
}

// WAY 2b — generic, leaky
public interface ILeakyRepository<T> where T : class
{
    Task<List<T>> FindAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate);
}
public class LeakyRepository<T>(AppDb db) : ILeakyRepository<T> where T : class
{
    public async Task<List<T>> FindAsync(System.Linq.Expressions.Expression<Func<T, bool>> p)
        => await db.Set<T>().Where(p).ToListAsync();
}

// WAY 3 — specific
public interface IBookingRepository
{
    Task<List<Booking>> GetConfirmedForEventAsync(string eventId);
}
public class BookingRepository(AppDb db) : IBookingRepository
{
    public async Task<List<Booking>> GetConfirmedForEventAsync(string eventId)
        => await db.Bookings
            .Where(b => b.EventId == eventId && b.Status == "Confirmed")
            .ToListAsync();
}

// WAY 4 — DDD aggregate
public class BookingAggregate
{
    public int Id { get; private set; }
    public string EventId { get; private set; } = "";
    public string Status { get; private set; } = "Pending";
    public List<string> Events { get; } = new();

    private BookingAggregate() { }

    public static BookingAggregate Create(string eventId)
        => new() { EventId = eventId, Status = "Pending" };

    public void Confirm()
    {
        if (Status == "Confirmed")
            throw new InvalidOperationException("Already confirmed.");
        Status = "Confirmed";
        Events.Add("BookingConfirmed");
    }
}

// WAY 5 — CQRS read side
public interface IQuery<T> where T : class { IQueryable<T> Get(); }
public class EfQuery<T>(AppDb db) : IQuery<T> where T : class
{
    public IQueryable<T> Get() => db.Set<T>().AsNoTracking();
}

// WAY 6 — Func over IQueryable
public class ComposableRepository<T>(AppDb db) where T : class
{
    public async Task<List<T>> QueryAsync(Func<IQueryable<T>, IQueryable<T>> shape)
        => await shape(db.Set<T>()).ToListAsync();
}

// WAY 7 — filter object
public class BookingFilter
{
    public string? EventId { get; set; }
    public string? Status { get; set; }
}
public class FilteredRepository(AppDb db)
{
    public async Task<List<Booking>> FindAsync(BookingFilter f)
    {
        var q = db.Bookings.AsQueryable();
        if (f.EventId is not null) q = q.Where(b => b.EventId == f.EventId);
        if (f.Status is not null) q = q.Where(b => b.Status == f.Status);
        return await q.ToListAsync();
    }
}
