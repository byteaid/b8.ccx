# Scale-out, Configuration, Diagnostics

Sticky sessions, Azure SignalR Service, Redis backplane, Nginx/IIS notes, full `HubOptions` / `HttpConnectionDispatcherOptions` reference, keep-alive table, diagnostics.

## Scale-out

### Sticky sessions (session affinity)

REQUIRED in any multi-server farm except:
1. Single server / single process.
2. Azure SignalR Service.
3. **All** clients use WebSockets only **and** `SkipNegotiation = true`.

Azure App Service uses ARR Affinity. Nginx: `ip_hash` (open-source) or `sticky cookie` (Plus).

### Azure SignalR Service

- Acts as proxy + backplane. Clients are redirected to the service on connect; servers hold ~constant N connections to the service.
- **No sticky sessions required.** App scales by message volume; service scales connections.
- Modes: **Default** (server-based hubs), **Serverless** (Azure Functions + REST), **Classic** (legacy).
- Streaming + client results (`Clients.Client(id).InvokeAsync`) **only supported in Default mode**.

### Redis backplane

```bash
dotnet add package Microsoft.AspNetCore.SignalR.StackExchangeRedis
```

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(connectionString, o =>
    {
        o.Configuration.ChannelPrefix = RedisChannel.Literal("MyApp");
    });
```

**Use a unique `ChannelPrefix` per app** when sharing a Redis instance. When Redis is down, SignalR throws (`Failed writing message`, `Failed to invoke hub method`); messages are **NOT buffered** and are **lost**. Auto-reconnects when Redis returns.

Redis Cluster: more nodes = higher availability, lower throughput (broadcast cost).

Disadvantages vs Azure SignalR Service: requires sticky sessions; scales with client count; persistent connections cost server resources. Use Redis only when on-prem or latency to Azure is high.

### Nginx minimal config (sketch)

```nginx
http {
  map $http_connection $connection_upgrade {
    "~*Upgrade" $http_connection;
    default keep-alive;
  }
  server {
    location /hubroute {
      proxy_pass http://backend;
      proxy_set_header Upgrade $http_upgrade;
      proxy_set_header Connection $connection_upgrade;
      proxy_http_version 1.1;
      proxy_buffering off;
      proxy_read_timeout 100s;
    }
  }
}
```

Sticky options: `ip_hash` (open-source), `sticky cookie srv_id` (Plus).

### IIS on Windows client OS

Windows 10/11 / 8.x IIS limit = **10 concurrent connections**. Use Kestrel or IIS Express for dev.

When YARP fronts SignalR — load `aspnet-core-yarp` for proxy config; remember sticky sessions.

## Configuration reference

### `HubOptions` (server, global)

| Option | Default | Description |
|---|---|---|
| `ClientTimeoutInterval` | 30 s | Server marks dead after this with no client message. >= 2x client `KeepAliveInterval`. |
| `HandshakeTimeout` | 15 s | Initial handshake window. |
| `KeepAliveInterval` | 15 s | Server ping when otherwise idle. |
| `SupportedProtocols` | all installed | Restrict to `JSON` / `MessagePack`. |
| `EnableDetailedErrors` | `false` | Send full exception messages to client. |
| `StreamBufferCapacity` | 10 | Buffer slots for client-to-server upload streams. |
| `MaximumReceiveMessageSize` | 32 KB | Max single inbound hub message. `0` disables — DoS risk. |
| `MaximumParallelInvocationsPerClient` | 1 | Max in-flight hub invocations per client. |
| `DisableImplicitFromServicesParameters` | `false` | Require `[FromServices]` for hub-method DI. |

Per-hub override: `.AddHubOptions<ChatHub>(o => ...)`.

### `HttpConnectionDispatcherOptions` (per `MapHub` endpoint)

| Option | Default | Description |
|---|---|---|
| `Transports` | all | `WebSockets | LongPolling | ServerSentEvents` flags. |
| `ApplicationMaxBufferSize` | 64 KB | Inbound bytes before backpressure. |
| `TransportMaxBufferSize` | 64 KB | Outbound bytes before backpressure. |
| `CloseOnAuthenticationExpiration` | `false` | Close connection on token expiry. |
| `LongPolling.PollTimeout` | 90 s | Max wait before terminating a long poll. |
| `WebSockets.CloseTimeout` | 5 s | Wait for client ack after server close. |
| `AuthorizationData` | auto | List of `IAuthorizeData`. |
| `MinimumProtocolVersion` | 0 | Min negotiate protocol version. |

```csharp
app.MapHub<ChatHub>("/chathub", o =>
{
    o.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
    o.LongPolling.PollTimeout = TimeSpan.FromSeconds(90);
});
```

### Keep-alive cheat sheet

| Side | Knob | Default | Rule |
|---|---|---|---|
| Server | `KeepAliveInterval` | 15 s | Ping when idle. |
| Server | `ClientTimeoutInterval` | 30 s | Dead if no client message. >= 2x client `KeepAliveInterval`. |
| Server | `HandshakeTimeout` | 15 s | Initial handshake window. |
| Client | `WithKeepAliveInterval` | 15 s | Client ping. |
| Client | `WithServerTimeout` | 30 s | Error if no server message. >= 2x server `KeepAliveInterval`. |
| Client | `HandshakeTimeout` | 15 s | Initial handshake. |

## Diagnostics

### Logging categories

- Server: `Microsoft.AspNetCore.SignalR` (hub protocol, activation, invocation), `Microsoft.AspNetCore.Http.Connections` (transports + low-level connection).
- .NET client: same categories.

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.SignalR": "Debug",
      "Microsoft.AspNetCore.Http.Connections": "Debug"
    }
  }
}
```

### Distributed tracing (`ActivitySource`)

- **Server:** `Microsoft.AspNetCore.SignalR.Server` — one activity per hub method call. **No parent** — activities don't bundle under the long-running connection.
- **.NET client:** `Microsoft.AspNetCore.SignalR.Client` — span per hub invocation, with **W3C trace context propagation** to the server.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddSource("Microsoft.AspNetCore.SignalR.Server");
        // client: t.AddSource("Microsoft.AspNetCore.SignalR.Client");
    });
```

### Metrics (`EventCounter` source `Microsoft.AspNetCore.Http.Connections`)

`connections-started`, `connections-stopped`, `connections-timed-out`, `current-connections`, `connections-duration`.

```bash
dotnet-counters monitor --process-id <PID> --counters Microsoft.AspNetCore.Http.Connections
```

### Network traces

- **Fiddler** — preferred, captures all transports including WebSockets payload. Save as `.saz`.
- **tcpdump** — `tcpdump -i [iface] -w trace.pcap`.
- Browser DevTools Network tab works for HTTP fallbacks; does **NOT** capture WebSocket/SSE message bodies.
