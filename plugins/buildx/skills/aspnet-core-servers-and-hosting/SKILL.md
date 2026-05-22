---
name: aspnet-core-servers-and-hosting
description: ASP.NET Core 10 server-and-hosting reference. Covers Kestrel / HTTP.sys / IIS (in-proc + out-of-proc), Kestrel endpoint config (TCP, UDS, named pipes, SNI, TLS, HTTP/1.1+2+3), limits, HTTP/2 trailers, HTTP/3 over QUIC, request draining, IIS hosting (ANCM, `web.config`, `IISServerOptions`), Linux deployment via Nginx/systemd/Apache, `UseForwardedHeaders`, Docker (`mcr.microsoft.com/dotnet/...`, multi-stage, SDK `/t:PublishContainer`, AOT, chiseled), Azure App Service + slot-aware Data Protection, Container Apps + K8s probes, `Microsoft.AspNetCore.Http.Timeouts`, graceful shutdown + `HostOptions.ShutdownTimeout`, `.pubxml` publish profiles.
when_to_use: |
  - Trigger keywords: Kestrel, HTTP.sys, IIS, ANCM, AspNetCoreHostingModel, web.config, ConfigureKestrel, ListenUnixSocket, UseHttps, SNI, HTTP/3, QUIC, KestrelServerLimits, MaxRequestBodySize, KeepAliveTimeout, MinDataRate, UseForwardedHeaders, ForwardedHeadersOptions, Nginx, systemd, Docker, jammy-chiseled, PublishContainer, Azure App Service, deployment slots, Data Protection key ring, AddRequestTimeouts, ShutdownTimeout, app_offline.htm, .pubxml.
  - Task shapes: pick server (Kestrel / HTTP.sys / IIS); configure listen endpoints + TLS + SNI; tune Kestrel limits; enable HTTP/3; place an app behind Nginx/Apache/IIS; write a Dockerfile or container-publish setup; pick a base image (chiseled, `aspnet` vs `runtime-deps`); deploy to Azure App Service with slot-independent Data Protection; configure forwarded headers; install request timeouts; debug a 502.5/502.3 ANCM error.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Program.cs", "**/web.config", "**/appsettings*.json", "**/Dockerfile", "**/*.pubxml", "**/*.csproj"]
---

# ASP.NET Core Servers, Host & Deploy — Reference

Reference for ASP.NET Core 10 server selection, listener configuration, deployment hosts, and shutdown semantics.

## Mental model

- `IServer` is the boundary between the network and the request pipeline. `WebApplication` wires whichever server you registered.
- Kestrel is the default and only cross-platform option. HTTP.sys is Windows kernel-mode (incompatible with IIS). IIS is a reverse-proxy or in-process host.
- Behind any reverse proxy, the app must run `UseForwardedHeaders` (or set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`) — otherwise scheme/IP/host detection is wrong.
- Containers, App Service, App Service for Containers, Container Apps, Kubernetes — all use Kestrel inside.
- Graceful shutdown depends on cooperation between the OS signal, `HostOptions.ShutdownTimeout`, and per-server drain logic.

## Non-negotiable rules

1. **Choose Kestrel by default.** HTTP.sys only when you need port sharing, kernel-mode Kerberos/NTLM, kernel response cache, or queue-transfer proxying. IIS in-process when you must integrate with an IIS estate.
2. **Behind any proxy, run `UseForwardedHeaders`** with explicit `ForwardedHeaders` (default `None` does nothing) — first in the pipeline, before HSTS / HTTPS redirection / auth.
3. **TLS protocol matrix:** HTTP/2 needs ALPN + TLS 1.2+. HTTP/3 needs TLS 1.3 + QUIC + `UseHttps`. Pair HTTP/3 with HTTP/2 fallback (`Http1AndHttp2AndHttp3`).
4. **Containers:** Use `mcr.microsoft.com/dotnet/aspnet:10.0` (or `-jammy-chiseled` for prod) — never the SDK image at runtime. Default port `8080`. Default user `app` (UID 1654).
5. **IIS in-process:** one app per pool, app architecture must match pool architecture. Single-file executables are NOT supported. `requestTimeout` web.config attribute does NOT apply.
6. **Azure App Service deployment slots:** Data Protection key ring is per-slot — swap signs everyone out. Use Azure Blob / Key Vault / SQL / Redis key ring.
7. **`UseForwardedHeaders` defaults assume one hop, loopback only** — for multi-hop or non-loopback proxies, set `ForwardLimit`, `KnownProxies`, and `KnownNetworks` explicitly.
8. **Linux env vars are case-sensitive.** `ASPNETCORE_ENVIRONMENT=Production` loads `appsettings.Production.json`, not `production`. Use `__` for `:` in nested keys.

## Server matrix

| Server | OS | Default? | Edge-facing | Notes |
|---|---|---|---|---|
| **Kestrel** | Win/Linux/macOS | Yes | Yes | Cross-platform, in-process. Best perf+memory. Pluggable transports. HTTP/1.1+2+3. |
| **IIS HTTP Server** (`IISHttpServer`) | Windows | Yes when behind IIS in-proc | Behind IIS only | In-proc inside `w3wp.exe` / `iisexpress.exe`. Activated by `UseIIS`. |
| **HTTP.sys** | Windows | Opt-in | Yes (or behind a proxy) | Kernel-mode driver. Port sharing, kernel auth, kernel response cache, direct file send. **Incompatible with IIS / IIS Express / ANCM**. |

HTTP/2 always requires ALPN + TLS 1.2+. `HttpRequest.Protocol == "HTTP/2"` confirms negotiation.

## Kestrel

`WebApplication.CreateBuilder(args)` calls `UseKestrel` internally.

### Endpoint configuration

```csharp
builder.WebHost.ConfigureKestrel((ctx, opts) =>
{
    opts.Listen(IPAddress.Loopback, 5000);
    opts.Listen(IPAddress.Loopback, 5001, lo => lo.UseHttps("testCert.pfx", "testPassword"));
    opts.ListenAnyIP(5005, lo =>
    {
        lo.UseHttps("testCert.pfx", "testPassword");
        lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;     // HTTPS mandatory for HTTP/3
    });
    opts.ListenLocalhost(5000);                                 // v4+v6
    opts.ListenUnixSocket("/tmp/kestrel-test.sock");            // Linux (faster than loopback TCP)
    opts.ListenNamedPipe("defaultPipe");                        // Windows
});
```

`HttpProtocols`: `Http1` | `Http2` | `Http3` | `Http1AndHttp2` (default) | `Http1AndHttp2AndHttp3`.

URL formats: `http://65.55.39.10:80/`, `http://[::1]:5000/`, `http://*:80/`, `http://+:80/`. `0.0.0.0` ≙ all v4. `[::]` ≙ all v6. Multiple URLs separated by `;`. Convenience env vars: `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`, `ASPNETCORE_URLS`.

Default endpoints when nothing configured: `http://localhost:5000`. Dev templates randomize HTTP 5000–5300, HTTPS 7000–7300 in `Properties/launchSettings.json`. Bind to port `0` for dynamic assignment, then read at runtime via `IServerAddressesFeature` (incompatible with `ListenLocalhost` and HTTP/3 mixing).

### `appsettings.json` Kestrel schema + TLS/SNI

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http":  { "Url": "http://localhost:5000" },
      "Https": { "Url": "https://localhost:5001" },
      "HttpsInlineCertFile": {
        "Url": "https://localhost:5001",
        "Certificate": { "Path": "cert.pfx", "Password": "$CREDENTIAL_PLACEHOLDER$" }
      },
      "MySniEndpoint": {
        "Url": "https://*", "SslProtocols": ["Tls12"],
        "Sni": {
          "a.example.org": { "Protocols": "Http1AndHttp2", "Certificate": { "Subject": "...", "Store": "My" } },
          "*.example.org": { "Certificate": { "Path": "wild.pfx", "Password": "..." } }
        }
      }
    },
    "EndpointDefaults": { "Protocols": "Http1AndHttp2" },
    "Certificates":     { "Default": { "Path": "default.pfx", "Password": "..." } }
  }
}
```

Reload-on-change is on by default. Modified endpoints get **5 s** to drain; new ones start immediately. `UseHttps` overloads accept `(filename, password)`, `(HttpsConnectionAdapterOptions)`, `(ServerOptionsSelectionCallback, state, handshakeTimeout)`, `(TlsHandshakeCallbackOptions)`. Apply `ConfigureHttpsDefaults` before any `Listen`. SNI via `ServerCertificateSelector` (dictionary by host), `ServerOptionsSelectionCallback` (full `SslServerAuthenticationOptions` per host), or `TlsHandshakeCallbackOptions` (supports `AllowDelayedClientCertificateNegotation`). Linux cipher policy via `OnAuthenticate`.

### Limits

| Property | Default |
|---|---|
| `KeepAliveTimeout` | 2 min |
| `RequestHeadersTimeout` | 30 s |
| `MaxConcurrentConnections` / `MaxConcurrentUpgradedConnections` | unlimited (separate counters) |
| `MaxRequestBodySize` | 30,000,000 (~28.6 MB). Disabled when running ANCM OOP. |
| `MinRequestBodyDataRate` / `MinResponseDataRate` | `MinDataRate(240, 5s grace)` |
| `Http2.MaxStreamsPerConnection` | 100 |
| `Http2.HeaderTableSize` | 4096 (HPACK octets) |
| `Http2.MaxFrameSize` | 16,384 (range 2^14..2^24-1) |
| `Http2.MaxRequestHeaderFieldSize` | 8192 |
| `Http2.InitialConnectionWindowSize` / `InitialStreamWindowSize` | 131,072 / 98,304 (≥ 65,535) |
| `Http2.KeepAlivePingDelay` / `KeepAlivePingTimeout` | `TimeSpan.MaxValue` (off) / 20 s |
| `AllowSynchronousIO` | `false` (sync IO risks thread-pool starvation) |

```csharp
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.Limits.MaxConcurrentConnections = 100;
    opts.Limits.MaxRequestBodySize = 100_000_000;
    opts.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    opts.Limits.Http2.MaxStreamsPerConnection = 100;
});
```

Per-request override via features (HTTP/2 caveat: `IHttpMinResponseDataRateFeature` is NOT present on HTTP/2; `IHttpMinRequestBodyDataRateFeature` is writable only to `null`):

```csharp
app.Use(async (context, next) =>
{
    var maxFeat = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (maxFeat is not null && !maxFeat.IsReadOnly) maxFeat.MaxRequestBodySize = 10 * 1024;
    await next(context);
});
```

MVC body size attribute: `[RequestSizeLimit(100_000_000)]`.

**Disabled when debugger attached**: `KeepAliveTimeout`, `RequestHeadersTimeout`, `MinRequestBodyDataRate`, `MinResponseDataRate`, the matching features, plus the request-timeouts middleware.

### HTTP/2 trailers + reset

```csharp
if (httpContext.Response.SupportsTrailers())
{
    httpContext.Response.DeclareTrailer("trailername");          // before headers
    await httpContext.Response.WriteAsync("Hello world");
    httpContext.Response.AppendTrailer("trailername", "TrailerValue");
}

httpContext.Features.Get<IHttpResetFeature>()!.Reset(errorCode: 2); // INTERNAL_ERROR
```

Trailers are HTTP/2-only on IIS+HTTP.sys; Kestrel supports both directions.

### HTTP/3 (QUIC)

Built on **MsQuic**. Not enabled by default. Always pair with HTTP/1.1+2 (QUIC traversal unreliable across some networks). Kestrel auto-emits `alt-svc` for upgrade. Requirements: Windows 11 22000+ / Server 2022+ + TLS 1.3; Linux `libmsquic` from `packages.microsoft.com`; macOS not supported. If reqs missing, Kestrel **silently falls back**.

```csharp
builder.WebHost.ConfigureKestrel((ctx, options) =>
    options.ListenAnyIP(5001, lo => { lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3; lo.UseHttps(); }));
```

`HttpsConnectionAdapterOptions` ignored/throws on HTTP/3: `HandshakeTimeout` (ignored), `OnAuthenticate` (ignored), `UseHttps(ServerOptionsSelectionCallback...)` and `UseHttps(TlsHandshakeCallbackOptions)` (throws). Browsers reject self-signed certs on HTTP/3.

### Request draining + abort

HTTP/1.1 keep-alive demands the body be fully consumed for connection reuse. Kestrel drains discarded bodies for **5 s** (not configurable); on timeout the connection closes. HTTP/2 sends RST_STREAM instead — no 5 s timeout, individual stream aborted, connection retained.

Forced abort (severe errors only — connection setup is expensive):

```csharp
await httpContext.Response.CompleteAsync();
httpContext.Abort();
```

Prefer `Expect: 100-continue` so clients avoid wasted bodies.

## HTTP.sys

```csharp
builder.WebHost.UseHttpSys(options =>
{
    options.AllowSynchronousIO        = false;
    options.Authentication.Schemes    = AuthenticationSchemes.Negotiate;   // Basic|Kerberos|NTLM|Negotiate|None
    options.Authentication.AllowAnonymous = true;
    options.MaxRequestBodySize        = 30_000_000;
    options.UrlPrefixes.Add("https://10.0.0.4:443");
});
```

Key options: `EnableResponseCaching` (kernel-mode cache, default `true`), `MaxAccepts` (`5 × Environment.ProcessorCount`), `RequestQueueLimit` (1000), `EnableKernelResponseBuffering` (default `false`; only with sync IO or async with ≤ 1 outstanding write), `RequestQueueSecurityDescriptor` (custom DACL). URL prefix wildcards: `*` weak/fallback, `+` strong/precedence — `*:80` / `+:80` are security-risky; prefer explicit hostnames. Kerberos requires machine account decrypt + SPN registered for the host (not worker user); user-mode auth not available alongside.

Server prep on Windows: `netsh http add urlacl url=https://10.0.0.4:443/ user=Users` + `netsh http add sslcert ipport=10.0.0.4:443 certhash=<thumb> appid="{GUID}"`. Per-request timestamps (Win 10 2004+ / Server 2022+) via `IHttpSysRequestTimingFeature.Timestamps`.

## IIS hosting

ANCM (ASP.NET Core Module) is a native IIS module that bridges IIS ↔ in-proc `IISHttpServer` or out-of-proc Kestrel. App pool isolation: required for in-proc (one app per pool), recommended for OOP. App pool architecture (x64/x86) MUST match published bitness for in-proc.

### In-process (default since 3.0)

Flow: HTTP.sys → IIS → ANCM → `IISHttpServer` (managed) → middleware. No loopback hop.

```xml
<PropertyGroup>
  <AspNetCoreHostingModel>InProcess</AspNetCoreHostingModel>
</PropertyGroup>
```

```csharp
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AutomaticAuthentication      = false;     // default true
    options.AllowSynchronousIO           = false;
    options.MaxRequestBodySize           = 30_000_000;
});
```

`IISServerOptions` (in-proc): `AutomaticAuthentication` (default `true`; if `false`, server only sets identity when scheme challenges), `AllowSynchronousIO` (`false`), `MaxRequestBodySize` (30,000,000 — but IIS `maxAllowedContentLength` runs first, so bump web.config too).

Constraints vs OOP: single-file executables NOT hostable in-proc; `requestTimeout` web.config attribute does NOT apply; one pool per app; `AuthenticateAsync` not called automatically; `IClaimsTransformation` won't run unless you `AddAuthentication(IISServerDefaults.AuthenticationScheme)`. Web Package (single-file MSDeploy) deployments not supported.

### Out-of-process

ANCM forwards to Kestrel on a random local port over plain HTTP (HTTPS not forwarded). `UseIISIntegration` activates the integration and configures Forwarded Headers Middleware automatically.

```xml
<PropertyGroup>
  <AspNetCoreHostingModel>OutOfProcess</AspNetCoreHostingModel>
</PropertyGroup>
```

`IISOptions` (OOP): `AutomaticAuthentication` (default `true`), `AuthenticationDisplayName`, `ForwardClientCertificate` (default `true`; populates `HttpContext.Connection.ClientCertificate` from `MS-ASPNETCORE-CLIENTCERT`).

Process name distinguishes models: `w3wp` / `iisexpress` (in-proc) vs `dotnet` (OOP).

### `web.config` and ANCM

Created/transformed at publish by `_TransformWebConfig` (Web SDK). Disable with `<IsTransformWebConfigDisabled>true</IsTransformWebConfigDisabled>`.

```xml
<configuration><system.webServer>
  <handlers><add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /></handlers>
  <aspNetCore processPath="dotnet" arguments=".\MyApp.dll"
              stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
    <environmentVariables>
      <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    </environmentVariables>
  </aspNetCore>
</system.webServer></configuration>
```

`<aspNetCore>` attributes: `processPath`, `arguments`, `hostingModel` (`inprocess`/`outofprocess`), `stdoutLogEnabled`, `stdoutLogFile`, `requestTimeout` (OOP only — default 2 min, max 24 days), `startupTimeLimit`, `shutdownTimeLimit`, `forwardWindowsAuthToken`. Drop `app_offline.htm` at site root for graceful deploy. ASP.NET Core ignores legacy `<system.web>`, `<appSettings>`, `<connectionStrings>`, `<location>` sections; `<system.webServer>` features (`<urlCompression>`, `<security><requestFiltering>`) still apply.

## Linux reverse proxies

Nginx (key directives — terminate TLS at proxy, forward to Kestrel on `127.0.0.1:5000`):

```nginx
map $http_connection $connection_upgrade { "~*Upgrade" $http_connection; default keep-alive; }
server {
    listen 443 ssl http2; server_name example.com;
    ssl_certificate /etc/ssl/certs/testCert.crt;
    ssl_certificate_key /etc/ssl/certs/testCert.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    location / {
        proxy_pass http://127.0.0.1:5000/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Unix-socket upstream is faster than loopback TCP: `proxy_pass http://unix:/tmp/kestrel-test.sock:/;` plus `chmod go+w /tmp/kestrel-test.sock`. Long-header workloads (Microsoft Entra) need both proxy and Kestrel header limits raised (`Limits.MaxRequestHeaderTotalSize`, `Http2.MaxRequestHeaderFieldSize`).

systemd unit (managed Kestrel daemon):

```ini
[Service]
WorkingDirectory=/var/www/helloapp
ExecStart=/usr/bin/dotnet /var/www/helloapp/helloapp.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ConnectionStrings__DefaultConnection={Connection String}
TimeoutStopSec=90
[Install]
WantedBy=multi-user.target
```

Apache (`mod_proxy` + `mod_proxy_http`): `ProxyPass / http://127.0.0.1:5000/` + `ProxyPassReverse`; `X-Forwarded-For` is added automatically.

## Forwarded Headers (`UseForwardedHeaders`)

Reads inbound proxy headers and rewrites `HttpContext` so HTTPS detection, redirect generation, and IP logic see the original client. Maps `X-Forwarded-For` → `Connection.RemoteIpAddress` (preserves `X-Original-For`); `X-Forwarded-Proto` → `Request.Scheme` (`X-Original-Proto`); `X-Forwarded-Host` → `Request.Host` (`X-Original-Host`); `X-Forwarded-Prefix` → `Request.PathBase` (`X-Original-Prefix`).

**Default `ForwardedHeaders = None`** — middleware does NOTHING until you set it. Defaults assume one hop and only loopback as known proxies.

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
    options.KnownProxies.Add(IPAddress.Parse("127.0.10.1"));
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
});
app.UseForwardedHeaders();   // FIRST — before HSTS, HTTPS redirect, auth
```

Magic env var `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` installs the middleware with sensible defaults at the very start of the pipeline (Azure App Service / IIS Integration set this for you when OOP). When the proxy enforces HTTPS but doesn't add `X-Forwarded-Proto`, set scheme manually before the middleware: `app.Use((ctx, next) => { ctx.Request.Scheme = "https"; return next(ctx); });`.

## Docker

Images at `mcr.microsoft.com/dotnet/...`: `sdk:10.0` (build stage only), `aspnet:10.0` (runtime, default for web), `runtime:10.0` (no ASP.NET; console/services), `runtime-deps:10.0` (OS deps only; for self-contained / AOT), `aspnet:10.0-jammy-chiseled` and `-noble-chiseled` (distroless-style, non-root, no shell — for prod), `aspnet:10.0-jammy-chiseled-extra` (chiseled + ICU/tzdata).

Tag suffixes: `-jammy` (Ubuntu 22.04), `-noble` (Ubuntu 24.04), `-bookworm-slim` (Debian 12), `-alpine`, `-windowsservercore-ltsc2022`, `-nanoserver-ltsc2022`; append `-chiseled` for chiseled Ubuntu. Default port in .NET 8+ images: **8080** (HTTP) / **8081** (HTTPS dev cert). Default user `app` (UID 1654), non-root.

Multi-stage Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY *.sln .
COPY aspnetapp/*.csproj ./aspnetapp/
RUN dotnet restore
COPY aspnetapp/. ./aspnetapp/
WORKDIR /source/aspnetapp
RUN dotnet publish -c release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "aspnetapp.dll"]
```

SDK-driven container build (no Dockerfile): `dotnet publish --os linux --arch x64 /t:PublishContainer -c Release`. Project knobs: `<ContainerRepository>`, `<ContainerImageTag>`, `<ContainerBaseImage>`, `<ContainerFamily>`, `<ContainerUser>`, `<ContainerPort Include="8080" Type="tcp" />`.

AOT containers — pair Native AOT publish with `runtime-deps` chiseled. Typically need `<PublishAot>true</PublishAot>` + `<InvariantGlobalization>true</InvariantGlobalization>` plus `clang` and `zlib1g-dev` in build stage.

`HEALTHCHECK CMD curl --fail http://localhost:8080/healthz || exit 1` — chiseled images may lack `curl`; use `wget` / static probe / app-internal startup probe.

inotify limit pitfall (`The configured user limit (128) on the number of inotify instances has been reached.`): set `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` or raise `fs.inotify.max_user_instances`.

## Azure App Service + Container Apps + Kubernetes

App Service:
- App Settings → process env vars; `__` for `:` in keys.
- `ASPNETCORE_ENVIRONMENT=Production` (or `Staging`); `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (typically pre-set); `WEBSITE_RUN_FROM_PACKAGE=1` for zip deploy.
- Deployment slots — Data Protection key ring is **per-slot** (`%HOME%\ASP.NET\DataProtection-Keys`). After swap, cookies/CSRF tokens encrypted in the previous slot can't decrypt — users get signed out. Fix: use Azure Blob / Key Vault / SQL Server / Redis key ring.
- Publish modes: framework-dependent (default, smaller) vs self-contained (`-r linux-x64 --self-contained`, ships runtime, needed for preview runtimes when no site extension is installed).
- 64-bit deploy: build with x64 SDK, set Platform = 64 Bit in App Service Configuration → General settings (Basic+ plan).

Container Apps / Kubernetes — same as any container. Target port `8080`. Forwarded headers must still be enabled (ACA terminates TLS at ingress, forwards over HTTP). Kubernetes probes:

```yaml
readinessProbe: { httpGet: { path: /healthz/ready, port: 8080 }, initialDelaySeconds: 30, timeoutSeconds: 1 }
livenessProbe:  { httpGet: { path: /healthz/live,  port: 8080 } }
```

## Health checks (deployment perspective)

`MapHealthChecks` preferred over `UseHealthChecks` (full routing integration). Detailed wiring (custom `IHealthCheck`, JSON writer, DB checks, publisher) → `aspnet-core-fundamentals` § health-checks. Readiness/liveness pattern for K8s — readiness check ties to a `BackgroundService` that flips a flag once startup work completes:

```csharp
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = hc => hc.Tags.Contains("ready") });
app.MapHealthChecks("/healthz/live",  new HealthCheckOptions { Predicate = _ => false });
```

`HealthStatus` → HTTP code defaults: `Healthy` 200, `Degraded` 200, `Unhealthy` 503; override via `HealthCheckOptions.ResultStatusCodes`.

## Request timeouts middleware

`Microsoft.AspNetCore.Http.Timeouts`. Per-endpoint or global. When triggered, `HttpContext.RequestAborted.IsCancellationRequested == true`; `Abort()` is NOT called — the handler can still produce a response. Default if unhandled = HTTP 504. Disabled under debugger.

```csharp
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromMilliseconds(1500) };
    options.AddPolicy("MyPolicy", TimeSpan.FromSeconds(2));
    options.AddPolicy("MyPolicy2", new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromMilliseconds(1000), TimeoutStatusCode = 503,
        WriteTimeoutResponse = async ctx => { ctx.Response.ContentType = "text/plain"; await ctx.Response.WriteAsync("Timeout!"); }
    });
});
app.UseRequestTimeouts();
app.MapGet("/x", () => "ok").WithRequestTimeout(TimeSpan.FromSeconds(2));
app.MapGet("/named", () => "ok").WithRequestTimeout("MyPolicy");
app.MapGet("/off",  [DisableRequestTimeout] () => "ok");
// Cancel inside the handler:
ctx.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();
```

For controllers / Razor Pages: `[RequestTimeout]` on action / class / page class.

## Graceful shutdown

- Console / generic host shuts down on SIGINT / SIGTERM. systemd unit needs `KillSignal=SIGINT` to opt in.
- Default host shutdown timeout = **30 s** (`HostOptions.ShutdownTimeout`):

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));
```

- App-level lifecycle: inject `IHostApplicationLifetime` → `ApplicationStarted`, `ApplicationStopping` (drain time), `ApplicationStopped`.
- ANCM out-of-proc: `requestTimeout` (web.config), `shutdownTimeLimit` (default 10 s).
- Kestrel keeps accepting until shutdown begins; existing requests get up to host shutdown timeout to drain.
- Drop `app_offline.htm` at IIS site root for graceful deploy (ANCM stops the app and serves the file).

## Visual Studio publish profiles (`.pubxml`)

Web SDK auto-includes build outputs + `wwwroot/**`, `**/*.config`, `**/*.json`, `**/*.cshtml`, `**/*.razor`. CLI: `dotnet publish` (default Debug — pass `-c`), `dotnet publish /p:PublishProfile=FolderProfile`, `dotnet build /p:DeployOnBuild=true /p:PublishProfile=FolderProfile`. `DeployOnBuild` works on `dotnet build` / `msbuild`, NOT `dotnet publish`. MSDeploy Windows-only. `.pubxml.user` holds encrypted creds — don't commit.

```xml
<!-- Properties/PublishProfiles/FolderProfile.pubxml -->
<Project>
  <PropertyGroup>
    <PublishProvider>FileSystem</PublishProvider>
    <PublishUrl>\\r8\Release\AdminWeb</PublishUrl>
    <WebPublishMethod>FileSystem</WebPublishMethod>
    <_TargetId>Folder</_TargetId>
    <EnvironmentName>Development</EnvironmentName>
  </PropertyGroup>
</Project>
```

Self-contained: `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` + `dotnet publish -c Release -r win-x64 --self-contained`. Include/exclude via `<Content Update="..." CopyToPublishDirectory="Never" />`, `<MsDeploySkipRules>`, `<DotNetPublishFiles>`, `<ResolvedFileToPublish>`. Run pre/post targets via `<Target Name="..." BeforeTargets="BeforePublish">` / `AfterTargets="AfterPublish"`.

## Quick decision matrix

| You need | Use |
|---|---|
| Default cross-platform | Kestrel |
| Public Internet edge | Kestrel + reverse proxy (recommended) OR Kestrel direct |
| Windows + IIS shop | IIS in-process (default) |
| Kerberos kernel auth on Windows | HTTP.sys |
| Share port across processes (Windows) | HTTP.sys |
| Single-file executable on IIS | OOP (in-proc unsupported) |
| Smallest container | `aspnet:10.0-jammy-chiseled` + `dotnet publish /t:PublishContainer` |
| AOT in containers | `runtime-deps:10.0-*-chiseled` |
| HTTP/3 today | Kestrel; `Http1AndHttp2AndHttp3`; HTTPS required; HTTP/2 fallback |
| K8s | Kestrel + `/healthz/{ready,live}` + `UseForwardedHeaders` |
| Azure App Service | Default; `ASPNETCORE_ENVIRONMENT`; slot-independent Data Protection key ring |

## Common gotchas

- `ForwardedHeaders.None` is the default — middleware does nothing until you set `XForwardedFor | XForwardedProto`. Run before HSTS / HTTPS redirection / auth.
- IIS in-proc disables Kestrel limits and uses IIS `maxAllowedContentLength` instead — bump web.config too. One app per pool; matching architecture; no single-file publish.
- Data Protection keys are per-slot on Azure App Service — swap signs everyone out unless a slot-independent provider is used.
- Linux env vars are case-sensitive in file lookup; `__` for `:` in nested keys.
- Self-signed certs aren't accepted by browsers on HTTP/3 — use the dev cert with `HttpClient` for loopback testing.
- `HttpContext.Abort` is for severe errors only — connection setup is expensive.
- Kestrel timeouts and the request-timeouts middleware are silently disabled under debugger.
- Chiseled images have no shell — `HEALTHCHECK CMD curl ...` typically won't work; use app-internal probes.

## Cross-references

- Public docs (servers): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/?view=aspnetcore-10.0
- Kestrel: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0
- HTTP.sys: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/httpsys?view=aspnetcore-10.0
- IIS: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0
- Linux + Nginx: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0
- Docker: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/?view=aspnetcore-10.0
- Azure App Service: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/azure-apps/?view=aspnetcore-10.0
- Proxy / load balancer: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- Health checks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0
- VS publish profiles: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/visual-studio-publish-profiles?view=aspnetcore-10.0
- Related: `aspnet-core-fundamentals` — DI, options, middleware, routing, error handling, health-check wiring.
- Related: `aspnet-core-performance` — Kestrel/HTTP/2/3 tuning, HttpClient, GC.
- Related: `aspnet-core-security` — auth, HTTPS, HSTS, Data Protection.
- Related: `aspnet-core-yarp` — YARP reverse proxy.
- Related: `dotnet-aspire` — local orchestration of containers + services.
