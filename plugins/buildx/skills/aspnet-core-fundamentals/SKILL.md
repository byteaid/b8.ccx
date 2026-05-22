---
name: aspnet-core-fundamentals
description: ASP.NET Core 10 fundamentals reference. Covers the minimal hosting model (`WebApplication`/`CreateSlimBuilder`), DI lifetimes + keyed services + scope validation, configuration binding, options (`IOptions`/`Snapshot`/`Monitor`, `ValidateOnStart`), `ILogger` + `LoggerMessage` source generator, middleware pipeline (canonical order, convention vs `IMiddleware`, `Map`/`MapWhen`/`UseWhen`, short-circuit), endpoint routing (templates, constraints, parameter transformers, route groups, `LinkGenerator`), error handling (`UseExceptionHandler`/`IExceptionHandler`, RFC 9457 ProblemDetails), `MapStaticAssets`, file providers, change tokens, request features, antiforgery, output caching, rate limiting, decompression/compression, host filtering, forwarded headers, URL rewriting, health checks.
when_to_use: |
  - Trigger keywords: WebApplication, CreateSlimBuilder, AddKeyedSingleton, IOptionsSnapshot, ValidateOnStart, ILogger, LoggerMessage, MapGroup, MapWhen, IMiddleware, LinkGenerator, parameter transformer, UseExceptionHandler, IExceptionHandler, ProblemDetails, MapStaticAssets, IChangeToken, HttpContext.Features, AddAntiforgery, AddOutputCache, AddRateLimiter, UseResponseCompression, UseForwardedHeaders, UseRewriter, AddHealthChecks.
  - Task shapes: scaffold `Program.cs`; pick a service lifetime; configure typed options with validation; author custom middleware; design an endpoint route table with groups and filters; produce ProblemDetails; install rate limiting/output caching/antiforgery; debug a captured-dependency failure.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Program.cs", "**/Startup.cs", "**/appsettings*.json", "**/*.csproj"]
---

# ASP.NET Core Fundamentals — Reference

Reference for ASP.NET Core 10 hosting, DI, configuration, middleware, routing, and error handling.

## Mental model

- An ASP.NET Core app is an `IHost` whose hosted services include an `IServer` that accepts HTTP and pushes requests through a `RequestDelegate` pipeline.
- `WebApplication` IS-A `IHost` / `IApplicationBuilder` / `IEndpointRouteBuilder`. `Build()` freezes services.
- Configuration is one flat key/value space (`:` separator) built from layered sources; later wins. `IOptions<T>` reads it strongly typed.
- Middleware pipeline is order-sensitive. `UseRouting` matches; the implicit terminal middleware executes the endpoint.
- DI is constructor-first. Three lifetimes + keyed variants (.NET 8+). `ValidateScopes` catches captured-dependency bugs in Dev.

## Non-negotiable rules

1. **`WebApplication.CreateBuilder(args)`** for new web apps. `CreateSlimBuilder` only for Native AOT minimal-API templates.
2. **Never register services after `Build()`.** `app.Services` is read-only.
3. **Don't capture scoped/transient in singletons.** Inject `IServiceScopeFactory`, create a scope per unit of work.
4. **Don't dispose container-resolved services.** The container disposes them. `AddSingleton(new T())` is NOT disposed by the container.
5. **Constructor injection only** — no service-locator (`sp.GetService<T>()`) outside composition.
6. **Middleware order:** `UseForwardedHeaders` first, `UseCors` → `UseAuthentication` → `UseAuthorization`, `UseAntiforgery` after auth and before endpoints.
7. **`.ValidateOnStart()`** on options that gate startup — fail fast at boot.
8. **`:` separator** in code, `__` in env vars. `GetSection` never returns null.

## Hosting model

| Builder | Use |
|---|---|
| `WebApplication.CreateBuilder(args)` | Default. Full services (logging, config, Kestrel, IIS, host filtering, forwarded headers when env enabled). |
| `WebApplication.CreateSlimBuilder(args)` | AOT/trim-friendly. |
| `WebApplication.CreateEmptyBuilder(opts)` | No defaults; explicit composition. |
| `Host.CreateApplicationBuilder(args)` | Generic host (workers, console). |

`WebApplicationBuilder` exposes `Services`, `Configuration` (read+write), `Environment`, `Logging`, `Host` (generic-host passthrough — `UseServiceProviderFactory`, `ConfigureContainer`), `WebHost` (`UseHttpSys`, `UseUrls`, `ConfigureKestrel`), `Metrics`.

`CreateBuilder` automatically adds: content root = cwd; layered host + app config; logging (Console, Debug, EventSource, EventLog on Windows); Kestrel bound to `Kestrel:` section; host filtering; forwarded headers (env-gated); IIS integration; `ValidateScopes` + `ValidateOnBuild` in Dev. Auto-inserted middleware on first `Map*`/`Run`: `UseDeveloperExceptionPage` (Dev), `UseRouting` if missing, terminal endpoint executor, `UseAuthentication`/`UseAuthorization` when registered.

URL binding precedence: command line > env > `app.Urls` > defaults. Env vars: `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`. CLI: `dotnet run --urls https://localhost:7777`.

## Dependency injection

| Lifetime | Method | Reuse scope |
|---|---|---|
| Transient | `AddTransient<TI,T>` | New instance per resolve. |
| Scoped | `AddScoped<TI,T>` | One per scope (per HTTP request). |
| Singleton | `AddSingleton<TI,T>` | One per root container. |

```csharp
services.AddScoped<IMyDep, MyDep>();
services.AddSingleton<IService>(sp => new Service(sp.GetRequiredService<IConfiguration>()));
services.AddSingleton(new MyOptions());                         // NOT auto-disposed
services.TryAddSingleton<IFoo, Foo>();                          // only if missing
services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilter, MyFilter>());

// Multiple impls — IEnumerable<TI> resolves all; bare TI resolves the LAST registered.
services.AddSingleton<IMyDep, MyDep>();
services.AddSingleton<IMyDep, DifferentDep>();
```

Keyed services (.NET 8+):

```csharp
builder.Services.AddKeyedSingleton<ICache, BigCache>("big");
builder.Services.AddKeyedSingleton<ICache, SmallCache>("small");

app.MapGet("/big", ([FromKeyedServices("big")] ICache c) => c.Get("k"));

public class MyHub([FromKeyedServices("cache2")] IStringCache cache) : Hub { }
```

Resolve at runtime: `sp.GetKeyedService<T>(key)` / `GetRequiredKeyedService<T>(key)`. Razor: `[Inject(Key = "name")]`.

```csharp
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });

using var scope = app.Services.CreateScope();
var svc = scope.ServiceProvider.GetRequiredService<IMyDependency>();
```

`HttpContext.RequestServices` exposes the per-request scope.

## Configuration

Default source order (later wins): host config (env `DOTNET_*`, env `ASPNETCORE_*`, args) → `appsettings.json` → `appsettings.{Env}.json` → user secrets (Dev) → env vars → args.

```csharp
builder.Configuration
    .AddJsonFile("config.json", optional: true, reloadOnChange: true)
    .AddInMemoryCollection(new Dictionary<string,string?> { ["Key"] = "Value" })
    .AddEnvironmentVariables(prefix: "MYAPP_")
    .AddCommandLine(args, switchMappings: new() { ["-k1"] = "Key1" });

var pos = builder.Configuration.GetSection("Position").Get<PositionOptions>();
int max = builder.Configuration.GetValue<int>("Limit", defaultValue: 100);
string? conn = builder.Configuration.GetConnectionString("Default");
```

Built-in providers: JSON / INI / XML files, env vars, command line, in-memory dict, user secrets, key-per-file (Docker), Azure Key Vault, Azure App Configuration. Connection-string env-var prefixes auto-map to `ConnectionStrings:*`: `CUSTOMCONNSTR_*`, `MYSQLCONNSTR_*`, `SQLAZURECONNSTR_*`, `SQLCONNSTR_*`.

Trim/AOT-safe binding: `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`.

Custom provider: implement `IConfigurationSource` + `ConfigurationProvider`; override `Load()`, populate case-insensitive `Data`, call `OnReload()`.

## Options pattern

| Interface | Lifetime | Reads updates? | Named? |
|---|---|---|---|
| `IOptions<T>` | Singleton | No | No |
| `IOptionsSnapshot<T>` | Scoped | Yes (per request) | Yes |
| `IOptionsMonitor<T>` | Singleton | Yes (`CurrentValue`, `OnChange`) | Yes |

```csharp
builder.Services.Configure<PositionOptions>(builder.Configuration.GetSection("Position"));

// OptionsBuilder — fluent + validation
builder.Services.AddOptions<KeyOptions>()
    .Bind(builder.Configuration.GetSection("Keys"))
    .ValidateDataAnnotations()
    .Validate(o => o.PrimaryKey.Length >= 16, "PrimaryKey too short")
    .ValidateOnStart();

// Service-driven configuration (up to 5 deps)
builder.Services.AddOptions<MyOpts>()
    .Configure<IConfiguration, IHostEnvironment>((o, cfg, env) =>
        o.SomeKey = cfg["X"] + env.EnvironmentName);

// Named options
builder.Services.Configure<TopItemSettings>("Month", builder.Configuration.GetSection("TopItem:Month"));
public class Reader(IOptionsSnapshot<TopItemSettings> s) { public TopItemSettings Month => s.Get("Month"); }
```

Validation paths (combine freely): DataAnnotations + `.ValidateDataAnnotations()`; `.Validate(predicate, "msg")`; `IValidateOptions<T>` for cross-field; `IValidatableObject` on the POCO; `.ValidateOnStart()` runs at boot. Map a key to a different property name with `[ConfigurationKeyName("json_key_name")]`.

## Environments

`DOTNET_ENVIRONMENT` > `ASPNETCORE_ENVIRONMENT` for `WebApplicationBuilder`; default `Production`. Built-in: `Development`, `Staging`, `Production`. Custom names allowed: `env.IsEnvironment("Testing")`. Set via shell, `launchSettings.json` (local dev only), process env, `WebApplicationOptions.EnvironmentName`, Azure App Settings, `<EnvironmentName>` in `.pubxml`, Docker `ENV` / `-e`.

## Logging

`ILogger<T>` injected by category = FQTN of `T`. Levels: `Trace=0`, `Debug=1`, `Information=2`, `Warning=3`, `Error=4`, `Critical=5`, `None=6`.

Built-in providers: Console (`AddSimpleConsole`, `AddJsonConsole`, `AddSystemdConsole`), Debug, EventSource, EventLog (Windows; defaults to Warning), Azure App Service, Application Insights.

Filtering precedence: provider+category > category > provider > default.

```json
{ "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" },
               "Console":  { "LogLevel": { "Microsoft.Hosting": "Warning" } } } }
```

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter<DebugLoggerProvider>("System", LogLevel.Information);
```

**Always use structured logging** — named placeholders, NOT string interpolation:

```csharp
logger.LogInformation("Order {OrderId} for {Customer} totalled {Total:C}", orderId, customer, total);
```

`LoggerMessage` source generator — trim/AOT-safe, zero-alloc on disabled levels:

```csharp
public static partial class Log
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
                   Message = "Customer {CustomerName} signed in at {SignInAt:O}")]
    public static partial void CustomerSignedIn(this ILogger logger, string customerName, DateTime signInAt);
}
```

Scopes wrap a block with shared key/values: `using (logger.BeginScope("Tx {TxId}", tx.Id)) { ... }`. Log exceptions: `logger.LogError(ex, "Failed processing {Id}", id)` — exception is the FIRST arg. `ILogger` is sync-only; sinks buffer internally.

## Middleware pipeline

`Use` continues, `Run` is terminal, `Map`/`MapWhen` branches without rejoin, `UseWhen` branches and rejoins:

```csharp
app.Use(async (ctx, next) => { /* before */ await next(ctx); /* after */ });
app.Run(async ctx => await ctx.Response.WriteAsync("Hello"));
app.Map("/admin", admin => admin.Run(...));
app.MapWhen(ctx => ctx.Request.Query.ContainsKey("x"), b => b.Run(...));
app.UseWhen(ctx => predicate, b => b.Use(...));
```

Prefer `Use((HttpContext, RequestDelegate))` over `Use((HttpContext, Func<Task>))` — saves two per-request allocations.

**Canonical order:**

```csharp
app.UseExceptionHandler("/Error", createScopeForErrors: true); // non-Dev
app.UseHsts();                                                  // non-Dev + HTTPS
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.MapStaticAssets();              // .NET 9+ replacement for UseStaticFiles
app.UseRouting();
app.UseRateLimiter();
app.UseRequestLocalization();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseSession();
app.UseResponseCompression();
app.UseResponseCaching();
app.UseOutputCache();
app.MapRazorPages();
app.MapControllers();
app.Run();
```

Order rules: `UseCors` → `UseAuthentication` → `UseAuthorization`; `UseCors` before `UseResponseCaching`; `UseRequestLocalization` before culture-checking middleware; `UseRateLimiter` after `UseRouting` if `[EnableRateLimiting]` is used; `UseForwardedHeaders` first.

### Custom middleware

Convention-based — singleton, scoped services in `InvokeAsync`:

```csharp
public class RequestCultureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IMyScopedService svc)
    {
        var q = ctx.Request.Query["culture"];
        if (!string.IsNullOrWhiteSpace(q))
        { var c = new CultureInfo(q!); CultureInfo.CurrentCulture = c; CultureInfo.CurrentUICulture = c; }
        await next(ctx);
    }
}
public static class Ext { public static IApplicationBuilder UseRequestCulture(this IApplicationBuilder a)
    => a.UseMiddleware<RequestCultureMiddleware>(); }
```

Factory-based — per-request activation, scoped services in the constructor:

```csharp
public class FactoryMiddleware(SampleDbContext db) : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next) { await next(ctx); }
}
builder.Services.AddTransient<FactoryMiddleware>();
app.UseMiddleware<FactoryMiddleware>();
```

Short-circuit endpoints (.NET 8+): `app.MapGet("/sc", () => "x").ShortCircuit();` and `app.MapShortCircuit(404, "robots.txt", "favicon.ico");`.

## Routing

Two middlewares: `UseRouting` matches → sets `HttpContext.GetEndpoint()`; the implicit terminal middleware executes. Auth runs between them and sees endpoint metadata.

Templates: `/products/{id}`, `/products/{id?}`, `/{controller=Home}/{action=Index}/{id?}`, `/files/{*path}` (catch-all), `/files/{**path}` (round-trip), `/a{b}c{d}` (complex segment).

Constraints: `int`, `bool`, `datetime`, `decimal`, `double`, `float`, `guid`, `long`, `minlength(n)`, `maxlength(n)`, `length(n)`, `length(min,max)`, `min(n)`, `max(n)`, `range(a,b)`, `alpha`, `regex(pat)`, `required`, `file`, `nonfile`. Combine: `{id:int:min(1)}`. Constraints validate; they don't disambiguate overlapping templates beyond template precedence.

Custom: implement `IRouteConstraint`, register via `services.AddRouting(o => o.ConstraintMap.Add("name", typeof(MyConstraint)))`. Parameter transformer (e.g., slugify): `IOutboundParameterTransformer`, register the same way; reference via `{controller:slugify=Home}`.

Endpoint metadata works on every `Map*`:

```csharp
app.MapGet("/hello", () => "hi")
   .WithName("Hello").WithTags("public")
   .RequireAuthorization().RequireCors("MyPolicy").RequireRateLimiting("api")
   .RequireHost("contoso.com", "*.contoso.com")
   .DisableAntiforgery()
   .Produces<Result>(200).ProducesProblem(404)
   .CacheOutput();
```

Route groups — apply filters/conventions to a prefix; nest freely. Outer filters run first when entering, last when returning:

```csharp
var todos = app.MapGroup("/todos").RequireAuthorization().WithTags("Todos");
todos.MapGet("/", GetAll); todos.MapPost("/", Create);
var v1 = app.MapGroup("/api/v1");
var users = v1.MapGroup("/users").RequireAuthorization("AdminsOnly");
```

Link generation: inject `LinkGenerator`; methods `GetPathByAction`/`GetUriByAction` (MVC), `GetPathByPage`/`GetUriByPage` (Razor Pages), `GetPathByName`/`GetUriByName` (named minimal), `GetPathByAddress<T>`/`GetUriByAddress<T>` (low-level). `LinkParser.ParsePathByEndpointName(name, url)` parses route values out of a URL.

## Error handling

| Layer | Use |
|---|---|
| `UseDeveloperExceptionPage` | Auto in Dev. |
| `UseExceptionHandler("/Error")` | Production. Re-executes through error path. |
| `IExceptionHandler` (.NET 8+) | Chain of typed handlers. Return `false` → next. |
| `UseStatusCodePages*` | Friendly responses for empty error bodies. |
| `AddProblemDetails` + `UseExceptionHandler` | RFC 9457 envelope. |

```csharp
app.UseExceptionHandler("/Error");

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is TimeoutException
        ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError
});

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> log) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    { log.LogError(ex, "Unhandled"); return ValueTask.FromResult(false); }
}
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddProblemDetails();
app.UseExceptionHandler();
```

.NET 10: handled exceptions don't emit diagnostics by default; re-enable per-case via `ExceptionHandlerOptions.SuppressDiagnosticsCallback`.

ProblemDetails (RFC 9457 — `type`, `title`, `status`, `traceId`):

```csharp
builder.Services.AddProblemDetails(o =>
    o.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["nodeId"] = Environment.MachineName);

public class SampleProblemDetailsWriter : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext c) => c.HttpContext.Response.StatusCode == 400;
    public ValueTask WriteAsync(ProblemDetailsContext c)
        => new(c.HttpContext.Response.WriteAsJsonAsync(c.ProblemDetails));
}
builder.Services.AddTransient<IProblemDetailsWriter, SampleProblemDetailsWriter>();
```

Write a ProblemDetails from any middleware via `IProblemDetailsService.WriteAsync(...)`.

## Static files, file providers, change tokens

`MapStaticAssets` (.NET 9+) — preferred. Build-time fingerprinting + pre-compressed gzip/brotli + strong ETags. `UseStaticFiles` always available — no fingerprinting. `UseDefaultFiles` (must precede) maps `/` to `index.html`. `UseFileServer` bundles defaults+static (+ optional dir browse). `UseDirectoryBrowser` lists directories; register `services.AddDirectoryBrowser()` first.

`UseStaticFiles` does NOT do authorization — `wwwroot` is public. Protect via an authorized endpoint OR (.NET 9+) `MapStaticAssets().Add(b => b.Metadata.Add(new AuthorizeAttribute("AdminsOnly")))`.

`IFileProvider` exposes `GetFileInfo(path)`, `GetDirectoryContents(path)`, `Watch(filter)`. Implementations: `PhysicalFileProvider`, `ManifestEmbeddedFileProvider`, `CompositeFileProvider`, `NullFileProvider`. Globs: `*` halts at `/` and `.`; `**` crosses dirs. Set `DOTNET_USE_POLLING_FILE_WATCHER=1` in containers.

`IChangeToken` — `HasChanged`, `ActiveChangeCallbacks`. `ChangeToken.OnChange(producer, action)` returns `IDisposable`:

```csharp
ChangeToken.OnChange(() => config.GetReloadToken(), () => Console.WriteLine("Reloaded"));
```

`CompositeChangeToken` aggregates; `CancellationChangeToken` wraps a `CancellationToken`. Sources: `IConfiguration.GetReloadToken()`, `IFileProvider.Watch(filter)`, `IOptionsMonitor<T>` rebinding, `MemoryCacheEntryOptions.AddExpirationToken`.

## Request features

`HttpContext.Features` is a typed bag (`IFeatureCollection`). Required on every server: `IHttpRequestFeature`, `IHttpResponseFeature`, `IHttpResponseBodyFeature`. Useful additional: `IHttpConnectionFeature` (local/remote address), `IHttpRequestLifetimeFeature` (`RequestAborted`, `Abort()`), `IHttpUpgradeFeature`, `IHttpWebSocketFeature`, `ITlsConnectionFeature` (client cert), `IHttpResetFeature` (HTTP/2/3 RST_STREAM), `IHttpMaxRequestBodySizeFeature`, `ISessionFeature`, `IItemsFeature`, `IServerVariablesFeature` (IIS).

## Antiforgery, output caching, compression, rate limiting

Antiforgery — register, then `app.UseAntiforgery()` after auth and before endpoints. Razor `<form>` tag helper emits `__RequestVerificationToken` automatically. Per-endpoint: `.DisableAntiforgery()` / `.RequireAntiforgery()`. Manual: `IAntiforgery.ValidateRequestAsync(httpContext)`.

```csharp
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "__Host-X-XSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

Output caching:

```csharp
builder.Services.AddOutputCache(o =>
{
    o.AddBasePolicy(b => b.Cache());
    o.AddPolicy("Tagged",  b => b.Tag("tagA").Expire(TimeSpan.FromMinutes(10)));
    o.AddPolicy("ByQuery", b => b.SetVaryByQuery("page", "size"));
});
app.UseOutputCache();
app.MapGet("/news", () => DateTime.UtcNow).CacheOutput("Tagged");
```

Eviction by tag: `IOutputCacheStore.EvictByTagAsync("tagA", ct)`. Default in-memory; Redis store available. Strategy/HybridCache discipline → `aspnet-core-performance`.

Response compression / request decompression:

```csharp
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
app.UseResponseCompression();

builder.Services.AddRequestDecompression();
app.UseRequestDecompression();
```

Rate limiting (.NET 7+) — algorithms: fixed window, sliding window, token bucket, concurrency. Partitioned (per-key) limiters via `RateLimitPartition.Get*`. Place `UseRateLimiter` after `UseRouting` if endpoint-attribute limiters are used.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", o => { o.PermitLimit = 5; o.Window = TimeSpan.FromSeconds(10); });
    options.AddPolicy("PerUser", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
app.UseRateLimiter();
app.MapGet("/x", () => "hi").RequireRateLimiting("fixed");
```

`[EnableRateLimiting("PerUser")]` / `[DisableRateLimiting]` on controllers/actions.

## Host filtering, forwarded headers, URL rewriting, health checks

```csharp
builder.Services.AddHostFiltering(o => o.AllowedHosts = new[] { "example.com", "*.example.com" });
app.UseHostFiltering();   // auto-enabled by CreateBuilder via AllowedHosts config

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));
    o.KnownNetworks.Clear();
});
app.UseForwardedHeaders();   // FIRST — before auth, HSTS, redirects

var rewrite = new RewriteOptions()
    .AddRedirect("redirect-rule/(.*)", "redirected/$1")
    .AddRewrite(@"^rewrite-rule/(\d+)/(\d+)", "rewritten?var1=$1&var2=$2", skipRemainingRules: true)
    .AddRedirectToHttps()
    .AddRedirectToWww();
app.UseRewriter(rewrite);

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddCheck<DatabaseHealthCheck>("db");
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
```

## Quick decision matrix

| Question | Answer |
|---|---|
| New web app entry point | `WebApplication.CreateBuilder(args)` |
| Native AOT minimal API | `CreateSlimBuilder(args)` |
| Service per request | `AddScoped` |
| Two impls of one interface | `AddKeyedSingleton<T>("name", ...)` + `[FromKeyedServices("name")]` |
| Config that updates on file change | `IOptionsMonitor<T>` |
| Validate options at startup | `.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` |
| Custom error response | `AddProblemDetails()` + `UseExceptionHandler()` |
| Per-request middleware with scoped DI | `IMiddleware` (factory-based) |
| Apply auth/CORS/rate-limit to many endpoints | `MapGroup` with chained `Require*` |
| Generate URL to a named endpoint | `LinkGenerator.GetPathByName(name, values)` |
| Static files with strong ETags | `MapStaticAssets()` |
| Cache HTTP responses server-side | `AddOutputCache` + `.CacheOutput("policy")` |

## Cross-references

- Public docs (fundamentals index): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0
- DI: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0
- Configuration / Options: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0
- Routing: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0
- Middleware: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0
- Error handling: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0
- Output caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0
- Rate limiting: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
- Health checks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0
- Related: `aspnet-core-servers-and-hosting` — Kestrel/HTTP.sys/IIS/Docker/App Service, request timeouts, graceful shutdown.
- Related: `aspnet-core-http-apis` — controllers (team forbids minimal APIs), OpenAPI, versioning.
- Related: `aspnet-core-mvc` — MVC views, view components, Tag Helpers.
- Related: `aspnet-core-razor-pages` — `@page` / `PageModel` / page handlers / page filters.
- Related: `aspnet-core-advanced-features` — deep model binding, raw `PipeReader`, raw WebSockets, YARP.
- Related: `aspnet-core-performance` — caching strategy, GC tuning, ObjectPool, load testing.
- Related: `aspnet-core-security` — authentication/authorization wiring.
- Related: `dotnet-conventions` — team C# style, banned patterns.
- Related: `dotnet-extensions` — `IHostedService`, `BackgroundService`.
