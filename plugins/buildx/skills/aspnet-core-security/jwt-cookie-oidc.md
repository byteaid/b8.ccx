# Cookie Auth, JWT Bearer, OIDC, Social, Claims

Cookie auth, JWT bearer, `dotnet user-jwts`, OIDC + PKCE + PAR, social/OAuth providers, claims & `IClaimsTransformation`.

## Cookie authentication

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name             = ".MyApp.Auth";
        o.Cookie.HttpOnly         = true;
        o.Cookie.SecurePolicy     = CookieSecurePolicy.Always;
        o.Cookie.SameSite         = SameSiteMode.Lax;       // Strict breaks OAuth2 callbacks
        o.Cookie.IsEssential      = true;                   // GDPR
        o.ExpireTimeSpan          = TimeSpan.FromMinutes(60);
        o.SlidingExpiration       = true;
        o.LoginPath               = "/Account/Login";
        o.AccessDeniedPath        = "/Account/Forbidden";
    });
```

Sign-in/out:

```csharp
var principal = new ClaimsPrincipal(new ClaimsIdentity(claims,
    CookieAuthenticationDefaults.AuthenticationScheme));
await HttpContext.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme, principal,
    new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
```

Revoke compromised sessions via `CookieAuthenticationEvents.OnValidatePrincipal` (subscribe by setting `o.EventsType = typeof(MyValidator)` and registering the validator as scoped). Runs on EVERY request — keep it cheap. Non-destructive renew: `ctx.ReplacePrincipal(newPrincipal); ctx.ShouldRenew = true;`.

`UseCookiePolicy` enforces `MinimumSameSitePolicy` etc.; effective `SameSite` is the *more restrictive* of cookie value and policy minimum.

## JWT bearer

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority           = "https://login.microsoftonline.com/{tenantId}/v2.0";
        o.Audience            = "api://my-api";
        o.RequireHttpsMetadata= true;     // false ONLY in dev
        o.MapInboundClaims    = false;    // keep short claim names
        o.SaveToken           = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuers = new[] { "https://issuer-1" },
            ValidateAudience = true, ValidAudiences = new[] { "api://my-api" },
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            RequireSignedTokens = true, ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name", RoleClaimType = "roles"
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>          // SignalR/WebSockets
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
```

`JwtBearerOptions` highlights: `Authority` (issuer base URL; OIDC discovery at `/.well-known/openid-configuration`; auto-refreshes JWKS), `MetadataAddress` (explicit), `ConfigurationManager` (JWKS cache, default 24h refresh / 5 min cooldown), `IncludeErrorDetails`.

Multiple bearer schemes against multiple issuers — combine via a policy:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiUser", p => p
        .AddAuthenticationSchemes("EntraId", "Internal")
        .RequireAuthenticatedUser());
```

### `dotnet user-jwts` (dev tooling)

App-specific JWTs signed by a key in user-secrets. Wires `appsettings.Development.json` `Authentication:Schemes:Bearer:{ValidIssuer,ValidAudiences}`.

```bash
dotnet user-jwts create --name alice --scope "myapi:read" --role admin --claim tenant=acme --valid-for 7d
dotnet user-jwts list / print {ID} --show-all / key --reset / clear
```

```csharp
builder.Services.AddAuthentication("Bearer").AddJwtBearer();   // binds to Authentication:Schemes:Bearer
```

## OpenID Connect (OIDC)

OIDC = OAuth2 + identity (`id_token`). Profile: **confidential client + Authorization Code flow + PKCE**. Public clients in the browser are no longer recommended — use BFF.

```csharp
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(o =>
{
    o.Authority    = builder.Configuration["Oidc:Authority"];
    o.ClientId     = builder.Configuration["Oidc:ClientId"];
    o.ClientSecret = builder.Configuration["Oidc:ClientSecret"];   // user-secrets / Key Vault
    o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    o.ResponseType = OpenIdConnectResponseType.Code;
    o.UsePkce      = true;                                          // default since .NET 7
    o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("email"); o.Scope.Add("offline_access");
    o.SaveTokens                       = true;
    o.GetClaimsFromUserInfoEndpoint    = true;
    o.MapInboundClaims                 = false;
    o.TokenValidationParameters.NameClaimType = "name";
    o.TokenValidationParameters.RoleClaimType = "roles";
    o.PushedAuthorizationBehavior      = PushedAuthorizationBehavior.UseIfAvailable;   // .NET 9+ RFC 9126
});
```

Default callback paths: `/signin-oidc`, `/signout-callback-oidc`, `/signout-oidc`. `OpenIdConnectEvents`: `OnRedirectToIdentityProvider`, `OnAuthorizationCodeReceived`, `OnTokenValidated`, `OnUserInformationReceived`, `OnAccessDenied`, `OnRemoteFailure`, `OnRemoteSignOut`, `OnSignedOutCallbackRedirect`, `OnPushAuthorization` (.NET 9+).

Login / logout:

```csharp
public IActionResult Login(string? returnUrl)
{
    if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl)) returnUrl = "/";
    return Challenge(new AuthenticationProperties { RedirectUri = returnUrl });
}

public IActionResult Logout() =>
    SignOut(new AuthenticationProperties { RedirectUri = "/SignedOut" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
```

`SignOut` with both schemes clears the local cookie AND redirects through the IdP `end_session_endpoint` with `id_token_hint` (sent automatically when `SaveTokens=true`).

Force authentication globally:

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```

For Entra: `AddMicrosoftIdentityWebApp(...)` from `Microsoft.Identity.Web`; for any other OIDC use plain `AddOpenIdConnect`.

## Social / OAuth providers

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(o => { o.ClientId = ...; o.ClientSecret = ...; o.SaveTokens = true; })
    .AddFacebook(o => { o.AppId = ...; o.AppSecret = ...; })
    .AddMicrosoftAccount(o => { o.ClientId = ...; o.ClientSecret = ...; });
```

Default callback paths: `/signin-google`, `/signin-facebook`, `/signin-microsoft`, `/signin-twitter`. Behind a proxy, enable `UseForwardedHeaders` early so the OAuth callback URL stays `https`. Other providers via `AspNet.Security.OAuth.Providers` (GitHub, Apple, LinkedIn, Discord, ...) and `AspNet.Security.OpenId.Providers` (Steam).

## Claims & claims transformation

`ClaimsPrincipal` (= `HttpContext.User`) wraps one or more `ClaimsIdentity`s; each holds `Claim`s.

Disable inbound long-URI claim mapping (per-scheme, preferred): `options.MapInboundClaims = false;`. Globally (.NET 8+): `JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();`.

`ClaimActions` map JSON keys from userinfo / id_token:

```csharp
options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
options.ClaimActions.MapJsonKey("website", "website");
options.ClaimActions.Remove("amr");
options.ClaimActions.MapJsonSubKey("urn:google:image", "image", "url");
```

`IClaimsTransformation` runs after a scheme produces the principal. **May be called multiple times per request — check before adding:**

```csharp
public sealed class TenantClaimsTransformation(ITenantStore store) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        const string Type = "tenant_id";
        if (principal.HasClaim(c => c.Type == Type)) return principal;
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub is null) return principal;
        var id = new ClaimsIdentity();
        id.AddClaim(new Claim(Type, await store.ResolveTenantAsync(sub)));
        principal.AddIdentity(id);
        return principal;
    }
}
builder.Services.AddTransient<IClaimsTransformation, TenantClaimsTransformation>();
```

For Identity-issued claims, override `IUserClaimsPrincipalFactory<TUser>.GenerateClaimsAsync`.
