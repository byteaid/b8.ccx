---
name: aspnet-core-advanced-features
description: ASP.NET Core 10 advanced / low-level reference. Covers deep model binding (`IModelBinder`/`IModelBinderProvider`, value providers, polymorphic, `[Bind]`/`[BindNever]`, `TryUpdateModelAsync`, .NET 10 `PipeReader` JSON + `ValueSequence`, `JsonSerializerContext` AOT), `Microsoft.Extensions.Validation` `AddValidation()`, middleware authoring (convention vs `IMiddleware`, `Map`/`MapWhen`/`UseWhen`, `MapShortCircuit()`), low-level body IO (`BodyReader`/`BodyWriter`, `EnableBuffering()`, `FileBufferingWriteStream`), URL rewriting (`RewriteOptions`, `AddIISUrlRewrite`, custom `IRule`), `HttpContext.Features`, `IChangeToken` (`OnChange`, `CompositeChangeToken`), request decompression + zip-bomb defense, host filtering, raw WebSockets (`UseWebSockets`, `DangerousEnableCompression`).
when_to_use: |
  - Trigger keywords: IModelBinder, BinderTypeModelBinder, polymorphic binding, TryUpdateModelAsync, PipeReader, BodyReader, BodyWriter, EnableBuffering, FileBufferingWriteStream, RewriteOptions, AddIISUrlRewrite, IRule, IChangeToken, AddRequestDecompression, HostFilteringOptions, UseWebSockets, DangerousEnableCompression, IMiddleware, MapShortCircuit, AddValidation.
  - Task shapes: write a custom `IModelBinder`; bind a polymorphic type; opt request body into multiple reads; transform a response body; author a custom `IRule`; debug `HasValueSequence` after .NET 10 PipeReader; install a custom `IDecompressionProvider`; set a host-filter allowlist; accept a raw WebSocket; choose convention vs `IMiddleware`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Program.cs", "**/*.cs"]
---

# ASP.NET Core Advanced Features — Reference

Reference for advanced / low-level ASP.NET Core 10 plumbing: deep model binding, custom middleware, raw body IO, URL rewriting, request features, change tokens, request decompression, host filtering, raw WebSockets, and a YARP pointer.

## Mental model

- The pipeline is bidirectional. Each middleware can run code before AND after `await next(ctx)`. The response phase runs in REVERSE order.
- Below MVC there is a `RequestDelegate` chain. `IFeatureCollection` is how the server, middleware, and frameworks communicate per-request capabilities and state.
- `Body` (Stream) and `BodyReader`/`BodyWriter` (Pipe) are two views over the same wire. Pipes win on byte-level scanning; Streams remain ubiquitous.
- Change tokens are one-shot signals. After a token fires, observers must request a fresh token from the producer.
- Razor Pages / MVC validation runs after binding; `[ApiController]` short-circuits invalid `ModelState` to 400 + `ValidationProblemDetails`.

## Non-negotiable rules

1. **Don't write to a started response.** Check `HttpResponse.HasStarted` before mutating headers/status. After the response has completed, calling `next` throws `ObjectDisposedException`/`InvalidOperationException`.
2. **Convention middleware is a singleton.** Inject scoped services through `Invoke[Async]` parameters, NEVER through the constructor.
3. **`[FromBody]` reads the body stream once.** Only one parameter per action. Source attributes on properties of the body type are ignored.
4. **`EnableBuffering` ≠ free.** Buffers in memory until threshold (default 30 KB), then spills to a temp file. Always rewind `Request.Body.Position = 0` before downstream readers.
5. **`HostFilteringOptions.AllowedHosts` excludes port numbers.** `*` matches any non-empty host; `*.example.com` matches subdomains but NOT the parent.
6. **Custom `IModelBinder` must NOT write the response or set status codes.** Add `ModelState` errors and let action filters / handler decide.
7. **WebSocket compression is dangerous over TLS** (BREACH-class attacks). Disable for sensitive payloads. CORS does NOT apply to WebSocket upgrades — validate `Origin` via `WebSocketOptions.AllowedOrigins`.
8. **HTTP/2 WebSockets use `CONNECT`, not `GET`.** Controller actions need `[Route]`, NOT `[HttpGet]`.

## Model binding — deep surface

(Source attributes, default order, `[BindProperty]` family, simple types, complex types, collections/dictionaries, records, globalization — see `aspnet-core-mvc` § model-binding for the basics. This section adds depth.)

### Custom `IModelBinder` + `IModelBinderProvider`

```csharp
public class AuthorEntityBinder(AuthorContext db) : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext ctx)
    {
        var name = ctx.ModelName;
        var vpr  = ctx.ValueProvider.GetValue(name);
        if (vpr == ValueProviderResult.None) return;
        ctx.ModelState.SetModelValue(name, vpr);
        if (string.IsNullOrEmpty(vpr.FirstValue)) return;
        if (!int.TryParse(vpr.FirstValue, out var id))
        { ctx.ModelState.TryAddModelError(name, "Author Id must be an integer."); return; }
        ctx.Result = ModelBindingResult.Success(await db.Authors.FindAsync(id));
    }
}

public class AuthorEntityBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext ctx)
        => ctx.Metadata.ModelType == typeof(Author)
            ? new BinderTypeModelBinder(typeof(AuthorEntityBinder))   // DI-aware factory
            : null;
}
builder.Services.AddControllers(o => o.ModelBinderProviders.Insert(0, new AuthorEntityBinderProvider()));
```

`BinderTypeModelBinder(typeof(MyBinder))` resolves the binder from DI per request. Without DI, `new` directly inside the provider. Attribute counterpart bypasses the provider: `[ModelBinder(BinderType = typeof(AuthorEntityBinder))] class Author { }` plus `[ModelBinder(Name = "id")] Author author` on the action parameter.

Best practice: prefer `IParsable<T>` or `TypeConverter` over `IModelBinder` when only `string → T` conversion is needed. Custom binders must NOT set status codes or write responses.

### Polymorphic binding (sketch)

Provider returns a custom binder that switches on a discriminator (e.g., `Kind` property) and delegates to per-subtype binders via `ctx.CreateBinder(ModelMetadata)`. Not recommended for public REST APIs — interop hostile, complicates validation. Prefer JSON polymorphism (`[JsonPolymorphic]` + `[JsonDerivedType]`) instead.

### Custom value providers

Bind from a non-default source (cookies, claims, etc.) — implement `IValueProvider` (`ContainsPrefix`, `GetValue` returning `ValueProviderResult`) + `IValueProviderFactory` (`CreateValueProviderAsync` adds the provider into `ctx.ValueProviders`); register via `o.ValueProviderFactories.Add(new CookieValueProviderFactory())`.

### Manual binding for over-posting protection

`TryUpdateModelAsync` opts into selective property binding without a separate DTO. Does NOT update **constructor-positional** record parameters — only init/settable properties.

```csharp
var i = new Instructor();
if (await TryUpdateModelAsync(i, prefix: "Instructor", x => x.LastName, x => x.HireDate))
{
    _store.Add(i);
    return RedirectToPage("./Index");
}
return Page();
```

### Missing vs invalid values

- **Missing simple value** → no error; nullable → `null`, value type → `default`, complex → `new T()`, `T[]` → `Array.Empty<T>()` (except `byte[]` → `null`).
- **Type-conversion failure** → `ModelState` invalid; the bad input is NOT round-tripped (property is `null`/default). To preserve, expose a separate `string` property and parse manually.

### `[Bind]`, `[BindRequired]`, `[BindNever]`, `[ValidateNever]`

| Attribute | Effect |
|---|---|
| `[Bind("A,B,C")]` | Allowlist of properties to bind (over-posting protection on form data; **does not** affect input formatters). |
| `[BindRequired]` | Adds a `ModelState` error if the value isn't bound (form/query/route — **NOT** body). |
| `[BindNever]` | Property is never bound (also valid on a class to exclude all properties). |
| `[ModelBinder(typeof(MyBinder))]` / `[ModelBinder(Name = "x")]` | Force a specific `IModelBinder` or rename source key. |
| `[ValidateNever]` | Skip validation for the property/parameter. |
| `[BindProperty(Name = "ai_user", SupportsGet = true)]` | Razor Pages — opt into GET binding with renamed source key. |

### .NET 10 PipeReader-based JSON

`JsonSerializer.DeserializeAsync` reads from `PipeReader` rather than `Stream`. Custom `JsonConverter<T>.Read` that called `reader.ValueSpan` directly may break for split sequences. Three remediations:

```csharp
// 1) compatibility flag (temporary)
AppContext.SetSwitch("Microsoft.AspNetCore.UseStreamBasedJsonParsing", true);

// 2) defensive read in Read()
public override T? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
{
    var span = r.HasValueSequence ? r.ValueSequence.ToArray() : r.ValueSpan;
    /* parse span */
}

// 3) handle both ReadOnlySpan and ReadOnlySequence paths optimally
```

`AddValidation()` (`Microsoft.Extensions.Validation` 10.0.0) lifts the validation engine out of the HTTP/MVC stack so background services and minimal APIs can reuse it.

## Middleware authoring

### Convention vs factory

| Aspect | Convention | Factory (`IMiddleware`) |
|---|---|---|
| Activation | Once per app | Once per request |
| Scoped DI | Only via `Invoke[Async]` parameters | Constructor + `InvokeAsync` |
| Strong typing | No (reflection on `Invoke[Async]`) | Yes — interface |
| `UseMiddleware<T>(args…)` extra args | Supported | **Not** supported (`NotSupportedException`) |
| Registration | Type discovered by `UseMiddleware<T>` | Must `services.AddTransient<T>()` |

Convention middleware shape: public ctor with `RequestDelegate next` (DI-allowed for **singleton** services only); public `Task Invoke(HttpContext)` or `InvokeAsync(HttpContext)`; extra `Invoke[Async]` parameters resolved per-request from DI (this is how scoped services are injected).

```csharp
public class RequestCultureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IMessageWriter scoped)
    {
        var c = ctx.Request.Query["culture"];
        if (!string.IsNullOrWhiteSpace(c))
        {
            var ci = new CultureInfo(c!);
            CultureInfo.CurrentCulture = ci; CultureInfo.CurrentUICulture = ci;
        }
        scoped.Write(DateTime.UtcNow.Ticks.ToString());
        await next(ctx);
    }
}
public static class Ext { public static IApplicationBuilder UseRequestCulture(this IApplicationBuilder b)
    => b.UseMiddleware<RequestCultureMiddleware>(); }
```

Factory middleware:

```csharp
public class FactoryActivatedMiddleware(SampleDbContext db) : IMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var k = ctx.Request.Query["key"];
        if (!string.IsNullOrWhiteSpace(k))
        { db.Requests.Add(new Request("Factory", k!)); await db.SaveChangesAsync(); }
        await next(ctx);
    }
}
builder.Services.AddTransient<FactoryActivatedMiddleware>();
app.UseMiddleware<FactoryActivatedMiddleware>();
```

`Use(HttpContext, RequestDelegate)` overload is preferred over `Use(HttpContext, Func<Task>)` — saves two per-request allocations.

### Branching

| Method | Match | Strips matched? | Rejoin? |
|---|---|---|---|
| `Map(path, branchBuilder)` | Path prefix | Yes — moves matched segments to `Request.PathBase` | No |
| `MapWhen(predicate, branchBuilder)` | `Func<HttpContext, bool>` | No | No |
| `UseWhen(predicate, branchBuilder)` | `Func<HttpContext, bool>` | No | **Yes**, when branch lacks terminal middleware |

```csharp
app.Map("/level1", l1 =>
{
    l1.Map("/level2a", b => b.Run(c => c.Response.WriteAsync("/level1/level2a")));
    l1.Map("/level2b", b => b.Run(c => c.Response.WriteAsync("/level1/level2b")));
});

app.UseWhen(c => c.Request.Query.ContainsKey("branch"),
    b => b.Use(async (ctx, next) => { /* extra */ await next(ctx); }));
```

### Short-circuit endpoints

```csharp
app.MapGet("/health", () => "ok").ShortCircuit();              // 200, skip auth/cors/etc.
app.MapShortCircuit(404, "/robots.txt", "/favicon.ico");       // canned 404
```

Requires `UseRouting`; the short-circuit fires immediately after routing, skipping `UseAuthentication` / `UseAuthorization` / `UseAntiforgery`.

## Reading request / writing response bodies

Two abstractions: `HttpRequest.Body` (Stream) vs `HttpRequest.BodyReader` (PipeReader); `HttpResponse.Body` (Stream) vs `HttpResponse.BodyWriter` (PipeWriter). Pipes win on byte-level scanning; setting `Body = newStream` triggers an automatic adapter that re-syncs `BodyReader`/`BodyWriter`.

PipeReader line-splitting skeleton (consumes `\n`-delimited UTF-8 records):

```csharp
while (true)
{
    var read = await reader.ReadAsync();
    var buffer = read.Buffer;
    SequencePosition? pos;
    do
    {
        pos = buffer.PositionOf((byte)'\n');
        if (pos != null)
        {
            var line = buffer.Slice(0, pos.Value);
            results.Add(line.IsSingleSegment ? Encoding.UTF8.GetString(line.First.Span) : Encoding.UTF8.GetString(line.ToArray()));
            buffer = buffer.Slice(buffer.GetPosition(1, pos.Value));
        }
    } while (pos != null);
    reader.AdvanceTo(buffer.Start, buffer.End);
    if (read.IsCompleted) break;
}
```

`HttpResponse.BodyWriter` buffers until `await writer.FlushAsync()`. Call `await Response.StartAsync()` to freeze headers and run `OnStarting` callbacks first; under Kestrel this also ensures memory returned by `GetMemory()` belongs to Kestrel's internal `Pipe`.

### `EnableBuffering` (re-readable request body)

Request body is forward-only by default. To allow multiple reads (logging + binding):

```csharp
app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering();   // default 30 KB threshold, unlimited size
    // or: ctx.Request.EnableBuffering(bufferThreshold: 1024 * 1024, bufferLimit: 50 * 1024 * 1024);

    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    ctx.Request.Body.Position = 0;          // critical — rewind
    await next(ctx);
});
```

Swaps `Request.Body` for a `FileBufferingReadStream` (memory until threshold, then a temp file).

### Response buffering / swapping

`FileBufferingWriteStream` is the symmetric primitive:

```csharp
app.Use(async (ctx, next) =>
{
    var original = ctx.Response.Body;
    using var buffered = new FileBufferingWriteStream();
    ctx.Response.Body = buffered;
    try
    {
        await next(ctx);
        buffered.Position = 0;
        // inspect/modify here
        await buffered.DrainBufferAsync(original);
    }
    finally { ctx.Response.Body = original; }
});
```

Caveats: must run BEFORE anything writes to the response; setting `Content-Length` after wrap is dangerous (let the framework recompute or clear it); `IHttpResponseBodyFeature.DisableBuffering()` opts out of any framework buffering; for HTTP/2/3 trailers see `IHttpResponseTrailersFeature`.

Useful features for body manipulation: `IHttpRequestBodyDetectionFeature.CanHaveBody`; `IHttpResponseBodyFeature.DisableBuffering()`; `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` (per-request override).

## URL rewriting & redirects

Package `Microsoft.AspNetCore.Rewrite` (built-in). `Redirect` = client round-trip + visible 3xx + `Location`; `Rewrite` = server-side, no round-trip, response served from target URL with original status.

```csharp
var options = new RewriteOptions()
    .AddRedirectToHttpsPermanent()
    .AddRedirectToWwwPermanent()                     // 308
    .AddRedirect("redirect-rule/(.*)", "redirected/$1")
    .AddRedirect("legacy/(.*)", "new/$1", StatusCodes.Status301MovedPermanently)
    .AddRewrite(@"^rewrite-rule/(\d+)/(\d+)", "rewritten?var1=$1&var2=$2", skipRemainingRules: true)
    .Add(new RedirectImageRequests(".png", "/png-images"));

using var apache = File.OpenText("ApacheModRewrite.txt");
using var iis    = File.OpenText("IISUrlRewrite.xml");
options.AddApacheModRewrite(apache).AddIISUrlRewrite(iis);
app.UseRewriter(options);
```

IIS XML supports `CONTENT_LENGTH/TYPE`, `HTTP_*`, `HTTPS`, `LOCAL_ADDR`, `QUERY_STRING`, `REMOTE_ADDR/PORT`, `REQUEST_FILENAME`, `REQUEST_URI`. Apache supports `CONN_REMOTE_ADDR`, `HTTP_*`, `HTTPS`, `IPV6`, `QUERY_STRING`, `REMOTE_ADDR/PORT`, `REQUEST_FILENAME`, `REQUEST_METHOD`, `REQUEST_SCHEME`, `REQUEST_URI`, `SCRIPT_FILENAME`, `SERVER_*`, `TIME_*`. **Unsupported on IIS:** outbound rules, custom server variables, wildcards, `LogRewrittenUrl`.

Custom `IRule`:

```csharp
public class RedirectImageRequests(string ext, string newPath) : IRule
{
    private readonly PathString _newPath = new(newPath);
    public void ApplyRule(RewriteContext context)
    {
        var req = context.HttpContext.Request;
        if (req.Path.StartsWithSegments(_newPath)) return;
        if (!req.Path.Value!.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return;
        var res = context.HttpContext.Response;
        res.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Result = RuleResult.EndResponse;
        res.Headers[HeaderNames.Location] = _newPath + req.Path + req.QueryString;
    }
}
```

`RuleResult`: `ContinueRules` (default), `EndResponse` (stop and send), `SkipRemainingRules` (stop rules but continue pipeline). Framework applies a 1-second regex timeout. Order rules by frequency; use `skipRemainingRules: true` aggressively. Prefer server-level rewriting (IIS URL Rewrite, Apache mod_rewrite, Nginx) when available.

## Request features (`HttpContext.Features`)

Mutable per-request typed bag. Required: `IHttpRequestFeature`, `IHttpResponseFeature`, `IHttpResponseBodyFeature` (replaced `IHttpSendFileFeature` and `IHttpResponseFeature.Body` from 3.0). Useful: `IHttpAuthenticationFeature` (`ClaimsPrincipal`); `IFormFeature`; `IHttpBodyControlFeature` (toggle sync IO); `IHttpActivityFeature`; `IHttpConnectionFeature` (`ConnectionId`, local/remote IP+port); `IHttpMaxRequestBodySizeFeature`; `IHttpRequestBodyDetectionFeature.CanHaveBody`; `IHttpRequestIdentifierFeature`; `IHttpRequestLifetimeFeature` (`RequestAborted`, `Abort()`); `IHttpRequestTrailersFeature` / `IHttpResponseTrailersFeature`; `IHttpResetFeature` (HTTP/2+3 RST_STREAM); `IHttpUpgradeFeature`; `IHttpWebSocketFeature` (`AcceptAsync`); `IHttpsCompressionFeature`; `IItemsFeature`; `IQueryFeature`; `IRequestBodyPipeFeature` (`PipeReader` view); `IRequestCookiesFeature` / `IResponseCookiesFeature`; `IServerVariablesFeature` (IIS); `IServiceProvidersFeature`; `ISessionFeature`; `ITlsConnectionFeature` (client cert); `ITlsTokenBindingFeature`; `ITrackingConsentFeature` (GDPR). Use via `ctx.Features.Get<IFoo>()` / `ctx.Features.Set<IMyFeature>(...)`.

## Change tokens

`Microsoft.Extensions.Primitives.IChangeToken` — low-level change-notification primitive. `HasChanged`, `ActiveChangeCallbacks` (false → polling mode), `RegisterChangeCallback(cb, state)`. Tokens fire ONCE — observers must request a fresh token after each fire (the factory you pass to `OnChange` does this).

```csharp
ChangeToken.OnChange(
    () => config.GetReloadToken(),
    state => InvokeChanged(state),
    env);

// File monitoring
ChangeToken.OnChange(() => provider.Watch("appsettings.json"), () => Reload());
```

`PhysicalFileProvider` uses `FileSystemWatcher`; expect duplicate callbacks for a single edit — debounce by hashing the file or checking timestamps.

Cache eviction by token — `MemoryCacheEntryOptions().AddExpirationToken(token)`. `CompositeChangeToken` aggregates (`HasChanged ⇔ any.HasChanged`); `CancellationChangeToken` wraps a `CancellationToken`. `IOptionsMonitor<T>.OnChange(...)` is a thin wrapper over `ChangeToken.OnChange` + `IOptionsChangeTokenSource<T>`.

## Request decompression

`Microsoft.AspNetCore.RequestDecompression` (in shared framework). Activates on requests with a known `Content-Encoding`; wraps `Request.Body` in a decompression `Stream` and removes the header. **Lazy** — happens when the body is read.

```csharp
builder.Services.AddRequestDecompression();
app.UseRequestDecompression();   // BEFORE any middleware that reads the body
```

Built-in providers: `br` → Brotli, `gzip` → GZip, `deflate` → Deflate. `zstd` requires a custom `IDecompressionProvider`:

```csharp
public sealed class ZstdDecompressionProvider : IDecompressionProvider
{
    public Stream GetDecompressionStream(Stream input) => new ZstdSharp.DecompressionStream(input);
}
builder.Services.AddRequestDecompression(o => o.DecompressionProviders.Add("zstd", new ZstdDecompressionProvider()));
```

Errors: unsupported `Content-Encoding`, multi-value, or invalid bytes → request flows untouched (decompression stream may then throw `InvalidDataException` / `InvalidOperationException` on read). Decompressed length is capped by the **endpoint's** request-size limit, in this precedence:

1. `IRequestSizeLimitMetadata.MaxRequestBodySize` (`[RequestSizeLimit]`, `[DisableRequestSizeLimit]`).
2. `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` (per-request override).
3. Server default: `KestrelServerLimits.MaxRequestBodySize` / `IISServerOptions.MaxRequestBodySize` / `HttpSysOptions.MaxRequestBodySize`.

Exceeding the limit throws `InvalidOperationException` mid-read (zip-bomb defense).

## Host filtering

Validates `Host:` against an allowlist; rejects with 400 otherwise. Disabled by default unless `AllowedHosts` is configured. Added by `CreateDefaultBuilder` via `AddHostFiltering`.

```json
{ "AllowedHosts": "example.com;www.example.com;localhost" }
```

Semantics: semicolon-delimited, port numbers excluded. `*` matches any non-empty host. `*.example.com` matches subdomains (`foo.example.com`) but NOT the parent (`example.com`). Unicode hosts allowed → punycode for comparison.

```csharp
builder.Services.AddHostFiltering(o =>
{
    o.AllowedHosts          = new List<string> { "example.com", "*.example.com" };
    o.AllowEmptyHosts       = true;     // accept HTTP/1.0 (no Host header)
    o.IncludeFailureMessage = true;
});
```

| Use | When |
|---|---|
| `HostFiltering(AllowedHosts)` | Kestrel directly facing internet, or `Host` header is forwarded intact |
| `ForwardedHeaders(AllowedHosts)` | Behind reverse proxy that overwrites/strips `Host` |

Do not rely on either as authentication.

## Raw WebSockets (no SignalR)

```csharp
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(2),    // ping period to keep proxies alive
    AllowedOrigins   = { "https://client.com", "https://www.client.com" },
});
```

`WebSocketOptions`: `KeepAliveInterval` (2 min); `KeepAliveTimeout` (infinite — abort after this without a frame); `AllowedOrigins` (empty = all allowed; CORS doesn't apply to WS).

Accept + echo (middleware) — keep the action alive for the duration of the socket; returning early causes `ObjectDisposedException`:

```csharp
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/ws")
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var buf = new byte[4096];
        var rcv = await ws.ReceiveAsync(buf, CancellationToken.None);
        while (!rcv.CloseStatus.HasValue)
        {
            await ws.SendAsync(new ArraySegment<byte>(buf, 0, rcv.Count), rcv.MessageType, rcv.EndOfMessage, CancellationToken.None);
            rcv = await ws.ReceiveAsync(buf, CancellationToken.None);
        }
        await ws.CloseAsync(rcv.CloseStatus!.Value, rcv.CloseStatusDescription, CancellationToken.None);
        return;
    }
    await next(ctx);
});
```

Park on a background worker via a `TaskCompletionSource` and `await done.Task` — NEVER `Task.Wait` / `Task.Result`.

Controller variant — `[Route("/ws")]`, NOT `[HttpGet]` (HTTP/2 WS uses `CONNECT`).

Compression — disabled by default. Enable via `new WebSocketAcceptContext { DangerousEnableCompression = true, ServerMaxWindowBits = 15, DisableServerContextTakeover = false }`. Over TLS exposes BREACH/CRIME-class attacks — disable for sensitive payloads (or set `WebSocketMessageFlags.DisableCompression` per-`SendAsync`). HTTP/2 WS support: Kestrel + Chrome/Edge/Firefox via RFC 8441, automatic negotiation, NOT supported on IIS in-process.

Origin enforcement — CORS does not apply to WS upgrades; validate via `AllowedOrigins`; `Origin` is spoofable. The server is not informed of network drops — require periodic client pings + server-side timeout (e.g., 2× ping interval). IIS — install **WebSocket Protocol** feature on Win Server 2012+ / Win 8+. To use socket.io on Node behind IIS, disable IIS's WS module via `<system.webServer><webSocket enabled="false" /></system.webServer>`.

## YARP (pointer)

YARP is a library, not a server — host it inside any ASP.NET Core app. Minimal sample:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();
app.MapReverseProxy();
```

Concepts: **Route** (`RouteConfig`) match → cluster + transforms; **Cluster** (`ClusterConfig`) destination group + load balancing + health checks + session affinity; **Destination** concrete back-end URL; **Transforms** request/response mutation. Programmatic config via `LoadFromMemory(routes, clusters)`.

When to choose YARP vs custom proxy middleware:

| Need | YARP | Custom middleware |
|---|---|---|
| HTTP/1.1+2+3, gRPC, WebSocket forwarding | yes | manual |
| Active/passive health checks | yes | manual |
| Hot config reload via `IConfiguration` | yes | n/a |
| Tiny single-route forward | overkill | OK with `HttpClient` + `app.Run` |

Full YARP surface (transforms catalog, health checks tuning, rate limiting integration) → `aspnet-core-yarp`.

## Cross-references

- Public docs (model binding): https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0
- Custom model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/advanced/custom-model-binding?view=aspnetcore-10.0
- Middleware authoring: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-10.0
- Factory-activated middleware: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/extensibility?view=aspnetcore-10.0
- Read/write request/response: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-response?view=aspnetcore-10.0
- URL rewriting: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/url-rewriting?view=aspnetcore-10.0
- Request features: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/request-features?view=aspnetcore-10.0
- Change tokens: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens?view=aspnetcore-10.0
- Request decompression: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-decompression?view=aspnetcore-10.0
- WebSockets: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0
- YARP: https://github.com/dotnet/yarp
- Related: `aspnet-core-fundamentals` — middleware order, DI, options, error handling.
- Related: `aspnet-core-mvc` — model-binding/validation primer, Razor, Tag Helpers.
- Related: `aspnet-core-http-apis` — `[ApiController]`, ProblemDetails, OpenAPI.
- Related: `aspnet-core-servers-and-hosting` — Kestrel limits, ForwardedHeaders, IIS.
- Related: `aspnet-core-yarp` — reverse-proxy feature surface.
- Related: `aspnet-core-signalr` — managed WebSocket layer.
- Related: `aspnet-core-performance` — caching, compression, body-IO performance tips.
- Related: `dotnet-conventions` — banned third-party libs.
