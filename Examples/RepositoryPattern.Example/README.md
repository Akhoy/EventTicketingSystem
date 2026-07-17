# RepositoryPattern.Example

Small, self-contained, runnable examples for the repository/aggregate sections of
`docs/system-design-concepts.html`. Uses EF Core's **In-Memory** provider, so no real
database is needed.

This single project contains **two demos**, picked by a command-line argument.

## Project layout

```
RepositoryPattern.Example/
  Program.cs              ← dispatcher: reads the first arg and runs one demo
  Hydrate/HydrateDemo.cs  ← demo 1  (namespace RepositoryPattern.Example.Hydrate)
  Ladder/LadderDemo.cs    ← demo 2  (namespace RepositoryPattern.Example.Ladder)
```

Each demo lives in its **own namespace** — both define their own `Booking` / `AppDb` /
repository types, and the namespaces keep them from colliding.

## Demo 1 — `hydrate`  (docs §14: aggregate as the unit for a repository)

Shows *"use repositories strictly to **hydrate** domain entities for state changes (**writes**);
separate out **reads** (CQRS)."*

| Path | What happens | Repository used? |
|------|--------------|------------------|
| **WRITE** | Load (`hydrate`) a `Booking` into a live object → `Confirm()` (rule enforced) → save | ✅ Yes |
| **READ** | Query straight into a flat `BookingRow` list just to display | ❌ No — direct query |

- **Hydrate** = take a lifeless database row and pour it into a live C# object that has behaviour.
- The `Booking` is a *rich domain entity*: private setters, private constructor, and a `Confirm()`
  method that enforces "cannot confirm an expired booking."

## Demo 2 — `ladder`  (docs §12: the 7-pattern repository debate)

Runs the **same task** — *"get all confirmed bookings for the taylor-swift event"* — **7 different
ways**, so you can see how each pattern from the famous r/dotnet thread behaves. All return the
correct 2 rows; the point is *how* they get there.

1. Direct EF · 2. Generic repo (naive) · 2b. Generic repo (leaky) · 3. Specific repo ·
4. DDD aggregate · 5. CQRS read side · 6. `Func<IQueryable>` · 7. Filter object

Watch two lines especially:
- WAY 2 prints **"Loaded ALL 5 rows into memory"** — the generic-repo performance trap.
- WAY 4 prints **"Blocked illegal action"** — the one thing only an aggregate can do.

## How to run

From the repo root (the `--` separates `dotnet` args from the program's args):

```bash
# Demo 1 — hydrate (this is also the default if you pass no argument)
dotnet run --project Examples/RepositoryPattern.Example -- hydrate

# Demo 2 — the 7-way ladder
dotnet run --project Examples/RepositoryPattern.Example -- ladder
```

Passing no argument runs `hydrate`. Passing an unknown name prints the list of demos.

> This project is intentionally **not** part of `TicketingSystem.slnx` — it's a standalone
> learning artifact. Run it directly with the commands above.
