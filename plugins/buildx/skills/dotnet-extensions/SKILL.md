---
name: dotnet-extensions
description: Reference for the `Microsoft.Extensions.*` family on .NET 10 — the substrate ASP.NET Core, Aspire, and Worker Services build on. Covers DI (lifetimes, scopes, captive deps, keyed services), Configuration (providers, binding), Options (`IOptions`/`IOptionsSnapshot`/`IOptionsMonitor`, validation, `[OptionsValidator]`), Logging, Generic Host (`HostApplicationBuilder`, `IHostedService`, graceful shutdown), Worker Services (`BackgroundService`, `PeriodicTimer`, Windows/systemd), caching (`IMemoryCache`/`IDistributedCache`/`HybridCache`), `IHttpClientFactory` + resilience, localization, file globbing.
when_to_use: |
  - Trigger keywords: Microsoft.Extensions.*, IServiceCollection, AddSingleton, captive dependency, ValidateScopes, AddKeyedSingleton, ActivatorUtilities, IConfiguration, IOptions, IOptionsMonitor, ValidateOnStart, OptionsValidator, ILogger, HostApplicationBuilder, IHostedService, BackgroundService, AddWindowsService, AddSystemd, IMemoryCache, IDistributedCache, HybridCache, IHttpClientFactory, AddStandardResilienceHandler, IStringLocalizer, Matcher.
  - Task shapes: stand up a host/worker; pick a DI lifetime; fix a captive-dep bug; design an options class with validation; add a config provider; write a `BackgroundService` with per-iteration scope; wire typed `IHttpClientFactory` + resilience; choose `IMemoryCache` vs `IDistributedCache` vs `HybridCache`; install a Windows service or systemd unit.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.csproj", "**/appsettings*.json", "**/Program.cs"]
---

# .NET Extensions (`Microsoft.Extensions.*`) — Reference

Reference for the runtime building blocks that ship under `Microsoft.Extensions.*` on .NET 10. Pin the rules; defer the long provider catalogues to the Microsoft docs cited at the bottom.

## Mental model

- The `Microsoft.Extensions.*` family is the **substrate**: ASP.NET Core, Aspire, Worker Services, Windows/systemd services, and minimal hosts all compose the same DI + Configuration + Options + Logging + Hosting blocks.
- `Host.CreateApplicationBuilder(args)` is the modern entry point — property-based, returns `HostApplicationBuilder`. Legacy callback-based `Host.CreateDefaultBuilder` (returns `IHostBuilder`) is supported but no longer the default.
- The host registers `IConfiguration`, `ILoggerFactory`, `IHostEnvironment`, `IHostApplicationLifetime`, `IServiceProvider`, `IServiceScopeFactory`, `IHost`. Every host type guarantees this set.
- DI lifetimes: **Singleton ⇒ root container; Scoped ⇒ per `IServiceScope`; Transient ⇒ per resolution.** Disposable transients resolved from the root scope live until shutdown — the classic leak.
- The Options pattern is the **only** correct way to inject configuration into services. Don't push raw `IConfiguration` into business code.

## Non-negotiable rules

1. **No service locator** — don't take `IServiceProvider` and call `GetService<T>` from business code. Constructor-inject.
2. **No `BuildServiceProvider()` inside a registration method** — creates a second container, double-disposal risk. Use the `(IServiceProvider sp) => ...` factory overload.
3. **No async ctors / async factories that block on `.Result`** — guaranteed deadlock under sync contexts.
4. **No disposable transients in the root scope** — make them scoped or always resolve from a child scope.
5. **Singleton depending on Scoped (or transient depending on Scoped) is a bug.** Inject `IServiceScopeFactory` and create a scope per unit of work, or `IDbContextFactory<TContext>` for EF Core. `ValidateScopes = true` catches it.
6. **`GetRequiredService<T>` over `GetService<T>`** — fails fast with a clear message.
7. **`ValidateOnBuild = true`** in non-trivial hosts — surfaces missing deps / ambiguous ctors at boot.
8. **Use Options pattern + `[OptionsValidator]`** (compile-time, AOT-safe) over runtime `ValidateDataAnnotations` for new code.
9. **Always create a scope inside `BackgroundService`** before resolving scoped services (DbContext, repos, `IOptionsSnapshot<T>`).
10. **Banned client integrations and team DI conventions** are owned by `dotnet-conventions` — load that before wiring third-party clients.

## Dependency Injection

Core types: `IServiceCollection` (registration), `ServiceDescriptor` (`(ServiceType, ImplementationType|Instance|Factory, Lifetime, [ServiceKey])`), `IServiceProvider` (resolution), `IServiceScope` / `IServiceScopeFactory` (scoped lifetimes; `IServiceScopeFactory` is always singleton), `ActivatorUtilities` (DI-aware construction of unregistered types), `ServiceLifetime` (`Singleton | Scoped | Transient`).

Lifetimes: **Singleton** (one per root container, disposed on container disposal); **Scoped** (one per `IServiceScope`, disposed with the scope); **Transient** (new per resolution, disposed by the *owning* scope/container — leak vector).

### Registration

```csharp
var services = Host.CreateApplicationBuilder(args).Services;

services.AddSingleton<IClock, SystemClock>();
services.AddScoped<IUnitOfWork, EfUnitOfWork>();
services.AddTransient<IEmailSender, SmtpEmailSender>();
services.AddSingleton<MetricsRecorder>();                                   // concrete
services.AddSingleton<IClock>(new SystemClock());                           // existing — NOT disposed by container
services.AddSingleton<IConnection>(sp =>
    new SqlConnection(sp.GetRequiredService<IConfiguration>().GetConnectionString("Db")!));
services.AddSingleton(typeof(IRepository<>), typeof(EfRepository<>));        // open generic
services.TryAddSingleton<IClock, SystemClock>();                            // no-op if already registered
services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidator, NameValidator>());
```

Constructor injection: public ctor required; container picks the ctor with the **most parameters whose types are all DI-resolvable** (ambiguity throws); default-valued ctor parameters not in DI are allowed; C# 12 primary ctors fully supported and idiomatic.

### Resolution and scoping

```csharp
using IServiceScope scope = host.Services.CreateScope();
var svc = scope.ServiceProvider.GetRequiredService<IOrderService>();   // throws if missing
var all = scope.ServiceProvider.GetServices<IValidator>();             // every registration
```

`IHost.Services` is the *root* provider (also the root scope). For per-iteration / per-request work that touches scoped services from a singleton, **always create a child scope** (`scopes.CreateAsyncScope()` for async cleanup; see Worker pattern below).

### Validation and disposal

`Host.CreateApplicationBuilder` enables `ValidateScopes` and `ValidateOnBuild` **only in Development** by default. Force them in prod via `builder.Host.UseDefaultServiceProvider((ctx, o) => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`. `ValidateScopes = true` throws `Cannot consume scoped service 'X' from singleton 'Y'` — captive-dependency signal. Disposal: container disposes every service it *created*; it does **not** dispose instances passed via `AddSingleton<T>(new T())` (caller owns). `ServiceProvider.DisposeAsync()` awaits each `IAsyncDisposable.DisposeAsync()` with `ConfigureAwait(false)`.

### Keyed services (.NET 8+) and other

```csharp
services.AddKeyedSingleton<IMessageWriter, MemoryMessageWriter>("memory");
services.AddKeyedSingleton<IMessageWriter, QueueMessageWriter>("queue");
public sealed class Dispatcher([FromKeyedServices("queue")] IMessageWriter writer);
var w = sp.GetRequiredKeyedService<IMessageWriter>("memory");
```

`KeyedService.AnyKey` registration acts as a fallback factory; factory signature `(IServiceProvider sp, object? key)`. **.NET 10 change:** `GetKeyedService<T>(KeyedService.AnyKey)` (singular) now throws `InvalidOperationException` — use `GetKeyedServices<T>(KeyedService.AnyKey)` (plural) to enumerate explicitly-keyed registrations.

`ActivatorUtilities.CreateInstance<T>(sp, runtimeArg1, ...)` constructs unregistered types using DI to fill ctor parameters; requires exactly one ctor whose non-DI parameters are supplied positionally.

The built-in container is feature-minimal by design (no property injection, no child containers, no convention scanning, no `Func<T>` lazy). For those, plug in Autofac / DryIoc / Lamar / Simple Injector via their `*.Microsoft.DependencyInjection` adapter.

## Configuration

Types: `IConfigurationBuilder` (build phase, ordered list of `IConfigurationSource`); `IConfigurationProvider` (loads a flat `IDictionary<string,string?>` keyed by `:`-separated paths; exposes change tokens); `IConfiguration` (read view — indexer, `GetSection`, `GetChildren`, `GetReloadToken`); `ConfigurationManager` (mutable, both builder *and* root, used by the host).

Keys are case-insensitive. Hierarchy delimiter is `:`. JSON / INI / XML translate nesting into `Parent:Child:Leaf`. Arrays use 0-based numeric segments. **Last-writer-wins**: providers added later override earlier providers for the same key.

### Default sources from `Host.CreateApplicationBuilder` (lowest priority first)

1. `ChainedConfigurationProvider` — any pre-existing `IConfiguration`.
2. `appsettings.json` (optional, reload-on-change).
3. `appsettings.{Environment}.json` (optional, reload-on-change).
4. **User secrets** — only when `Environment == Development` and the entry assembly has a `UserSecretsId`.
5. Environment variables (Host also reads `DOTNET_*` for host config).
6. Command-line args (when `args` is provided).

### Reading values

```csharp
IConfiguration cfg = host.Services.GetRequiredService<IConfiguration>();
string? raw = cfg["Parent:Child:Name"];
int n = cfg.GetValue<int>("Parent:FavoriteNumber");
string? cs = cfg.GetConnectionString("Default");           // = "ConnectionStrings:Default"
```

`GetRequiredSection(name)` throws if absent — preferred for required configuration.

### Built-in providers

- **JSON/INI/XML files** throw `FormatException` on duplicate keys; XML repeating elements are auto-indexed.
- **Environment variables**: `__` replaces `:` (`:` is illegal in shells). `Logging__LogLevel__Default=Warning` ⇒ `Logging:LogLevel:Default`. `AddEnvironmentVariables(prefix: "MYAPP_")` strips the prefix. Special connection-string prefixes (`CUSTOMCONNSTR_`, `MYSQLCONNSTR_`, `SQLAZURECONNSTR_`, `SQLCONNSTR_`) → `ConnectionStrings:KEY`.
- **Command-line**: `Key=Value`, `--Key Value`, `--Key=Value`, `/Key Value`, `/Key=Value`. Switch mappings: `new Dictionary<string,string> { ["-n"] = "Settings:Name" }`.
- **User secrets** (Development only): `<UserSecretsId>` in csproj; `dotnet user-secrets set "MyKey" "value"`. Stored under `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` (Win) or `~/.microsoft/usersecrets/<id>/secrets.json` (Unix). Auto-added by `Host.CreateApplicationBuilder` in Development.
- **Key-per-file**: `AddKeyPerFile("/run/secrets", optional: true)` — each file becomes one key (filename → key, contents → value); `__` in filename becomes `:`. Standard for Docker secrets / K8s secret volumes.
- **In-memory**: `AddInMemoryCollection(new Dictionary<string,string?> { ... })` — tests and default fallback.
- **Azure App Configuration / Key Vault**: separate packages (`Microsoft.Azure.AppConfiguration.AspNetCore`, `Azure.Extensions.AspNetCore.Configuration.Secrets`). Key Vault secret names with `--` translate to `:`.

### Binding

`Microsoft.Extensions.Configuration.Binder` (reflection). `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` enables the trim/AOT-friendly source-gen analyzer.

```csharp
cfg.GetSection("Settings").Bind(s);                          // into existing instance
Settings? s2 = cfg.GetSection("Settings").Get<Settings>();   // new instance
services.Configure<Settings>(cfg.GetSection("Settings"));    // register as IOptions<Settings>
```

Binder rules: public parameterless ctor OR exactly one public parameterized ctor whose parameter names match property names (records OK, .NET 7+); only public properties with setters bind (no fields); `IReadOnlyList<T>` / `IReadOnlyDictionary<TKey,TValue>` not bound directly — use the mutable forms; dictionary keys cannot contain `:`.

### Custom configuration provider

Subclass `ConfigurationProvider`, override `Load()` to populate `Data` (internal `IDictionary<string,string?>`, case-insensitive comparer mandatory). For change support, watch the underlying source and call `OnReload()` — that propagates a reload `IChangeToken` to all `IOptionsMonitor<T>` consumers.

## Options pattern

| Interface | Lifetime | Reload aware | Named options |
|---|---|---|---|
| `IOptions<T>` | Singleton | No | No — snapshot at first access; never refreshed |
| `IOptionsSnapshot<T>` | Scoped | Yes (per scope) | Yes — **cannot be injected into singletons** |
| `IOptionsMonitor<T>` | Singleton | Yes (`CurrentValue` always fresh) | Yes — `OnChange(...)` callback for singletons / background services |

`OnChange` fires once per provider reload. File-based providers fire when the file is written. **Non-file providers (env vars, command-line, in-memory) never fire.**

```csharp
services.Configure<MyOpts>(builder.Configuration.GetSection("MyOpts"));      // bind a section
services.Configure<MyOpts>(o => { o.Timeout = TimeSpan.FromSeconds(30); });  // code
services.AddOptions<MyOpts>()                                                // combine
        .Bind(builder.Configuration.GetSection("MyOpts"))
        .Configure(o => { o.UserAgent ??= "MyApp/1.0"; });
```

### Named options

`services.Configure<Features>("Personalize", cfg.GetSection("Features:Personalize"));` — consume via `IOptionsSnapshot<Features>.Get("Personalize")`. Default name = `Options.DefaultName` = `string.Empty`. `ConfigureAll<T>` / `PostConfigureAll<T>` apply to every name. Names are case-sensitive.

### Post-configuration

`services.PostConfigure<MyOpts>(o => ...)` runs after every `Configure` for the same `T` and name — useful to enforce invariants regardless of source.

### Validation

```csharp
services.AddOptions<SettingsOptions>()
    .Bind(builder.Configuration.GetSection("MyCustomSettingsSection"))
    .ValidateDataAnnotations()                                    // [Required], [Range], etc.
    .Validate(o => o.VerbosityLevel > o.Scale, "VerbosityLevel must be > Scale.")
    .ValidateOnStart();
```

`ValidateDataAnnotations` requires `Microsoft.Extensions.Options.DataAnnotations`. `ValidateOnStart()` registers an `IHostedService` that resolves `IOptions<T>.Value` on host start. `AddOptionsWithValidateOnStart<T>(name?)` is a one-line shortcut. `OptionsValidationException.Failures` is an `IEnumerable<string>` — log every entry.

For complex rules: implement `IValidateOptions<T>` and register additively via `services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<T>, MyValidator>())`. `ValidateOptionsResult` factories: `Success`, `Skip`, `Fail`.

Recursive validation (.NET 9+): `[ValidateObjectMembers]` on a nested object property; `[ValidateEnumeratedItems]` on a collection. Without these attributes, nested data annotations are not evaluated.

**Compile-time validator (preferred for AOT/ReadyToRun):** `[OptionsValidator] public partial class MyOptsValidator : IValidateOptions<MyOpts> { }` — the `Microsoft.Extensions.Options` source generator emits the validation code at build time, trim/AOT-safe.

`OptionsBuilder<T>.Configure<TDep1..TDep5>((o, dep1, ...) => ...)` accepts up to five DI-resolved dependencies — runs once per options *instance materialization* (lazy). `IOptionsMonitorCache<T>.Clear()` / `TryRemove(name)` forces re-creation through `IOptionsFactory<T>` — runs all `IConfigureOptions<T>` / `IPostConfigureOptions<T>` again on next `CurrentValue` / `Get(name)`.

## Logging

Levels: `Trace=0, Debug=1, Information=2, Warning=3, Error=4, Critical=5, None=6`. Categories: `ILogger<T>` resolves to `typeof(T).FullName`.

Built-in providers: `AddSimpleConsole` / `AddJsonConsole` / `AddSystemdConsole` (`Microsoft.Extensions.Logging.Console`); `AddDebug`; `AddEventSourceLogger` (surfaces as `Microsoft-Extensions-Logging` ETW/EventPipe events); `AddEventLog` (Windows Event Log); `AddTraceSource` (`System.Diagnostics.TraceSource`). Production sinks (Application Insights, OTel, Serilog, NLog) are external.

```csharp
builder.Logging
    .ClearProviders()
    .AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .AddJsonConsole()
    .AddEventSourceLogger();
```

Filter-rule selection per `(provider, category)`:

1. Match rules whose provider matches (alias or full type name); else fall back to provider-less rules.
2. Of those, pick rules with the longest matching category prefix; else take rules with no category.
3. If multiple remain, **last one wins**.
4. If still none, use `SetMinimumLevel`.

`Logging.{Provider}.LogLevel.*` overrides `Logging.LogLevel.*` for that provider only. Override via env var: `Logging__LogLevel__Microsoft=Warning`.

```csharp
builder.Logging
    .SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft", LogLevel.Warning)
    .AddFilter<ConsoleLoggerProvider>("MyApp.Hot", LogLevel.Trace);
```

Structured templates, `[LoggerMessage]` source-gen, scopes, redaction, and OTel logs configuration are owned by `dotnet-diagnostics` § Logging — not duplicated here.

## Generic Host

Builders: `HostApplicationBuilder` (modern, property-based) ← `Host.CreateApplicationBuilder(args)`; `IHostBuilder` (legacy callback-based) ← `Host.CreateDefaultBuilder(args)`; `WebApplicationBuilder` (ASP.NET Core); `DistributedApplicationBuilder` (Aspire — load `dotnet-aspire`).

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<MyOpts>(builder.Configuration.GetSection("MyOpts"));
builder.Logging.AddJsonConsole();

using IHost host = builder.Build();
await host.RunAsync();
```

`CreateApplicationBuilder` sets content root to `Directory.GetCurrentDirectory()`; loads host config (env vars `DOTNET_*` → command-line) for `IHostEnvironment` (`DOTNET_ENVIRONMENT`, `DOTNET_CONTENTROOT`, `DOTNET_APPLICATIONNAME`); loads app config (see § Configuration); registers Console + Debug + EventSource (+ EventLog on Windows) logging; in Development enables `ValidateScopes` and `ValidateOnBuild`.

DI services guaranteed after `Build()`: `IConfiguration` / `IConfigurationRoot`; `ILoggerFactory` + open generic `ILogger<>`; `IHostEnvironment` (with `IsDevelopment()`, `IsStaging()`, `IsProduction()`, `IsEnvironment(name)` helpers); `IHostApplicationLifetime` (`ApplicationStarted` / `ApplicationStopping` / `ApplicationStopped` tokens, `StopApplication()`); `IHostLifetime` (default `ConsoleLifetime`); `IServiceProvider`, `IServiceScopeFactory`, `IHost`.

### `IHostedService` and lifecycle

```csharp
public interface IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`StartAsync` runs in **registration order**; `StopAsync` runs in **reverse order**. Both receive a token that cancels after `HostOptions.ShutdownTimeout` (default 30s).

`IHostedLifecycleService` (.NET 8+) adds finer hooks. Start order: `StartingAsync` → `StartAsync` → `StartedAsync` → `ApplicationStarted`. Stop order: `ApplicationStopping` → `StoppingAsync` → `StopAsync` → `StoppedAsync` → `ApplicationStopped`.

`ConsoleLifetime` listens for SIGINT / SIGQUIT / SIGTERM. Shutdown sequence: set `ApplicationStopping`; call `StopAsync` in reverse order with the `ShutdownTimeout` budget; set `ApplicationStopped`; release the host. `Environment.Exit()` does **not** go through this — `finally` blocks won't run. To request shutdown from inside a service, call `IHostApplicationLifetime.StopApplication()`.

```csharp
builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(60);
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;  // or Ignore
    o.ServicesStartConcurrently = true;          // .NET 8+
    o.ServicesStopConcurrently = true;
});
```

`StopHost` (default) crashes the host when `BackgroundService.ExecuteAsync` throws. `Ignore` swallows the failure but logs it as critical.

## Worker Services

Project SDK: `Microsoft.NET.Sdk.Worker`. Created with `dotnet new worker`. Pulls `Microsoft.Extensions.Hosting`.

```csharp
public sealed class Worker(ILogger<Worker> log, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IProcessor>();
                await processor.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Tick failed; continuing"); }
        }
    }
}
```

Idioms: `PeriodicTimer` instead of `Task.Delay` (drift-free, awaits cancellation natively); catch and log inside the loop (uncaught exceptions propagate and by default **stop the host**); always create an explicit scope before resolving scoped services; honor `stoppingToken`. Single-shot worker: call `IHostApplicationLifetime.StopApplication()` in a `finally`, otherwise the host runs forever.

Worker template defaults: `ServerGarbageCollection` is **off** by default; long-running services with non-trivial allocation churn should enable `<ServerGarbageCollection>true</ServerGarbageCollection>`. `ConcurrentGarbageCollection` defaults to `true`. User Secrets requires explicit `Microsoft.Extensions.Configuration.UserSecrets` reference.

## Windows Services and systemd

Windows package: `Microsoft.Extensions.Hosting.WindowsServices`. Target a Windows-only TFM (`net10.0-windows`).

```csharp
builder.Services.AddWindowsService(options => options.ServiceName = ".NET Joke Service");
LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
```

`AddWindowsService` replaces `IHostLifetime` with `WindowsServiceLifetime` (talks to the SCM via `ServiceBase`); sets `IHostEnvironment.ContentRootPath` to `AppContext.BaseDirectory` so the service finds files relative to the executable; adds the EventLog provider (default filter `Warning`). Install via `sc.exe create / failure / start / stop / delete`.

For SCM-driven recovery to fire, the process must exit with a non-zero code. With `BackgroundServiceExceptionBehavior.StopHost` (the .NET 6+ default) the host stops cleanly with exit code 0, so SCM ignores the failure. Force a non-zero exit on unhandled exceptions: `catch (Exception ex) { logger.LogError(ex, "..."); Environment.Exit(1); }`.

Linux equivalent: `Microsoft.Extensions.Hosting.Systemd` package, `builder.Services.AddSystemd();` — replaces lifetime with `SystemdLifetime` (responds to `SIGTERM` from `systemctl stop`) and adds the systemd console formatter (severity prefixes that `journalctl` understands).

## Caching

Three abstractions, pick by topology: `IMemoryCache` (single process, fast L1, object values — `Microsoft.Extensions.Caching.Memory`); `IDistributedCache` (multi-process/multi-node, byte values — `Microsoft.Extensions.Caching.Abstractions` + provider); `HybridCache` (both layers + stampede protection, .NET 9+ — `Microsoft.Extensions.Caching.Hybrid`).

### `IMemoryCache`

```csharp
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = 1024;                              // mandatory if you call SetSize
    o.CompactionPercentage = 0.25;
    o.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
});

cache.GetOrCreateAsync($"prod:{id}", entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
    entry.SlidingExpiration = TimeSpan.FromMinutes(1);
    entry.Size = 1;
    return db.LoadAsync(id, ct);
});
```

`MemoryCacheEntryOptions` knobs: `AbsoluteExpiration` / `AbsoluteExpirationRelativeToNow` (hard deadline); `SlidingExpiration` (bumped on access, capped by absolute); `Size` (requires `SizeLimit`); `Priority` (`Low | Normal | High | NeverRemove` — compaction skips `NeverRemove`); `AddExpirationToken(IChangeToken)` (invalidate on file change / config reload / custom token); `RegisterPostEvictionCallback`. `EvictionReason`: `None | Removed | Replaced | Expired | TokenExpired | Capacity`. Thread-safe; built on `ConcurrentDictionary`.

### `IDistributedCache`

`builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = ...; o.InstanceName = "myapp:"; });`. Also `AddDistributedSqlServerCache(...)` (table created with `dotnet sql-cache create`); dev-only `AddDistributedMemoryCache()` (in-process, NOT distributed).

Surface (byte-only): `Get/GetAsync`, `Set/SetAsync`, `Refresh/RefreshAsync` (resets sliding only), `Remove/RemoveAsync`. `DistributedCacheEntryOptions`: `AbsoluteExpiration`, `AbsoluteExpirationRelativeToNow`, `SlidingExpiration`. No built-in update — `Remove` + `Set`. String helpers `GetString` / `SetString` (UTF-8).

### `HybridCache` (.NET 9+)

Solves the boilerplate of layering `IMemoryCache` + `IDistributedCache` + serialization + stampede protection.

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "redis:6379");
builder.Services.AddHybridCache(o =>
{
    o.MaximumPayloadBytes = 1 * 1024 * 1024;
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(15),         // L2 (distributed)
        LocalCacheExpiration = TimeSpan.FromMinutes(2) // L1 (in-memory)
    };
});

public sealed class WeatherService(HybridCache cache, IWeatherApi api)
{
    public ValueTask<Weather> GetAsync(string city, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            key: $"weather:{city}",
            factory: async ct2 => await api.GetAsync(city, ct2),
            options: null,
            tags: new[] { "weather", $"city:{city}" },
            cancellationToken: ct);

    public ValueTask InvalidateAsync(string city, CancellationToken ct) =>
        cache.RemoveByTagAsync($"city:{city}", ct);
}
```

Features: **stampede protection** (concurrent callers for the same missing key share one factory invocation); two-level lookup (L1 → L2 → factory); tag-based invalidation via `RemoveByTagAsync(tag)` (`*` invalidates all); pluggable `IHybridCacheSerializer<T>` (default `System.Text.Json`); works without `IDistributedCache` (degrades to in-memory + stampede).

## `HttpClient` and `IHttpClientFactory`

The lifetime problem: `new HttpClient()` per request → socket exhaustion + DNS staleness; `static HttpClient` for the process life → no socket exhaustion but DNS is never re-resolved.

Two correct strategies on .NET 10: long-lived static client with `SocketsHttpHandler.PooledConnectionLifetime` set so the underlying connection rotates and DNS re-resolves; or `IHttpClientFactory` (pool of `HttpMessageHandler`s, `HandlerLifetime` default 2 min).

```csharp
private static readonly HttpClient s_client = new(new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    AutomaticDecompression      = DecompressionMethods.All
});
```

Caveat: single static client = single connection pool, single cookie container. **Don't use `IHttpClientFactory` if your app relies on cookies** — handler pooling shares `CookieContainer` across logically distinct callers and loses cookies on rotation.

### Registration

```csharp
builder.Services.AddHttpClient();                                // anonymous
builder.Services.AddHttpClient("github", c =>                   // named
{
    c.BaseAddress = new Uri("https://api.github.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0");
});
builder.Services.AddHttpClient<TodoService>(c =>                // typed — preferred
    c.BaseAddress = new Uri("https://jsonplaceholder.typicode.com/"));
```

Each `CreateClient` returns a *new* `HttpClient` wrapping a *pooled* `HttpMessageHandler`. Disposal of the `HttpClient` does **not** dispose the handler.

### Handler pipeline

```
HttpClient → DelegatingHandler[1] → ... → DelegatingHandler[N] → PrimaryHandler (SocketsHttpHandler)
```

```csharp
builder.Services.AddHttpClient<TodoService>(c => c.BaseAddress = new("https://api.example.com/"))
    .AddHttpMessageHandler<AuthHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .RedactLoggedHeaders(new[] { "Authorization", "Cookie" });
```

For maximum control (opt out of handler rotation, let `PooledConnectionLifetime` drive DNS refresh): `.UseSocketsHttpHandler((h, _) => h.PooledConnectionLifetime = TimeSpan.FromMinutes(2)).SetHandlerLifetime(Timeout.InfiniteTimeSpan)`.

### Resilience (`Microsoft.Extensions.Http.Resilience` — Polly v8)

`builder.Services.AddHttpClient<PaymentClient>().AddStandardResilienceHandler();`

`StandardResilienceOptions` defaults: total request timeout 30s; attempt timeout 10s; retry 3 attempts with exponential backoff + jitter (transient HTTP — `5xx`, `408`, `HttpRequestException`, `TaskCanceledException`); circuit breaker at 10% failure ratio over 30s, breaks 5s; rate limiter 1000 concurrent. Override knobs include `o.Retry.MaxRetryAttempts`, `o.AttemptTimeout.Timeout`, `o.CircuitBreaker.FailureRatio`. Hedging variant: `.AddStandardHedgingHandler(o => { o.Hedging.MaxHedgedAttempts = 3; o.Hedging.Delay = TimeSpan.FromMilliseconds(100); });`.

JSON helpers (`System.Net.Http.Json`): `await http.GetFromJsonAsync<Todo[]>("todos", ct)`; `await http.PostAsJsonAsync("orders", new Order(...), ct)` then `resp.EnsureSuccessStatusCode(); await resp.Content.ReadFromJsonAsync<Order>(ct);`.

### Pitfalls

- **Typed client in singleton service** captures the `HttpClient` for the singleton's lifetime, defeating handler rotation. Either inject `IHttpClientFactory` and call `CreateClient` per call, or use `SocketsHttpHandler` with `PooledConnectionLifetime`.
- **Number of named registrations must be bounded.** Don't derive client name from user input — handler caching leaks per name.
- **Message Handler scope ≠ app DI scope.** A `DelegatingHandler` is resolved from a separate DI scope tied to handler lifetime — don't capture `HttpContext` or scoped data inside handlers.
- **Don't depend on the "factory-default" primary handler type.** Use `ConfigureHttpClientDefaults` to pin one explicitly if your code casts to `HttpClientHandler` / `SocketsHttpHandler`.

## Localization and file globbing

Localization (`Microsoft.Extensions.Localization`): resources in `.resx` files (`<TypeFullName>[.<Locale>].resx`). Wire with `builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");`. Inject `IStringLocalizer<MessageService>` and use `localizer["GreetingMessage"]` or `localizer["DinnerPriceFormat", when, amount]`. `LocalizedString` exposes `Name`, `Value`, `ResourceNotFound`, `SearchedLocation`; implicit conversion to `string` returns `Value`. Culture fallback (`CultureInfo.CurrentUICulture`): `xx-Yyyy-ZZ` → `xx-Yyyy` → `xx` → invariant. Lookup base name = `<RootNamespace>.<TypePath>` with `ResourcesPath` prepended — avoid placing the class itself inside the `Resources` folder, or use `factory.Create(string baseName, string location)` to dodge path doubling.

File globbing (`Microsoft.Extensions.FileSystemGlobbing`): `Matcher` evaluates include/exclude glob patterns against an in-memory file list or a real directory tree. Patterns: `name.ext`, `*.txt`, `*word*`, `dir/**/*`, `**/*.cs`. `?` is **not** a single-char wildcard. Comparisons default to `OrdinalIgnoreCase`.

```csharp
var matcher = new Matcher();
matcher.AddIncludePatterns(new[] { "**/*.md" });
matcher.AddExcludePatterns(new[] { "**/node_modules/**/*", "**/bin/**/*" });
PatternMatchingResult result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo("./parent")));
foreach (string full in matcher.GetResultsInFullPath("./parent")) { /* ... */ }
```

Ordered evaluation (.NET 10) lets exclusion → re-inclusion: `new Matcher(preserveFilterOrder: true)`, then `AddInclude("**/*"); AddExclude("logs/**/*"); AddInclude("logs/important/**/*");`.

## Quick decision matrix

| Question | Answer |
|---|---|
| New service registration | `AddSingleton` (stateless cross-cutting); `AddScoped` (per-request state — DbContext, UoW); `AddTransient` (cheap stateless). |
| Singleton needs scoped state | Inject `IServiceScopeFactory`, child scope per work unit. `IDbContextFactory<T>` for EF. |
| Configuration into a service | `IOptions<T>` (singletons, snapshot at start); `IOptionsSnapshot<T>` (scoped); `IOptionsMonitor<T>` (singletons needing fresh values + change callbacks). |
| Validate options at startup | `AddOptionsWithValidateOnStart<T>()` + `[OptionsValidator]` partial class. |
| Custom settings source | Implement `IConfigurationProvider` + `IConfigurationSource`, expose via `IConfigurationBuilder` extension method. |
| New background work | `BackgroundService` driven by `PeriodicTimer`, scope per iteration, catch + log inside the loop. |
| Long-lived background CPU | Worker Service with `<ServerGarbageCollection>true</ServerGarbageCollection>`. |
| Windows / systemd service | `AddWindowsService` (force `Environment.Exit(1)` for SCM recovery) / `AddSystemd`. |
| HTTP client | Typed client via `AddHttpClient<T>`; `AddStandardResilienceHandler` for retries/breaker/timeout; pin `PooledConnectionLifetime` on `SocketsHttpHandler` to opt out of rotation. |
| Cache layer | `IMemoryCache` (single proc); `IDistributedCache` (Redis/SQL); `HybridCache` for both + stampede + tag invalidation. |
| Bounded async queue producer/consumer | `Channel<T>` — load `dotnet-parallel-and-threading`. |

## Cross-references

- Public docs (Runtime libraries overview): https://learn.microsoft.com/en-us/dotnet/standard/runtime-libraries-overview
- Public docs (DI / DI guidelines): https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection / `..-guidelines`
- Public docs (Configuration / providers / custom): https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration
- Public docs (Options): https://learn.microsoft.com/en-us/dotnet/core/extensions/options
- Public docs (Logging): https://learn.microsoft.com/en-us/dotnet/core/extensions/logging
- Public docs (Generic Host): https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- Public docs (Worker Services): https://learn.microsoft.com/en-us/dotnet/core/extensions/workers
- Public docs (Windows Service): https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
- Public docs (Caching): https://learn.microsoft.com/en-us/dotnet/core/extensions/caching
- Public docs (`HttpClient` guidelines): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
- Public docs (`IHttpClientFactory`): https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory
- Public docs (Localization): https://learn.microsoft.com/en-us/dotnet/core/extensions/localization
- Public docs (File globbing): https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing
- Related skill: `dotnet-aspire` — AppHost orchestration; `ServiceDefaults` (OTel + health + resilience + service discovery) on top of the generic host.
- Related skill: `dotnet-diagnostics` — `[LoggerMessage]` source-gen, structured-logging templates, OTel logs/metrics/traces wiring, scopes.
- Related skill: `dotnet-conventions` § integrations — banned-list (e.g. AutoMapper) and DI-extension authoring rules.
- Related skill: `dotnet-asynchronous-programming` — `BackgroundService` cancellation hygiene, `await using` for `AsyncServiceScope`.
- Related skill: `dotnet-parallel-and-threading` — `Channel<T>`, `Parallel.ForEachAsync` for in-process producer/consumer.
- Related skill: `dotnet-networking` — `SocketsHttpHandler` knobs, HTTP/3, certificate handling.
