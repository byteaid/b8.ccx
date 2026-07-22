#:property PublishAot=false
#:package Azure.Identity@1.13.1
#:package Azure.ResourceManager@1.13.1
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

// =========================================================================
// Stage 8 — fetch the resilience/availability surface: Recovery Services
// vaults + protected backup items (who HAS backup), App Service certificates
// (expiry dates), autoscale settings (which targets scale), SQL Database
// backup policies (PITR retention + long-term retention), storage blob-service
// properties (soft delete), top-level availability zones for the types whose
// zones are NOT inside `properties` (VMs, VMSS, Redis, App Gateway), per-site
// App Service backup configuration (POST config/backup/list — needs
// Contributor), and Key Vault certificate expiry for App Gateway listener
// certs (data-plane read; only the expiry is staged, never the secret value).
// The operator is ASSUMED to hold Contributor or higher — privileged checks
// are always ATTEMPTED first; when access is denied the affected rows degrade
// to a NotVerifiable marker instead of failing the stage. Everything
// derivable from staged ARM properties (zoneRedundant flags, storage
// replication SKU, embedded App Gateway certs) stays in build-usage-report.
// Each sub-fetch degrades independently: a failure leaves an empty list and
// a stderr note, never a failed stage.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var subscriptionOption = new Option<string?>("--subscription") { Description = "Subscription id. Falls back to resources.json or subscriptions.json.", HelpName = "ID" };
var forceOption = new Option<bool>("--force") { Description = "Overwrite resilience.json." };

var rootCommand = new RootCommand("Stage 8 — fetch backup coverage, certificate expiry, and autoscale settings.")
{
    stageDirOption, subscriptionOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var subscriptionArg = parseResult.GetValue(subscriptionOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "resilience.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-resilience] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var subscriptionId = subscriptionArg ?? await Arm.ResolveSubscription(stageDir.FullName, ct);
    if (subscriptionId is null)
    {
        await Console.Error.WriteLineAsync("[fetch-resilience] No subscription resolvable. Pass --subscription or run earlier stages first.");
        return 2;
    }

    TokenCredential credential;
    try { credential = new DefaultAzureCredential(); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-resilience] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var subUrl = $"https://management.azure.com/subscriptions/{subscriptionId}";

    // --- Recovery Services vaults + their protected items ---
    var vaults = new List<object>();
    var protectedItems = new List<object>();
    try
    {
        var vaultIds = new List<(string id, string name)>();
        await foreach (var v in Arm.GetPagedAsync(http, credential, $"{subUrl}/providers/Microsoft.RecoveryServices/vaults?api-version=2024-04-01", "fetch-resilience", ct))
        {
            var id = Arm.Str(v, "id") ?? "";
            var name = Arm.Str(v, "name") ?? "";
            if (id.Length == 0) continue;
            vaultIds.Add((id, name));
            vaults.Add(new { id, name });
        }
        foreach (var (vaultId, vaultName) in vaultIds)
        {
            try
            {
                var itemsUrl = $"https://management.azure.com{vaultId}/backupProtectedItems?api-version=2024-04-01";
                await foreach (var item in Arm.GetPagedAsync(http, credential, itemsUrl, "fetch-resilience", ct))
                {
                    if (!item.TryGetProperty("properties", out var p)) continue;
                    protectedItems.Add(new
                    {
                        vault = vaultName,
                        friendlyName = Arm.Str(p, "friendlyName"),
                        sourceResourceId = Arm.Str(p, "sourceResourceId") ?? Arm.Str(p, "virtualMachineId"),
                        protectionState = Arm.Str(p, "protectionState"),
                        lastBackupTime = Arm.Str(p, "lastRecoveryPoint") ?? Arm.Str(p, "lastBackupTime"),
                    });
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[fetch-resilience] vault {vaultName}: protected items unavailable: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-resilience] Recovery Services vaults unavailable: {ex.Message}");
    }

    // --- App Service certificates (managed + uploaded; expirationDate drives the expiry check) ---
    var webCertificates = new List<object>();
    try
    {
        await foreach (var c in Arm.GetPagedAsync(http, credential, $"{subUrl}/providers/Microsoft.Web/certificates?api-version=2024-04-01", "fetch-resilience", ct))
        {
            if (!c.TryGetProperty("properties", out var p)) continue;
            var hostNames = new List<string>();
            if (p.TryGetProperty("hostNames", out var hn) && hn.ValueKind == JsonValueKind.Array)
                foreach (var h in hn.EnumerateArray())
                    if (h.ValueKind == JsonValueKind.String) hostNames.Add(h.GetString()!);
            webCertificates.Add(new
            {
                name = Arm.Str(c, "name"),
                expirationDate = Arm.Str(p, "expirationDate"),
                issuer = Arm.Str(p, "issuer"),
                hostNames,
            });
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-resilience] Web certificates unavailable: {ex.Message}");
    }

    // --- Autoscale settings (which resources have autoscale wired, and its bounds) ---
    var autoscaleSettings = new List<object>();
    try
    {
        await foreach (var a in Arm.GetPagedAsync(http, credential, $"{subUrl}/providers/Microsoft.Insights/autoscaleSettings?api-version=2022-10-01", "fetch-resilience", ct))
        {
            if (!a.TryGetProperty("properties", out var p)) continue;
            string? minCap = null, maxCap = null;
            if (p.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array
                && profiles.GetArrayLength() > 0 && profiles[0].TryGetProperty("capacity", out var cap))
            {
                minCap = Arm.Str(cap, "minimum");
                maxCap = Arm.Str(cap, "maximum");
            }
            var enabled = p.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
            autoscaleSettings.Add(new
            {
                name = Arm.Str(a, "name"),
                targetResourceUri = Arm.Str(p, "targetResourceUri"),
                enabled,
                minCapacity = minCap,
                maxCapacity = maxCap,
            });
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-resilience] autoscale settings unavailable: {ex.Message}");
    }

    // --- Inventory-driven sub-fetches (need resources.json for the per-resource ids) ---
    var inventory = await Arm.LoadInventory(stageDir.FullName, ct);
    if (inventory.Count == 0)
        await Console.Error.WriteLineAsync("[fetch-resilience] resources.json not staged — skipping SQL backup policies and storage soft-delete checks.");

    // --- SQL Database backup policies: short-term PITR retention + long-term retention.
    //     PITR is always on; the report's gap check is a missing LTR policy. ---
    var sqlBackupPolicies = new List<object>();
    foreach (var (id, type, name) in inventory)
    {
        if (!type.Equals("Microsoft.Sql/servers/databases", StringComparison.OrdinalIgnoreCase)) continue;
        if (name.Equals("master", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            int? retentionDays = null;
            string? weekly = null, monthly = null, yearly = null;
            using (var doc = await Arm.GetWithRetryAsync(http, credential,
                $"https://management.azure.com{id}/backupShortTermRetentionPolicies/default?api-version=2021-11-01", "fetch-resilience", ct))
            {
                if (doc.RootElement.TryGetProperty("properties", out var p)
                    && p.TryGetProperty("retentionDays", out var rd) && rd.ValueKind == JsonValueKind.Number)
                    retentionDays = rd.GetInt32();
            }
            using (var doc = await Arm.GetWithRetryAsync(http, credential,
                $"https://management.azure.com{id}/backupLongTermRetentionPolicies/default?api-version=2021-11-01", "fetch-resilience", ct))
            {
                if (doc.RootElement.TryGetProperty("properties", out var p))
                {
                    weekly = Arm.Str(p, "weeklyRetention");
                    monthly = Arm.Str(p, "monthlyRetention");
                    yearly = Arm.Str(p, "yearlyRetention");
                }
            }
            sqlBackupPolicies.Add(new
            {
                databaseId = id,
                retentionDays,
                weeklyRetention = weekly,
                monthlyRetention = monthly,
                yearlyRetention = yearly,
            });
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[fetch-resilience] SQL backup policy {name}: unavailable: {ex.Message}");
        }
    }

    // --- Storage blob services: soft delete / versioning (the recoverability control). ---
    var storageBlobServices = new List<object>();
    foreach (var (id, type, name) in inventory)
    {
        if (!type.Equals("Microsoft.Storage/storageAccounts", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            using var doc = await Arm.GetWithRetryAsync(http, credential,
                $"https://management.azure.com{id}/blobServices/default?api-version=2023-01-01", "fetch-resilience", ct);
            if (!doc.RootElement.TryGetProperty("properties", out var p)) continue;
            bool SoftDelete(string prop, out int? days)
            {
                days = null;
                if (!p.TryGetProperty(prop, out var pol) || pol.ValueKind != JsonValueKind.Object) return false;
                var enabled = pol.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                if (enabled && pol.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number) days = d.GetInt32();
                return enabled;
            }
            var blobEnabled = SoftDelete("deleteRetentionPolicy", out var blobDays);
            var containerEnabled = SoftDelete("containerDeleteRetentionPolicy", out var containerDays);
            storageBlobServices.Add(new
            {
                accountId = id,
                blobSoftDeleteEnabled = blobEnabled,
                blobSoftDeleteDays = blobDays,
                containerSoftDeleteEnabled = containerEnabled,
                containerSoftDeleteDays = containerDays,
                versioningEnabled = p.TryGetProperty("isVersioningEnabled", out var vv) && vv.ValueKind == JsonValueKind.True,
            });
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[fetch-resilience] storage blob service {name}: unavailable: {ex.Message}");
        }
    }

    // --- Azure Files shares per storage account (management-plane listing).
    //     `build-usage-report` matches them against Recovery Services
    //     protectedItems (AzureStorage container) for the backup family. ---
    var fileShares = new List<object>();
    foreach (var (id, type, name) in inventory)
    {
        if (!type.Equals("Microsoft.Storage/storageAccounts", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            await foreach (var share in Arm.GetPagedAsync(http, credential,
                $"https://management.azure.com{id}/fileServices/default/shares?api-version=2023-01-01", "fetch-resilience", ct))
            {
                if (Arm.Str(share, "name") is { } shareName)
                    fileShares.Add(new { accountId = id, shareName });
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[fetch-resilience] file shares {name}: unavailable: {ex.Message}");
        }
    }

    // --- Availability zones for types whose `zones` array is top-level (not in
    //     properties, so discover-resources' GenericResource fetch drops it). ---
    var resourceZones = new List<object>();
    var zoneSources = new[]
    {
        $"{subUrl}/providers/Microsoft.Compute/virtualMachines?api-version=2024-03-01",
        $"{subUrl}/providers/Microsoft.Compute/virtualMachineScaleSets?api-version=2024-03-01",
        $"{subUrl}/providers/Microsoft.Cache/redis?api-version=2023-08-01",
        $"{subUrl}/providers/Microsoft.Network/applicationGateways?api-version=2023-11-01",
    };
    foreach (var sourceUrl in zoneSources)
    {
        try
        {
            await foreach (var item in Arm.GetPagedAsync(http, credential, sourceUrl, "fetch-resilience", ct))
            {
                var id = Arm.Str(item, "id");
                if (id is null) continue;
                var zones = new List<string>();
                if (item.TryGetProperty("zones", out var za) && za.ValueKind == JsonValueKind.Array)
                    foreach (var z in za.EnumerateArray())
                        if (z.ValueKind == JsonValueKind.String) zones.Add(z.GetString()!);
                string? availabilitySetId = null;
                if (item.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object
                    && p.TryGetProperty("availabilitySet", out var av) && av.ValueKind == JsonValueKind.Object)
                    availabilitySetId = Arm.Str(av, "id");
                resourceZones.Add(new { id, zones, availabilitySetId });
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[fetch-resilience] zone listing unavailable ({sourceUrl[..Math.Min(sourceUrl.Length, 100)]}): {ex.Message}");
        }
    }

    // --- App Service backup configuration (per site, POST config/backup/list).
    //     Needs Contributor (Microsoft.Web/sites/config/list/action): 200 →
    //     Configured, 404 → NotConfigured, 401/403 → NotVerifiable. ---
    var webAppBackups = new List<object>();
    var backupUnverifiable = 0;
    foreach (var (id, type, name) in inventory)
    {
        if (!type.Equals("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            var (status, doc) = await Arm.RequestAsync(http, credential, HttpMethod.Post,
                $"https://management.azure.com{id}/config/backup/list?api-version=2024-04-01",
                "https://management.azure.com/.default", "fetch-resilience", ct);
            using var _ = doc;
            if (status == 200 && doc is not null && doc.RootElement.TryGetProperty("properties", out var p))
            {
                var enabled = p.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                int? retentionDays = null;
                string? frequency = null;
                if (p.TryGetProperty("backupSchedule", out var sched) && sched.ValueKind == JsonValueKind.Object)
                {
                    if (sched.TryGetProperty("retentionPeriodInDays", out var rp) && rp.ValueKind == JsonValueKind.Number)
                        retentionDays = rp.GetInt32();
                    if (sched.TryGetProperty("frequencyInterval", out var fi) && fi.ValueKind == JsonValueKind.Number)
                        frequency = $"{fi.GetRawText()} {Arm.Str(sched, "frequencyUnit") ?? ""}".Trim();
                }
                webAppBackups.Add(new
                {
                    siteId = id,
                    status = enabled ? "Configured" : "NotConfigured",
                    retentionDays,
                    frequency,
                });
            }
            else if (status == 404)
            {
                webAppBackups.Add(new { siteId = id, status = "NotConfigured" });
            }
            else
            {
                webAppBackups.Add(new { siteId = id, status = "NotVerifiable" });
                backupUnverifiable++;
                if (backupUnverifiable == 1)
                    await Console.Error.WriteLineAsync(
                        $"[fetch-resilience] App Service backup config HTTP {status} on {name} — needs Contributor; affected sites staged as NotVerifiable.");
            }
        }
        catch (Exception ex)
        {
            webAppBackups.Add(new { siteId = id, status = "NotVerifiable" });
            await Console.Error.WriteLineAsync($"[fetch-resilience] App Service backup config {name}: {ex.Message}");
        }
    }

    // --- Key Vault certificate expiry for App Gateway keyVaultSecretId refs.
    //     Data-plane read (Key Vault Secrets User / access policy); only
    //     attributes.exp is staged — the secret VALUE is never persisted. ---
    var keyVaultCertificates = new List<object>();
    foreach (var secretId in await Arm.LoadAppGatewayKvSecretIds(stageDir.FullName, ct))
    {
        try
        {
            var url = secretId.Contains('?') ? secretId : $"{secretId}?api-version=7.4";
            var (status, doc) = await Arm.RequestAsync(http, credential, HttpMethod.Get, url,
                "https://vault.azure.net/.default", "fetch-resilience", ct);
            using var _ = doc;
            string? expiration = null;
            if (status == 200 && doc is not null
                && doc.RootElement.TryGetProperty("attributes", out var attrs)
                && attrs.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
                expiration = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            if (expiration is null)
                await Console.Error.WriteLineAsync(
                    $"[fetch-resilience] Key Vault cert HTTP {status} on {secretId} — needs data-plane read; staged without expiry (No verificable).");
            keyVaultCertificates.Add(new { secretId, expirationDate = expiration });
        }
        catch (Exception ex)
        {
            keyVaultCertificates.Add(new { secretId, expirationDate = (string?)null });
            await Console.Error.WriteLineAsync($"[fetch-resilience] Key Vault cert {secretId}: {ex.Message}");
        }
    }

    var payload = new
    {
        subscriptionId,
        generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        vaults,
        protectedItems,
        webCertificates,
        autoscaleSettings,
        sqlBackupPolicies,
        storageBlobServices,
        fileShares,
        resourceZones,
        webAppBackups,
        keyVaultCertificates,
    };

    await Arm.WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Arm.JsonOpts), ct);
    await Console.Error.WriteLineAsync(
        $"[fetch-resilience] wrote {outputPath} ({vaults.Count} vaults, {protectedItems.Count} protected items, " +
        $"{webCertificates.Count} certificates, {autoscaleSettings.Count} autoscale settings, " +
        $"{sqlBackupPolicies.Count} SQL backup policies, {storageBlobServices.Count} storage blob services, " +
        $"{resourceZones.Count} zone entries, {fileShares.Count} file shares, " +
        $"{webAppBackups.Count} web app backup configs, {keyVaultCertificates.Count} Key Vault certs)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

// Shared ARM REST helpers (self-contained per file-based-app convention).
static class Arm
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static async IAsyncEnumerable<JsonElement> GetPagedAsync(
        HttpClient http, TokenCredential credential, string url, string stage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? next = url;
        while (next is not null)
        {
            using var doc = await GetWithRetryAsync(http, credential, next, stage, ct);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                    yield return item.Clone();
            }
            next = doc.RootElement.TryGetProperty("nextLink", out var nl) && nl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nl.GetString()) ? nl.GetString() : null;
        }
    }

    public static async Task<JsonDocument> GetWithRetryAsync(
        HttpClient http, TokenCredential credential, string url, string stage, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode == 429 && attempt < maxAttempts)
            {
                var backoff = TimeSpan.FromSeconds(10 * Math.Pow(2, attempt - 1));
                await Console.Error.WriteLineAsync($"[{stage}] 429, backing off {backoff.TotalSeconds:0}s (attempt {attempt}/{maxAttempts}).");
                await Task.Delay(backoff, ct);
                continue;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} on {url[..Math.Min(url.Length, 120)]}: {Truncate(json, 300)}");
            return JsonDocument.Parse(json);
        }
    }

    // Generic request against an arbitrary scope (ARM or Key Vault data plane).
    // Returns the status code instead of throwing so callers can branch on
    // 403 (permission missing) vs 404 (feature not configured).
    public static async Task<(int status, JsonDocument? doc)> RequestAsync(
        HttpClient http, TokenCredential credential, HttpMethod method, string url, string scope, string stage, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            if (method == HttpMethod.Post)
                request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode == 429 && attempt < maxAttempts)
            {
                var backoff = TimeSpan.FromSeconds(10 * Math.Pow(2, attempt - 1));
                await Console.Error.WriteLineAsync($"[{stage}] 429, backing off {backoff.TotalSeconds:0}s (attempt {attempt}/{maxAttempts}).");
                await Task.Delay(backoff, ct);
                continue;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(json); } catch { /* non-JSON error body */ }
            return ((int)response.StatusCode, doc);
        }
    }

    // keyVaultSecretId refs from the staged App Gateway sslCertificates.
    public static async Task<List<string>> LoadAppGatewayKvSecretIds(string stageDir, CancellationToken ct)
    {
        var ids = new List<string>();
        var path = Path.Combine(stageDir, "resources.json");
        if (!File.Exists(path)) return ids;
        using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("resources", out var arr) || arr.ValueKind != JsonValueKind.Array) return ids;
        foreach (var r in arr.EnumerateArray())
        {
            if (!string.Equals(Str(r, "type"), "Microsoft.Network/applicationGateways", StringComparison.OrdinalIgnoreCase)) continue;
            if (!r.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object) continue;
            if (!props.TryGetProperty("sslCertificates", out var certs) || certs.ValueKind != JsonValueKind.Array) continue;
            foreach (var c in certs.EnumerateArray())
                if (c.TryGetProperty("properties", out var cp) && Str(cp, "keyVaultSecretId") is { Length: > 0 } sid
                    && !ids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                    ids.Add(sid);
        }
        return ids;
    }

    public static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // (id, type, name) triples from resources.json; empty when not staged.
    public static async Task<List<(string id, string type, string name)>> LoadInventory(string stageDir, CancellationToken ct)
    {
        var list = new List<(string, string, string)>();
        var path = Path.Combine(stageDir, "resources.json");
        if (!File.Exists(path)) return list;
        using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("resources", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in arr.EnumerateArray())
        {
            if (Str(r, "id") is not { } id) continue;
            list.Add((id, Str(r, "type") ?? "", Str(r, "name") ?? ""));
        }
        return list;
    }

    public static async Task<string?> ResolveSubscription(string stageDir, CancellationToken ct)
    {
        foreach (var name in new[] { "resources.json", "subscriptions.json" })
        {
            var path = Path.Combine(stageDir, name);
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("subscriptionId", out var single))
                return single.GetString();
            if (doc.RootElement.TryGetProperty("subscriptions", out var arr) && arr.GetArrayLength() > 0)
                return arr[0].GetProperty("id").GetString();
        }
        return null;
    }

    public static async Task WriteAtomic(string path, string content, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, ct);
        File.Move(tmp, path, overwrite: true);
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
