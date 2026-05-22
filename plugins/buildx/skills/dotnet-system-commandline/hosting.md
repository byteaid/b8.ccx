# Hosting Integration & Deprecated Companion Packages

The 2.0 simplification deprecated the legacy companion packages that filled gaps in the older builder-based surface. Do not introduce them in new code; replace them in old code as you touch it.

## Deprecated packages

NuGet banner on each: *"this package has been deprecated as it is legacy and is no longer maintained."* None of these are referenced anywhere in the current `/dotnet/standard/commandline/` Microsoft Learn pages.

| Package | Replacement |
|---|---|
| `System.CommandLine.Hosting` | Manual glue against `Microsoft.Extensions.Hosting` (this file). |
| `System.CommandLine.NamingConventionBinder` | `Option<T>` / `Argument<T>` graph + `parseResult.GetValue(...)`. |
| `System.CommandLine.DragonFruit` | Idiomatic `RootCommand` + `Option<T>` graph. The convention-based `Main(string fileOption, ...)` binding is gone. |
| `System.CommandLine.Rendering` | No first-party replacement. Write directly to `InvocationConfiguration.Output` or use a third-party rendering library (e.g. Spectre.Console). |

Why: the 2.0 surface shrunk so much (interfaces 11 → 0, classes/structs 56 → 38, methods 378 → 235) that the value those packages added in beta4 disappeared.

## Manual `Microsoft.Extensions.Hosting` integration

The supported pattern after the `System.CommandLine.Hosting` deprecation keeps parsing and the host strictly separate, with no intermediate package:

1. **Generic Host** (`Host.CreateApplicationBuilder`) owns DI, configuration, logging, and lifetime.
2. **System.CommandLine** owns the command graph, parsing, and routing.
3. The "glue" is hand-written in the action delegate: resolve the per-command service from `host.Services` and call it.

### Reference shape

Single-file program, async actions, transient per-command "job" classes:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;

var builder = Host.CreateApplicationBuilder(args);

// DI: infrastructure + per-command "job" classes.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IOrderService, OrderService>();
builder.Services.AddTransient<SyncOrdersJob>();
builder.Services.AddTransient<CleanupJob>();

using var host = builder.Build();

// Command surface.
var root = new RootCommand("CLI description");

var sinceOption = new Option<DateTimeOffset?>("--since")
{
    Description = "Sync orders updated after this UTC instant.",
};
var syncOrders = new Command("sync-orders", "Pulls new orders from upstream.");
syncOrders.Options.Add(sinceOption);
syncOrders.SetAction((parseResult, ct) =>
{
    var job = host.Services.GetRequiredService<SyncOrdersJob>();
    return job.ExecuteAsync(parseResult.GetValue(sinceOption), ct);
});

var retentionOption = new Option<int>("--retention-days")
{
    DefaultValueFactory = _ => 30,
};
var cleanup = new Command("cleanup", "Removes stale records.");
cleanup.Options.Add(retentionOption);
cleanup.SetAction((parseResult, ct) =>
{
    var job = host.Services.GetRequiredService<CleanupJob>();
    return job.ExecuteAsync(parseResult.GetValue(retentionOption), ct);
});

root.Subcommands.Add(syncOrders);
root.Subcommands.Add(cleanup);

return await root.Parse(args).InvokeAsync();
```

### The per-command "job" class

Encapsulates the work, depends on the rest of the DI graph normally, returns an exit code.

```csharp
public sealed class SyncOrdersJob(
    IOrderService orders,
    ILogger<SyncOrdersJob> logger)
{
    public async Task<int> ExecuteAsync(DateTimeOffset? since, CancellationToken ct)
    {
        logger.LogInformation("Sync started. Since={Since}", since);
        try
        {
            var count = await orders.PullAsync(since, ct);
            logger.LogInformation("Sync finished. Processed={Count}", count);
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Sync cancelled by termination signal.");
            return 130; // 128 + SIGTERM, idiomatic on Linux
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed.");
            return 1;
        }
    }
}
```

## Notes specific to this pattern

- `SetAction` receives `(ParseResult, CancellationToken)` directly. `InvocationContext` (the pre-beta5 carrier of services) does not exist anymore — resolving from `host.Services` inside the lambda is the deliberate replacement.
- The `CancellationToken` argument is the one wired by `InvocationConfiguration.ProcessTerminationTimeout` (default 2 s) to Ctrl+C / SIGINT / SIGTERM. **Forward it** to every async call so the action returns cleanly inside the timeout instead of being killed; failing to forward triggers analyzer warning CA2016.
- **Lifetime.** The example does `using var host = builder.Build()` and never calls `host.Run()` / `host.StartAsync()`. That is intentional for a short-lived CLI binary — the host exists only as a DI / configuration / logging container. If a command genuinely needs `IHostedService` lifecycle (background workers, listeners), call `await host.StartAsync(ct)` at the top of the relevant action and `await host.StopAsync(ct)` before returning.
- **Lifetime of job classes.** Transient registration is the safe default because the process executes a single command and exits; singleton works too but offers no benefit at this lifetime.
- **Carrying services through `ParseResult`.** If you prefer a cleaner action signature, derive from `InvocationConfiguration` and put `IServiceProvider` on the subclass; pass the subclass to `Invoke / InvokeAsync` and cast inside the action. See [configuration.md](configuration.md) § "Picking the right phase".

## Replacing `NamingConventionBinder` / `DragonFruit`

The convention-based `Main(string foo, int bar)` binding is gone. Migrate as follows:

```csharp
// Before (DragonFruit):
static int Main(string fileOption, int delay = 5) { ... }

// After:
var fileOption  = new Option<string>("--file-option");
var delayOption = new Option<int>("--delay") { DefaultValueFactory = _ => 5 };

var root = new RootCommand("Description") { fileOption, delayOption };
root.SetAction(parseResult =>
{
    var file  = parseResult.GetValue(fileOption);
    var delay = parseResult.GetValue(delayOption);
    return Run(file, delay);
});

return root.Parse(args).Invoke();
```

For `NamingConventionBinder` (which inferred constructor parameters of a model type from option names), instantiate the model explicitly inside the action. Trying to keep the convention layer alive against the 2.0 surface is more work than just writing the construction.

## Rendering replacements

`System.CommandLine.Rendering` had layout primitives (regions, tables, ANSI). For new code:

- Plain output → write to `InvocationConfiguration.Output` (`TextWriter`).
- Tables, progress, prompts, colour → use [`Spectre.Console`](https://spectreconsole.net/) (third-party) or write your own helpers.
- Markup, JSON, YAML output → pick a serializer and write to `Output`.

## Cross-references

- [types-and-construction.md](types-and-construction.md) — `SetAction` signature.
- [configuration.md](configuration.md) — `InvocationConfiguration` and the subclass trick.
- [migration.md](migration.md) — full beta4 → beta5+ rename table.
- NuGet banner: https://www.nuget.org/packages/System.CommandLine.Hosting
- Repo status (deprecated companion packages, project status): https://github.com/dotnet/command-line-api
