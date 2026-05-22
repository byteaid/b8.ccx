---
name: aspnet-core-performance
description: ASP.NET Core 10 performance + tuning reference. Covers hot-path practices (async-all-the-way, no sync-over-async/`async void`, `ReadFormAsync`, `IHttpClientFactory`, allocation discipline), `HttpContext` lifetime + `CreateAsyncScope`, GC tuning (Server vs Workstation, LOH 85,000-byte + container auto-compaction, `runtimeconfig.json`, leak workflow), the five caching surfaces (`IMemoryCache`, `IDistributedCache`, `HybridCache` two-level + stampede + tags, Output Caching, Response Caching), Output Caching Redis backend, response compression (Brotli/Gzip, CRIME/BREACH), short-circuit endpoints, `ObjectPool<T>` + `ArrayPool<T>.Shared`, Kestrel/HTTP/2/HTTP/3 tuning, diagnostic toolchain, load-testing (load/stress/soak/spike).
when_to_use: |
  - Trigger keywords: hot path, async-all-the-way, sync-over-async, async void, ReadFormAsync, IHttpClientFactory, CreateAsyncScope, LOH, ServerGarbageCollection, runtimeconfig, IMemoryCache, IDistributedCache, HybridCache, AddHybridCache, RemoveByTagAsync, IOutputCachePolicy, [ResponseCache], AddResponseCompression, BrotliCompressionProvider, MapShortCircuit, ObjectPool, IResettable, ArrayPool, dotnet-counters, dotnet-gcdump, dotnet-monitor, PerfView, NBomber, k6.
  - Task shapes: optimize a hot path; pick a cache (Memory/Distributed/Hybrid/Output/Response); migrate `IDistributedCache` to `HybridCache`; tune GC for high-density containers; chase a memory leak; pool `byte[]`/`StringBuilder`; add `ShortCircuit` for probes; tune Kestrel + HTTP/3; design a load+stress+soak plan.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Program.cs", "**/*.cs", "**/*.csproj"]
---

# ASP.NET Core Performance — Reference

Reference for performance work in ASP.NET Core 10 / .NET 10. Hot path = code on the request pipeline / controller / Razor page that runs on every request or many times per request. Optimize there first.

## Mental model

- Async runs on the thread pool; sync-over-async wastes a TP thread per blocked call. Under load you starve the pool and incur queueing.
- Anything ≥ **85,000 bytes** lands on the Large Object Heap (LOH) — Gen 2 immediately, full-GC-only collection. Avoid LOH allocations on hot paths.
- `HttpContext` is per-request and not thread-safe. Capturing it in long-lived state (fields, fire-and-forget, background tasks) is a leak / crash waiting to happen.
- Pick caching by **scope** (single process vs cluster) and **durability** (per-request vs across restarts). HybridCache is the new default.
- Server-level compression (IIS, Nginx, Apache) usually beats the middleware. Use middleware only when no edge proxy is available.

## Non-negotiable rules

1. **Async all the way** — `async Task<...>` end-to-end. **Never** `Task.Wait` / `.Result` / `GetAwaiter().GetResult()`. **Never** `async void` outside event handlers (request completes at first `await` → writes to `Response` afterward crash the process).
2. **Never** read `Request.Form` directly — `await Request.ReadFormAsync()`. **Never** `new StreamReader(Request.Body).ReadToEnd()` — `await JsonSerializer.DeserializeAsync<T>(Request.Body)`. Kestrel rejects sync body reads by default.
3. **Never store `HttpContext`** in a field, or capture in a fire-and-forget task. Copy primitives into locals first; `IServiceScopeFactory.CreateAsyncScope()` for scoped services in background tasks.
4. **Always use `IHttpClientFactory`** for outbound HTTP. `new HttpClient()` per call → `TIME_WAIT` socket exhaustion.
5. **Don't allocate ≥ 85 KB on hot paths.** Use `ArrayPool<byte>.Shared`, `Span<T>` / `Memory<T>`, `IBufferWriter<T>`, JSON source generators.
6. **Don't `GC.Collect()` in production** — diagnostics only.
7. **`HttpRequest.ContentLength` is `null`** when the header is absent. `null > 1024` is `false` — guard explicitly.
8. **Don't write to a started response** — guard with `Response.HasStarted` or register `Response.OnStarting(...)`. Don't call `next()` after writing.

## Hot-path checklist (printable)

- [ ] Whole call stack async; no `Task.Wait` / `.Result`.
- [ ] `ReadFormAsync` / `DeserializeAsync(Request.Body)` — never sync body access.
- [ ] No `HttpContext` capture in fields, threads, or fire-and-forget.
- [ ] No allocations ≥ 85 KB on hot paths; pool buffers via `ArrayPool` / `ObjectPool`.
- [ ] `IHttpClientFactory` for all outbound HTTP.
- [ ] `System.Text.Json` source generators on serialized types (also a Native AOT requirement).
- [ ] Output Caching for UI; HybridCache for new code; Response Caching only for public GET/HEAD APIs.
- [ ] Brotli + Gzip compression behind IIS/Nginx where possible.
- [ ] Rate-limiting policies attached to all public endpoints.
- [ ] Per-endpoint request timeouts on long-running paths.
- [ ] `ShortCircuit` on `robots.txt` / `favicon.ico` and trivial probes.
- [ ] Server GC + concurrent GC enabled; Workstation GC only for high-density containers.
- [ ] HTTP/2 enabled; HTTP/3 where supported.
- [ ] In-process IIS hosting on Windows, or Kestrel directly.
- [ ] `dotnet-counters` baseline captured pre-deploy.
- [ ] Load test in Release / Production env before each release; soak test for leaks.

## Fire-and-forget done right

```csharp
[HttpGet("/fire")]
public IActionResult GoodFireAndForget([FromServices] IServiceScopeFactory scopeFactory)
{
    string path = HttpContext.Request.Path;            // copy primitives BEFORE Task.Run
    _ = Task.Run(async () =>
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ContosoDbContext>();
        ctx.Contoso.Add(new Contoso());
        await ctx.SaveChangesAsync();
    });
    return Accepted();
}
```

For durable background work prefer `IHostedService` / `BackgroundService` over fire-and-forget — see `dotnet-extensions`.

## Memory & GC tuning

Generations: short-lived (per-request) objects stay in Gen 0; singletons typically migrate to Gen 2. Lower gens collected more often. Objects ≥ 85,000 bytes go directly to LOH (Gen 2; not compacted by default; collected only during full Gen 2 GCs). **In containers since .NET Core 3.0, LOH is auto-compacted.**

Manually compact LOH (sparingly):

```csharp
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
```

| | Workstation GC | Server GC |
|---|---|---|
| Optimized for | Desktop | Server (default for ASP.NET Core) |
| Threads | Single | One per logical core |
| Collections | Many/sec under load | Fewer, larger |
| Working set | Low | High |
| When useful | High-density hosting, small containers, single-core | Standard web servers |

Server GC is **not available** on a single-core machine — falls back to workstation. Toggle:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

```json
{ "runtimeOptions": { "configProperties": {
  "System.GC.Server": true, "System.GC.Concurrent": true,
  "System.GC.RetainVM": false, "System.GC.HeapHardLimit": 0
}}}
```

For small containers / high-density hosting, Workstation GC can outperform Server GC — measure first.

Memory leak culprits: `static` fields / `ConcurrentBag<>` / event handlers; captured closures held by long-lived delegates; improperly disposed `IDisposable` (e.g., `PhysicalFileProvider` retains native file watcher resources); `HttpClient` per-request (port exhaustion). Symptoms — working set + Allocated memory grow linearly under load and never plateau, even after Gen 2 GC.

Detection workflow: Task Manager / `dotnet-counters` to confirm growth → `dotnet-counters monitor --refresh-interval 1 -p <PID> System.Runtime` (watch `gen-2-gc-count`, `loh-size`, `gc-heap-size`) → `dotnet-dump collect -p <PID>` or `dotnet-gcdump collect` → analyze in Visual Studio / PerfView (retainers, root chains).

GC cannot collect native memory. Wrap with `IDisposable` and dispose deterministically. Detail on GC theory → `dotnet-garbage-collection`.

## Caching strategy

| Surface | Scope | Backing | Tagging | Stampede | Best for |
|---|---|---|---|---|---|
| `IMemoryCache` | Single process | Server RAM | No | No | Sticky web farm or single node |
| `IDistributedCache` | Cluster | Redis / SQL Server / Postgres / Cosmos / NCache / in-memory dev | No | No | Stateless cluster, session, antiforgery key ring |
| `HybridCache` (.NET 9+) | Process + cluster | L1 in-process + L2 `IDistributedCache` | Yes | **Yes** | New code; replaces both above |
| Output Caching middleware | Server | `IOutputCacheStore` (memory or Redis) | Yes | Yes (resource locking) | UI apps, server-controlled full-response caching |
| Response Caching middleware | Server | In-memory only | No | No | Public GET/HEAD APIs honoring HTTP cache headers |

### `IMemoryCache`

```csharp
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024;                    // unitless; only enforced if every entry sets Size
    options.CompactionPercentage = 0.25;         // when over SizeLimit, evict 25 %
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
});

var v = await cache.GetOrCreateAsync(CacheKeys.Entry, entry =>
{
    entry.SlidingExpiration = TimeSpan.FromSeconds(3);
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20);
    return Task.FromResult(DateTime.UtcNow);
});
```

Rules: pair sliding with absolute (sliding alone may never expire under continuous access); expiration is NOT background-driven — activity triggers a scan; `SizeLimit` requires every entry to set `Size`; `Compact(0.25)` evicts in this order: expired → lowest priority → LRU → earliest absolute → earliest sliding (`CacheItemPriority.NeverRemove` items are exempt); cache dependencies via `CancellationChangeToken`; **no built-in stampede protection** — multiple concurrent callbacks may execute on cache miss.

### `IDistributedCache`

Methods: `Get/Async`, `Set/Async`, `Refresh/Async` (resets sliding), `Remove/Async`. Values are `byte[]`. Implementations: Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`, recommended for prod), SQL Server (`Microsoft.Extensions.Caching.SqlServer`; provision with `dotnet sql-cache create`), Postgres (`Microsoft.Extensions.Caching.Postgres`; `AddDistributedPostgresCache`), Cosmos (`Microsoft.Extensions.Caching.Cosmos`), NCache, in-memory dev (`AddDistributedMemoryCache`).

```csharp
builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = builder.Configuration.GetConnectionString("MyRedisConStr");
    o.InstanceName  = "SampleInstance";
});
```

Recommendations: Redis usually beats SQL Server on throughput/latency. If using SQL Server, dedicate the instance to cache (sharing with app data degrades both). Don't override built-in registration lifetimes.

### `HybridCache` (recommended for new code)

```csharp
builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes  = 1024 * 1024;     // 1 MB default
    options.MaximumKeyLength     = 1024;
    options.DefaultEntryOptions  = new HybridCacheEntryOptions
    {
        Expiration           = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
})
.AddSerializer<MyType, MyTypeProtoSerializer>()
.AddSerializerFactory<GoogleProtobufSerializerFactory>();

builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration.GetConnectionString("RedisConnectionString"));
```

Stateless overload (preferred):

```csharp
public class SomeService(HybridCache cache)
{
    public Task<string> GetAsync(string name, int id, CancellationToken ct = default)
        => cache.GetOrCreateAsync(
            $"{name}-{id}",
            async cancel => await LoadFromSourceAsync(name, id, cancel),
            cancellationToken: ct);
}
```

Stateful overload (avoids closure allocations on very hot paths):

```csharp
return await cache.GetOrCreateAsync(
    $"{name}-{id}", (name, id, obj: this),
    static async (state, ct) => await state.obj.LoadAsync(state.name, state.id, ct),
    cancellationToken: token);
```

Tags + invalidation:

```csharp
var tags = new[] { "tag1", "tag2" };
await cache.GetOrCreateAsync(key, factory, new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(1) }, tags, ct);
await cache.RemoveByTagAsync("tag1", ct);
await cache.RemoveAsync(key, ct);
await cache.RemoveByTagAsync("*", ct);    // wildcard invalidates everything
```

Key features: two-level (L1 in-process + L2 `IDistributedCache`); stampede protection — only ONE caller per key invokes the factory; configurable serialization (default `System.Text.Json`); tags are *logical* (in-memory entries on other servers don't get evicted by `RemoveByTagAsync` until natural expiry); reuse instances safely with `sealed` + `[ImmutableObject(true)]`; avoid `byte[]` allocations via `IBufferDistributedCache`-aware preview backends; works back to .NET Standard 2.0. Cache key uniqueness is the caller's job — `$"order/{customerId}/{orderId}"` style; never put raw user input directly. Wildcard tag `*` is reserved.

### Output caching

Server-controlled, configurable independently of HTTP headers, with policies + tags + resource locking. Wiring + policies + per-endpoint application → `aspnet-core-fundamentals` § output-caching. Performance angles:

Defaults: caches HTTP **200** + GET/HEAD only; skips responses that set cookies; skips authenticated requests. Override via custom `IOutputCachePolicy` (`CacheRequestAsync` / `ServeFromCacheAsync` / `ServeResponseAsync`).

Storage:
- Default: in-process `MemoryCache` (lost on restart, per-server).
- Redis (multi-node consistency):
  ```csharp
  builder.Services.AddStackExchangeRedisOutputCache(o =>
  {
      o.Configuration = builder.Configuration.GetConnectionString("MyRedisConStr");
      o.InstanceName  = "SampleInstance";
  });
  ```
- Do NOT use plain `IDistributedCache` — lacks atomic ops needed for tagging.

Resource locking is ON by default — mitigates stampede. Disable per-policy with `SetLocking(false)`.

Cache revalidation: automatic when client sends `If-None-Match` / `If-Modified-Since` and the response carries `ETag` / freshness — server returns 304 instead of body. Just set the ETag in the handler.

`UseCors` must come BEFORE `UseOutputCache`; `UseOutputCache` must come AFTER `UseRouting` if endpoint metadata is used.

### Response caching (HTTP-spec)

Honors RFC 9111 cache directives. Useful only for **public GET/HEAD APIs** — browsers send `Cache-Control: max-age=0` on F5, defeating it for UI apps. Use Output Caching for UI.

`[ResponseCache]` attribute (sets HTTP headers; works with or without the middleware): `Duration` (`Cache-Control: max-age=<sec>`), `Location = Any|Client|None`, `NoStore = true`, `VaryByHeader`, `VaryByQueryKeys` (middleware-only; `*` = all), `CacheProfileName`. Cache profiles via `o.CacheProfiles.Add("Default30", new CacheProfile { Duration = 30 })`.

Conditions for caching (every must hold): 200 status; GET/HEAD; no `Authorization` header; `Cache-Control` valid and `public`; no `Pragma: no-cache`; no `Set-Cookie`; `Vary` not `*`; body within `MaximumBodySize`; total within `SizeLimit`. **Antiforgery middleware sets `Cache-Control: no-cache, Pragma: no-cache` → kills response caching for forms.**

Test with Fiddler or Firefox Developer (browsers send `Cache-Control: no-cache` on F5).

## Response compression

`Microsoft.AspNetCore.ResponseCompression`. Use server-based compression (IIS, Nginx, Apache) when available — middleware loses the perf race. Fall back to middleware on Kestrel-only / HTTP.sys-only deployments.

```csharp
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;     // CRIME/BREACH risk — see below
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "image/svg+xml" });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>  (o => o.Level = CompressionLevel.SmallestSize);
app.UseResponseCompression();   // BEFORE any middleware that writes bodies
```

If no providers explicitly added → both Brotli and Gzip default. If any provider added → ONLY the ones added are used (re-add defaults explicitly). `CompressionLevel`: `Fastest` (default), `Optimal`, `SmallestSize`, `NoCompression`. Don't compress already-compressed assets (PNG, JPEG, MP4, woff2, .br/.gz files); files smaller than ~150–1000 bytes (overhead exceeds savings).

Headers — middleware automatically removes `Content-Length` and `Content-MD5` after compression; adds `Vary: Accept-Encoding`; inspects `Content-Type` against MIME list (wildcards `text/*` NOT supported).

HTTPS pitfall: `EnableForHttps = false` by default. Brotli/Gzip on dynamic HTTPS responses with reflected user input enables CRIME/BREACH. Mitigate via antiforgery tokens + per-request entropy if you must enable. Behind Nginx — Nginx strips `Accept-Encoding` from forwarded requests by default → middleware sees no header → no compression. Configure pass-through.

## Short-circuit middleware

Routes that bypass the rest of the pipeline (auth, CORS, custom middleware after `UseRouting`). Use for boring probe traffic.

```csharp
app.MapGet("/short-circuit", () => "ok").ShortCircuit();
app.MapGet("/health", () => Results.Ok("Healthy")).ShortCircuit(200);
app.MapShortCircuit(404, "robots.txt", "favicon.ico", ".well-known/security.txt");
```

Bypassed: `UseAuthentication`, `UseAuthorization`, `UseCors`, `UseRateLimiter` (when after `UseRouting`), any custom middleware after `UseRouting`. Still runs (before `UseRouting`): `UseExceptionHandler`, `UseHttpLogging`, custom logging middleware. `.ShortCircuit()` on an endpoint that also calls `.RequireAuthorization()` / `.RequireCors()` throws `InvalidOperationException`.

Use cases: `robots.txt`, `favicon.ico`, `sitemap.xml`, `.well-known/*`, lightweight unauthenticated probes.

## ObjectPool / ArrayPool

### ObjectPool (`Microsoft.Extensions.ObjectPool`)

For objects expensive to allocate, scarce, or predictably / frequently used. Otherwise slower than just `new`. Pool retains a bounded number of instances; **doesn't cap allocations** (only retentions).

Types: `ObjectPool<T>` (`Get()` / `Return(obj)`); `ObjectPoolProvider` / `DefaultObjectPoolProvider`; `IPooledObjectPolicy<T>` / `PooledObjectPolicy<T>`; `DefaultPooledObjectPolicy<T>` (`new T()`); `StringBuilderPooledObjectPolicy`; `IResettable` (auto-reset on `Return`).

```csharp
builder.Services.TryAddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
builder.Services.TryAddSingleton<ObjectPool<ReusableBuffer>>(sp =>
    sp.GetRequiredService<ObjectPoolProvider>().Create(new DefaultPooledObjectPolicy<ReusableBuffer>()));

app.MapGet("/hash/{name}", (string name, ObjectPool<ReusableBuffer> pool) =>
{
    var buffer = pool.Get();
    try
    {
        for (int i = 0; i < name.Length; i++) buffer.Data[i] = (byte)name[i];
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer.Data.AsSpan(0, name.Length), hash);
        return "Hash: " + Convert.ToHexString(hash);
    }
    finally { pool.Return(buffer); }                 // IResettable.TryReset auto-runs
});

public class ReusableBuffer : IResettable
{
    public byte[] Data { get; } = new byte[1024 * 1024];
    public bool TryReset() { Array.Clear(Data); return true; }
}
```

Disposal semantics (`DefaultObjectPoolProvider` + `T : IDisposable`): items not returned → disposed when GC'd; pool disposed by DI → all retained items disposed; after pool disposed `Get` throws `ObjectDisposedException` and `Return(x)` disposes `x`.

### ArrayPool (`System.Buffers.ArrayPool<T>`)

Built-in shared pool: `ArrayPool<byte>.Shared`. Use anywhere a large `byte[]` / `T[]` would be allocated per request. Pair with `IDisposable` wrappers + `HttpContext.Response.RegisterForDispose(...)` for request-scoped lifetime:

```csharp
private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create();

private sealed class PooledArray(int size) : IDisposable
{
    public byte[] Array { get; } = _arrayPool.Rent(size);
    public void Dispose() => _arrayPool.Return(Array);
}

[HttpGet("pooled/{size}")]
public byte[] Get(int size)
{
    var pa = new PooledArray(size);
    new Random().NextBytes(pa.Array);
    HttpContext.Response.RegisterForDispose(pa);     // returns to pool when request ends
    return pa.Array;
}
```

Effect: dramatic reduction in allocated bytes and Gen 0 collections under load.

## HttpClient

`HttpClient` implements `IDisposable` but is **designed for reuse**. Closed instances leave sockets in `TIME_WAIT` → port exhaustion under load. Always go through `IHttpClientFactory` (or a static singleton if you can't add the factory). The factory rotates underlying handlers transparently to honor DNS changes.

```csharp
builder.Services.AddHttpClient("github", c => c.BaseAddress = new Uri("https://api.github.com/"));
```

For typed clients use `AddHttpClient<TClient>()` and let DI inject the configured `HttpClient`. Resilience handler (retries, circuit breakers, hedging) → `dotnet-networking`.

## Kestrel / HTTP/2 / HTTP/3 tuning

(Listener configuration, TLS / SNI, HTTP/3 requirements, IIS hosting model → `aspnet-core-servers-and-hosting`.) Tuning levers — defaults are good for most apps; tune only with measurement:

```csharp
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxConcurrentConnections           = null;     // unlimited default
    o.Limits.MaxRequestBodySize                 = 30_000_000;
    o.Limits.MaxRequestHeadersTotalSize         = 32_768;
    o.Limits.MaxRequestLineSize                 = 8_192;
    o.Limits.RequestHeadersTimeout              = TimeSpan.FromSeconds(30);
    o.Limits.KeepAliveTimeout                   = TimeSpan.FromSeconds(130);

    // HTTP/2
    o.Limits.Http2.MaxStreamsPerConnection      = 100;
    o.Limits.Http2.HeaderTableSize              = 4096;
    o.Limits.Http2.MaxFrameSize                 = 16_384;
    o.Limits.Http2.MaxRequestHeaderFieldSize    = 16_384;
    o.Limits.Http2.InitialConnectionWindowSize  = 1_048_576;
    o.Limits.Http2.InitialStreamWindowSize      = 524_288;
    o.Limits.Http2.KeepAlivePingDelay           = TimeSpan.FromSeconds(30);
    o.Limits.Http2.KeepAlivePingTimeout         = TimeSpan.FromSeconds(15);

    // HTTP/3 (QUIC; requires TLS + supported OS)
    o.Limits.Http3.HeaderTableSize              = 4096;
    o.Limits.Http3.MaxRequestHeaderFieldSize    = 16_384;

    o.ListenAnyIP(443, lo =>
    {
        lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;   // ALPN negotiates
        lo.UseHttps();
    });
});
```

Notes: enable HTTP/3 by setting `HttpProtocols.Http1AndHttp2AndHttp3` and serving HTTPS (clients negotiate via Alt-Svc); QUIC requires `Microsoft.AspNetCore.Server.Kestrel.Transport.Quic` and a supported runtime; Kestrel timeouts are paused while a debugger is attached; don't write synchronously to `Response.Body` — Kestrel rejects sync I/O by default; if you absolutely must (legacy), set `IHttpBodyControlFeature.AllowSynchronousIO = true` per-request, but redesign instead.

## Diagnostic tools

| Tool | Use for |
|---|---|
| Visual Studio Diagnostic Tools | First-pass profiling at dev time |
| Application Insights | APM, distributed tracing, dependencies, on-demand Profiler |
| **PerfView** | Windows ETW + GC pause time, %CPU in GC, gen counts, TP starvation |
| **dotnet-counters** | EventCounters live (`gen-2-gc-count`, `working-set`, `cpu-usage`, `requests-per-second`, `tp-thread-count`) |
| **dotnet-trace** | EventPipe (cross-platform) — CPU sampling, GC events, runtime + ASP.NET events |
| **dotnet-dump** | Process dump for post-mortem analysis with SOS |
| **dotnet-gcdump** | Heap snapshot — retainer chains, leak hunting (in-proc) |
| **dotnet-monitor** | Always-on collector; HTTP API for traces/dumps/logs (cluster diagnostics) |
| **dotnet-sos** | SOS plugin for native debug analysis |
| Windows Performance Toolkit (WPR/WPA) | Windows kernel + ETW (deep OS visualization) |
| **PerfCollect** | Linux (perf + LTTng) — collect on Linux, analyze in PerfView on Windows |
| MiniProfiler | Per-request inline timings (dev only) |
| dotTrace / dotMemory (JetBrains) | Commercial profiling alternative |

EventPipe is the cross-platform substrate; `dotnet-trace`/`dotnet-counters`/`dotnet-monitor`/`dotnet-gcdump` sit on top. Common knobs:

```bash
dotnet-counters monitor -p <PID> --refresh-interval 1 \
  System.Runtime Microsoft.AspNetCore.Hosting Microsoft.AspNetCore.Server.Kestrel \
  Microsoft.AspNetCore.RateLimiting

dotnet-trace collect -p <PID> --providers Microsoft-DotNETCore-SampleProfiler \
  --duration 00:00:30 -o cpu.nettrace

dotnet-gcdump collect -p <PID> -o leak.gcdump
dotnet-dump  collect -p <PID> -o full.dmp

dotnet tool install -g dotnet-monitor && dotnet monitor collect
```

PerfView GC indicators: `% Time in GC` (target < 10 % under steady load), `Pause Time` (target < 200 ms; long pauses indicate Gen 2 / LOH), counts of Gen 0 vs 1 vs 2 (Gen 2 should be rare).

Detailed runtime-level diagnostics → `dotnet-diagnostics`.

## Load and stress testing

Load test in **Release** mode against the **Production** environment configuration. Debug builds aren't optimized; Development env toggles extra logging that distorts results.

| Tool | Strength |
|---|---|
| Azure Load Testing | Managed, high-scale, JMeter ingest |
| Apache JMeter | GUI, scriptable, mature |
| k6 | JS scripting, modern, CI-friendly |
| NBomber | C#-native scenarios |
| Gatling | Scala DSL, throughput-oriented |
| Locust | Python scripting |
| wrk / Bombardier / ApacheBench (`ab`) | Quick CLI baselines |
| Vegeta | Constant-rate Go tool, ideal for SLA-style tests |
| Crank | ASP.NET team's internal benchmarking driver |

Visual Studio 2019 cloud-based load testing is **deprecated** — do not adopt.

Patterns:
- **Load test** — validate normal throughput + latency target (e.g., P95 < 200 ms at 1000 RPS for 30 min).
- **Stress test** — ramp until failure to find the cliff; verify recovery.
- **Soak test** — long duration (hours) at moderate load to surface leaks (rising allocated bytes / working set / Gen 2 counts).
- **Spike test** — sudden 10× load step to stress autoscale + queues.

Pair load tests with `dotnet-counters` running on the SUT to capture runtime metrics in lock-step. For rate-limit policy validation specifically: stress-test policies before production (Azure Load Testing + JMeter recommended).

## Cross-references

- Public docs (best practices): https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices?view=aspnetcore-10.0
- Memory & GC: https://learn.microsoft.com/en-us/aspnet/core/performance/memory?view=aspnetcore-10.0
- Caching overview: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/overview?view=aspnetcore-10.0
- Output caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0
- HybridCache: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
- IMemoryCache: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0
- IDistributedCache: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-10.0
- Response compression: https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression?view=aspnetcore-10.0
- Diagnostic tools: https://learn.microsoft.com/en-us/aspnet/core/performance/diagnostic-tools?view=aspnetcore-10.0
- ObjectPool: https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool?view=aspnetcore-10.0
- Load tests: https://learn.microsoft.com/en-us/aspnet/core/test/load-tests?view=aspnetcore-10.0
- Related: `aspnet-core-fundamentals` — middleware order, output caching API, rate limiting algorithms.
- Related: `aspnet-core-servers-and-hosting` — Kestrel listener config, IIS in-proc, request timeouts middleware.
- Related: `aspnet-core-http-apis` — Native AOT (`JsonSerializerContext`).
- Related: `aspnet-core-advanced-features` — body buffering, `EnableBuffering`, `FileBufferingWriteStream`.
- Related: `dotnet-asynchronous-programming` — async-all-the-way, `IAsyncEnumerable<T>`, `ValueTask`.
- Related: `dotnet-garbage-collection` — GC theory, ephemeral segments, LOH internals.
- Related: `dotnet-extensions` — `IHostedService` / `BackgroundService` for durable background work, HybridCache feature surface.
- Related: `dotnet-networking` — `HttpClient` resilience handler, retries, Polly.
- Related: `dotnet-diagnostics` — runtime-level profilers / dump analysis.
- Related: `dotnet-conventions` — banned third-party libs.
