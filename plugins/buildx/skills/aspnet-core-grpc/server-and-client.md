# Server, Client, and Method Shapes

Server bootstrap, Kestrel HTTP/2 + TLS, the four method shapes, errors, channels, client factory, deadlines, retries, hedging, interceptors.

## Packages and project shape

| Concern | Package |
|---|---|
| Server SDK | `Grpc.AspNetCore` (metapackage = `Grpc.AspNetCore.Server` + `Grpc.Tools` + `Google.Protobuf`) |
| .NET client | `Grpc.Net.Client` + `Grpc.Tools` (`PrivateAssets="all"`) + `Google.Protobuf` |
| Client factory | `Grpc.Net.ClientFactory` |
| Server-side propagation | `Grpc.AspNetCore.Server.ClientFactory` (provides `EnableCallContextPropagation`) |
| gRPC-Web (server) | `Grpc.AspNetCore.Web` |
| gRPC-Web (client) | `Grpc.Net.Client.Web` (>= 2.29.0) |
| JSON transcoding | `Microsoft.AspNetCore.Grpc.JsonTranscoding` |
| Health checks | `Grpc.AspNetCore.HealthChecks` / `Grpc.HealthCheck` |
| Code-first | `protobuf-net.Grpc` + `protobuf-net.Grpc.AspNetCore` |
| Project SDK | `Microsoft.NET.Sdk.Web`, or `Microsoft.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for non-web hosts |

`.csproj` (server, web host):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="2.47.0" />
    <Protobuf Include="Protos\greet.proto" GrpcServices="Server" />
  </ItemGroup>
</Project>
```

## Server bootstrap

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Use a gRPC client.");
app.Run();
```

`MapGrpcService<T>` registers an endpoint per RPC declared in the `.proto`. MVC, minimal APIs, SignalR, and gRPC coexist on the same routing pipeline.

### Kestrel HTTP/2 + TLS

```json
{
  "Kestrel": {
    "Endpoints": {
      "HttpsInlineCertFile": {
        "Url": "https://localhost:5001",
        "Protocols": "Http2",
        "Certificate": { "Path": "<path>.pfx", "Password": "<pwd>" }
      }
    }
  }
}
```

In code:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5001, lo =>
    {
        lo.Protocols = HttpProtocols.Http2;
        lo.UseHttps("<path>.pfx", "<pwd>");
    });
});
```

Hosting caveats:
- IIS / HTTP.sys need Windows 11 Build 22000 / Windows Server 2022 Build 20348 or later.
- **Azure App Service / IIS do NOT support bidirectional streaming.** Use Kestrel direct or a different host.

### Service implementation

```csharp
public class GreeterService(ILogger<GreeterService> log) : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest req, ServerCallContext ctx)
    {
        log.LogInformation("Saying hello to {Name}", req.Name);
        return Task.FromResult(new HelloReply { Message = $"Hello {req.Name}" });
    }
}
```

Standard DI lifetimes apply. Bridge to `HttpContext` via `ctx.GetHttpContext()`.

## Service method shapes

```protobuf
service ExampleService {
  rpc UnaryCall            (ExampleRequest)         returns (ExampleResponse);
  rpc StreamingFromServer  (ExampleRequest)         returns (stream ExampleResponse);
  rpc StreamingFromClient  (stream ExampleRequest)  returns (ExampleResponse);
  rpc StreamingBothWays    (stream ExampleRequest)  returns (stream ExampleResponse);
}
```

### Server side

```csharp
// Server streaming
public override async Task StreamingFromServer(
    ExampleRequest req, IServerStreamWriter<ExampleResponse> resp, ServerCallContext ctx)
{
    while (!ctx.CancellationToken.IsCancellationRequested)
    {
        await resp.WriteAsync(new ExampleResponse());
        await Task.Delay(TimeSpan.FromSeconds(1), ctx.CancellationToken);
    }
}

// Client streaming
public override async Task<ExampleResponse> StreamingFromClient(
    IAsyncStreamReader<ExampleRequest> req, ServerCallContext ctx)
{
    await foreach (var msg in req.ReadAllAsync()) { /* aggregate */ }
    return new ExampleResponse();
}

// Bidi
public override async Task StreamingBothWays(
    IAsyncStreamReader<ExampleRequest> req,
    IServerStreamWriter<ExampleResponse> resp, ServerCallContext ctx)
{
    await foreach (var msg in req.ReadAllAsync())
        await resp.WriteAsync(new ExampleResponse());
}
```

For independent reader/writer threads, fan-in via `System.Threading.Channels.Channel<T>` (single producer per side).

### Client side

```csharp
// Unary
var resp = await client.SayHelloAsync(new HelloRequest { Name = "World" });

// Server streaming
using var call = client.SayHellos(new HelloRequest { Name = "W" });
await foreach (var r in call.ResponseStream.ReadAllAsync()) Console.WriteLine(r.Message);

// Client streaming
using var call = client.AccumulateCount();
for (var i = 0; i < 3; i++) await call.RequestStream.WriteAsync(new CounterRequest { Count = 1 });
await call.RequestStream.CompleteAsync();
var resp = await call;

// Bidi
using var call = client.Echo();
var readTask = Task.Run(async () =>
{
    await foreach (var r in call.ResponseStream.ReadAllAsync()) Console.WriteLine(r.Message);
});
// write loop ...
await call.RequestStream.CompleteAsync();
await readTask;
```

### Headers / trailers

Server: `ctx.RequestHeaders.GetValue("user-agent")`, `await ctx.WriteResponseHeadersAsync(new Metadata { ... })`, `ctx.ResponseTrailers.Add(...)`.

Client: `await call.ResponseHeadersAsync`, `call.GetTrailers()` after the response/stream completes. Trailers also surface on `RpcException.Trailers`.

## Errors and the status model

Throwing structured failure:

```csharp
throw new RpcException(new Status(StatusCode.NotFound, "User missing"),
    new Metadata { { "user-id", id.ToString() } });
```

Catching:

```csharp
try { await client.SayHelloAsync(req); }
catch (RpcException ex)
{
    var statusCode = ex.StatusCode;
    var detail     = ex.Status.Detail;
    var trailerVal = ex.Trailers.GetValue("user-id");
}
```

Status table (subset): `OK`, `Cancelled`, `Unknown`, `InvalidArgument`, `DeadlineExceeded`, `NotFound`, `AlreadyExists`, `PermissionDenied`, `ResourceExhausted`, `FailedPrecondition`, `Aborted`, `OutOfRange`, `Unimplemented`, `Internal`, `Unavailable`, `DataLoss`, `Unauthenticated`.

## Channel and client (`Grpc.Net.Client`)

```csharp
var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client  = new Greet.GreeterClient(channel);
var reply   = await client.SayHelloAsync(new HelloRequest { Name = "World" });
```

Channels and clients are **thread-safe**. Concurrent calls share the HTTP/2 connection via stream multiplexing.

### `GrpcChannelOptions` highlights

| Option | Default | Notes |
|---|---|---|
| `MaxSendMessageSize` | null (unlimited) | bytes |
| `MaxReceiveMessageSize` | 4 MB | bytes; null = unlimited |
| `Credentials` | null | `ChannelCredentials.Create(new SslCredentials(), callCreds)` |
| `CompressionProviders` | gzip | extend with custom |
| `ThrowOperationCanceledOnCancellation` | false | throw OCE instead of `RpcException` for cancel/deadline |
| `MaxRetryAttempts` | 5 | upper bound; null = unlimited; needs `ServiceConfig` |
| `MaxRetryBufferSize` | 16 MB | shared across calls |
| `MaxRetryBufferPerCallSize` | 1 MB | per call |
| `ServiceConfig` | null | retry / hedging / load-balancing |
| `LoggerFactory` | null | client-side logs |

## Client factory (`Grpc.Net.ClientFactory`)

```csharp
builder.Services.AddGrpcClient<Greeter.GreeterClient>(o =>
{
    o.Address = new Uri("https://localhost:5001");
});
```

Client = transient; underlying `HttpMessageHandler` managed by `HttpClientFactory` and recycled per `HandlerLifetime`.

### Configure handler / channel

```csharp
builder.Services
    .AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri(...))
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var h = new HttpClientHandler();
        h.ClientCertificates.Add(LoadCertificate());
        return h;
    })
    .ConfigureChannel(o => { o.Credentials = new CustomCredentials(); });
```

### Server-to-server context propagation

```csharp
builder.Services
    .AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri(...))
    .EnableCallContextPropagation(o => o.SuppressContextNotFoundErrors = true);
```

Pulls `Deadline` and `CancellationToken` from the current `ServerCallContext` and applies to the outgoing call. Smaller of (parent, child) deadline wins.

## Deadlines and cancellation

- `CallOptions.Deadline` is **UTC absolute**. No default — calls run forever until cancelled.
- Past/current time -> immediately `DeadlineExceeded`.
- Server-side `ctx.CancellationToken` fires when the deadline elapses or the client cancels.
- Deadlines apply across retry attempts (exhausted -> `DeadlineExceeded`, no further retries).

```csharp
try
{
    var r = await client.SayHelloAsync(req,
        deadline: DateTime.UtcNow.AddSeconds(5));
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded) { /* timeout */ }
```

Client cancel: dispose the call (`call.Dispose()`) sends `RST_STREAM`; server CT fires.

## Retries and hedging (`ServiceConfig`)

Requires `Grpc.Net.Client` >= 2.36.0.

```csharp
var defaultMethodConfig = new MethodConfig
{
    Names = { MethodName.Default },
    RetryPolicy = new RetryPolicy
    {
        MaxAttempts          = 5,
        InitialBackoff       = TimeSpan.FromSeconds(1),
        MaxBackoff           = TimeSpan.FromSeconds(5),
        BackoffMultiplier    = 1.5,
        RetryableStatusCodes = { StatusCode.Unavailable }
    }
};

var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
{
    ServiceConfig = new ServiceConfig { MethodConfigs = { defaultMethodConfig } }
});
```

Actual delay = `random(0, currentBackoff)`. After each attempt: `currentBackoff = min(currentBackoff * multiplier, MaxBackoff)`. The framework adds a `grpc-previous-rpc-attempts` header (integer N) on retried attempts.

**Retries DON'T fire when:**
- Deadline already exceeded.
- Call is "committed" — client received response headers OR send-buffer overflowed `MaxRetryBufferSize` / `MaxRetryBufferPerCallSize`.
- Server / bidi streaming after the first response message has been read.
- Client / bidi streaming if request bytes exceeded the buffer.

**Hedging policy** (mutually exclusive with `RetryPolicy`):

```csharp
HedgingPolicy = new HedgingPolicy
{
    MaxAttempts        = 5,
    HedgingDelay       = TimeSpan.Zero,
    NonFatalStatusCodes = { StatusCode.Unavailable }
}
```

Sends parallel attempts; first non-fatal success wins. **Idempotent operations only.**

## Interceptors

Inherit `Grpc.Core.Interceptors.Interceptor`. Server and client overrides are different — not interchangeable.

| Side | Override |
|---|---|
| Client | `BlockingUnaryCall`, `AsyncUnaryCall`, `AsyncClientStreamingCall`, `AsyncServerStreamingCall`, `AsyncDuplexStreamingCall` |
| Server | `UnaryServerHandler`, `ClientStreamingServerHandler`, `ServerStreamingServerHandler`, `DuplexStreamingServerHandler` |

### Client wiring (chain order is REVERSE of `.Intercept(...)`)

```csharp
var invoker = channel
    .Intercept(new ClientTokenInterceptor())
    .Intercept(new ClientMonitoringInterceptor())
    .Intercept(new ClientLoggerInterceptor());
var client  = new Greeter.GreeterClient(invoker);
// Order: Logger -> Monitoring -> Token
```

Client-factory: `.AddInterceptor<LoggingInterceptor>(InterceptorScope.Client);` — `InterceptorScope.Client` required when the interceptor needs Scoped/Transient DI services.

### Server wiring

```csharp
// Global
builder.Services.AddGrpc(o => o.Interceptors.Add<ServerLoggerInterceptor>());

// Per-service (overrides global ordering — globals run first)
builder.Services.AddGrpc()
    .AddServiceOptions<GreeterService>(o => o.Interceptors.Add<ServerLoggerInterceptor>());

// Override default per-request lifetime
builder.Services.AddSingleton<ServerLoggerInterceptor>();
```

### Interceptor vs. middleware

| Aspect | Interceptor | Middleware |
|---|---|---|
| Layer | gRPC abstraction (`ServerCallContext`) | HTTP/2 (`HttpContext`) |
| Sees | Deserialized message in/out | Raw bytes |
| Order | After middleware | Before interceptors |
| Use for | Auth/logging/validation specific to gRPC, exception translation | HTTP-level: CORS, response compression, routing |

## Server configuration

`AddGrpc` and `AddServiceOptions<T>` options:

| Option | Default | Notes |
|---|---|---|
| `MaxSendMessageSize` | null (unlimited) | bytes |
| `MaxReceiveMessageSize` | 4 MB | bytes; null = unlimited |
| `EnableDetailedErrors` | false | leaks info; dev only |
| `CompressionProviders` | gzip | extend with custom |
| `ResponseCompressionAlgorithm` | null | name; client must advertise via `grpc-accept-encoding` |
| `ResponseCompressionLevel` | null | passed to provider |
| `Interceptors` | empty | per-request lifetime by default |
| `IgnoreUnknownServices` | false | unknown calls fall through to next middleware instead of `UNIMPLEMENTED` |

Per-service options override global. Service-scoped interceptors run AFTER global interceptors.

## Performance

- **Channel reuse** — single `GrpcChannel` for the app.
- **HTTP/2 stream concurrency**: cap is `MAX_CONCURRENT_STREAMS` (server default 100). Beyond that, calls queue on the client. To allow multiple HTTP/2 connections from one channel:

```csharp
var channel = GrpcChannel.ForAddress(addr, new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
});
```

The default `SocketsHttpHandler` inside `GrpcChannel` already sets this; override only when supplying a custom handler. **Don't** raise Kestrel `Http2.MaxStreamsPerConnection` — TCP head-of-line blocking, write contention.

- **Server GC for client console apps**: `<ServerGarbageCollection>true</ServerGarbageCollection>`. ASP.NET Core hosts already do this.
- **Always async**.
- **Load balancing**: L4 TCP balancers can't balance multiplexed HTTP/2 — calls pin to one endpoint. Use client-side (`Grpc.Net.Client.Balancer` with `DnsResolver` + `RoundRobin`/`PickFirst`) or an L7 proxy (Envoy, Linkerd, YARP). Once a streaming call is established, all messages stay on a single endpoint regardless.
- **IPC**: Unix domain sockets / named pipes via `SocketsHttpHandler.ConnectCallback`.
- **Keep-alive pings**:

```csharp
var handler = new SocketsHttpHandler
{
    PooledConnectionIdleTimeout    = Timeout.InfiniteTimeSpan,
    KeepAlivePingDelay             = TimeSpan.FromSeconds(60),
    KeepAlivePingTimeout           = TimeSpan.FromSeconds(30),
    EnableMultipleHttp2Connections = true,
};
```

Server must support pings or it sends `GOAWAY`.

- **HTTP/2 flow control** (Kestrel): default stream window 768 KB. Raise when messages exceed it or latency is high; connection >= stream:

```csharp
builder.WebHost.ConfigureKestrel(o =>
{
    var http2 = o.Limits.Http2;
    http2.InitialConnectionWindowSize = 1024 * 1024 * 2; // 2 MB
    http2.InitialStreamWindowSize     = 1024 * 1024;     // 1 MB
});
```

- **Streaming**: dispose every call; end gracefully (`RequestStream.CompleteAsync()`); replacing repeated unary calls with bidi streams reduces HTTP/2 framing overhead — at the cost of restart logic.
- **Large binary payloads**: stream chunks <= 85_000 bytes, OR offload to a separate ASP.NET HTTP endpoint for raw blobs. Use `UnsafeByteOperations.UnsafeWrap(data)` to send without copy; consume via `byteString.Span` / `Memory`.

## Diagnostics

### Logging

```json
{ "Logging": { "LogLevel": {
    "Default": "Information", "Grpc": "Debug" } } }
```

Server categories: `Grpc.AspNetCore.Server.*`. Client categories: `Grpc.Net.Client.Internal.*`. Client log scopes: `GrpcMethodType` (e.g. `Unary`), `GrpcUri` (e.g. `/greet.Greeter/SayHellos`).

### Tracing

| Side | DiagnosticSource | Activity | Tags |
|---|---|---|---|
| Server | `Microsoft.AspNetCore` | `Microsoft.AspNetCore.Hosting.HttpRequestIn` | `grpc.method`, `grpc.status_code` |
| Client | `Grpc.Net.Client` | `Grpc.Net.Client.GrpcOut` | `grpc.method`, `grpc.status_code` |

Tracing fires only at start/stop of the call activity (no per-message events on streams).

### Metrics (`EventCounter`)

Server `Grpc.AspNetCore.Server`: `total-calls`, `current-calls`, `calls-failed`, `calls-deadline-exceeded`, `messages-sent`, `messages-received`, `calls-unimplemented`. Client `Grpc.Net.Client`: same minus `calls-unimplemented`.

```bash
dotnet-counters monitor --process-id 1902 --counters Grpc.AspNetCore.Server
```
