# File Logging — `Blaztrap.Aspire.FileLogging`

In-process file logger that captures **both** per-resource stdout/stderr (`{resource}.log`) and everything the AppHost itself emits (`apphost.log`). Same API works in `aspire run` (exploratory provisioning) and in `DistributedApplicationTestingBuilder` (integration tests). No dependency on `Aspire.Hosting.Testing`.

NuGet: `Blaztrap.Aspire.FileLogging` ≥ 0.1.0. Local feed: `D:\Nuget\Feeds\ByteAid\`.

## 1. Why this package

Two failures that motivated it:

- `Blaztrap.Aspire.Testing.FileLogging` v0.1.x captures per-resource child stdout but only inside an integration-test session — it relies on `ResourceLoggerForwarderService` from `Aspire.Hosting.Testing`, which is auto-registered only by `DistributedApplicationTestingBuilder`. It also drops everything the AppHost itself logs (Aspire framework, DCP, dashboard, `Microsoft.Hosting.*`).
- External "tail the dashboard" scripts only see what the dashboard sees and break when the host crashes before the dashboard initialises.

`Blaztrap.Aspire.FileLogging` is one `ILoggerProvider` that handles both streams: an internal pump replicates `ResourceLoggerForwarderService` using only public types from `Aspire.Hosting`, so the AppHost project does not pull `Aspire.Hosting.Testing` for exploratory runs.

## 2. File layout produced

For an AppHost that registers `webfrontend`, `api`, `worker`:

```
{outputDirectory}/
  apphost.log         # AppHost / Aspire / DCP / Dashboard / Microsoft.Hosting.* / Default
  webfrontend.log     # ProjectResource "webfrontend" stdout + stderr
  api.log             # ProjectResource "api" stdout + stderr
  worker.log          # ExecutableResource "worker" stdout + stderr
```

Per-resource line format (no category — the file already identifies the source):

```
[2026-04-23T15:42:07.1234567Z] [information] Application started. Press Ctrl+C to shut down.
```

`apphost.log` line format (category prefixed):

```
[2026-04-23T15:42:07.1234567Z] [information] Aspire.Hosting.Dcp.DcpHostService: Starting DCP...
[2026-04-23T15:42:07.5670000Z] [information] Microsoft.Hosting.Lifetime: Application started.
```

Exceptions serialise on the line(s) following the message.

## 3. Public API

| Type | Purpose |
|---|---|
| `DistributedApplicationBuilderExtensions.AddFileLogging(this IDistributedApplicationBuilder builder, string outputDirectory)` | High-level. Snapshots `ProjectResource` + `ExecutableResource` names from `builder.Resources`, registers `FileLoggerProvider`, wires `ResourceLogPump` as a hosted service. **Call AFTER every `AddProject` / `AddExecutable`, BEFORE `Build`/`BuildAsync`.** |
| `LoggingBuilderExtensions.AddFileLogging(this ILoggingBuilder loggingBuilder, string outputDirectory, params string[] resourceNames)` | Low-level. Pass the resource-name whitelist yourself. Useful when you only want host-side logs and provide your own resource forwarder, or outside the AppHost context. |
| `FileLoggerProvider` (public) | The `ILoggerProvider` itself. Constructable directly if you need fine-grained control. |

## 4. Wiring

### 4.1 Regular AppHost (`aspire run` / `dotnet run`)

```csharp
using Blaztrap.Aspire.FileLogging;

var builder = DistributedApplication.CreateBuilder(args);

var api    = builder.AddProject<Projects.Contoso_Foo_Api>("api");
var web    = builder.AddProject<Projects.Contoso_Foo_Web>("web");
var worker = builder.AddProject<Projects.Contoso_Foo_Worker>("worker");

// AFTER all AddProject / AddExecutable calls, BEFORE Build:
builder.AddFileLogging(Path.Combine(AppContext.BaseDirectory, "logs"));

builder.Build().Run();
```

### 4.2 MSTest integration test (`[ClassInitialize]`)

The per-class mount from `dotnet-testing` § mstest-integration. The artefact root comes from `TestContext.TestRunResultsDirectory` via the `TestArtifacts.RunDir(context)` helper defined in that skill:

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    var appHost = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.Contoso_Foo_AppHost>([]);

    appHost.Services.ConfigureHttpClientDefaults(c =>
        c.AddStandardResilienceHandler());

    appHost.AddFileLogging(Path.Combine(TestArtifacts.RunDir(context), "logs"));

    _app = await appHost.BuildAsync();
    await _app.StartAsync();
}
```

`TestContext.TestRunResultsDirectory` is MSTest's per-run results folder; it is stable for the duration of one `dotnet test` invocation. No env vars are read. See `dotnet-testing` § Test artefacts for the full path contract.

### 4.3 Low-level (host-side logs only)

When something else handles per-resource forwarding (e.g. a different fixture you can't replace):

```csharp
builder.Services.AddLogging(b =>
    b.AddFileLogging(
        outputDirectory: Path.Combine(logsDir, "logs"),
        resourceNames: ["api", "worker"]));   // only these match per-resource files
```

`ResourceLogPump` is NOT wired here — categories matching the names go to `{name}.log`, everything else goes to `apphost.log`.

## 5. Resource-discovery snapshot semantics

`AddFileLogging(builder, outputDirectory)` walks `builder.Resources` **once** and freezes the resulting list of names. Resources added later (rare in practice) are not captured. If you genuinely need late-bound resources:

- Call the low-level overload with an explicit name list, **or**
- Compose the AppHost so all resource registrations precede `AddFileLogging`.

## 6. Coexistence with the dashboard and other consumers

`ResourceLoggerService.WatchAsync(resource)` returns an **independent stream per subscriber**. The pump and the dashboard subscribe simultaneously without interfering; stopping the pump (host shutdown) does not affect the dashboard or any other consumer.

## 7. Coexistence with `ResourceLoggerForwarderService`

When `DistributedApplicationTestingBuilder.CreateAsync<T>` builds the model, it also registers `ResourceLoggerForwarderService` (the testing-only forwarder). Both that forwarder AND `ResourceLogPump` end up subscribed to `ResourceLoggerService.WatchAsync` for every resource — each line is forwarded **twice** through `ILoggerFactory`, producing duplicate lines in `{resource}.log`.

Two workarounds when this matters in tests:

```csharp
// (A) Remove the testing forwarder before BuildAsync.
appHost.Services.RemoveAll<IHostedService>(d =>
    d.ImplementationType?.Name == "ResourceLoggerForwarderService");

// (B) Suppress duplicates at filter level for the resource categories.
appHost.Services.AddLogging(b => b.AddFilter(
    (provider, category, level) =>
        provider != typeof(FileLoggerProvider).FullName ||
        !IsForwardedResourceCategory(category) ||
        level >= LogLevel.Warning));
```

Option (A) is preferred in greenfield suites. Option (B) helps when other consumers still need the forwarder.

## 8. Caveats

- **Failures before a resource starts.** If a container or project fails to start (image missing, port collision, bad connection string), the failing resource produces zero lines on stdout — there is nothing for the pump to forward, so `{resource}.log` stays empty. The matching error is in `apphost.log` under `Aspire.Hosting.Dcp.*`. **Inspect `apphost.log` first when debugging.**
- **Resources added after the call** are not captured. See § 5.
- **Log levels.** The pump assigns `LogLevel.Information` to every line not flagged as an error by Aspire. Standard .NET logging filters (`Logging:LogLevel:{resourceName}`, `Logging:LogLevel:Aspire.*`) apply exactly as for any other logger and let you reduce verbosity per resource or per framework subsystem.
- **Filename sanitisation.** Characters illegal on the host filesystem are replaced with `_`. Resource-name collisions after sanitisation (rare) result in shared writes to the same file.
- **No log rotation.** Per-file streams grow until the AppHost stops. For long-running exploratory sessions, restart the AppHost to roll the files (or pre-process them with a separate tail/archival tool).
- **`AutoFlush = true`** on every `StreamWriter` so a crashing host still leaves a tailable file on disk. Slight write-throughput cost is intentional.

## 9. Format details (reference)

Line shape: `[<ISO-8601 UTC timestamp>] [<level-padded-to-11>] <category-prefix-or-empty>: <message>`.

- Per-resource files emit `<message>` without prefix (file is the source identifier).
- `apphost.log` emits `<category>: <message>` (multiple sources interleave on this file).
- Exceptions serialise via `Exception.ToString()` on subsequent lines.
- Newlines normalised to `\n`.

A per-sink lock serialises writes per output file, so categories interleaving on `apphost.log` cannot tear lines.

## 10. Verification checklist

- [ ] `AddFileLogging(...)` is called AFTER every `AddProject`/`AddExecutable` and BEFORE `Build`/`BuildAsync`.
- [ ] `outputDirectory` exists or is created by the host (`Directory.CreateDirectory(...)` if necessary).
- [ ] `apphost.log` has lines after a healthy `aspire run` startup.
- [ ] Each `{resource}.log` has lines once the resource emits stdout.
- [ ] When a resource fails, the matching error appears in `apphost.log`.
- [ ] In test mode: `TestContext.TestRunResultsDirectory` resolves to the current run folder; logs land under `{TestArtifacts.RunDir(context)}/logs/`.
- [ ] In test mode: no duplicate lines per resource (otherwise apply § 7 workaround).

## Cross-references

- Live (`ResourceNotificationService`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.resourcenotificationservice
- Live (`ResourceLoggerService`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.resourceloggerservice
- Live (`ProjectResource`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.projectresource
- Live (`ExecutableResource`): https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.applicationmodel.executableresource
- Live (`ILoggerProvider`): https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.iloggerprovider
- Sibling skill: `dotnet-testing` § mstest-integration — per-class mount, `TestArtifacts.RunDir(context)`, artefact layout.
- Sibling: [scaffolding.md](scaffolding.md) — adding the AppHost project that hosts this call.
