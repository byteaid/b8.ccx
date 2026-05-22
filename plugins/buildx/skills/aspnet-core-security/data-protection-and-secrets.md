# Data Protection and Secrets

Data Protection consumer API, key persistence, multi-instance, Azure Blob + Key Vault, app isolation caveats, password hashing, secrets management.

Use cases: auth cookies, antiforgery tokens, Identity tokens, `MapIdentityApi`/`AddBearerToken` opaque tokens, TempData cookies.

## Consumer API

```csharp
public sealed class UrlSigner(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("MyApp.UrlSigner.v1");
    public string Sign(string payload)         => _protector.Protect(payload);
    public string Verify(string sealedPayload) => _protector.Unprotect(sealedPayload);
}

var tlp = _protector.ToTimeLimitedDataProtector();
var token = tlp.Protect("data", lifetime: TimeSpan.FromMinutes(10));
```

Purpose strings make protectors **non-interchangeable**. Always include a stable identifier (component name + version). Never use user input.

## Default key location

| Host | Key location | Encryption at rest |
|---|---|---|
| Kestrel on Windows (user profile) | `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys\` | DPAPI (current user) |
| Windows Service / shared profile | `%PROGRAMDATA%\Microsoft\AspNetCore\DataProtection-Keys\` | DPAPI (machine) |
| Azure App Service | App Service-managed | App Service-managed |
| Linux / Docker | `~/.aspnet/DataProtection-Keys/` | **None by default** — must configure |

Default key lifetime: **90 days**, automatic rotation.

## Multi-instance config

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("MyApp")                                 // share across instances
    .SetDefaultKeyLifetime(TimeSpan.FromDays(30))
    .PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
    .ProtectKeysWithAzureKeyVault(new Uri(keyId), credential);
```

Persistence providers: `PersistKeysToFileSystem(DirectoryInfo)`, `PersistKeysToDbContext<T>()` (where `T : IDataProtectionKeyContext`), `PersistKeysToAzureBlobStorage`, `PersistKeysToStackExchangeRedis`, `PersistKeysToAWSSystemsManager`.

Encryption at rest: `ProtectKeysWithDpapi()` / `ProtectKeysWithDpapiNG()` (Windows), `ProtectKeysWithCertificate(thumbprint | X509Certificate2)` (cross-platform; rotate via `UnprotectKeysWithAnyCertificate`), `ProtectKeysWithAzureKeyVault(Uri keyId, TokenCredential)`.

## Azure: Blob + Key Vault canonical config

```csharp
TokenCredential credential = builder.Environment.IsProduction()
    ? new ManagedIdentityCredential("{ManagedIdentityClientId}")
    : new DefaultAzureCredential();

builder.Services.AddDataProtection()
    .SetApplicationName("MyApp")
    .PersistKeysToAzureBlobStorage(new Uri("https://acct.blob.core.windows.net/keys/keys.xml"), credential)
    .ProtectKeysWithAzureKeyVault(new Uri("https://kv.vault.azure.net/keys/data-protection"), credential);
```

Use a **versionless** Key Vault key identifier so rotation does not break old payloads. Retain expired Key Vault key versions; never delete. Use modern packages `Azure.Extensions.AspNetCore.DataProtection.Blobs` / `.Keys`; the older `Microsoft.AspNetCore.DataProtection.Azure*` are **deprecated**.

## App isolation caveat

Without `SetApplicationName`, Data Protection isolates apps by content root path. **`WebApplicationBuilder` (.NET 6+) normalizes the content root path with a trailing `\` or `/`**, while older hosts don't — set `SetApplicationName` explicitly when migrating:

```csharp
var trimmed = builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar);
builder.Services.AddDataProtection().SetApplicationName(trimmed);
```

App isolation is **NOT a security boundary** — apps in the same key ring can read each other's payloads.

## Redis caveat

Only Redis with **persistence enabled** (RDB/AOF, or Azure Cache Premium tier with persistence). Without persistence, key-ring loss invalidates all sessions on restart.

## Password hashing (raw)

```csharp
byte[] salt = RandomNumberGenerator.GetBytes(128 / 8);
string hash = Convert.ToBase64String(
    KeyDerivation.Pbkdf2(password, salt,
        prf: KeyDerivationPrf.HMACSHA256,
        iterationCount: 100_000,
        numBytesRequested: 256 / 8));
```

## Secrets management

User Secrets (dev only):

```bash
dotnet user-secrets init / set "Movies:ServiceApiKey" "12345" / list / clear
```

Storage: `%APPDATA%\Microsoft\UserSecrets\<Id>\secrets.json` (Win) or `~/.microsoft/usersecrets/<Id>/secrets.json` (Linux/macOS). Auto-loaded by `WebApplication.CreateBuilder(args)` when `EnvironmentName == Development`. **Not encrypted.**

Production: Azure Key Vault.

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential(),
    new AzureKeyVaultConfigurationOptions { ReloadInterval = TimeSpan.FromMinutes(5) });
```

Grant App Service Managed Identity the **Key Vault Secrets User** role.

Env vars: `__` (double underscore) auto-replaced with `:` (Bash doesn't support `:`). Generally unencrypted — not for prod secrets.
