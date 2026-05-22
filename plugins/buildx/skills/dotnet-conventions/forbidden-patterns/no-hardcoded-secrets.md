# Forbidden — Hardcoded secrets, connection strings, URLs

## What it looks like

```csharp
// Program.cs / Service classes
const string SqlCs = "Server=prod-sql.acme.com;Database=Foo;User=sa;Password=P@ssw0rd!;";
services.AddDbContext<AppDb>(opts => opts.UseSqlServer(SqlCs));

services.AddHttpClient<IStripeClient, StripeClient>(c =>
{
    c.BaseAddress = new Uri("https://api.stripe.com/v1/");
    c.DefaultRequestHeaders.Add("Authorization", "Bearer sk_live_AbCdEf1234567890");
});

builder.Configuration["JwtSigningKey"] = "ThisIsTheJwtSigningKeyForProduction";
```

```json
// appsettings.json — secrets in source control
{
  "ConnectionStrings": {
    "Default": "Server=...;User=...;Password=P@ssw0rd!"
  },
  "Stripe": { "SecretKey": "sk_live_..." }
}
```

```bicep
// infrastructure
param sqlAdminPassword string = 'P@ssw0rd!'      // banned: should be @secure() with no default
```

## Why it's banned

1. **Secrets in source control are a credential leak.** Git history is forever; a single push exposes the secret to everyone with read access, ever.
2. **Configuration drift.** Hardcoded values defeat the per-environment configuration story (dev vs staging vs prod). Aspire injects connection strings; hardcoding ignores them.
3. **No rotation.** A hardcoded secret cannot be rotated without redeployment. Real secrets live in Key Vault / managed identity and rotate behind the app.
4. **Static analyzers and secret scanners catch these** — the team's CI is configured to fail builds with detected secrets.

## What to do instead

```csharp
// Program.cs — read everything via configuration
var sqlCs = builder.Configuration.GetConnectionString("gymtrackerdb")
    ?? throw new InvalidOperationException("Connection string 'gymtrackerdb' missing.");
services.AddDbContext<AppDb>(opts => opts.UseSqlServer(sqlCs));

services.AddHttpClient<IStripeClient, StripeClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Stripe:BaseUrl"]
        ?? throw new InvalidOperationException("Stripe:BaseUrl missing."));
});
services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
// Stripe:SecretKey arrives from Aspire parameters / Key Vault — never hardcoded.
```

Secret sources, in order of preference:

1. **Aspire parameters** with `secret: true` for dev (stored in user secrets, never in `appsettings.json`).
2. **Azure Key Vault** for production, accessed via managed identity.
3. **`@secure()`-marked Bicep parameters** with no defaults — values flow in from Key Vault references or pipeline variables.
4. **User secrets** (`dotnet user-secrets`) for local dev convenience.

## What is NOT a secret (but still doesn't belong hardcoded)

- Public base URLs of dependencies (Stripe API host, Auth0 tenant) — still configured, never hardcoded, so dev/staging/prod can differ.
- Resource names that the AppHost owns — read via `GetConnectionString` / service discovery.
- Feature flags — read via configuration.

## Enforcement

- **On sight, inside a file you're editing:** move the value to configuration. If a value is genuinely missing from configuration, STOP and report — do not invent a default secret.
- **Secret scanners:** GitHub secret scanning, `gitleaks`, or equivalent must run on every PR.
- **Quick scan:**

  ```bash
  grep -rE "Password=|Pwd=|sk_live_|sk_test_|Bearer [A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}" src/ infrastructure/
  ```

  any match is a blocking finding.
