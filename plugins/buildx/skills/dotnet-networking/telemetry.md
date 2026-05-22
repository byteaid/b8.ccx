# Networking Telemetry — Metrics, Tracing, EventSource, OTel/Aspire

Built-in metrics + activities for `HttpClient`, name resolution, sockets; `EventSource` providers; OpenTelemetry / Aspire wiring. Load when instrumenting outbound HTTP, debugging DNS rotation, or wiring OTel exporters.

| Layer | API | Use |
|---|---|---|
| Metrics | `System.Diagnostics.Metrics` (multi-dimensional, OTel-compatible) | dashboards & alerts |
| Distributed tracing | `System.Diagnostics.Activity` / `ActivitySource` | request flow / spans |
| Events | `EventSource` / `EventListener` | low-level debugging |
| EventCounters | legacy lightweight metrics | quick `dotnet-counters` |

## Metrics

`Meter` names: `System.Net.Http`, `System.Net.NameResolution`. Key built-in instruments:

| Instrument | Type | Tags (selected) |
|---|---|---|
| `http.client.request.duration` | histogram (s) | `http.request.method`, `url.scheme`, `server.address`, `server.port`, `network.protocol.version`, `http.response.status_code`, `error.type` |
| `http.client.active_requests` | up-down counter | method, scheme, server |
| `http.client.open_connections` | up-down counter | `http.connection.state` (idle/active), `network.protocol.version`, `server.address` |
| `http.client.connection.duration` | histogram (s) | server, protocol, scheme |
| `http.client.request.time_in_queue` | histogram (s) | scheme, method, server |
| `dns.lookup.duration` | histogram (s) | `dns.question.name`, `error.type` |

```csharp
metrics.AddMeter("System.Net.Http").AddMeter("System.Net.NameResolution");
```

Enrich `http.client.request.duration` with custom tags from a `DelegatingHandler`:

```csharp
sealed class EnrichmentHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        HttpMetricsEnrichmentContext.AddCallback(req, static ctx =>
        {
            if (ctx.Response is { } r && r.Headers.TryGetValues("Enrichment-Value", out var v))
                ctx.AddCustomTag("enrichment_value", v.First());
        });
        return base.SendAsync(req, ct);
    }
}
```

Per-collection isolation via `SocketsHttpHandler.MeterFactory`. Test with `MetricCollector<T>` (NuGet `Microsoft.Extensions.Diagnostics.Testing`).

## Distributed tracing

| Activity | `ActivitySource` | Notes |
|---|---|---|
| HTTP client request | `System.Net.Http` | name, status, exception, OTel HTTP client semconv tags (since .NET 9). |
| HTTP wait_for_connection (experimental) | `Experimental.System.Net.Http.Connections` | child of HTTP client request. |
| HTTP connection_setup (experimental) | `Experimental.System.Net.Http.Connections` | separate trace root, linked from request span. |
| DNS lookup (experimental) | `Experimental.System.Net.NameResolution` | child of connection_setup. |
| Socket connect (experimental) | `Experimental.System.Net.Sockets` | child of connection_setup. |
| TLS handshake (experimental) | `Experimental.System.Net.Security` | child of connection_setup. |

```csharp
.WithTracing(t => t.AddSource("System.Net.Http").AddSource("Experimental.System.Net.*"));
```

Connections live across requests; a request span has a *link* (not parent edge) to its connection_setup span. Some APMs walk links aggressively — be careful enabling `Experimental.*` in production. Propagation is controlled by `SocketsHttpHandler.ActivityHeadersPropagator` (defaults to W3C TraceContext; `null` disables).

## `EventSource` providers

`System.Net.Http`, `System.Net.NameResolution`, `System.Net.Security`, `System.Net.Sockets`, `Microsoft.AspNetCore.Hosting`, `Microsoft-AspNetCore-Server-Kestrel`, `Private.InternalDiagnostics.System.Net.*` (debug-grade, internal, may contain PII).

```csharp
public sealed class MyListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource es)
    {
        if (es.Name == "System.Net.Http") EnableEvents(es, EventLevel.Informational);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e) => Console.WriteLine(
        $"{e.EventName}: {string.Join(' ', e.PayloadNames!.Zip(e.Payload!).Select(p => $"{p.First}={p.Second}"))}");
}
```

Sample `System.Net.Http` events: `RequestStart`, `ConnectionEstablished`, `RequestLeftQueue`, `RequestHeaders{Start,Stop}`, `ResponseHeaders{Start,Stop}`, `ResponseContent{Start,Stop}`, `RequestStop`, `ConnectionClosed`.

Out-of-process: `dotnet-trace collect --providers System.Net.Http,System.Net.Security,System.Threading.Tasks.TplEventSource:0x80:4 -p <pid>`. Internal diagnostics: `Private.InternalDiagnostics.System.Net.Http:0xf`.

Strongly typed in-process consumption: NuGet `Yarp.Telemetry.Consumption` (`IHttpTelemetryConsumer`, `INameResolutionTelemetryConsumer`, `INetSecurityTelemetryConsumer`, `ISocketsTelemetryConsumer`, `IKestrelTelemetryConsumer`). Correlate across threads with `AsyncLocal<T>` (`[ThreadLocal]` won't work — async I/O thread hopping).

Since .NET 6: requests and connections have **independent** lifecycles. You can see DNS-Start → DNS-Stop **after** Request-Stop because the request was served by another connection that arrived first.

## EventCounters (legacy)

Providers: `System.Net.Http`, `System.Net.NameResolution`, `System.Net.Security`, `System.Net.Sockets`, ASP.NET Core hosts. Sample counters: `requests-started`, `requests-failed`, `current-requests`, `http{11,20,30}-connections-current-total`, `http11-requests-queue-duration`, `dns-lookups-{requested,duration}`, `outgoing-connections-established`, `bytes-{received,sent}`, `all-tls-sessions-open`.

```bash
dotnet-counters monitor --counters System.Net.Http,System.Net.NameResolution -n MyApp
```

## OpenTelemetry / Aspire wiring

```csharp
.WithMetrics(m => m
    .AddAspNetCoreInstrumentation()
    .AddMeter("System.Net.Http")
    .AddMeter("System.Net.NameResolution")
    .AddRuntimeInstrumentation())
.WithTracing(t => t
    .AddAspNetCoreInstrumentation()
    .AddSource("System.Net.Http")
    .AddSource("Experimental.System.Net.*"));
```

Or via `AddHttpClientInstrumentation()` from `OpenTelemetry.Instrumentation.Http`.
