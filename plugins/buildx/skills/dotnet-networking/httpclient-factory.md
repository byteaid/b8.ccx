# `HttpClient`, `HttpContent`, `IHttpClientFactory`, `SocketsHttpHandler`

Lifecycle, content types, factory patterns, and `SocketsHttpHandler` knobs. Load when picking an `HttpClient` lifetime, configuring named/typed clients, tuning the connection pool, or routing through a UDS / proxy.

## `HttpClient` — lifecycle

| Situation | Recommendation |
|---|---|
| Singleton service, stable DNS | static `HttpClient` + `PooledConnectionLifetime` ≥ 15 min |
| Many configurations / DI heavy | `IHttpClientFactory` typed clients |
| Need cookies + session affinity | static `HttpClient` (or set `UseCookies = false` + manual) |
| Singleton holding typed client | enforce `SocketsHttpHandler.PooledConnectionLifetime` |
| .NET Framework | `IHttpClientFactory` only |

```csharp
// Singleton with handler that rotates DNS
var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) };
var sharedClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") };
```

After the first request, `BaseAddress` / `DefaultRequestHeaders` / `Timeout` / `DefaultRequestVersion` / `DefaultVersionPolicy` are effectively read-only — mutating leads to races.

### Verbs

| Verb | API |
|---|---|
| GET | `GetAsync`, `GetByteArrayAsync`, `GetStreamAsync`, `GetStringAsync` |
| POST/PUT/PATCH/DELETE | `PostAsync` / `PutAsync` / `PatchAsync` / `DeleteAsync` |
| Any (HEAD/OPTIONS/TRACE) | `SendAsync(HttpRequestMessage)` with `HttpMethod.Head/Options/Trace` |

The synchronous `Send` exists; do not use it for I/O-bound paths.

### Canonical request handling

```csharp
using HttpResponseMessage response = await httpClient.GetAsync("todos/3");
response.EnsureSuccessStatusCode();
var body = await response.Content.ReadAsStringAsync();
```

Status checks: `StatusCode`, `IsSuccessStatusCode` (2xx), `EnsureSuccessStatusCode()` throws `HttpRequestException` (carries `StatusCode` since .NET 5).

The "shorthand" methods (`GetByteArrayAsync`, `GetStreamAsync`, `GetStringAsync`) **implicitly call** `EnsureSuccessStatusCode`, so catch `HttpRequestException.StatusCode` to differentiate failures:

```csharp
try { using var s = await httpClient.GetStreamAsync(url); }
catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound) { /* 404 */ }
```

### Cancellation vs timeout

```csharp
using var cts = new CancellationTokenSource();
try { using var r = await httpClient.GetAsync(url, cts.Token); }
catch (OperationCanceledException) when (cts.IsCancellationRequested)            { /* user cancel */ }
catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException){ /* timeout    */ }
```

### Reading response content

```csharp
await using Stream s = await response.Content.ReadAsStreamAsync();
byte[] bytes         = await response.Content.ReadAsByteArrayAsync();
string body          = await response.Content.ReadAsStringAsync();
T? obj               = await response.Content.ReadFromJsonAsync<T>();
```

## `HttpContent` types

| Type | Use |
|---|---|
| `StringContent` | string + `Encoding` + media type |
| `ByteArrayContent` | raw bytes |
| `ReadOnlyMemoryContent` | `ReadOnlyMemory<byte>` zero-copy |
| `StreamContent` | wraps `Stream` (request body from disk/network) |
| `FormUrlEncodedContent` | `application/x-www-form-urlencoded` from `IEnumerable<KeyValuePair<string,string>>` |
| `MultipartContent` / `MultipartFormDataContent` | `multipart/*` containers |
| `JsonContent` | `application/json` body via `JsonContent.Create<T>(value, options)` |

```csharp
using var form = new MultipartFormDataContent();
form.Add(new StringContent("metadata"), "name");
form.Add(new StreamContent(File.OpenRead("a.bin")), "file", "a.bin");
await client.PostAsync("upload", form);
```

## `System.Net.Http.Json` extensions

NuGet `System.Net.Http.Json`. Built on `System.Text.Json` — see `dotnet-serialization`.

```csharp
List<Todo>? todos = await client.GetFromJsonAsync<List<Todo>>("todos?userId=1");

using var post = await client.PostAsJsonAsync("todos", new Todo(1, 0, "x", false));
Todo? created  = await post.Content.ReadFromJsonAsync<Todo>();

await foreach (Comment? c in client.GetFromJsonAsAsyncEnumerable<Comment>("comments")) { /* ... */ }
```

No `PatchAsJsonAsync` shipped — build a `StringContent` manually for PATCH. `GetFromJsonAsync` and friends call `EnsureSuccessStatusCode` internally and throw `HttpRequestException` on non-2xx.

## `IHttpClientFactory`

NuGet `Microsoft.Extensions.Http`. `services.AddHttpClient(...)`. Adds metrics services (`AddMetrics`) automatically since .NET 8.

Benefits: DI-ready `HttpClient`; central place to name/configure clients; outgoing-middleware via `DelegatingHandler` chains; pool of `HttpMessageHandler` instances → no port exhaustion; auto rotation of handlers (default `HandlerLifetime` = 2 min); `ILogger`-based request/response logging.

### Patterns

```csharp
// Basic
builder.Services.AddHttpClient();
HttpClient client = factory.CreateClient();

// Named
builder.Services.AddHttpClient("Todos", c =>
{
    c.BaseAddress = new Uri("https://jsonplaceholder.typicode.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-docs");
});
HttpClient c = factory.CreateClient("Todos");

// Typed
builder.Services.AddHttpClient<TodoService>(c => c.BaseAddress = new Uri("https://..."));
public sealed class TodoService(HttpClient httpClient) { /* ... */ }

// Generated (Refit)
builder.Services.AddRefitClient<ITodoService>()
    .ConfigureHttpClient(c => c.BaseAddress = new("https://..."));
```

`AddHttpClient<TClient>` registers `TClient` as **transient** — don't register `TClient` separately. Don't derive client names from unbounded input — each name keeps a handler pool.

### Handler lifetime

Default 2 min. Override per client:

```csharp
services.AddHttpClient("X").SetHandlerLifetime(TimeSpan.FromMinutes(5));
```

Disposing factory-built `HttpClient` does **not** dispose its handler; the factory tracks them.

### Configuring the primary handler

```csharp
services.AddHttpClient("X")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseDefaultCredentials = true
    });

// Or typed builder over SocketsHttpHandler:
services.AddHttpClient("X")
    .UseSocketsHttpHandler((h, _) => h.PooledConnectionLifetime = TimeSpan.FromMinutes(2))
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);   // disable factory rotation
```

Don't depend on the factory-default primary handler type — pin via `ConfigureHttpClientDefaults` if you need to cast.

### `IHttpClientBuilder` methods

| Method | Effect |
|---|---|
| `AddHttpMessageHandler` | Append a `DelegatingHandler` to the chain. |
| `ConfigureHttpClient` | Configure the `HttpClient` itself. |
| `ConfigurePrimaryHttpMessageHandler` | Set/replace the primary handler. |
| `RedactLoggedHeaders` | Redact specific header values in logs. |
| `SetHandlerLifetime` | Per-client handler lifetime. |
| `UseSocketsHttpHandler` | Typed builder for `SocketsHttpHandler`. |
| `AddStandardResilienceHandler` / `AddStandardHedgingHandler` / `AddResilienceHandler` | See § Resilience. |
| `RemoveAllResilienceHandlers` | Wipe previously registered resilience handlers. |

### Singletons + typed clients

A typed client is **transient**. Capturing one in a singleton freezes its handler → DNS rot. Either inject `IHttpClientFactory` and `CreateClient` per call, or set `SocketsHttpHandler.PooledConnectionLifetime` so even a captured handler rotates connections.

### Message-handler scopes

`IHttpClientFactory` runs each `HttpMessageHandler` in its **own DI scope**, separate from the inbound request scope. Lifetime can outlive several inbound requests. Do not cache `HttpContext`-derived data in handlers.

## `SocketsHttpHandler`

Default `HttpMessageHandler` since .NET Core 2.1. Cross-platform, no `libcurl`. Sealed, `[UnsupportedOSPlatform("browser")]`.

### Properties

| Property | Default | Purpose |
|---|---|---|
| `PooledConnectionLifetime` | infinite | Max connection age; bound for DNS refresh. |
| `PooledConnectionIdleTimeout` | 1 min | Idle timeout before connection drop. |
| `MaxConnectionsPerServer` | `int.MaxValue` | Per-origin TCP cap. |
| `ConnectTimeout` | infinite | TCP connect deadline. |
| `ResponseDrainTimeout` | 2 s | Time to drain response before recycling connection. |
| `MaxResponseDrainSize` | 1 MB | Bytes to drain. |
| `MaxResponseHeadersLength` | 64 KB | Header size cap. |
| `KeepAlivePingDelay` / `KeepAlivePingTimeout` / `KeepAlivePingPolicy` | infinite / 20 s / `WithActiveRequests` | HTTP/2 keep-alive PING. |
| `EnableMultipleHttp2Connections` / `EnableMultipleHttp3Connections` | false / false | Open extra H2/H3 connections when stream limit hit. |
| `InitialHttp2StreamWindowSize` | (default) | H2 receive-window size. |
| `Expect100ContinueTimeout` | 1 s | "Expect: 100-continue" wait. |
| `AutomaticDecompression` | (none) | gzip / deflate / brotli. |
| `AllowAutoRedirect` / `MaxAutomaticRedirections` | true / 50 | Redirects. |
| `UseCookies` / `CookieContainer` | true / new | Cookies. |
| `UseProxy` / `Proxy` / `DefaultProxyCredentials` | true / `DefaultProxy` / null | Proxy. |
| `Credentials` / `PreAuthenticate` | null / false | Auth. |
| `SslOptions` | (default) | `SslClientAuthenticationOptions` (client cert, SNI, callbacks, protocols). |
| `ConnectCallback` | null | Replace TCP connect (UDS, custom dialer). |
| `PlaintextStreamFilter` | null | Wrap the post-TLS stream. |
| `RequestHeaderEncodingSelector` / `ResponseHeaderEncodingSelector` | null | Per-header encoding override. |
| `ActivityHeadersPropagator` | W3C TraceContext | Tracing context propagation; null disables. |
| `MeterFactory` | null | Custom `Meter` for metrics isolation. |
| `Properties` | empty | Bag for handler-side state. |

### Custom `ConnectCallback` (Unix domain socket)

```csharp
var handler = new SocketsHttpHandler
{
    ConnectCallback = async (ctx, ct) =>
    {
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await s.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/api.sock"), ct);
        return new NetworkStream(s, ownsSocket: true);
    }
};
```

## HTTP versions

`HttpVersion`: `Version10`, `Version11`, `Version20`, `Version30`, `Unknown`.

`HttpVersionPolicy`:

| Value | Semantics |
|---|---|
| `RequestVersionOrLower` (default) | Use requested version; on TLS, downgrade to HTTP/1.1 if peer doesn't advertise it (no cleartext H2C). |
| `RequestVersionOrHigher` | Use highest available, but never below requested. Allows cleartext at requested version. |
| `RequestVersionExact` | Exactly the requested version; allows cleartext H2C / H3-only. |

Negotiation: HTTP/1.1 is cleartext or TLS, no negotiation. HTTP/2 is TLS + ALPN `h2`; cleartext H2C only with `RequestVersionExact`. HTTP/3 is QUIC (UDP+TLS 1.3); discovered via `Alt-Svc` header (subsequent requests upgrade) or forced via `RequestVersionExact`. Linux requires `libmsquic`.

```csharp
var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
{
    Version = HttpVersion.Version30,
    VersionPolicy = HttpVersionPolicy.RequestVersionExact
};
using var resp = await client.SendAsync(req);
```

Global switches via `AppContext.SetSwitch` / runtimeconfig / env: `System.Net.SocketsHttpHandler.Http2Support`, `Http3Support` (env `DOTNET_SYSTEM_NET_SOCKETSHTTPHANDLER_HTTP3SUPPORT`), `Http3DraftSupport`.

## Proxy

`HttpClient.DefaultProxy` is a process-wide static. Initialization rules:

- **Windows:** env vars first, else WinINET / WPAD / PAC.
- **macOS:** env vars first, else system proxy settings.
- **Linux:** env vars only; otherwise no proxy.

| Env var | Use |
|---|---|
| `HTTP_PROXY` | proxy for HTTP requests |
| `HTTPS_PROXY` | proxy for HTTPS requests |
| `ALL_PROXY` | both, when above absent |
| `NO_PROXY` | comma-list of hosts to bypass; leading `.` = subdomain match (no `*` wildcards) |

Proxy URL must start with `http://` (not `https://`), may include `user:pass@`, host/port; nothing after the port.

```csharp
var handler = new HttpClientHandler
{
    UseProxy = true,
    Proxy = new WebProxy("http://corp:8080") { BypassProxyOnLocal = true,
                                                 BypassList = ["\\.internal\\."] },
    DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials
};

// Force NO proxy for one client
new SocketsHttpHandler { UseProxy = false };
new HttpClientHandler { Proxy = GlobalProxySelection.GetEmptyWebProxy() };
```

`HttpClientHandler` proxy bypass evaluation: bypassed when host is flat name (no `.`), loopback, local IP, or domain suffix matches `IPGlobalProperties.GetIPGlobalProperties().DomainName`. Wildcards in bypass list become regex (`nt*` → `nt.*`).

## Authentication

```csharp
// Basic / Bearer
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

// NTLM / Negotiate / Kerberos
var handler = new SocketsHttpHandler
{
    Credentials = CredentialCache.DefaultNetworkCredentials,    // current Windows identity
    PreAuthenticate = false
};
var cache = new CredentialCache();
cache.Add(new Uri("https://api.contoso.com/"), "Negotiate",
          new NetworkCredential("user", "pwd", "CONTOSO"));
handler.Credentials = cache;
```

`PreAuthenticate = true` sends `Authorization` on every request after the first 401 negotiation. For programmatic SPNEGO/Kerberos token construction use `System.Net.Security.NegotiateAuthentication`.

```csharp
// Client certificates
var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        ClientCertificates = new X509CertificateCollection { cert },
        LocalCertificateSelectionCallback = (_, _, certs, _, _) => certs[0],
        RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None
    }
};
```
