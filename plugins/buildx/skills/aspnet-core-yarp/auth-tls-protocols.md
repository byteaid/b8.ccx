# Auth, Headers, TLS, WebSockets, gRPC, HTTP/3

Per-route auth, header guidelines, TLS termination, WebSockets, gRPC over HTTP/2, HTTP/3.

## Auth

Off unless route opts in. Per-route `AuthorizationPolicy`. Special values: `default` (use `AuthorizationOptions.DefaultPolicy`), `anonymous` (no authz regardless of `FallbackPolicy`).

```csharp
services.AddAuthorization(o =>
    o.AddPolicy("customPolicy", p => p.RequireAuthenticatedUser()));
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();
```

| Auth type | Behavior at YARP |
|---|---|
| Cookie / Bearer / API key | Headers flow by default; destination still verifies. |
| OAuth2 / OIDC / WS-Federation | Run flow at proxy -> cookie -> flows as normal header. Load `aspnet-core-security` for the wiring. |
| Windows / Negotiate / NTLM / Kerberos | **Connection-bound** — cannot authenticate end user at destination through YARP. Can authenticate the proxy itself to the destination, no impersonation. |
| Client cert | `ClientCert` transform (Base64 in custom header). Cert must already be on the connection. |
| Type swap | Custom request transform mints a JWT (or whatever) from the validated identity. |

## Header guidelines

YARP automatically removes connection / security-sensitive headers — `Connection`, `KeepAlive`, `Close`, `Transfer-Encoding`, `TE` (except `TE: trailers` for gRPC), `Upgrade` (re-added only for WebSockets/SPDY), `Proxy-*`, `Alt-Svc` (response).

Distributed-tracing headers (`TraceParent`, `Request-Id`, `TraceState`, `Baggage`, `Correlation-Context`) auto-removed via `DistributedContextPropagator.Fields` — `HttpClient` re-injects updated values. Pass-through: set `handler.ActivityHeadersPropagator = null`.

`Strict-Transport-Security`: destination's value not copied if proxy already added one. `Host`: removed by default (`RequestHeaderOriginalHost` to keep). `X-Forwarded-*` / `Forwarded`: existing values stripped and replaced (anti-spoof). `X-Http-Method-Override` / `X-Http-Method` / `X-Method-Override`: proxied (block via `RequestHeaderRemove`). `Set-Cookie`, `Location`: proxied as-is — recommend configuring destination to read `Forwarded` headers so it generates correct values. `Server`, `X-Powered-By`: proxied as-is — remove via `ResponseHeaderRemove`. Suppressing the proxy's own server header is server-specific (`KestrelServerOptions.AddServerHeader = false`).

## HTTPS / TLS, WebSockets, gRPC, HTTP/3

- **TLS terminates at YARP.** No CONNECT tunneling. Inbound TLS at the host. Outbound TLS uses destination address Host for SNI; `RequestHeaderOriginalHost=true` switches SNI to the original Host.
- **CORS** per-route `CorsPolicy`. Off by default. `default` / `disable` special values.
- **WebSockets / SPDY**: enabled by default. HTTP/1.1 upgrade flow on `101 Switching Protocols`. **HTTP/2 WebSockets (RFC 8441)** supported in .NET 7+ / YARP 2.0+ (inbound only via Kestrel). Outbound version follows `HttpRequest.Version` / `VersionPolicy`. After WS handshake, request `Timeout` disabled; `ActivityTimeout` still active. WebSocket pings reset `ActivityTimeout`; HTTP/2 pings do **not**.
- **gRPC** requires HTTP/2 end-to-end. For `http://` + HTTP/2: `"Kestrel": { "Endpoints": { "http": { "Url":"http://localhost:5000", "Protocols":"Http2" } } }`. Outbound: `"HttpRequest": { "Version":"2", "VersionPolicy":"RequestVersionExact" }`. gRPC-Web (`application/grpc-web`) is HTTP/1.1-compatible and works without special config (caveat: no client/bidi streaming). For deep gRPC config, load `aspnet-core-grpc`.
- **HTTP/3** (YARP 1.1+, .NET 7 HTTP/3): Kestrel `Protocols = Http1AndHttp2AndHttp3`; outbound `HttpRequest.Version = "3"`.
