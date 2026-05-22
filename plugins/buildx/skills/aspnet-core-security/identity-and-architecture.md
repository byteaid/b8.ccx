# Identity Solution Selection and Authentication Architecture

Pick an identity solution; design the auth pipeline; multi-scheme + policy schemes; ASP.NET Core Identity (`UserManager`/`SignInManager`/2FA); `MapIdentityApi` for SPAs.

## Identity solution decision

```
Need to share login across apps OR expose APIs to external apps?
├── No  → ASP.NET Core Identity (cookies; optional bearer for SPA via MapIdentityApi)
└── Yes → OIDC server
            ├── Disconnected / on-prem      → Duende, OpenIddict, Keycloak
            ├── Azure / MS-shop             → Microsoft Entra ID + Microsoft.Identity.Web
            └── Vendor-managed cloud        → Auth0, Okta, Entra External ID
```

| Solution | Good for | Watch-outs |
|---|---|---|
| ASP.NET Core Identity | Single-app login; SPA via `MapIdentityApi` | NOT an IdP/SSO; tokens are proprietary opaque, not JWTs |
| Duende IdentityServer | Multi-app SSO, full IdP, on-prem | Commercial license over thresholds |
| OpenIddict | OSS alternative to Duende | Lower-level setup |
| Entra ID / External ID | Azure-hosted apps, B2B/B2C, MFA | Requires Internet; Azure-specific |
| Auth0 / Okta / Keycloak | Cross-platform SSO, enterprise federation | Vendor lock-in (cloud); cost |

## Authentication architecture

Pipeline (rule 1):

```csharp
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapControllers();
```

Multi-scheme + policy schemes (e.g. cookies for browser, JWT for `/api`; or distinct JWTs per issuer):

```csharp
const string POLICY = "MultiAuth";

builder.Services.AddAuthentication(o => { o.DefaultScheme = POLICY; o.DefaultChallengeScheme = POLICY; })
    .AddJwtBearer("EntraId", o => { /* ... */ })
    .AddJwtBearer("Vendor",  o => { /* ... */ })
    .AddPolicyScheme(POLICY, displayName: null, opt =>
    {
        opt.ForwardDefaultSelector = ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer "))
            {
                var token = auth["Bearer ".Length..].Trim();
                var handler = new JsonWebTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var iss = handler.ReadJsonWebToken(token).Issuer;
                    if (iss == EntraIssuer)  return "EntraId";
                    if (iss == VendorIssuer) return "Vendor";
                }
            }
            return "EntraId";
        };
    });
```

Resolution order on `AuthenticationSchemeOptions`: most-specific `Forward{Op}` -> `ForwardDefaultSelector` -> `ForwardDefault`.

For multi-tenant per-tenant providers, ASP.NET Core has no built-in solution — use Orchard Core, ABP Framework, or Finbuckle.MultiTenant.

## ASP.NET Core Identity

| Service | Role |
|---|---|
| `IdentityUser` / `IdentityRole` (`<TKey>` variants) | Default POCO; subclass to add fields |
| `IdentityDbContext<TUser,TRole,TKey>` | EF Core schema |
| `UserManager<TUser>` | CRUD users, password ops, lockout, 2FA, claims, roles |
| `SignInManager<TUser>` | `PasswordSignInAsync`, `TwoFactorSignInAsync`, external login, sign-out, security stamp validation |
| `RoleManager<TRole>` | CRUD roles |
| `IPasswordHasher<TUser>` | PBKDF2-HMAC-SHA512, 100k iterations (v3 format) by default |
| `IUserStore<TUser>` / `IRoleStore<TRole>` | Persistence; EF impl in `Microsoft.AspNetCore.Identity.EntityFrameworkCore` |

Variants:
- `AddDefaultIdentity<TUser>` — Identity + cookie + UI (`Microsoft.AspNetCore.Identity.UI`).
- `AddIdentity<TUser,TRole>` — Identity + cookies, no UI (manual scaffolding).
- `AddIdentityCore<TUser>` — `UserManager` only (no cookies / `SignInManager` / roles); base for API-only setups, compose roles via `.AddRoles<IdentityRole>()`.

```csharp
builder.Services.AddDefaultIdentity<IdentityUser>(o => o.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.Configure<IdentityOptions>(o =>
{
    o.Password.RequiredLength          = 12;
    o.Password.RequireNonAlphanumeric  = true;
    o.Lockout.MaxFailedAccessAttempts  = 5;
    o.Lockout.DefaultLockoutTimeSpan   = TimeSpan.FromMinutes(15);
    o.User.RequireUniqueEmail          = true;
    o.SignIn.RequireConfirmedEmail     = true;
});

builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(30));
```

Common calls:

```csharp
await _userManager.CreateAsync(new IdentityUser { UserName = email, Email = email }, password);
var code  = await _userManager.GenerateEmailConfirmationTokenAsync(user);
await _userManager.ConfirmEmailAsync(user, decodedCode);
var reset = await _userManager.GeneratePasswordResetTokenAsync(user);
await _userManager.ResetPasswordAsync(user, reset, newPassword);

var result = await _signInManager.PasswordSignInAsync(email, password,
    isPersistent: false, lockoutOnFailure: true);
// result.RequiresTwoFactor / IsLockedOut / IsNotAllowed / Succeeded

await _userManager.SetTwoFactorEnabledAsync(user, true);
await _signInManager.TwoFactorAuthenticatorSignInAsync(totp, isPersistent, rememberClient);
await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

await _userManager.AddToRoleAsync(user, "Admin");
await _userManager.AddClaimAsync(user, new Claim("tenant", "acme"));

var info = await _signInManager.GetExternalLoginInfoAsync();
await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
    isPersistent: false, bypassTwoFactor: true);
```

Token providers: `TokenOptions.DefaultProvider`, `DefaultEmailProvider`, `DefaultPhoneProvider`, `DefaultAuthenticatorProvider` (TOTP/RFC 6238). Custom: `.AddTokenProvider<MyProvider>("MyName")`.

`ProtectPersonalData = true` requires `IPersonalDataProtector`, `ILookupProtectorKeyRing`, `ILookupProtector` registered + `[ProtectedPersonalData]` on user model properties.

.NET 10 emits **Identity metrics** via `Meter` + `System.Diagnostics.Metrics` for sign-in, 2FA, lockout, password ops — surfaces via OpenTelemetry.

## `MapIdentityApi<TUser>` for SPAs

For SPAs talking to an ASP.NET Core API host. Issues either an authentication cookie or a **proprietary opaque bearer + refresh token (NOT a JWT)**.

```csharp
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

app.MapIdentityApi<IdentityUser>();

// Logout endpoint (intentionally not provided by MapIdentityApi)
app.MapPost("/logout", async (SignInManager<IdentityUser> sm, [FromBody] object _) =>
    { await sm.SignOutAsync(); return Results.Ok(); }).RequireAuthorization();
```

Endpoints: `POST /register`, `POST /login?useCookies={bool}`, `POST /refresh`, `GET /confirmEmail`, `POST /resendConfirmationEmail`, `POST /forgotPassword`, `POST /resetPassword`, `POST /manage/2fa`, `GET|POST /manage/info`.

`AccessTokenResponse`: `{ tokenType: "Bearer", accessToken, expiresIn, refreshToken }`. Tune via `BearerTokenOptions.{BearerTokenExpiration, RefreshTokenExpiration}` (defaults 1h / 14d).

| Aspect | `useCookies=true` | `useCookies=false` |
|---|---|---|
| Browser apps | Recommended | Avoid (XSS risk; never `localStorage`) |
| Mobile/CLI | Not natively | Recommended |
| Refresh | Sliding expiration | `POST /refresh` |
| Logout | Cookie deletion | Discard tokens (no server revocation by default) |

Standalone bearer scheme without Identity: `builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme, o => o.BearerTokenExpiration = TimeSpan.FromMinutes(15));`.
