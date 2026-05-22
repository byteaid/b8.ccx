# Tracing, Patterns, Direct Forwarding, Middleware Integration, Diagnostics

Distributed tracing, A/B testing, `IHttpForwarder` direct forwarding, HTTP.sys delegation, `IReverseProxyFeature`, logging, telemetry consumers, defaults table, common pitfalls.

## Distributed tracing

ASP.NET Core auto-propagates `traceparent`. YARP creates additional Activities only when an `ActivitySource` listener for **`Yarp.ReverseProxy`** is registered:

- Forwarding requests.
- Active health checks.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()       // required for HttpClient spans
        .AddSource("Yarp.ReverseProxy")
        .AddOtlpExporter());
```

Pass-through (no participation):

```csharp
services.AddReverseProxy()
    .ConfigureHttpClient((_, h) => h.ActivityHeadersPropagator = null);
```

## Patterns

### A/B testing & rolling upgrades

No built-in module. Use `IProxyStateLookup` + `HttpContext.ReassignProxyRequest(ClusterState)` in custom middleware:

```csharp
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use((ctx, next) =>
    {
        var lookup = ctx.RequestServices.GetRequiredService<IProxyStateLookup>();
        if (lookup.TryGetCluster(ChooseCluster(ctx), out var cluster))
            ctx.ReassignProxyRequest(cluster);
        return next();
    });
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
});
```

Same affinity config across A/B clusters (or expect conflicts).

### Direct forwarding (`IHttpForwarder`)

When the routing/discovery model is too rigid or the route table won't fit in memory:

```csharp
services.AddHttpForwarder();
app.MapForwarder("/{**catch-all}", "https://localhost:10000/", requestConfig, transformer, httpClient);
```

API: `ValueTask<ForwarderError> SendAsync(HttpContext ctx, string destinationPrefix, HttpMessageInvoker httpClient, ForwarderRequestConfig requestConfig, HttpTransformer transformer)`. Errors: `ForwarderError != None` -> inspect `httpContext.GetForwarderErrorFeature()`. **Excludes:** routing, LB, affinity, retries.

`HttpTransformer` derived class — override `TransformRequestAsync(HttpContext, HttpRequestMessage, string destinationPrefix, CancellationToken)`. Build URIs safely with `Yarp.ReverseProxy.Forwarder.RequestUtilities.MakeDestinationAddress(prefix, path, query)`.

### HTTP.sys delegation

Windows kernel queue handoff. Requires ASP.NET Core HTTP.sys server + Windows Server 2019 / Win10 1809+. Per-destination metadata `HttpSysDelegationQueue`. Cannot read body or start response before delegating; response not visible (impacts session affinity / passive health checks). Add `UseHttpSysDelegation()` to the custom pipeline.

## Middleware integration

`IReverseProxyFeature` (`HttpContext.GetReverseProxyFeature()`):

- `Route` / `Cluster` (snapshots — won't change mid-request even if config reloads).
- `AllDestinations` — full cluster list.
- `AvailableDestinations` — eligible (default = `AllDestinations` minus Unhealthy).
- `ProxiedDestination` — set after final forward.

Reduce `AvailableDestinations` to one entry by end of pipeline; otherwise random pick. Empty -> 503.

Error inspection after `await next()`:

```csharp
var err = ctx.GetForwarderErrorFeature();
if (err is not null) Report(err.Error, err.Exception);
```

Retry: if `!HttpResponse.HasStarted`, can `Response.Clear()`, reset proxy feature fields, re-`next()`.

`MapReverseProxy()` parameterless includes: session affinity -> load balancing -> passive health checks -> final forward. The action overload includes only minimal setup + final forward; **you must add** `UseSessionAffinity` / `UseLoadBalancing` / `UsePassiveHealthChecks`.

## Diagnostics

### Logging

```json
{ "Logging": { "LogLevel": {
    "Default": "Information",
    "Microsoft": "Warning",
    "Yarp": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
} } }
```

`Yarp.ReverseProxy.*` at `Debug` for verbose routing/matching/forwarder traces. `app.UseHttpLogging();` for full inbound/outbound headers.

### Telemetry consumers

Implement `Yarp.Telemetry.Consumption` interfaces:

- `IForwarderTelemetryConsumer`: `OnForwarderInvoke`, `OnForwarderStart`, `OnForwarderStage`, `OnForwarderStop`, `OnForwarderFailed`, `OnContentTransferring`, `OnContentTransferred`. **`OnForwarderInvoke` is NOT raised in direct-forwarding scenarios.**
- `IHttpClientTelemetryConsumer` (transport layer).

`services.AddTelemetryConsumer<MyConsumer>();`. Use with `IHttpContextAccessor` for cross-correlation inside callbacks.

### Network tracing

- **Fiddler** captures inbound only (YARP outbound uses `UseProxy=false`).
- **Wireshark + Npcap** captures both.
- HTTPS opaque to monitors unless workarounds used.

## Common pitfalls

- Forgetting `app.UseRouting()` when adding extra middleware before `MapReverseProxy`.
- Mutating route config in-place (must produce new immutable instances; use `with` syntax on the records).
- Throwing in `IProxyConfigProvider.GetConfig()` after first load — disables future reloads (`IChangeToken` is single-use).
- Returning `HttpClient` from a custom `IForwarderHttpClientFactory`.
- Setting `""` as a header value via transforms — undefined behavior.
- `AffinityKeyName` collisions across clusters.
- Mismatched encoding settings between Kestrel (`RequestHeaderEncodingSelector`) and `HttpClient.RequestHeaderEncoding`.
- Custom pipeline missing `UseSessionAffinity` / `UseLoadBalancing` / `UsePassiveHealthChecks`.

## Defaults reference (where docs state)

| Field | Default |
|---|---|
| `Order` | unset; precedence by specificity |
| `MaxRequestBodySize` | server's limit (Kestrel default 30 MB) |
| `LoadBalancingPolicy` | `PowerOfTwoChoices` |
| `SessionAffinity.Enabled` | `false` |
| `SessionAffinity.Policy` | `HashCookie` |
| `HealthCheck.Active.Enabled` | `false`, `Interval` 15 s, `Timeout` 10 s |
| `HealthCheck.Passive.Enabled` | `false` |
| `AvailableDestinationsPolicy` | `HealthyOrPanic` |
| `ConsecutiveFailuresHealthPolicy.Threshold` | `2` |
| `TransportFailureRate` | `DetectionWindowSize` 60 s, `MinimalTotalCountThreshold` 10, `DefaultFailureRateLimit` 0.3 |
| `HttpClient.MaxConnectionsPerServer` | `int.MaxValue` |
| `HttpClient.EnableMultipleHttp2Connections` | `true` |
| `HttpRequest.ActivityTimeout` | 100 s |
| `HttpRequest.Version` | `"2"` |
| `HttpRequest.VersionPolicy` | `RequestVersionOrLower` |
| `HttpRequest.AllowResponseBuffering` | `false` |
