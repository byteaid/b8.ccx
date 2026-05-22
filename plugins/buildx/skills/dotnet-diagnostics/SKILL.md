---
name: dotnet-diagnostics
description: Production observability reference for .NET 10. Covers the three pillars (logs, metrics, distributed traces) on the runtime APIs (`ActivitySource`/`Activity`, `Meter`/`Instrument`, `ILogger<T>` + `[LoggerMessage]`); OpenTelemetry .NET SDK wiring (TracerProvider, MeterProvider, OTLP + Prometheus exporters); W3C Trace Context; EventPipe + diagnostic port; legacy `EventSource`/`EventCounter`; built-in metrics catalogue (`System.Runtime`, ASP.NET Core, Kestrel, `System.Net.Http`, EF Core); Aspire Dashboard.
when_to_use: |
  - Trigger keywords: OpenTelemetry, OTel, OTLP, ActivitySource, Activity, traceparent, W3C Trace Context, baggage, Meter, Counter, Histogram, Gauge, IMeterFactory, MeterListener, exemplar, MetricCollector, LoggerMessage, EventSource, EventCounter, EventPipe, DOTNET_DiagnosticPorts, AddOpenTelemetry, WithTracing, WithMetrics, AddOtlpExporter, AddPrometheusExporter, AddAspNetCoreInstrumentation, ServiceDefaults, Aspire dashboard, http.server.request.duration.
  - Task shapes: instrument a service with `ActivitySource`; add a `Counter`/`Histogram`/`Gauge`; wire OTel + OTLP/Prometheus; propagate W3C trace context; expose `/metrics` on Kestrel; write a custom listener; migrate `EventCounter` → `Meter`; replace `LogX(...)` with `[LoggerMessage]`; set `service.name`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.csproj", "**/appsettings*.json", "**/Program.cs"]
---

# .NET Diagnostics & Instrumentation — Reference

Reference for instrumenting .NET 10 services for production observability. Pin the rules; defer the exhaustive metric/keyword tables to the Microsoft docs cited at the bottom.

## Mental model — three pillars on the BCL

| Pillar | Public API | Out-of-process transport | OTel surface |
|---|---|---|---|
| Logs | `Microsoft.Extensions.Logging.ILogger<T>` | EventPipe (via `EventSource`-backed providers) / sinks | OTel Logs SDK consumes `ILogger` |
| Metrics | `System.Diagnostics.Metrics.Meter` + instruments | EventPipe (Meter listener bridge), Prometheus scrape, OTLP push | `MeterProvider`, `AddMeter(...)` |
| Distributed traces | `System.Diagnostics.ActivitySource` / `Activity` | EventPipe + `DiagnosticSource` events | `TracerProvider`, `AddSource(...)` |

Key invariant on .NET: **library authors use only the BCL APIs** (`ILogger`, `Meter`, `ActivitySource`). The collection / export choice (OTel, Application Insights, third-party APM, dotnet-monitor, custom listeners) is the **app developer's** decision and is layered on top. OTel .NET deliberately does NOT define its own instrumentation API — it consumes the runtime APIs.

## Non-negotiable rules

1. **One `ActivitySource` per library/component**, named after the assembly, kept in a static field. Pass a version string. `StartActivity` returns `null` when no listener is interested — guard with `?.` to avoid CPU waste.
2. **One `Meter` per library**, named after the assembly. In hosted apps, prefer `IMeterFactory.Create(name)` over `new Meter(name)` so the host owns disposal and tests can spin up isolated factories with `MetricCollector<T>`.
3. **Use `ILogger<T>` + `[LoggerMessage]`** for any structured log on a hot path. Bare `_logger.LogInformation("template", args)` boxes value types and allocates `params object?[]`. **Never** use string interpolation in log calls (`CA2254`) — it eagerly formats and bypasses structured logging.
4. **Cardinality budget on metrics:** keep tag-value combinations under ~1000 per instrument (much lower for histograms). Never use customer IDs / GUIDs / URL paths as tag values.
5. **Set `service.name`** on the `ResourceBuilder` (or via `OTEL_SERVICE_NAME`). Backends and the Aspire dashboard key on it.
6. **Errors:** `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` AND set `error.type` tag for metric correlation. Use `AddEvent` with `exception.*` tags only for low-volume; high-volume exception detail belongs in `ILogger`.
7. **OTLP env vars override code config.** `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_TRACES_SAMPLER`, `OTEL_METRIC_EXPORT_INTERVAL` etc. wins over the builder-style options. Plan deployment around this.

## Distributed tracing — `ActivitySource` / `Activity`

| .NET type | OTel concept | Notes |
|---|---|---|
| `ActivitySource` | `Tracer` | One per library; static field. |
| `Activity` | `Span` | `using` scope; `StartActivity` → `Dispose` stops. |
| `ActivityKind` | `SpanKind` | `Internal` (default), `Server`, `Client`, `Producer`, `Consumer`. |
| `ActivityContext` | `SpanContext` | `TraceId` (16 B), `SpanId` (8 B), `TraceFlags`, `TraceState`. |
| `ActivityLink` | `Link` | Multi-parent / batch. **Immutable after `StartActivity`.** OTel limit: 128. |
| `ActivityEvent` | `SpanEvent` | Timestamped tags. Modest volumes only — high-volume → `ILogger`. |
| `ActivityStatusCode` | `Status` | `Unset` / `Ok` / `Error` → OTel `otel.status_code`. |
| `Activity.AddBaggage` | `Baggage` | Propagates via the `baggage` HTTP header. |
| `Activity.SetTag(string, object?)` | `Attributes` | Snake-case keys per OTel semconv. |

W3C Trace Context: `Activity.IdFormat` defaults to `W3C` on .NET 5+. `Activity.Id` IS the `traceparent` header (`00-{traceid32hex}-{spanid16hex}-{flags2hex}`). `tracestate` carries vendor state. Default `HttpClient` instrumentation injects `traceparent` automatically; `ActivityContext.Parse(traceparent, tracestate)` parses on the inbound side.

```csharp
internal static class Telemetry
{
    public const string ActivitySourceName = "Contoso.Orders";
    public static readonly ActivitySource Source = new(ActivitySourceName, "1.0.0");
}

public sealed class OrderService
{
    public async Task<Order> PlaceAsync(OrderRequest req, CancellationToken ct)
    {
        using Activity? activity = Telemetry.Source.StartActivity("OrderService.Place", ActivityKind.Internal);
        activity?.SetTag("order.customer_id", req.CustomerId);

        try
        {
            var order = await SaveAsync(req, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return order;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            throw;
        }
    }
}
```

Use `Activity.Current` to read the ambient context (managed by `AsyncLocal`). `Activity.IsAllDataRequested` short-circuits expensive tag computation when no recording listener is attached.

`ActivitySource.AddActivityListener(...)` installs an in-process listener without an OTel SDK — useful for diagnostic harnesses. `ActivityLink` is set at start time only:

```csharp
using var act = Telemetry.Source.StartActivity(
    "BigBatchOfWork", ActivityKind.Internal,
    parentContext: default,
    links: requestContexts.Select(ctx => new ActivityLink(ctx)));
```

Don't wrap every method — produces noise. One activity per logical operation (request, message handle, job step).

## Metrics — `System.Diagnostics.Metrics`

| Instrument | Factory | Use for |
|---|---|---|
| `Counter<T>` | `Meter.CreateCounter<T>` | Monotonically increasing — push via `Add` |
| `UpDownCounter<T>` | `Meter.CreateUpDownCounter<T>` | Sums that may decrease (queue length, in-flight) |
| `Histogram<T>` | `Meter.CreateHistogram<T>` | Distributions — push via `Record` |
| `Gauge<T>` (.NET 9+) | `Meter.CreateGauge<T>` | Latest-value push |
| `ObservableCounter<T>` / `ObservableUpDownCounter<T>` / `ObservableGauge<T>` | callback variants | Pull on collection |

`T` accepts `byte`, `short`, `int`, `long`, `float`, `double`, `decimal` — pick the smallest sufficient type.

**Naming + units:** lowercase OTel hierarchical dotted names (`contoso.orders.placed`, `contoso.cache.entries`); units follow UCUM (`s`, `By`, `{request}` — curly braces denote annotations); time → seconds, double. Tags are `KeyValuePair<string, object?>`. Up to 3 tags is allocation-free; for more use `TagList`.

### Static-field pattern

```csharp
public sealed class HatStore
{
    static readonly Meter s_meter = new("HatCo.Store", "1.0.0");
    static readonly Counter<int> s_hatsSold = s_meter.CreateCounter<int>(
        "hatco.store.hats_sold", unit: "{hats}", description: "Hats sold since start");
    static readonly Histogram<double> s_orderTime = s_meter.CreateHistogram<double>(
        "hatco.store.order_processing_time", unit: "s",
        advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = new[] { 0.01, 0.05, 0.1, 0.5, 1, 5 }
        });

    public void Place(int qty, string color, double seconds)
    {
        s_hatsSold.Add(qty,
            new("product.color", color));
        s_orderTime.Record(seconds);
    }
}
```

### `IMeterFactory` (preferred for hosted apps, .NET 8+)

```csharp
public sealed class HatCoMetrics
{
    readonly Counter<int> _hatsSold;
    public HatCoMetrics(IMeterFactory factory)
    {
        var meter = factory.Create("HatCo.Store");
        _hatsSold = meter.CreateCounter<int>("hatco.store.hats_sold");
    }
    public void HatsSold(int qty) => _hatsSold.Add(qty);
}

builder.Services.AddMetrics();
builder.Services.AddSingleton<HatCoMetrics>();
```

The factory keeps one Meter per scope — required for parallel tests via `MetricCollector<T>`. Don't dispose the Meter; the factory owns the lifetime.

### Observable callback gotchas

- Run on a **separate thread** unsynchronized with the producer; use `Volatile.Read` / `Volatile.Write` or a `lock`.
- Long-running callbacks block ALL metric collection — keep them O(1).
- Don't store the returned observable instrument *only* in a static — C# lazy-static may never run if nothing references it. Capture it in a static field with explicit assignment.

### `MeterListener` — in-process consumption (no SDK)

```csharp
var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "HatCo.Store") l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<int>((inst, value, tags, _) =>
    Console.WriteLine($"{inst.Name} = {value}"));
listener.Start();
listener.RecordObservableInstruments();   // on demand
```

### Exemplars

Exemplars (an example raw measurement attached to an aggregated bucket, carrying the trace/span id) are computed by the **OTel SDK**, not the BCL. The SDK harvests `Activity.Current.TraceId/SpanId` automatically when `Histogram.Record` runs inside an active span. OTLP exemplars flow when `MeterProviderBuilder.AddOtlpExporter()` is configured — no extra app code required.

### Testing

```csharp
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

var services = new ServiceCollection().AddMetrics()
    .AddSingleton<HatCoMetrics>().BuildServiceProvider();
var metrics = services.GetRequiredService<HatCoMetrics>();
var factory = services.GetRequiredService<IMeterFactory>();
var collector = new MetricCollector<int>(factory, "HatCo.Store", "hatco.store.hats_sold");

metrics.HatsSold(15);
var snap = collector.GetMeasurementSnapshot();
Assert.Equal(15, snap[0].Value);
```

## Logging — `ILogger<T>` and source-gen

Categories: `ILogger<T>` resolves to category `typeof(T).FullName`. 7 levels: `Trace=0, Debug=1, Information=2, Warning=3, Error=4, Critical=5, None=6`.

Structured templates use named placeholders. Placeholder *order* (not name) maps to argument order; names become structured properties on enriched providers (App Insights, OTel, JsonConsole). Format specifiers OK: `"Logged {When:yyyy-MM-dd}"`. Escape literal braces: `"{{Number}}"`.

```csharp
log.LogInformation("Reading {OrderId} for {CustomerId}", id, customerId);
// State = { OrderId=42, CustomerId="ACME-1", {OriginalFormat}="Reading {OrderId} for {CustomerId}" }
```

### `[LoggerMessage]` source generator

Replaces runtime `LogX(template, args)` with compile-time-emitted code that avoids boxing of value types, avoids the temporary `params object?[]`, calls `IsEnabled(level)` first to short-circuit allocations, and validates template ↔ parameters at build time.

Method requirements: `partial`, returns `void`, name and parameter names not starting with `_`, no `params`/`out`/`scoped`/`ref struct` parameters.

```csharp
internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Order {OrderId} took {Elapsed}ms (threshold {Threshold}ms)")]
    public static partial void OrderSlow(ILogger logger, int orderId, long elapsed, long threshold);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process {OrderId}")]
    public static partial void Failed(ILogger logger, Exception ex, int orderId);
}

// Instance form (.NET 9+ — primary-ctor parameter):
public partial class OrderService(ILogger<OrderService> logger)
{
    [LoggerMessage(LogLevel.Information, "Got {OrderId}")]
    public partial void GotOrder(int orderId);
}
```

Special parameters recognised positionally (not by name): first `ILogger` is the logger; first `LogLevel` (when `Level` is omitted) becomes dynamic level; first `Exception` is the exception. Generator diagnostics: `SYSLIB1006`–`SYSLIB1030`.

The compile-time logger automatically stamps `TraceId`/`SpanId` on the active `Activity` when `IncludeScopes = true` and OTel logs is wired.

### Scopes

```csharp
using (log.BeginScope(new Dictionary<string, object>
{
    ["TransactionId"] = txnId,
    ["UserId"] = userId
}))
{
    log.LogInformation("Reading {Id}", id);
}
```

State recommendation for structured providers: `Dictionary<string, object>` or `IReadOnlyList<KeyValuePair<string, object>>`. Plain string also works but loses structure. Scopes are surfaced only when the provider has `IncludeScopes = true`.

### Redaction (`Microsoft.Extensions.Telemetry`)

```csharp
[LoggerMessage(0, LogLevel.Information, "User SSN: {SSN}")]
public static partial void LogPrivateInformation(this ILogger logger,
    [PrivateData] string SSN);

services.AddLogging(b => b.EnableRedaction());
services.AddRedaction(b => b.SetRedactor<StarRedactor>(
    new DataClassificationSet(MyTaxonomyClassifications.Private)));
```

Redactors apply by data classification at log-write time; raw values never reach the sink. `ILogger` is **synchronous by design** — to bridge slow sinks, queue and drain from a `BackgroundService`. Most production sinks (OTel, App Insights, Serilog) already do this internally.

Logging providers, filters, and configuration via `Logging:LogLevel:*` are owned by `dotnet-extensions` § Logging — not duplicated here.

## OpenTelemetry .NET — packages and wiring

Status: Traces / Metrics / Logs are all **stable**; supported on every officially supported .NET and .NET Framework version except .NET FX 3.5 SP1.

| Package | Purpose |
|---|---|
| `OpenTelemetry` | Core SDK (`Sdk.CreateTracerProviderBuilder`, `CreateMeterProviderBuilder`) |
| `OpenTelemetry.Extensions.Hosting` | `builder.Services.AddOpenTelemetry()`, hosted-service exporter lifecycle |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP exporter (gRPC + HTTP/protobuf) |
| `OpenTelemetry.Exporter.Console` | Stdout, debugging |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | `/metrics` ASP.NET Core scrape endpoint |
| `OpenTelemetry.Exporter.Zipkin` | Zipkin trace exporter |
| `OpenTelemetry.Instrumentation.AspNetCore` | Bridges ASP.NET Core + Kestrel `ActivitySource`s and Meters |
| `OpenTelemetry.Instrumentation.Http` | Bridges `HttpClient` and `HttpWebRequest` |
| `OpenTelemetry.Instrumentation.GrpcNetClient` / `.SqlClient` | Per-tech bridges |
| `OpenTelemetry.Instrumentation.Runtime` | Subscribes runtime EventCounters as metrics on .NET ≤ 8 (no longer needed when `System.Runtime` Meter is in use, .NET 9+) |

### Canonical wiring (ASP.NET Core, .NET 10)

```csharp
using OpenTelemetry; using OpenTelemetry.Logs; using OpenTelemetry.Metrics;
using OpenTelemetry.Resources; using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: "Contoso.Orders", serviceVersion: "1.0.0")
        .AddAttributes(new[]
        {
            new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
        }))
    .WithTracing(t => t
        .AddSource("Contoso.Orders")                 // your ActivitySource
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .SetSampler(new TraceIdRatioBasedSampler(0.1))
        .AddOtlpExporter())                          // OTLP/gRPC → http://localhost:4317
    .WithMetrics(m => m
        .AddMeter("Contoso.Orders")
        .AddMeter("System.Runtime")                  // .NET 9+ runtime Meter
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddMeter("System.Net.NameResolution")
        .AddOtlpExporter()
        .AddPrometheusExporter());                   // exposes /metrics

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.ParseStateValues = true;
    o.AddOtlpExporter();
});

var app = builder.Build();
app.MapPrometheusScrapingEndpoint();
app.Run();
```

### OTLP exporter env vars (override code defaults)

| Variable | Default | Notes |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` (gRPC) | Per-signal: `..._TRACES_ENDPOINT`, `..._METRICS_ENDPOINT`, `..._LOGS_ENDPOINT` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` | or `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | – | `key1=val1,key2=val2` (e.g. `Authorization=Bearer ...`) |
| `OTEL_EXPORTER_OTLP_TIMEOUT` | `10000` ms | – |
| `OTEL_SERVICE_NAME` | – | Resource `service.name` |
| `OTEL_RESOURCE_ATTRIBUTES` | – | comma-list `k=v,...` |
| `OTEL_TRACES_SAMPLER` / `_SAMPLER_ARG` | `parentbased_always_on` | – |
| `OTEL_METRIC_EXPORT_INTERVAL` | `60000` ms | Periodic reader interval |
| `OTEL_LOGS_EXPORTER` / `_TRACES_EXPORTER` / `_METRICS_EXPORTER` | `otlp` | Set to `none` to disable a signal |

### Programmatic batcher overrides

`AddOtlpExporter` registers a batched exporter by default (queue 2048, scheduled delay 5s, max batch 512). Override via `(exp, processor) => { exp.Endpoint = ...; exp.Protocol = OtlpExportProtocol.HttpProtobuf; processor.ScheduledDelayMilliseconds = 1000; processor.MaxQueueSize = 4096; }`.

### Manual SDK pattern (console / non-host)

```csharp
using var tp = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MySample"))
    .AddSource("Sample.DistributedTracing")
    .AddConsoleExporter()
    .Build();
```

`tp` / `mp` must outlive any instrumented code. Disposal flushes pending exports.

## Built-in `ActivitySource`s worth `AddSource`

`Microsoft.AspNetCore` (inbound HTTP — Activity per request), `System.Net.Http` (outbound `HttpClient`), `Azure.*` (Azure SDK per-package), `Microsoft.EntityFrameworkCore` (query / SaveChanges), `MassTransit` / `Confluent.Kafka` (third-party), `Microsoft.Extensions.Hosting` (host lifetime). The Instrumentation packages typically register the source automatically — `AddAspNetCoreInstrumentation()` adds `Microsoft.AspNetCore` and the related Meters; `AddHttpClientInstrumentation()` adds `System.Net.Http`.

## Built-in metrics catalogue (selected — .NET 10)

### `System.Runtime` Meter (.NET 9+)

`dotnet.process.cpu.time` (Counter `s`, tag `cpu.mode`); `dotnet.process.memory.working_set` (UpDownCounter `By`); `dotnet.gc.collections` (Counter, tag `gc.heap.generation` ∈ {`gen0`,`gen1`,`gen2`}); `dotnet.gc.heap.total_allocated` (Counter `By`); `dotnet.gc.last_collection.heap.size` (UpDownCounter `By`, tag `gc.heap.generation` ∈ {`gen0..gen2`,`loh`,`poh`}); `dotnet.gc.pause.time` (Counter `s`); `dotnet.thread_pool.thread.count` and `..queue.length` (UpDownCounters); `dotnet.monitor.lock_contentions` (Counter); `dotnet.exceptions` (Counter, tag `error.type`); `dotnet.assembly.count`, `dotnet.timer.count`. JIT: `dotnet.jit.compiled_methods`, `..compilation.time`, `..compiled_il.size`.

On .NET 8 and earlier, `dotnet-counters` falls back to the `System.Runtime` **EventCounters** with the older display names.

### ASP.NET Core (.NET 10)

`Microsoft.AspNetCore.Hosting`: `http.server.request.duration` (Histogram `s`) — tags `http.route`, `http.request.method`, `http.response.status_code`, `network.protocol.version`, `url.scheme`, `error.type`, `aspnetcore.request.is_unhandled`. `http.server.active_requests` (UpDownCounter).

`Microsoft.AspNetCore.Server.Kestrel`: `kestrel.active_connections`, `kestrel.connection.duration` (with `tls.protocol.version`), `kestrel.rejected_connections`, `kestrel.queued_connections`, `kestrel.queued_requests` (HTTP/2+3 stream queue), `kestrel.upgraded_connections`, `kestrel.tls_handshake.duration`, `kestrel.active_tls_handshakes`.

`Microsoft.AspNetCore.RateLimiting`: `..active_request_leases`, `..request_lease.duration`, `..queued_requests`, `..request.time_in_queue`, `..requests` — tags `..policy`, `..result`.

Other Meters: `Microsoft.AspNetCore.Routing`, `..Diagnostics`, `..Http.Connections` (SignalR), `..Components`, `..Authentication`, `..Authorization`.

### `System.Net` (.NET 8+)

`System.Net.NameResolution`: `dns.lookup.duration` (Histogram `s`) — tags `dns.question.name`, `error.type`.

`System.Net.Http` (only on `SocketsHttpHandler` — the default): `http.client.request.duration` (Histogram, tags `http.request.method`, `http.response.status_code`, `error.type`, `network.protocol.version`, `server.address`, `server.port`, `url.scheme`); `http.client.open_connections` (tag `http.connection.state` ∈ {`active`,`idle`}); `http.client.connection.duration`, `http.client.request.time_in_queue`, `http.client.active_requests`.

### EF Core 10

`Microsoft.EntityFrameworkCore`: `ec.entityframeworkcore.active_dbcontexts`, `..queries`, `..savechanges`, `..compiled_query_cache_hit_rate` (ObservableGauge), `..execution_strategy_operation_failures`, `..optimistic_concurrency_failures`. EF-specific tuning → `dotnet-ef-core`.

## EventPipe and the diagnostic port

EventPipe is a runtime-internal aggregator that serialises `EventSource` and runtime events into the `.nettrace` format and exposes them via the diagnostic port — the cross-platform replacement for ETW.

| OS | Default endpoint |
|---|---|
| Windows | Named pipe `\\.\pipe\dotnet-diagnostic-{pid}` |
| Linux/macOS | Unix domain socket `${TMPDIR}/dotnet-diagnostic-{pid}-{starttime}-socket` |
| Android/iOS/tvOS (Mono) | TCP, configured via `DOTNET_DiagnosticPorts` |

Custom port: `DOTNET_DiagnosticPorts="addr[,(listen|connect)][,(suspend|nosuspend)];..."`. Default modifiers are `connect,suspend`. `suspend` stalls managed-code execution until a tool issues a resume command — for startup tracing.

Disable all diagnostic ports (security-hardened): `DOTNET_EnableDiagnostics=0` (also blocks debugging). Suspend default port: `DOTNET_DefaultDiagnosticPortSuspend=1`.

EventPipe env-var mode (no tool required):

| Variable | Purpose |
|---|---|
| `DOTNET_EnableEventPipe=1` | Stream a session to disk on app start |
| `DOTNET_EventPipeOutputPath=path` | Output `.nettrace` path; `{pid}` substituted |
| `DOTNET_EventPipeCircularMB=400` | Hex; `0x400` = 1024 MB default circular buffer |
| `DOTNET_EventPipeConfig=Provider:Keyword:Level[,...]` | Provider list |

Well-known providers: `Microsoft-Windows-DotNETRuntime` (GC, JIT, AssemblyLoader, threading, exceptions), `Microsoft-Windows-DotNETRuntimeRundown` (end-of-trace rundown for stack symbolication), `Microsoft-DotNETCore-SampleProfiler` (~1ms managed CPU sampling), `Microsoft-Diagnostics-DiagnosticSource` (`DiagnosticSource` + `Activity` bridge), `Microsoft-Extensions-Logging` (`ILogger` events), `System.Runtime` (built-in counters / Meter).

The `dotnet-counters` / `dotnet-trace` / `dotnet-dump` / `dotnet-gcdump` / `dotnet-stack` / `dotnet-monitor` / `dotnet-symbol` CLI tools all speak this port. For test-failure invocation reference, load `dotnet-testing` § debugging-and-diagnostics.

`Microsoft.Diagnostics.NETCore.Client` is the programmatic API that wraps the port — use it to build custom diagnostic harnesses (`StartEventPipeSession`, `WriteDump`, `ResumeRuntime`, `EnablePerfMap`).

## `EventSource` / `EventCounter` (legacy, still supported)

`EventSource` is the older high-performance structured-logging API that backs ETW on Windows, EventPipe cross-platform, and `EventListener` in-process. It pre-dates `Meter`/`ActivitySource` and is what the .NET runtime itself uses for GC, JIT, threadpool, exception, assembly-loader events.

EventCounters (legacy metric carrier): `EventCounter` (push `WriteMetric(value)`, reports min/max/mean per interval); `IncrementingEventCounter` (push `Increment(delta)`, reports sum/sec); `PollingCounter` (tool pulls a callback for snapshots); `IncrementingPollingCounter` (callback, reports first-difference per interval).

Microsoft recommends `System.Diagnostics.Metrics` (`Meter`) for **new** code — EventCounters lack histograms / percentiles / multi-dimensional tags. They remain supported and are still emitted by .NET libraries for back-compat. `EventCounterIntervalSec` is the only filter recognised; in-process consumption via `EventListener.OnEventSourceCreated` → `EnableEvents(src, EventLevel.Verbose, EventKeywords.All, args)`.

## Aspire dashboard relationship

- The Aspire Dashboard is an OTLP receiver bundled with the Aspire workload, also available as a standalone OCI image (`mcr.microsoft.com/dotnet/aspire-dashboard`).
- Aspire AppHost orchestration injects `OTEL_EXPORTER_OTLP_ENDPOINT` into every child resource pointing at the dashboard.
- The dashboard renders structured logs (with trace/span correlation), traces (Gantt timeline + waterfall), and metrics (instrument browser + chart).
- `dotnet new aspire-servicedefaults` produces `Extensions.cs` containing the canonical `AddOpenTelemetry()` block with ASP.NET Core, HttpClient, and Runtime instrumentation pre-registered — usable **standalone** without orchestration (`builder.AddServiceDefaults()` on a plain ASP.NET Core host).
- Telemetry shape is plain OTel. Any compliant backend (Jaeger/Tempo + Prometheus + Loki, Grafana Cloud, Honeycomb, App Insights via OTel exporter, ...) replaces the dashboard transparently.

For Aspire AppHost orchestration / resource modelling itself → load `dotnet-aspire`.

## Comparison matrix — metric APIs

| Feature | `Meter` (`System.Diagnostics.Metrics`) | EventCounters (`EventSource`) | `PerformanceCounter` (legacy) |
|---|---|---|---|
| Cross-platform | Yes | Yes (.NET Core 3.1+) | No |
| Multi-dimensional tags | Yes | No | No |
| Histograms / percentiles | Yes | No | No |
| Multiple simultaneous listeners | Yes | Limited | N/A |
| OTel native | Yes | Bridge via `EventCountersInstrumentation` | No |
| Recommended for new code | **Yes** | No | No |

## Quick decision matrix

| Question | Answer |
|---|---|
| New metric for my code | `System.Diagnostics.Metrics` (`Counter` / `Histogram` / `UpDownCounter` / `Gauge`); register Meter name with OTel `MeterProvider.AddMeter`. |
| New trace for my code | `ActivitySource.StartActivity` + tags + status; register Source with OTel `TracerProvider.AddSource`. |
| New log on a hot path | `[LoggerMessage]` source-gen + OTel `ILoggingBuilder.AddOpenTelemetry`. |
| Need to short-circuit expensive log construction | `if (logger.IsEnabled(LogLevel.X))` before computing args. |
| Inbound HTTP request observability | `AddAspNetCoreInstrumentation()` — covers `Microsoft.AspNetCore` Source + Hosting/Kestrel Meters. |
| Outbound `HttpClient` observability | `AddHttpClientInstrumentation()` — covers `System.Net.Http` Source + Meter. |
| Production sidecar collection | `dotnet-monitor` (REST + collection rules) — load `dotnet-testing` § debugging-and-diagnostics for invocation reference. |
| Local-only telemetry dashboard | Aspire dashboard (auto with AppHost) or `docker run mcr.microsoft.com/dotnet/aspire-dashboard`. |
| Pin a specific OTel SDK behavior across deploys | Use the `OTEL_*` env vars — they override builder code. |
| Sampling (drop most spans) | `SetSampler(new TraceIdRatioBasedSampler(0.1))` or `OTEL_TRACES_SAMPLER=traceidratio + OTEL_TRACES_SAMPLER_ARG=0.1`. |
| Migrate `EventCounter` to `Meter` | Replace `EventCounter.WriteMetric` → `Histogram.Record`; `IncrementingEventCounter.Increment` → `Counter.Add`. Keep both during transition; remove EventCounter once consumers migrate. |

## Cross-references

- Public docs (Diagnostics overview): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/
- Public docs (Distributed tracing): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing
- Public docs (Distributed-tracing walkthroughs): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs
- Public docs (Metrics): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics
- Public docs (Metrics instrumentation): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation
- Public docs (Compare metric APIs): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/compare-metric-apis
- Public docs (Observability with OTel): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
- Public docs (Built-in metrics index): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics
- Public docs (`System.Runtime` Meter): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime
- Public docs (`System.Net` Meters): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-system-net
- Public docs (ASP.NET Core Meters): https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/built-in?view=aspnetcore-10.0
- Public docs (EventCounters): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/event-counters
- Public docs (EventPipe): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe
- Public docs (Diagnostic port): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostic-port
- Public docs (Logging vs Tracing landscape): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/logging-tracing
- Public docs (`[LoggerMessage]` generator): https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator
- Public docs (OpenTelemetry .NET status): https://opentelemetry.io/docs/instrumentation/net/
- Related skill: `dotnet-testing` § debugging-and-diagnostics — `dotnet-counters`, `dotnet-trace`, `dotnet-dump`, `dotnet-gcdump`, `dotnet-stack`, `dotnet-monitor` invocation reference for live triage.
- Related skill: `dotnet-extensions` — `ILogger` provider catalogue, `Logging:LogLevel:*` filter rules, generic-host wiring.
- Related skill: `dotnet-aspire` — AppHost orchestration that auto-launches the dashboard and injects OTLP env vars.
- Related skill: `dotnet-garbage-collection` — `dotnet.gc.*` metric semantics, server-vs-workstation GC, heap-walk interpretation.
- Related skill: `dotnet-events-exceptions` — exception authoring rules referenced by `error.type` tagging.
