---
name: aspnet-core-yarp
description: YARP reverse-proxy reference for .NET 10 / `Yarp.ReverseProxy` 2.3+. Covers `AddReverseProxy().LoadFromConfig(...)` + `MapReverseProxy()`, Routes/Clusters/Destinations config, route matching (path/host/method/headers/query), per-cluster `HttpClient` + `HttpRequest`, transform pipeline (built-in path/query/header/X-Forwarded; custom `ITransformProvider`/`ITransformFactory`), load-balancing, session affinity, active + passive health checks, rate limiting, timeouts, output caching, edge auth, gRPC + WebSockets + HTTP/3 proxying, tracing, direct forwarding via `IHttpForwarder`/`MapForwarder`, hot config reload, custom `IProxyConfigProvider`/`IProxyConfigFilter`/`IDestinationResolver`, `IReverseProxyFeature` integration.
when_to_use: |
  - Trigger keywords: YARP, Yarp.ReverseProxy, AddReverseProxy, MapReverseProxy, MapForwarder, IHttpForwarder, RouteConfig, ClusterConfig, DestinationConfig, LoadBalancingPolicy, PowerOfTwoChoices, SessionAffinity, HashCookie, ActiveHealthCheck, ITransformProvider, AddXForwarded, IDestinationResolver, IProxyConfigFilter, RateLimiterPolicy, OutputCachePolicy, IReverseProxyFeature.
  - Task shapes: scaffold a YARP proxy; design Routes + Clusters + Destinations; add path/header/query routing; transform a request (path/header rewrite, X-Forwarded); pick load-balancing + session affinity; wire active + passive health checks; route gRPC / WebSockets / HTTP/3; do direct forwarding for dynamic destinations; debug a 502/504/"no destination".
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/Program.cs", "**/appsettings*.json"]
---

# ASP.NET Core YARP — Reference

Reference for authoring and reviewing YARP reverse-proxy apps on .NET 10 / `Yarp.ReverseProxy` 2.3+. Pin the rules; load the matching sub-file for depth.

## Mental model

- YARP is a **library** (NuGet `Yarp.ReverseProxy`), not a product or container image. You build a normal ASP.NET Core app on Kestrel / IIS / HTTP.sys, add `AddReverseProxy()`, map the proxy, and you have a proxy.
- YARP is a **L7 HTTP proxy**. **TLS terminates at YARP.** **No CONNECT tunneling** (and no plan to add it).
- Inbound and outbound connections are independent: HTTP versions, lifetimes, URL space, headers all decouple. Per-route transforms shape the outgoing request/response.
- Two moving pieces in config: **Routes** (match incoming requests) -> reference **Clusters** (group of destinations + load-balancing + health + HttpClient settings).
- `appsettings.json` is the canonical config source; **hot-reload is on** — edit the file at runtime and YARP rebuilds atomically without restart.
- Routes/Clusters config is **immutable**. Updates produce new instances. Custom `IProxyConfigProvider` registers as **singleton**.

## Non-negotiable rules

1. **TLS terminates at YARP.** Inbound TLS is set at the host (Kestrel `ListenOptions.UseHttps()`, IIS, HTTP.sys). Outbound TLS uses the destination's `Address` Host for SNI + cert validation by default.
2. **Custom `IForwarderHttpClientFactory` MUST return `HttpMessageInvoker`, never `HttpClient`.** `HttpClient` buffers responses -> breaks streaming, raises memory and latency.
3. **Routes need at least `Path` or `Hosts` in `Match`.** `RouteId`, `ClusterId` mandatory.
4. **`Order` is lower-wins.** Without `Order`, precedence is path > method > host > headers > query parameters.
5. **`SessionAffinity.AffinityKeyName` MUST be unique across clusters when affinity is enabled** — collisions cross-deliver users.
6. **`UseSessionAffinity()` MUST come before `UseLoadBalancing()`** in the proxy pipeline. The default `MapReverseProxy()` does this for you; if you call `MapReverseProxy(Action<IReverseProxyApplicationBuilder>)` you must add them yourself in the right order.
7. **YARP cannot impersonate end users on Windows-bound auth** (NTLM/Kerberos/Negotiate are connection-bound). It can authenticate the proxy itself to the destination. For end-user identity, mint a downstream token with a custom request transform.
8. **`X-Forwarded-*` / `Forwarded` headers are stripped and replaced by default** to prevent spoofing. Destinations must be configured to trust the proxy's values.
9. **Don't mutate request fields in custom middleware** — use transforms. Modifying request fields breaks retry semantics.
10. **Always check `HttpResponse.HasStarted` before modifying response after `await next()`.** Otherwise you ship a half-written response.
11. **`appsettings.Development.json` overrides `appsettings.json` in Dev environment** — surprise source of mismatched routes when debugging.
12. **Hot-reload is fragile if `IProxyConfigProvider.GetConfig()` throws after first load** — `IChangeToken` is single-use; an exception disables future reloads. Validate via `IConfigValidator` instead.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `AddReverseProxy`, `LoadFromConfig`, `MapReverseProxy`, full route + cluster + destination + per-cluster `HttpClient`/`HttpRequest` config, `IDestinationResolver` | [clusters-and-routes.md](clusters-and-routes.md) | Bootstrapping a proxy or designing the route/cluster graph. |
| Default transforms, built-in request/response/trailer transforms, `ITransformProvider`, custom transform classes, body-transform escape hatch | [transforms.md](transforms.md) | Rewriting paths, headers, X-Forwarded, mTLS cert headers; or writing a custom transform. |
| Load-balancing policies, session affinity (cookie / header), active + passive health checks, available-destinations policies, rate limiting, timeouts, output caching | [health-and-affinity.md](health-and-affinity.md) | Designing failover, sticky sessions, or rate/timeout/cache resilience. |
| Per-route auth, header guidelines, TLS termination, WebSockets (incl. RFC 8441 HTTP/2 WS), gRPC over HTTP/2, HTTP/3 | [auth-tls-protocols.md](auth-tls-protocols.md) | Securing the proxy edge or proxying WebSockets / gRPC / HTTP/3. |
| Distributed tracing, A/B testing & rolling upgrades, `IHttpForwarder` direct forwarding, HTTP.sys delegation, `IReverseProxyFeature` middleware integration, logging, telemetry consumers, defaults table, common pitfalls | [telemetry-and-patterns.md](telemetry-and-patterns.md) | Wiring OpenTelemetry, doing direct forwarding, embedding custom middleware, debugging diagnostics. |

## Cross-references

- Public docs (Overview): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/yarp-overview?view=aspnetcore-10.0
- Public docs (Getting started): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0
- Public docs (Config files): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0
- Public docs (Transforms): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms?view=aspnetcore-10.0
- Public docs (Load balancing): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/load-balancing?view=aspnetcore-10.0
- Public docs (Session affinity): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/session-affinity?view=aspnetcore-10.0
- Public docs (Health checks): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/dests-health-checks?view=aspnetcore-10.0
- Public docs (Timeouts): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0
- Public docs (Auth/Authz): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0
- Public docs (Header guidelines): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/header-guidelines?view=aspnetcore-10.0
- Public docs (gRPC): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/grpc?view=aspnetcore-10.0
- Public docs (WebSockets): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0
- Public docs (Distributed tracing): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/distributed-tracing?view=aspnetcore-10.0
- Public docs (Direct forwarding): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/direct-forwarding?view=aspnetcore-10.0
- Public docs (Diagnosing YARP): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/diagnosing-yarp-issues?view=aspnetcore-10.0
- Related skill: `aspnet-core-grpc` — for the gRPC service being proxied (HTTP/2 endpoint config, deadlines, retries).
- Related skill: `aspnet-core-signalr` — sticky sessions still required when YARP fronts SignalR.
- Related skill: `aspnet-core-security` — for OIDC / cookie / Identity flows that terminate at the proxy.
- Related skill: `dotnet-networking` — `SocketsHttpHandler`, HTTP/2 / HTTP/3, distributed-tracing propagators that the per-cluster handler relies on.
