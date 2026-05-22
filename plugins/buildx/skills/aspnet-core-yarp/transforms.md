# Transforms

Default transforms, built-in request/response/trailer transforms, custom transforms, `ITransformProvider`, body transforms.

Transforms modify the proxy request/response — never the original `HttpContext` request. **Body transforms are not built in** (do them in middleware before YARP and update `Content-Length`).

## Default transforms (every route)

- **Suppress incoming `Host`** (proxy uses destination host). Toggle: `RequestHeaderOriginalHost`.
- **`X-Forwarded-For`** = `HttpContext.Connection.RemoteIpAddress`.
- **`X-Forwarded-Proto`** = scheme.
- **`X-Forwarded-Host`** = original Host (Punycode for IDN).
- **`X-Forwarded-Prefix`** = `HttpContext.Request.PathBase` (URL-encoded).

## Built-in request transforms (highlights)

| Config key | Code helper | Behavior |
|---|---|---|
| `PathPrefix` | `AddPathPrefix(prefix)` | Prepend prefix. |
| `PathRemovePrefix` | `AddPathRemovePrefix(prefix)` | Remove matching prefix on `/` boundary. |
| `PathSet` | `AddPathSet(path)` | Replace path. |
| `PathPattern` | `AddPathRouteValues(pattern)` | Substitute `{token}` from route values; `{**remainder}` catch-all. |
| `QueryValueParameter` + `Set`/`Append` | `AddQueryValue` | Static query value. |
| `QueryRouteParameter` | `AddQueryRouteValue` | Query value from route value. |
| `QueryRemoveParameter` | `AddQueryRemoveKey` | Remove. |
| `HttpMethodChange` + `Set` | `AddHttpMethodChange(from,to)` | E.g. PUT->POST. |
| `RequestHeadersCopy` (default `true`) | `WithTransformCopyRequestHeaders` | Toggle copying all incoming headers. |
| `RequestHeaderOriginalHost` | `AddOriginalHost(bool)` | Forward original Host. |
| `RequestHeader` + `Set`/`Append` | `AddRequestHeader` | Set / append header. |
| `RequestHeaderRouteValue` | `AddRequestHeaderRouteValue` | Set header from route value. |
| `RequestHeaderRemove` | `AddRequestHeaderRemove` | Remove. |
| `RequestHeadersAllowed` | `AddRequestHeadersAllowed` | Allow-list (disables `RequestHeadersCopy`). |
| `X-Forwarded` (`For`/`Proto`/`Host`/`Prefix`/`HeaderPrefix`) | `AddXForwarded`, `AddXForwardedFor/Host/Proto/Prefix(name, action)` | Per-header `Set` / `Append` / `Remove` / `Off`. Disable all: `{ "X-Forwarded": "Off" }` or `UseDefaultForwarders=false`. |
| `Forwarded` | `AddForwarded(useHost,useProto,forFormat,byFormat,action)` | RFC 7239. **Enabling disables the default `X-Forwarded` set.** Formats: `Random`/`RandomAndPort`/`Ip`/`IpAndPort`/etc. |
| `ClientCert` | `AddClientCertHeader(name)` | Base64 of `HttpContext.Connection.ClientCertificate` into header. Only fires if cert already present. |

`ForwardedTransformActions`: `Set` / `Append` / `Remove` / `Off`. `Set` clears any existing value when value unavailable (anti-spoof).

## Built-in response & response-trailer transforms

`ResponseHeader{,Remove}` + `When` (default `Success` = status<400; `Failure` / `Always`), `ResponseHeadersCopy` (default `true`), `ResponseHeadersAllowed`, and the symmetrical `ResponseTrailer*` keys (no request-trailer support — `HttpClient` limitation). `ResponseCondition` enum: `Success` / `Failure` / `Always`.

## Adding transforms in code

```csharp
services.AddReverseProxy()
    .LoadFromConfig(...)
    .AddTransforms(builderContext =>
    {
        builderContext.AddPathPrefix("/prefix");
        if (!string.IsNullOrEmpty(builderContext.Route.AuthorizationPolicy))
            builderContext.AddRequestTransform(async tc =>
                tc.ProxyRequest.Headers.Add("CustomHeader","CustomValue"));
    });
```

## Custom transforms

- Derive `RequestTransform` / `ResponseTransform` / `ResponseTrailersTransform`. A request transform short-circuits by setting `HttpResponse.StatusCode != 200`, calling `HttpResponse.StartAsync()`, or writing to body — remaining transforms skipped, request not proxied.
- `ITransformProvider` for DI-aware + validation:

```csharp
public interface ITransformProvider
{
    void ValidateRoute(TransformRouteValidationContext context);
    void ValidateCluster(TransformClusterValidationContext context);
    void Apply(TransformBuilderContext context);
}
services.AddReverseProxy().AddTransforms<MyTransformProvider>();
```

- `ITransformFactory` for custom JSON keys (return `true` when factory owns the key set; unknown keys cause config rejection).
- Body transforms: replace `HttpContext.Request.Body` with `MemoryStream` and update `Content-Length`. To **add** a body where none exists, do it in middleware before YARP and replace `IHttpRequestBodyDetectionFeature` with one returning `CanHaveBody = true`.
