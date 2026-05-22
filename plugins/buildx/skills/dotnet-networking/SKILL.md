---
name: dotnet-networking
description: Networking reference for .NET 10. Covers `HttpClient` lifecycle (singleton + `PooledConnectionLifetime` vs `IHttpClientFactory`), DNS pitfalls, `HttpContent`, `System.Net.Http.Json`, `IHttpClientFactory` (basic/named/typed/Refit), handler-lifetime + cookie caveat, `SocketsHttpHandler` (timeouts, pool caps, HTTP/2 keep-alive, `ConnectCallback` UDS), HTTP version selection, proxy resolution (env vars), `Microsoft.Extensions.Http.Resilience` (standard / hedging / Polly v8), auth (Basic/Bearer/NTLM/Negotiate/Kerberos/client certs), `System.Net.Sockets` (TCP/UDP/UDS, `SocketAsyncEventArgs`), `System.Net.Quic` (MsQuic), `Dns`/`IPNetwork`/`NetworkInterface`/`Ping`, and telemetry (Metrics, tracing, EventSource, OTel/Aspire).
when_to_use: |
  - Trigger keywords: HttpClient, IHttpClientFactory, AddStandardResilienceHandler, AddStandardHedgingHandler, SocketsHttpHandler, PooledConnectionLifetime, ConnectCallback, HttpVersionPolicy, HTTP/3, QUIC, MsQuic, DefaultProxy, NTLM, Negotiate, ClientCertificates, TcpClient, UdpClient, UnixDomainSocketEndPoint, QuicConnection, Dns.GetHostAddressesAsync, AddHttpClientInstrumentation.
  - Task shapes: pick `HttpClient` lifetime; configure typed clients; cap retries on POST/DELETE; build a hedging pool; force HTTP/3; talk to a Unix domain socket; thread bearer tokens via `DelegatingHandler`; consume QUIC streams; subscribe to HTTP client metrics; debug DNS rotation.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Networking — Reference

Reference for `HttpClient`, `IHttpClientFactory`, resilience, sockets, QUIC, and networking telemetry on .NET 10. ASP.NET Core hosting/middleware lives elsewhere; this file is the client/transport story.

## Mental model

- `HttpClient` is a *settings + connection-pool wrapper around `SocketsHttpHandler`*. There is no "just `new`" path that scales.
- DNS is resolved on connect; without bounded connection lifetime, the pool keeps stale IPs forever.
- Resilience is one outer pipeline per client. The standard pipeline encodes years of operational wisdom — start there.
- Below HTTP, sockets/QUIC are first-class. UDP, raw TCP, Unix domain sockets, `SocketAsyncEventArgs` (zero-allocation receive loops), and full QUIC streams are all in-box.
- Telemetry: prefer the modern Metrics + Activities pipeline (OTel-compatible) over EventCounters.

## Non-negotiable rules

1. **Pick one `HttpClient` lifetime model.** Long-lived singleton with `PooledConnectionLifetime` set, **or** short-lived clients from `IHttpClientFactory`. Never `using var client = new HttpClient(); ...` per request — port exhaustion (`TIME-WAIT`).
2. **Set `PooledConnectionLifetime`** on any singleton handler (15 min is typical) so the pool re-resolves DNS.
3. **`HttpClient.Timeout` raises `OperationCanceledException`** with `InnerException = TimeoutException` — distinguish from caller cancellation by the token's `IsCancellationRequested`.
4. **Use `HttpCompletionOption.ResponseHeadersRead`** for streaming/large bodies; otherwise the whole body is buffered before `await SendAsync` returns.
5. **`IHttpClientFactory` pools handlers** — disposing a factory-built `HttpClient` does **not** dispose its handler.
6. **Avoid `IHttpClientFactory` when cookies matter** (pooled handlers share `CookieContainer`), or set `UseCookies = false` and manage cookies manually.
7. **One resilience handler per client.** Do not stack standard + custom.
8. **Forward `CancellationToken`** to every `*Async` method — see `dotnet-asynchronous-programming` for cancellation rules.
9. **HTTP/3 requires `libmsquic`** on Linux (≥ 2.2 from `packages.microsoft.com`). Capability-check via `QuicConnection.IsSupported`.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `HttpClient` lifecycle, `HttpContent`, `IHttpClientFactory`, `SocketsHttpHandler`, HTTP versions, proxy, authentication | [httpclient-factory.md](httpclient-factory.md) | Picking lifetime, configuring named/typed clients, tuning pool caps / timeouts, UDS, NTLM/Bearer/client certs. |
| Resilience — standard + hedging + custom Polly v8 pipelines, dynamic reload, singleton wiring | [resilience.md](resilience.md) | Adding retries, circuit breakers, hedging across endpoints. |
| Sockets, QUIC, DNS, `IPNetwork`, NIC enumeration, ping | [sockets-and-quic.md](sockets-and-quic.md) | Working below HTTP — TCP/UDP/UDS, raw `Socket`, `SocketAsyncEventArgs`, QUIC streams. |
| Telemetry — built-in metrics, distributed tracing, `EventSource`, EventCounters, OTel/Aspire wiring | [telemetry.md](telemetry.md) | Instrumenting HTTP client, debugging DNS rotation, exporting to OTel. |

## Quick decision matrices

### `HttpClient` lifetime

| Situation | Use |
|---|---|
| Singleton service, stable DNS | static `HttpClient` + `PooledConnectionLifetime` ≥ 15 min |
| Many configurations / DI heavy | `IHttpClientFactory` typed clients |
| Need cookies + session affinity | static `HttpClient` (or `UseCookies = false` + manual) |
| Singleton holding typed client | enforce `SocketsHttpHandler.PooledConnectionLifetime` |
| .NET Framework | `IHttpClientFactory` only |

### HTTP version

| Need | `Version` | `VersionPolicy` |
|---|---|---|
| Default browser-grade | `2.0` | `RequestVersionOrLower` |
| Force HTTP/3 | `3.0` | `RequestVersionExact` |
| Auto-upgrade to highest | `2.0` | `RequestVersionOrHigher` |
| Cleartext H2C (gRPC over plaintext) | `2.0` | `RequestVersionExact` |

### Sockets vs helpers

| Need | API |
|---|---|
| TCP client/server, simple stream IO | `TcpClient` / `TcpListener` + `NetworkStream` |
| Datagrams, multicast, broadcast | `UdpClient` |
| Unix domain sockets, raw SO_* tuning, `SocketAsyncEventArgs`, dual-stack toggling | `Socket` |
| Mux streams, low-latency, TLS-mandatory transport | `System.Net.Quic` |

## Cross-references

- Public docs (Networking index): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/
- Public docs (`HttpClient`): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient
- Public docs (`HttpClient` guidelines): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
- Public docs (`IHttpClientFactory`): https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory
- Public docs (`HttpVersionPolicy`): https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpversionpolicy
- Public docs (`SocketsHttpHandler`): https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler
- Public docs (Sockets): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/sockets/socket-services
- Public docs (`TcpClient`/`TcpListener`): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/sockets/tcp-classes
- Public docs (QUIC): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview
- Public docs (HTTP resilience): https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience
- Public docs (Networking telemetry): https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/overview
- Public docs (Built-in metrics — System.Net): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-system-net
- Public docs (Built-in tracing activities): https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities
- Related skill: `dotnet-serialization` — JSON/MessagePack content for HTTP bodies.
- Related skill: `dotnet-io` — `Stream`, `PipeReader`/`PipeWriter`, `RandomAccess` underneath HTTP content.
- Related skill: `dotnet-asynchronous-programming` — `await`, cancellation tokens, deadlock-free patterns for `*Async` methods.
- Related skill: `dotnet-diagnostics` — broader `EventSource`, `Activity`, OpenTelemetry guidance.
