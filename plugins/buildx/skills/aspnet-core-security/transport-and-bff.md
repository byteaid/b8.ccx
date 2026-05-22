# HTTPS, HSTS, Forwarded Headers, Antiforgery, CORS, BFF, Threat Model

Transport-layer hardening, antiforgery synchronizer-token pattern, CORS rules, BFF reference architecture, threat-mitigation matrix.

## HTTPS enforcement

```csharp
builder.Services.AddHttpsRedirection(o =>
{
    o.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;   // dev
    // o.RedirectStatusCode = StatusCodes.Status308PermanentRedirect; // prod
    o.HttpsPort = 443;
});
app.UseHttpsRedirection();
```

Port discovery order: `HttpsRedirectionOptions.HttpsPort` -> `ASPNETCORE_HTTPS_PORT` env -> `IServerAddressesFeature` (only when one secure port is bound) -> `https_port` in config.

HSTS:

```csharp
builder.Services.AddHsts(o =>
{
    o.Preload          = true;
    o.IncludeSubDomains= true;
    o.MaxAge           = TimeSpan.FromDays(365);
});

if (!app.Environment.IsDevelopment()) app.UseHsts();
```

Excludes loopback by default. Don't ship in dev (sticky cache). Browser-only — APIs serving non-browsers don't depend on it.

Behind a proxy:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
app.UseForwardedHeaders();
app.UseHttpsRedirection();   // AFTER forwarded headers
```

Without this, `Request.Scheme` is the proxy-to-app scheme (often `http`) and HTTPS redirect loops occur. Don't apply `RequireHttpsAttribute` to APIs receiving sensitive payloads — listen on HTTPS only instead.

Dev cert: `dotnet dev-certs https --trust` (Win/macOS); on Linux export and `update-ca-certificates`.

## Antiforgery (XSRF/CSRF)

Synchronizer-token pattern: a request token bound to a session cookie token per user.

```csharp
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName                  = "X-XSRF-TOKEN";
    o.FormFieldName               = "__RequestVerificationToken";
    o.Cookie.Name                 = "__Host-X-XSRF-TOKEN";
    o.Cookie.HttpOnly             = true;
    o.Cookie.SecurePolicy         = CookieSecurePolicy.Always;
    o.Cookie.SameSite             = SameSiteMode.Strict;
});

app.UseAuthentication(); app.UseAuthorization();
app.UseAntiforgery();   // .NET 7+
app.MapRazorPages(); app.MapControllers();
```

Validation attributes:
- `[ValidateAntiForgeryToken]` — explicit per-action.
- `[AutoValidateAntiforgeryToken]` (controller / global filter) — validates on unsafe methods (POST/PUT/PATCH/DELETE).
- `[IgnoreAntiforgeryToken]` — opt-out.
- Razor Pages: GET exempt; non-GET handlers validate by default.

Razor `<form>` injects the hidden token automatically.

**Minimal API + form binding (.NET 8+): `[FromForm]` automatically requires antiforgery.** Disable with `.DisableAntiforgery()`:

```csharp
app.MapPost("/upload", ([FromForm] IFormFile file) => Results.Ok())
   .DisableAntiforgery();
```

Manual SPA pattern:

```csharp
app.MapGet("/antiforgery/token", (IAntiforgery anti, HttpContext ctx) =>
{
    var tokens = anti.GetAndStoreTokens(ctx);
    ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
        new CookieOptions { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict });
    return Results.Ok();
}).RequireAuthorization();
```

Notes: refresh token after authentication (request token bound to user identity). CSRF irrelevant for bearer tokens in headers. SignalR negotiate handshake includes its own anti-forgery handling when paired with cookies.

## CORS

CORS *relaxes* same-origin policy — **not** a security feature. Browser still sends the request; CORS just decides whether JS can read the response.

```csharp
builder.Services.AddCors(o =>
{
    o.AddPolicy("Spa", p => p
        .WithOrigins("https://app.contoso.com", "https://admin.contoso.com")
        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
        .WithHeaders(HeaderNames.ContentType, HeaderNames.Authorization, "X-XSRF-TOKEN")
        .WithExposedHeaders("X-Total-Count", "X-Pagination")
        .AllowCredentials()                              // cookies / Authorization
        .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
});

app.UseRouting();
app.UseCors();
app.UseAuthentication(); app.UseAuthorization();
app.MapControllers().RequireCors("Spa");
```

`CorsPolicyBuilder` highlights: `WithOrigins` (case-sensitive), `AllowAnyOrigin` (cannot combine with `AllowCredentials`), `SetIsOriginAllowed`, `SetIsOriginAllowedToAllowWildcardSubdomains` (`https://*.contoso.com`). Default exposed: `Cache-Control`, `Content-Language`, `Content-Type`, `Expires`, `Last-Modified`, `Pragma`.

Preflight `OPTIONS` is sent when method !in {GET, HEAD, POST}, OR custom headers, OR `Content-Type` !in {`application/x-www-form-urlencoded`, `multipart/form-data`, `text/plain`}. Misconfigured `Access-Control-Max-Age` causes hard-to-debug failures.

For SignalR-specific CORS (must include `AllowCredentials`, sticky session cookies, WebSocket origin validation in middleware), load `aspnet-core-signalr`.

## Backend-for-Frontend (BFF) pattern

The .NET 10 recommendation for browser apps. BFF runs the OIDC dance server-side, stores tokens server-side, issues an `HttpOnly` `Secure` cookie. Tokens never reach JS.

```csharp
// Cookie + OIDC
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(o =>
{
    o.Cookie.Name           = "__Host-bff";
    o.Cookie.HttpOnly       = true;
    o.Cookie.SecurePolicy   = CookieSecurePolicy.Always;
    o.Cookie.SameSite       = SameSiteMode.Strict;
    o.SlidingExpiration     = true;
})
.AddOpenIdConnect(o =>
{
    o.Authority    = builder.Configuration["Oidc:Authority"];
    o.ClientId     = builder.Configuration["Oidc:ClientId"];
    o.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
    o.ResponseType = OpenIdConnectResponseType.Code;
    o.UsePkce      = true;
    o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("offline_access"); o.Scope.Add("api://my-api/.default");
    o.SaveTokens                    = true;
    o.GetClaimsFromUserInfoEndpoint = true;
    o.MapInboundClaims              = false;
    o.PushedAuthorizationBehavior   = PushedAuthorizationBehavior.UseIfAvailable;
});

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

builder.Services.AddDataProtection()
    .SetApplicationName("Bff")
    .PersistKeysToAzureBlobStorage(blobUri, credential)
    .ProtectKeysWithAzureKeyVault(kvKeyId, credential);

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication(); app.UseAuthorization();
app.UseAntiforgery();

app.MapGet ("/auth/login",  (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }));
app.MapPost("/auth/logout", (HttpContext ctx) =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme }));

app.MapGet("/auth/me", (ClaimsPrincipal u) =>
    Results.Ok(new
    {
        name   = u.Identity?.Name,
        claims = u.Claims.Select(c => new { c.Type, c.Value })
    })).RequireAuthorization();

// Reverse proxy to upstream API, attaching server-side access_token (YARP)
app.MapForwarder("/api/{**catch-all}", "https://my-api/", transform => transform
    .AddRequestTransform(async ctx =>
    {
        var token = await ctx.HttpContext.GetTokenAsync("access_token");
        if (token is not null)
            ctx.ProxyRequest.Headers.Authorization = new("Bearer", token);
    })).RequireAuthorization();
```

`MapForwarder` ships in YARP — install `Yarp.ReverseProxy`. For YARP depth load `aspnet-core-yarp`.

Key BFF rules:
1. Cookie is `__Host-` prefixed, `HttpOnly`, `Secure`, `SameSite=Strict` (or `Lax` if cross-site nav needed).
2. Antiforgery on every state-changing endpoint accessible via cookies.
3. Tokens (access/refresh) live in the BFF only — encrypted at rest via Data Protection.
4. Refresh tokens used silently server-side; clients receive 401 on session expiry and call `/auth/login`.
5. Logout uses both `Cookies` and `OpenIdConnect` schemes so `id_token_hint` is sent for OIDC end-session.

For the **Blazor surface** (`AuthenticationStateProvider`, `AuthorizeView`, `AuthorizeRouteView`, antiforgery wiring on Blazor) load `aspnet-core-blazor` § Blazor surface for auth.

## Threat-model cheat sheet

| Vector | Mitigation |
|---|---|
| Credential theft | HTTPS + HSTS + secure cookies + Identity password hashing (PBKDF2) |
| Session hijacking | Cookie `HttpOnly` + `Secure` + `SameSite`, Data Protection key ring, security stamp validation |
| Token forgery / tamper | Data Protection (cookies, antiforgery), JWT signature validation, OIDC PAR (RFC 9126) |
| CSRF / XSRF | `AddAntiforgery` + `UseAntiforgery` + `[Auto]ValidateAntiforgeryToken`; SameSite cookies |
| XSS | Razor automatic encoding; CSP headers; never `Html.Raw` untrusted |
| Open redirect | `LocalRedirect`, validate `returnUrl`, `Url.IsLocalUrl` |
| SQL injection | EF Core parameterized queries; never concatenate user input into raw SQL |
| Click-jacking | Antiforgery middleware emits `X-Frame-Options: SAMEORIGIN` |
| Brute force | Identity `LockoutOptions`, `lockoutOnFailure: true` in `PasswordSignInAsync` |
| Replay of stolen cookie | `SecurityStampValidator` periodic revalidation |
| Insecure auth flow | Avoid ROPC; OIDC code+PKCE confidential client; managed identities for Azure |
| Secrets in code | User Secrets (dev), Azure Key Vault (prod) |
