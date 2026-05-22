# Bootstrap, Routes, Clusters, Destinations

`AddReverseProxy`, `LoadFromConfig`, `MapReverseProxy`, route matching, cluster + destination config, per-cluster `HttpClient` + `HttpRequest`.

## Bootstrap

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();
app.MapReverseProxy();
app.Run();
```

`appsettings.json` (minimum):

```json
{
  "ReverseProxy": {
    "Routes": {
      "route1": {
        "ClusterId": "cluster1",
        "Match": { "Path": "{**catch-all}" }
      }
    },
    "Clusters": {
      "cluster1": {
        "Destinations": {
          "destination1": { "Address": "https://example.com/" }
        }
      }
    }
  }
}
```

## DI surface

| Method | Purpose |
|---|---|
| `AddReverseProxy()` -> `IReverseProxyBuilder` | Register YARP services. |
| `.LoadFromConfig(IConfiguration)` | Load from `IConfiguration`. May be called multiple times to compose sources (since 1.1). |
| `.LoadFromMemory(routes, clusters)` | Code-defined; uses `InMemoryConfigProvider`. |
| `.AddConfigFilter<T>()` | Register `IProxyConfigFilter` (additive, in registration order). |
| `.AddTransforms(Action<TransformBuilderContext>)` | Add per-route transforms via callback. |
| `.AddTransforms<T>()` | DI-aware `ITransformProvider` (validation supported). |
| `.AddTransformFactory<T>()` | `ITransformFactory` for custom JSON keys. |
| `.ConfigureHttpClient((ctx, SocketsHttpHandler) => ...)` | Customize the per-cluster handler. |
| `.AddDnsDestinationResolver(o => ...)` | Built-in DNS-based `IDestinationResolver`. |
| `IServiceCollection.AddHttpForwarder()` | For direct-forwarding scenarios. |
| `IEndpointRouteBuilder.MapReverseProxy()` | Map proxy endpoints with **default pipeline** (session affinity + load balancing + passive health checks + final forward). |
| `IEndpointRouteBuilder.MapReverseProxy(Action<...>)` | Map with **custom pipeline** — you choose the steps. |
| `IEndpointRouteBuilder.MapForwarder(pattern, prefix, ...)` | Map a single direct-forwarder endpoint. |
| Pipeline builders | `UseSessionAffinity()`, `UseLoadBalancing()`, `UsePassiveHealthChecks()`, `UseHttpSysDelegation()`. |

## Routes

```jsonc
"Routes": {
  "allrouteprops": {
    "ClusterId": "allclusterprops",
    "Order": 100,                        // lower wins
    "MaxRequestBodySize": 1000000,       // bytes; -1 disables
    "AuthorizationPolicy": "Anonymous",  // policy name | "Default" | "Anonymous"
    "CorsPolicy": "Default",             // name | "Default" | "Disable"
    "RateLimiterPolicy": "customPolicy", // name | "disable"
    "TimeoutPolicy": "customPolicy",     // name | "disable"
    "Timeout": "00:01:00",               // mutually exclusive with TimeoutPolicy
    "OutputCachePolicy": "customPolicy",
    "Match": {
      "Path": "/something/{**remainder}",
      "Hosts": ["www.aaaaa.com","www.bbbbb.com"],
      "Methods": ["GET","PUT"],
      "Headers": [
        { "Name":"H","Values":["v1","v2"],"Mode":"ExactHeader","IsCaseSensitive":true }
      ],
      "QueryParameters": [
        { "Name":"q","Values":["v1"],"Mode":"Exact","IsCaseSensitive":true }
      ]
    },
    "Metadata": { "MyName": "MyValue" },
    "Transforms": [ { "RequestHeader":"MyHeader","Set":"MyValue" } ]
  }
}
```

### Header match modes (`HeaderMatchMode`)

| Mode | Behavior |
|---|---|
| `ExactHeader` (default) | Full match against any value (split on `,`/`;`; one pair of quotes stripped). |
| `HeaderPrefix` | Prefix match. |
| `Exists` / `NotExists` | Header present-and-non-empty / absent. `Values` not required. |
| `Contains` / `NotContains` | Substring match across values. |

Header `Name` is always case-insensitive (HTTP RFCs); `IsCaseSensitive` defaults `false` for values. Multiple `Headers[]` entries are AND — use multiple routes for OR.

### Query-parameter match modes (`QueryParameterMatchMode`)

| Mode | Behavior |
|---|---|
| `Exact` (default) | Full match. **Multiple values for the same name -> no match** (single-value only). |
| `Prefix` | Prefix match. |
| `Exists` | Param present with non-empty value. Multi-value supported. |
| `Contains` / `NotContains` | Substring. |

## Clusters & destinations

```jsonc
"Clusters": {
  "allclusterprops": {
    "Destinations": {
      "first":  { "Address":"https://contoso.com" },
      "second": { "Address":"https://10.20.30.40", "Health":"https://10.20.30.40:12345/test" }
    },
    "LoadBalancingPolicy": "PowerOfTwoChoices",
    "SessionAffinity": { /* see health-and-affinity.md */ },
    "HealthCheck":     { /* see health-and-affinity.md */ },
    "HttpClient":      { /* see below */ },
    "HttpRequest":     { /* see below */ },
    "Metadata": {
      "ConsecutiveFailuresHealthPolicy.Threshold":"3",
      "TransportFailureRateHealthPolicy.RateLimit":"0.5"
    }
  }
}
```

`Destination.Address` may be `http(s)://`. `Destination.Health` (optional) overrides `Address` for active health probes. `Destination.Host` overrides the default `Host` header — used by `IDestinationResolver` to expand hostnames to IPs without breaking SNI/host-routing.

### Per-cluster `HttpClient`

Each cluster has a dedicated `HttpMessageInvoker` (default factory `ForwarderHttpClientFactory` creates a new invoker only when `HttpClientConfig` changes).

| Key | Default | Notes |
|---|---|---|
| `SslProtocols` | `None` (system default) | `Tls12`/`Tls13` recommended. |
| `MaxConnectionsPerServer` | `int.MaxValue` | HTTP/1.1 max concurrent. |
| `DangerousAcceptAnyServerCertificate` | `false` | Disables cert validation entirely. |
| `RequestHeaderEncoding` / `ResponseHeaderEncoding` | ASCII | e.g. `"utf-8"`, `"iso-8859-1"`. Kestrel must be configured to accept matching encoding. |
| `EnableMultipleHttp2Connections` | `true` | Add HTTP/2 connections when streams exhausted. |
| `WebProxy` | none (`UseProxy=false`) | Set to use a forward proxy upstream. |

For lightweight tweaks use `.ConfigureHttpClient((ctx, handler) => { ... })` instead of replacing the factory:

```csharp
services.AddReverseProxy()
    .LoadFromConfig(...)
    .ConfigureHttpClient((ctx, h) =>
    {
        h.SslOptions.RemoteCertificateValidationCallback = MyValidator;
    });
```

### Per-cluster `HttpRequest` (`ForwarderRequestConfig`)

| Key | Default | Notes |
|---|---|---|
| `ActivityTimeout` | `00:01:40` (100 s) | Idle timeout — resets on any read/write of body, response headers, or WebSocket pings. **TCP keepalives and HTTP/2 PINGs do NOT reset it.** Always applies (even with debugger attached). |
| `Version` | `"2"` | `"1.0"` / `"1.1"` / `"2"` / `"3"`. |
| `VersionPolicy` | `RequestVersionOrLower` | / `RequestVersionOrHigher` / `RequestVersionExact`. |
| `AllowResponseBuffering` | `false` | **Buffering breaks SSE.** |

### Custom `IForwarderHttpClientFactory`

Recommended `SocketsHttpHandler` baseline (matches default factory):

```csharp
new SocketsHttpHandler {
    UseProxy = false,
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false,
    EnableMultipleHttp2Connections = true,
    ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
    ConnectTimeout = TimeSpan.FromSeconds(15),
};
```

### Destination resolvers (`IDestinationResolver`)

```csharp
services.AddReverseProxy()
    .AddDnsDestinationResolver(o => o.AddressFamily = AddressFamily.InterNetwork);
```

`DnsDestinationResolverOptions`: `RefreshPeriod` (5 min default), `AddressFamily` (`InterNetwork` / `InterNetworkV6` / `null` for both). Custom resolvers register as singleton; same lifecycle as `IProxyConfigProvider` (throw / block / return-empty + reload via change token; 5-minute polling fallback). Set `DestinationConfig.Host` to keep SNI/host routing working when expanding hostnames to IPs.
