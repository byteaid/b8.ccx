# Hubs and Clients

`Hub`, `Hub<T>`, `IHubContext<THub>`, `IUserIdProvider`, .NET client, JS client, lifecycle, reconnect.

## Server: hub class

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
var app = builder.Build();
app.MapHub<ChatHub>("/Chat");
app.Run();

public class ChatHub : Hub
{
    public async Task SendMessage(string user, string message)
        => await Clients.All.SendAsync("ReceiveMessage", user, message);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // exception == null on intentional disconnect; != null on network/error.
        return base.OnDisconnectedAsync(exception);
    }
}
```

`RemoveFromGroupAsync` is **not** required in `OnDisconnectedAsync` — group cleanup is automatic.

### `Hub.Context` (`HubCallerContext`)

| Member | Description |
|---|---|
| `ConnectionId` | Unique per connection. |
| `UserIdentifier` | From `ClaimTypes.NameIdentifier` by default. |
| `User` | `ClaimsPrincipal`. |
| `Items` | Per-connection KV store, persists across method invocations. |
| `Features` | Connection feature collection. |
| `ConnectionAborted` | `CancellationToken` fired on abort. |
| `GetHttpContext()` | Returns `HttpContext` for HTTP-based connections. |
| `Abort()` | Aborts the connection. |

### `Hub.Clients` targets

`All`, `Caller`, `Others`, `AllExcept(ids)`, `Client(id)`, `Clients(ids)`, `Group(name)`, `GroupExcept(name, ids)`, `Groups(names)`, `OthersInGroup(name)`, `User(userId)`, `Users(userIds)`. Each returns an `IClientProxy.SendAsync(method, args...)`. `Caller` and `Client(id)` additionally return `ISingleClientProxy.InvokeAsync<T>(...)` for **client results**.

### Strongly-typed hubs (`Hub<T>`)

Eliminates magic-string method names; disables `SendAsync`. The `Async` suffix is **NOT** stripped — `MyMethodAsync` requires `.on('MyMethodAsync')` on the client.

```csharp
public interface IChatClient { Task ReceiveMessage(string user, string message); }
public class ChatHub : Hub<IChatClient>
{
    public Task SendMessage(string u, string m) => Clients.All.ReceiveMessage(u, m);
}
```

### Client results

```csharp
public async Task<string> WaitForMessage(string connectionId)
    => await Clients.Client(connectionId).InvokeAsync<string>("GetMessage");
```

Client side returns from its `.On(...)` handler. Limitation: only Default-mode Azure SignalR Service supports cross-server client invocations.

### Method renaming and DI

```csharp
[HubMethodName("SendMessageToUser")]
public Task DirectMessage(string user, string message) => /* ... */;

// Hub-method parameter DI is on by default — disable globally to require [FromServices]:
services.AddSignalR(o => o.DisableImplicitFromServicesParameters = true);

public Task SendMessage(string u, string m, [FromServices] IDatabaseService db) { /* ... */ }

// Keyed services
public void Small([FromKeyedServices("small")] ICache cache) { /* ... */ }
```

### Errors

```csharp
public Task ThrowException() => throw new HubException("Sent to client.");
```

Default `Exception`s give the client `HubException: An unexpected error occurred...`; only `HubException.Message` is propagated. Connection stays open. `EnableDetailedErrors = true` for dev.

## `IHubContext` — calling clients from outside a hub

For sending notifications from controllers, middleware, hosted services — **not** for invoking server hub methods.

```csharp
public class HomeController(IHubContext<NotificationHub> hub) : Controller
{
    public async Task<IActionResult> Index()
    {
        await hub.Clients.All.SendAsync("Notify", $"Loaded at {DateTime.Now}");
        return View();
    }
}
```

Strongly-typed: `IHubContext<THub, T>` — `Clients.All.MyMethod(args)`. From outside the hub there is **no caller** — `Caller`, `Others`, `ConnectionId` are unavailable.

`IHubContext<THub>` is castable to non-generic `IHubContext` for shared library code.

## Custom user identifier

```csharp
public class EmailBasedUserIdProvider : IUserIdProvider
{
    public string GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst(ClaimTypes.Email)?.Value!;
}
builder.Services.AddSingleton<IUserIdProvider, EmailBasedUserIdProvider>();
```

Chosen value MUST be unique system-wide; non-uniqueness causes message cross-delivery. For Windows auth, use `Identity.Name` (no `NameIdentifier` claim is supplied).

## .NET client (`Microsoft.AspNetCore.SignalR.Client`)

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://example.com/chathub")
    .WithAutomaticReconnect()    // defaults: 0, 2, 10, 30 seconds (4 attempts)
    .Build();

connection.On<string, string>("ReceiveMessage", (u, m) => { /* ... */ });
await connection.StartAsync();
await connection.InvokeAsync("SendMessage", user, message);   // awaits server completion
await connection.SendAsync("SendMessage",   user, message);   // fire-and-forget
```

`On` registrations must occur **before** `StartAsync` to avoid missing early messages. Strongly-typed hubs on the server still require **string-based** `On` on the .NET client.

### Lifecycle events

```csharp
connection.Closed       += async error => { /* permanent close */ };
connection.Reconnecting += error => { /* about to retry */; return Task.CompletedTask; };
connection.Reconnected  += newConnectionId => { /* back online */; return Task.CompletedTask; };
```

`Reconnected` `connectionId` parameter is `null` when negotiation is skipped.

### Reconnect

`WithAutomaticReconnect()` defaults: `[0, 2 s, 10 s, 30 s]`. Custom delay table or `IRetryPolicy`:

```csharp
public class RandomRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext ctx)
    {
        if (ctx.ElapsedTime < TimeSpan.FromSeconds(60))
            return TimeSpan.FromSeconds(Random.Shared.NextDouble() * 10);
        return null;   // stop
    }
}
```

State machine: lost -> `Reconnecting` -> `Reconnected` (new `ConnectionId`); exhausted -> `Disconnected` -> `Closed`. **Initial start failures are NOT retried** — handle manually with a retry loop calling `StartAsync`.

### Stateful reconnect

Replays buffered messages on reconnect:

```csharp
var builder = new HubConnectionBuilder()
    .WithUrl(hubUrl).WithStatefulReconnect();
builder.Services.Configure<HubConnectionOptions>(o => o.StatefulReconnectBufferSize = 1000);
```

### Configuration via `WithUrl(..., options => ...)`

| Option | Default | Notes |
|---|---|---|
| `AccessTokenProvider` | `null` | Async; called **before every** HTTP request — renew here. |
| `SkipNegotiation` | `false` | WebSockets-only mode. |
| `Transports` | all | `HttpTransportType.WebSockets | LongPolling | ServerSentEvents`. |
| `Headers`, `Cookies`, `Credentials`, `ClientCertificates`, `Proxy`, `UseDefaultCredentials` | — | Standard HTTP knobs. |
| `WebSocketConfiguration` | `null` | `Action<ClientWebSocketOptions>`. |
| `HttpMessageHandlerFactory` | `null` | Wrap inner handler. |
| `ApplicationMaxBufferSize` | 1 MB | Inbound from server. |
| `TransportMaxBufferSize` | 1 MB | Outbound to server. |
| `CloseTimeout` | 5 s | WS close ack timeout. |

Keep-alive on the builder: `WithServerTimeout` (default 30 s), `WithKeepAliveInterval` (15 s). `HandshakeTimeout` (15 s) on the built `HubConnection`.

## JS / TS client (`@microsoft/signalr`)

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/chathub")
    .withAutomaticReconnect()    // default [0, 2000, 10000, 30000]
    .configureLogging(signalR.LogLevel.Information)
    .build();

async function start() {
    try { await connection.start(); }
    catch (err) { setTimeout(start, 5000); }
}
connection.onclose(async () => { await start(); });

connection.on("ReceiveMessage", (user, message) => { /* ... */ });
await connection.invoke("SendMessage", user, message);  // expects server result
await connection.send  ("SendMessage", user, message);  // fire-and-forget
```

JS `IRetryPolicy` shape: `{ nextRetryDelayInMilliseconds(ctx) { ... } }` returning `null` to stop. Initial `start()` failures are NOT retried.

Browser sleeping tabs may freeze SignalR connections — hold a Web Lock to keep awake (`navigator.locks.request("name", { mode: "shared" }, () => promise)`).

## Blazor SignalR patterns

Custom hub from a Blazor component (WebAssembly or Server) — implement `IAsyncDisposable` and dispose `HubConnection` in cleanup. Use `await InvokeAsync(StateHasChanged)` from the `On<...>` callback if it runs outside the dispatcher.

BFF / cross-origin auth from Blazor WASM — use a `DelegatingHandler` that sets `BrowserRequestCredentials.Include`:

```csharp
public class IncludeRequestCredentialsMessageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(req, ct);
    }
}

hubConnection = new HubConnectionBuilder()
    .WithUrl(new Uri(Navigation.ToAbsoluteUri("/chathub")), o =>
        o.HttpMessageHandlerFactory = inner =>
            new IncludeRequestCredentialsMessageHandler { InnerHandler = inner })
    .Build();
```

Blazor Web App (Interactive Server) — circuit hub uses the same `HubOptions`:

```csharp
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o =>
    {
        o.ClientTimeoutInterval     = TimeSpan.FromSeconds(60);
        o.HandshakeTimeout          = TimeSpan.FromSeconds(30);
        o.KeepAliveInterval         = TimeSpan.FromSeconds(15);
        o.MaximumReceiveMessageSize = 64 * 1024;
    });
```

Disable WS compression (CRIME/BREACH defense): `app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.DisableWebSocketCompression = true);` — see `aspnet-core-blazor` § WebSocket compression security note.
