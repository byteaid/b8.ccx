---
name: aspnet-core-signalr
description: ASP.NET Core SignalR reference for .NET 10. Covers `Hub`/`Hub<T>`, `IHubContext<THub>` outside hubs, transports (WebSockets / SSE / long-polling) + negotiation, groups vs users, server- and upload-streaming via `IAsyncEnumerable<T>`/`ChannelReader<T>`, the .NET client (`HubConnection`, auto-reconnect, `IRetryPolicy`, stateful reconnect), JS client (`@microsoft/signalr`), Blazor circuit knobs, JWT/cookie/Windows auth (incl. `access_token` query string for WebSockets), `IHubFilter`, hub-method DI, MessagePack, scale-out (Azure SignalR / Redis / sticky sessions), CORS, tracing, EventCounter metrics, trimming/AOT.
when_to_use: |
  - Trigger keywords: SignalR, Hub, IHubContext, MapHub, HubConnection, WithAutomaticReconnect, WithStatefulReconnect, IRetryPolicy, AccessTokenProvider, Clients.Group, Groups.AddToGroupAsync, IUserIdProvider, OnConnectedAsync, IAsyncEnumerable streaming, ChannelReader, IHubFilter, AddMessagePackProtocol, AddStackExchangeRedis, Azure SignalR Service, sticky sessions, KeepAliveInterval, ClientTimeoutInterval, MaximumReceiveMessageSize, HubException, HubInvocationContext.
  - Task shapes: scaffold a Hub + JS/.NET client; pick a transport / configure CORS for browsers; design groups vs users for fan-out; expose a streaming hub method; consume an upload-stream; secure a hub with JWT incl. WebSocket query-string fallback; wire `IHubFilter`; pick scale-out (Azure SignalR vs Redis); switch to MessagePack; tune keep-alive vs server-timeout; debug reconnect / `HubException`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.ts", "**/*.js", "**/Program.cs", "**/appsettings*.json"]
---

# ASP.NET Core SignalR — Reference

Reference for authoring and reviewing SignalR hubs and clients on .NET 10. Pin the rules; load the matching sub-file for depth.

## Mental model

- SignalR = real-time RPC. Server pushes to clients; clients invoke server methods via *hubs*. Two built-in hub protocols: JSON (default) and MessagePack (binary, smaller, strict typing).
- A **Hub** is an HTTP endpoint mapped via `app.MapHub<T>("/route")`. Each connection lives across many requests; the hub instance itself is **transient** (constructed per invocation).
- Three transports negotiated in order of preference: **WebSockets** (best, bidi) -> **Server-Sent Events** (one-way + POST upstream) -> **Long Polling** (last resort).
- **Groups** are application-managed named collections of connection IDs (cheap, in-memory, **not a security primitive**). **Users** are identified by `IUserIdProvider` (default = `ClaimTypes.NameIdentifier`); a single user may have many concurrent connections (desktop + phone).
- **Streaming** flows in either direction via `IAsyncEnumerable<T>` (preferred) or `ChannelReader<T>`.
- A multi-server farm needs **sticky sessions** OR Azure SignalR Service OR all-clients-on-WebSockets-with-`SkipNegotiation=true`. Otherwise add a backplane (Azure SignalR Service or Redis) so cross-server fan-out works.

## Non-negotiable rules

1. **Hubs are transient — never store state on the hub instance.** Use `Hub.Context.Items` (per-connection KV store, persists across method invocations on the same connection) for connection-scoped state, or a singleton service for app-scoped state.
2. **Always `await` async hub calls.** Fire-and-forget `SendAsync` may race the hub method's completion.
3. **Group membership does not survive reconnect.** Re-add on `OnConnectedAsync`. Group lookup, count, listing — **none exist** by design (impossible across scale-out).
4. **Groups are not a security primitive.** Claims have expiry/revocation; group membership doesn't. Remove explicitly when permission is revoked.
5. **Group names and `UserIdentifier` are case-sensitive.** Method names on JS receive are case-sensitive; hub URL resolution is case-insensitive.
6. **CORS for cross-origin browser clients**: specific origin (no `*`), `GET` and `POST` methods, **`AllowCredentials()`** (sticky session cookies need it even without auth), `app.UseCors()` BEFORE `MapHub`.
7. **Browsers cannot set `Authorization` headers for WebSockets/SSE** — JWT bearer ships in `?access_token=...` query string; hook `JwtBearerEvents.OnMessageReceived` to fish it out and gate it to your hub path. **`Microsoft.AspNetCore.Hosting` logs full URLs at `Information`** — set the category to `Warning`+ in production or strip `access_token` in middleware. **HTTPS only.**
8. **`HubException.Message` is the only exception detail shipped to the client** by default. Stack traces are dropped. The connection is **not** closed on hub-method exception. `EnableDetailedErrors = true` is **dev only**.
9. **Server timeout >= 2 x keep-alive interval.** Default: server pings at 15 s, server timeout at 30 s. Same on the client side. Mismatched timers cause spurious disconnects.
10. **Streaming + `Clients.Client(id).InvokeAsync` (client-results) only work in Azure SignalR Service `Default` mode**, not Serverless mode.
11. **Don't expose `ConnectionId` to untrusted parties.** The actual session secret is `ConnectionToken` (not exposed via API), but older clients (<= 2.2) used `ConnectionId` as the secret.
12. **MessagePack is strict-typed**: no JSON-style `"42" <-> 42` coercion; no preservation of `DateTime.Kind` (always send UTC); JS payloads must use `PascalCase` to match C# property names (or `[Key]` to remap).

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `Hub`, `Hub<T>`, `IHubContext<THub>`, `IUserIdProvider`, .NET client, JS client, lifecycle, reconnect, Blazor patterns | [hubs-and-clients.md](hubs-and-clients.md) | Authoring a hub or client; debugging reconnect / lifecycle; configuring `WithUrl` options. |
| Server- and upload-streaming via `IAsyncEnumerable<T>` / `ChannelReader<T>`; `IHubFilter`; MessagePack hub protocol; `[JsonPolymorphic]` | [streaming-and-filters.md](streaming-and-filters.md) | Building a streaming hub method, intercepting hub calls with a filter, switching to MessagePack. |
| JWT (incl. `?access_token=` for WebSockets), cookie / Windows / mTLS, `[Authorize]`, `HubInvocationContext`, CORS, WebSocket origin validation | [auth-and-cors.md](auth-and-cors.md) | Securing a hub or wiring CORS for cross-origin browsers. |
| Sticky sessions, Azure SignalR Service, Redis backplane, Nginx/IIS notes; full `HubOptions` / `HttpConnectionDispatcherOptions` reference; keep-alive table; logging, tracing, metrics, Fiddler/tcpdump | [scale-out-and-config.md](scale-out-and-config.md) | Designing for scale-out, tuning timeouts, or chasing diagnostics. |
| Trimming / Native AOT scenarios + constraints | [aot-and-trimming.md](aot-and-trimming.md) | Publishing a trimmed or AOT app with SignalR. |

## Quick decision matrix

| Question | Answer |
|---|---|
| Real-time fan-out to many browsers | SignalR. (gRPC has no broadcast.) |
| Point-to-point typed RPC | gRPC — load `aspnet-core-grpc`. |
| Need server -> client method invocations with return | Client results via `Clients.Client(id).InvokeAsync<T>` (Default mode only on Azure SignalR Service). |
| Per-user fan-out | `Clients.User(userId)` + `IUserIdProvider` returning a unique value. |
| Per-room fan-out | Groups; re-add on `OnConnectedAsync`. |
| Multi-server farm | Sticky sessions + Azure SignalR Service or Redis backplane. Load `scale-out-and-config.md`. |
| Browser cross-origin | CORS w/ `AllowCredentials()`, `UseCors` before `MapHub`; validate `Origin` for WS in middleware. Load `auth-and-cors.md`. |
| JWT in browser via WebSockets | `?access_token=...` query string + `JwtBearerEvents.OnMessageReceived` filter. Load `auth-and-cors.md`. |
| Smaller payload, strict types | MessagePack hub protocol. Load `streaming-and-filters.md`. |
| Block / mutate hub call generically | `IHubFilter`. Load `streaming-and-filters.md`. |

## Cross-references

- Public docs (Overview): https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0
- Public docs (Hubs): https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs?view=aspnetcore-10.0
- Public docs (`IHubContext`): https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-10.0
- Public docs (Users / groups): https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0
- Public docs (Streaming): https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming?view=aspnetcore-10.0
- Public docs (.NET client): https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0
- Public docs (JS client): https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0
- Public docs (Configuration): https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-10.0
- Public docs (Auth): https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0
- Public docs (Security): https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0
- Public docs (Scale-out): https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0
- Public docs (Redis backplane): https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0
- Public docs (Azure SignalR Service): https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-overview
- Public docs (MessagePack): https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0
- Public docs (Hub filters): https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters?view=aspnetcore-10.0
- Public docs (Diagnostics): https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics?view=aspnetcore-10.0
- Related skill: `aspnet-core-blazor` — Blazor Server's circuit *uses* SignalR; this skill applies when you tune the circuit's `HubOptions` or author your own hub.
- Related skill: `aspnet-core-grpc` — for typed point-to-point RPC; gRPC has no broadcast primitive.
- Related skill: `aspnet-core-yarp` — when YARP fronts SignalR (sticky sessions still required).
- Related skill: `aspnet-core-security` — JWT issuance, OIDC, Identity, Data Protection.
- Related skill: `dotnet-asynchronous-programming` — `IAsyncEnumerable<T>`, `CancellationToken`, `await foreach` semantics SignalR streams rely on.
- Related skill: `dotnet-parallel-and-threading` — `System.Threading.Channels` for `ChannelReader<T>` streaming.
