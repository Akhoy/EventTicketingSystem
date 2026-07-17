using RepositoryPattern.Example.Hydrate;
using RepositoryPattern.Example.Ladder;

// ════════════════════════════════════════════════════════════════════════
//  Dispatcher — picks which demo to run from the first command-line argument.
//
//    dotnet run --project Examples/RepositoryPattern.Example -- hydrate   (default)
//    dotnet run --project Examples/RepositoryPattern.Example -- ladder
//
//  See each demo's file header and README.md for what it teaches.
// ════════════════════════════════════════════════════════════════════════

var which = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "hydrate";

switch (which)
{
    case "hydrate":
        await HydrateDemo.RunAsync();
        break;

    case "ladder":
        await LadderDemo.RunAsync();
        break;

    default:
        Console.WriteLine($"Unknown demo '{which}'. Choose one of:");
        Console.WriteLine("  hydrate   §14 — hydrate a domain entity for a write; reads bypass the repo");
        Console.WriteLine("  ladder    §12 — the same query done all 7 ways from the repository debate");
        Environment.ExitCode = 1;
        break;
}
