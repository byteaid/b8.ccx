---
name: aspnet-core-security
description: ASP.NET Core 10 security & identity reference. Covers identity-solution selection (Identity / Duende / OpenIddict / Entra ID), auth scheme model, cookie auth, JWT bearer + `dotnet user-jwts`, OIDC + PKCE + PAR (RFC 9126), social logins, claims + `IClaimsTransformation`, ASP.NET Core Identity (`UserManager`/`SignInManager`/2FA), `MapIdentityApi<TUser>` for SPAs, `[Authorize]` + role/policy/resource-based authz, custom `IAuthorizationPolicyProvider`, Data Protection API (key persistence, app isolation, Azure Blob + Key Vault), secrets, HTTPS/HSTS/forwarded headers, antiforgery, CORS, BFF pattern.
when_to_use: |
  - Trigger keywords: AddAuthentication, AddCookie, AddJwtBearer, AddOpenIdConnect, AddIdentity, MapIdentityApi, AddBearerToken, AddAuthorizationBuilder, IAuthorizationRequirement, IAuthorizationHandler, IAuthorizationPolicyProvider, IClaimsTransformation, JwtBearerOptions, OpenIdConnectOptions, PKCE, UseAntiforgery, AddCors, UseHsts, UseForwardedHeaders, AddDataProtection, PersistKeysToAzureBlobStorage, ProtectKeysWithAzureKeyVault, dotnet user-jwts, BFF, [Authorize].
  - Task shapes: pick an identity solution; wire cookie + OIDC for a server app; configure JWT bearer for an API; protect SignalR with JWT incl. WebSocket query-string; add `MapIdentityApi` to an SPA backend; design a policy with a custom requirement + handler; write resource-based authorization; configure Data Protection key persistence to Azure Blob + Key Vault; add antiforgery to a form handler; configure CORS for credentialed SPA; build a BFF.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/Program.cs", "**/appsettings*.json", "**/*.razor"]
---

# ASP.NET Core Security & Identity — Reference

Reference for security wiring on ASP.NET Core 10. Pin the rules; load the matching sub-file for depth.

## Mental model

- **Authentication** = "who are you" (produces `HttpContext.User`). **Authorization** = "what can you do". Separate middleware: `app.UseAuthentication()` then `app.UseAuthorization()`.
- Authentication is built around named **schemes** (name + handler + options). Operations on `HttpContext`: `AuthenticateAsync`, `ChallengeAsync`, `ForbidAsync`, `SignInAsync`, `SignOutAsync`. Defaults: `DefaultScheme` and `DefaultAuthenticate/Challenge/Forbid/SignIn/SignOut` schemes.
- Authorization is built around **policies**. Policy = name + 1..n `IAuthorizationRequirement`s; each requirement is satisfied by 1..n `IAuthorizationHandler`s. **Multiple requirements in a policy = AND. Multiple handlers per requirement = OR.**
- **Data Protection** is the symmetric-crypto substrate behind auth cookies, antiforgery tokens, Identity tokens, `MapIdentityApi` bearer tokens, TempData. Multi-instance apps MUST share a key ring or sessions break.
- **Identity solution choice** drives everything else: ASP.NET Core Identity for single-app login; an OIDC server (Duende / OpenIddict / Entra ID / Auth0) for SSO across apps. **`MapIdentityApi` is not an IdP** — its bearer tokens are proprietary opaque tokens, not JWTs.
- **BFF** (Backend-for-Frontend) is the .NET 10 recommendation for browser apps: tokens live server-side; the browser holds an `HttpOnly` `Secure` cookie; the BFF proxies API calls and attaches the access token.

## Non-negotiable rules

1. **Pipeline order.** `UseRouting` -> `UseCors` -> `UseAuthentication` -> `UseAuthorization` -> `UseAntiforgery` -> endpoints. `UseForwardedHeaders` (when behind a proxy) goes BEFORE `UseHttpsRedirection`.
2. **No production secrets in `appsettings.json` or env vars.** Use Azure Key Vault + Managed Identity. User Secrets is **dev only**.
3. **Avoid Resource Owner Password Credentials (ROPC).** Use OIDC code+PKCE confidential client. Managed Identity for Azure resources.
4. **No public OAuth clients in browsers.** Use BFF.
5. **`MapIdentityApi` issues opaque tokens, NOT JWTs.** For real IdP behavior use Duende, OpenIddict, or Entra.
6. **Data Protection multi-instance**: `SetApplicationName` (same on every instance) + shared persistence (Azure Blob / DB / Redis-with-persistence) + encryption at rest (Key Vault / certificate). Without these, restart/scale-out invalidates all sessions.
7. **`Cookie.SameSite = Strict` breaks OAuth2 redirect callbacks.** Use `Lax` for cookies that participate in OIDC.
8. **`[Authorize]` `Roles` is OR within an attribute; multiple `[Authorize]` attributes are AND.** Role names case-sensitive; policy names case-insensitive.
9. **`AllowAnyOrigin()` cannot combine with `AllowCredentials()`** — runtime exception.
10. **`MapInboundClaims = false`** on `JwtBearerOptions` / `OpenIdConnectOptions` — keep short JWT claim names (`sub`, `roles`) instead of long `ClaimTypes.*` URIs. Set `NameClaimType` / `RoleClaimType` to match the issuer's claims.
11. **Browsers cannot set `Authorization` headers for WebSockets/SSE.** JWT must arrive in `?access_token=...`; fish it out via `JwtBearerEvents.OnMessageReceived`, gate by request path. **HTTPS only.** `Microsoft.AspNetCore.Hosting` logs full URLs at `Information` — set to `Warning`+ in prod or strip `access_token` in middleware.
12. **CSRF is irrelevant for bearer tokens carried in headers** — browsers don't auto-attach `Authorization`. Required for cookie-authed unsafe methods.
13. **.NET 10**: known API endpoints no longer redirect to login pages with cookie auth — they return `401`/`403`.
14. **`PushedAuthorizationBehavior.UseIfAvailable` is the .NET 9+ default for `AddOpenIdConnect`** (RFC 9126). Set `Require` for hardened configs.
15. **Don't put hundreds of roles in tokens** — bloat. Prefer policies + claims.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Identity-solution decision matrix; pipeline architecture; multi-scheme + policy schemes; ASP.NET Core Identity (`UserManager`/`SignInManager`/2FA); `MapIdentityApi<TUser>` for SPAs | [identity-and-architecture.md](identity-and-architecture.md) | Choosing an identity solution; designing the auth pipeline; wiring Identity tables; exposing `MapIdentityApi` for an SPA backend. |
| Cookie auth; JWT bearer + `dotnet user-jwts`; OIDC + PKCE + PAR; social/OAuth providers; claims + `IClaimsTransformation` | [jwt-cookie-oidc.md](jwt-cookie-oidc.md) | Configuring cookie / JWT / OIDC schemes, social logins, or shaping the principal's claims. |
| `[Authorize]` attributes; role / policy / resource-based authz; custom requirements + handlers; default & fallback policies; custom `IAuthorizationPolicyProvider` | [authorization.md](authorization.md) | Designing policies, writing custom handlers, parameterizing policies via attributes. |
| Data Protection consumer API; key persistence; multi-instance; Azure Blob + Key Vault; app isolation caveats; password hashing; secrets management (User Secrets / Key Vault) | [data-protection-and-secrets.md](data-protection-and-secrets.md) | Persisting / encrypting Data Protection keys, building protectors with purpose strings, hashing passwords by hand, configuring secrets sources. |
| HTTPS / HSTS / forwarded headers; antiforgery (synchronizer-token); CORS; BFF reference architecture; threat-model cheat sheet | [transport-and-bff.md](transport-and-bff.md) | Hardening transport, wiring antiforgery and CORS, building a BFF, reviewing the threat model. |

## Cross-references

- Public docs (Security overview): https://learn.microsoft.com/en-us/aspnet/core/security/?view=aspnetcore-10.0
- Public docs (Choose identity solution): https://learn.microsoft.com/en-us/aspnet/core/security/how-to-choose-identity-solution?view=aspnetcore-10.0
- Public docs (Cookie auth): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0
- Public docs (`dotnet user-jwts`): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn?view=aspnetcore-10.0
- Public docs (Policy schemes): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/policyschemes?view=aspnetcore-10.0
- Public docs (OIDC web auth): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0
- Public docs (Identity): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0
- Public docs (Identity API endpoints): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
- Public docs (Authorization policies): https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0
- Public docs (Custom policy provider): https://learn.microsoft.com/en-us/aspnet/core/security/authorization/iauthorizationpolicyprovider?view=aspnetcore-10.0
- Public docs (Data Protection): https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction?view=aspnetcore-10.0
- Public docs (App secrets): https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0
- Public docs (HTTPS): https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0
- Public docs (Antiforgery): https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0
- Public docs (CORS): https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0
- Related skill: `aspnet-core-blazor` § Blazor surface for auth.
- Related skill: `aspnet-core-signalr` — JWT for SignalR (WebSocket `?access_token`), `HubInvocationContext`-based authorization.
- Related skill: `aspnet-core-grpc` — `[Authorize]` on services, `CallCredentials`, mTLS.
- Related skill: `aspnet-core-yarp` — per-route `AuthorizationPolicy`, BFF reverse-proxy via `MapForwarder`.
- Related skill: `dotnet-ef-core` — `IdentityDbContext` and `IDataProtectionKeyContext`.
