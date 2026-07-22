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
// Stage 7 — fetch the Microsoft Defender for Cloud security posture:
// secure score + unhealthy assessments, the SQL server auditing state
// (auditingSettings/default per server enumerated from resources.json), and
// the per-site network exposure summary (config/web: access restrictions +
// publicNetworkAccess) that feeds the WAF-coverage check in the report.
// Degrades gracefully (present:false) when Defender is not enabled on the
// subscription — the report's Seguridad section then rests on Advisor
// Security recommendations + own checks only.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var subscriptionOption = new Option<string?>("--subscription") { Description = "Subscription id. Falls back to resources.json or subscriptions.json.", HelpName = "ID" };
var maxItemsOption = new Option<int>("--max-items") { Description = "Cap on unhealthy assessments kept in the output.", DefaultValueFactory = _ => 500 };
var forceOption = new Option<bool>("--force") { Description = "Overwrite security.json." };

var rootCommand = new RootCommand("Stage 7 — fetch Defender for Cloud secure score and unhealthy assessments.")
{
    stageDirOption, subscriptionOption, maxItemsOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var subscriptionArg = parseResult.GetValue(subscriptionOption);
    var maxItems = parseResult.GetValue(maxItemsOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "security.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-security] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var subscriptionId = subscriptionArg ?? await Arm.ResolveSubscription(stageDir.FullName, ct);
    if (subscriptionId is null)
    {
        await Console.Error.WriteLineAsync("[fetch-security] No subscription resolvable. Pass --subscription or run earlier stages first.");
        return 2;
    }

    TokenCredential credential;
    try { credential = new DefaultAzureCredential(); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-security] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var baseUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Security";

    // --- secure score (the built-in "ascScore" initiative) ---
    object? secureScore = null;
    try
    {
        await foreach (var item in Arm.GetPagedAsync(http, credential, $"{baseUrl}/secureScores?api-version=2020-01-01", "fetch-security", ct))
        {
            if (!item.TryGetProperty("properties", out var p) || !p.TryGetProperty("score", out var s)) continue;
            secureScore = new
            {
                current = Num(s, "current"),
                max = Num(s, "max"),
                percentage = Math.Round(Num(s, "percentage") * 100, 1),
            };
            break; // ascScore is the only initiative-level score we need
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-security] secure score unavailable: {ex.Message}");
    }

    // --- assessments (all of them for counts; keep only Unhealthy in the payload) ---
    var total = 0; var unhealthy = 0;
    var bySeverity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["High"] = 0, ["Medium"] = 0, ["Low"] = 0 };
    var unhealthyItems = new List<object>();
    var assessmentsOk = true;
    try
    {
        var url = $"{baseUrl}/assessments?api-version=2021-06-01&$expand=metadata";
        await foreach (var item in Arm.GetPagedAsync(http, credential, url, "fetch-security", ct))
        {
            if (!item.TryGetProperty("properties", out var p)) continue;
            total++;
            var statusCode = p.TryGetProperty("status", out var st) ? Arm.Str(st, "code") : null;
            if (!string.Equals(statusCode, "Unhealthy", StringComparison.OrdinalIgnoreCase)) continue;
            unhealthy++;

            var severity = p.TryGetProperty("metadata", out var md) ? Arm.Str(md, "severity") ?? "Unknown" : "Unknown";
            bySeverity[severity] = bySeverity.TryGetValue(severity, out var c) ? c + 1 : 1;

            if (unhealthyItems.Count < maxItems)
            {
                string? resourceId = null;
                if (p.TryGetProperty("resourceDetails", out var rd))
                    resourceId = Arm.Str(rd, "Id") ?? Arm.Str(rd, "id");
                unhealthyItems.Add(new
                {
                    displayName = Arm.Str(p, "displayName"),
                    severity,
                    resourceId,
                });
            }
        }
    }
    catch (Exception ex)
    {
        assessmentsOk = false;
        await Console.Error.WriteLineAsync($"[fetch-security] assessments unavailable: {ex.Message}");
    }

    var present = secureScore is not null || (assessmentsOk && total > 0);
    if (!present)
        await Console.Error.WriteLineAsync("[fetch-security] Defender for Cloud data unavailable; writing present:false (Seguridad section degrades to Advisor + own checks).");

    // --- SQL server auditing state (Reader-readable; servers from resources.json) ---
    var sqlAuditing = new List<object>();
    foreach (var (id, name) in await Arm.LoadByType(stageDir.FullName, "Microsoft.Sql/servers", ct))
    {
        try
        {
            using var doc = await Arm.GetWithRetryAsync(http, credential,
                $"https://management.azure.com{id}/auditingSettings/default?api-version=2021-11-01", "fetch-security", ct);
            var state = doc.RootElement.TryGetProperty("properties", out var p) ? Arm.Str(p, "state") : null;
            sqlAuditing.Add(new { serverId = id, name, state = state ?? "Unknown" });
        }
        catch (Exception ex)
        {
            sqlAuditing.Add(new { serverId = id, name, state = "Unknown" });
            await Console.Error.WriteLineAsync($"[fetch-security] SQL auditing {name}: unavailable: {ex.Message}");
        }
    }

    // --- Per-site network exposure: access restrictions + publicNetworkAccess
    //     (config/web, Reader-readable). `openToAll` = the direct endpoint
    //     accepts traffic from anywhere; the WAF-coverage verdict itself is
    //     decided in build-usage-report. ---
    var webAccessRestrictions = new List<object>();
    foreach (var (id, name) in await Arm.LoadByType(stageDir.FullName, "Microsoft.Web/sites", ct))
    {
        try
        {
            using var doc = await Arm.GetWithRetryAsync(http, credential,
                $"https://management.azure.com{id}/config/web?api-version=2024-04-01", "fetch-security", ct);
            if (!doc.RootElement.TryGetProperty("properties", out var p)) continue;
            var pna = Arm.Str(p, "publicNetworkAccess");
            var specific = 0;
            var denyAll = false;
            if (p.TryGetProperty("ipSecurityRestrictions", out var restr) && restr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in restr.EnumerateArray())
                {
                    var ip = Arm.Str(e, "ipAddress");
                    var isAny = ip is null || ip.Equals("Any", StringComparison.OrdinalIgnoreCase);
                    var deny = string.Equals(Arm.Str(e, "action"), "Deny", StringComparison.OrdinalIgnoreCase);
                    if (isAny && deny) denyAll = true;
                    else if (!isAny) specific++;
                }
            }
            var defaultDeny = string.Equals(Arm.Str(p, "ipSecurityRestrictionsDefaultAction"), "Deny", StringComparison.OrdinalIgnoreCase);
            var openToAll = !string.Equals(pna, "Disabled", StringComparison.OrdinalIgnoreCase)
                && !denyAll && !defaultDeny && specific == 0;
            webAccessRestrictions.Add(new
            {
                siteId = id,
                name,
                publicNetworkAccess = pna,
                restrictionCount = specific,
                denyAll,
                openToAll,
            });
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[fetch-security] access restrictions {name}: unavailable: {ex.Message}");
        }
    }

    // --- Database-server firewall rules (SQL + MySQL/PostgreSQL flexible):
    //     the 0.0.0.0-255.255.255.255 rule is the "any origin" marker the
    //     build stage looks for. ---
    var dbFirewallRules = new List<object>();
    var firewallTargets = new (string type, string api)[]
    {
        ("Microsoft.Sql/servers", "2021-11-01"),
        ("Microsoft.DBforMySQL/flexibleServers", "2023-06-30"),
        ("Microsoft.DBforPostgreSQL/flexibleServers", "2022-12-01"),
    };
    foreach (var (fwType, fwApi) in firewallTargets)
        foreach (var (id, name) in await Arm.LoadByType(stageDir.FullName, fwType, ct))
        {
            try
            {
                await foreach (var rule in Arm.GetPagedAsync(http, credential,
                    $"https://management.azure.com{id}/firewallRules?api-version={fwApi}", "fetch-security", ct))
                {
                    string? start = null, end = null;
                    if (rule.TryGetProperty("properties", out var rp))
                    {
                        start = Arm.Str(rp, "startIpAddress");
                        end = Arm.Str(rp, "endIpAddress");
                    }
                    dbFirewallRules.Add(new
                    {
                        serverId = id,
                        name = Arm.Str(rule, "name"),
                        startIpAddress = start,
                        endIpAddress = end,
                    });
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[fetch-security] firewall rules {name}: unavailable: {ex.Message}");
            }
        }

    var payload = new
    {
        subscriptionId,
        generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        present,
        secureScore,
        assessmentCounts = new { total, unhealthy, bySeverity },
        unhealthyAssessments = unhealthyItems,
        sqlAuditing,
        webAccessRestrictions,
        dbFirewallRules,
    };

    await Arm.WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Arm.JsonOpts), ct);
    await Console.Error.WriteLineAsync(
        $"[fetch-security] wrote {outputPath} (score={(secureScore is null ? "n/a" : "ok")}, {unhealthy}/{total} unhealthy assessments, " +
        $"{sqlAuditing.Count} SQL servers audited, {webAccessRestrictions.Count} site exposure summaries, " +
        $"{dbFirewallRules.Count} db firewall rules)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static double Num(JsonElement el, string prop) =>
    el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
        ? v.GetDouble() : 0;

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

    public static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // (id, name) pairs of one resource type from resources.json; empty when not staged.
    public static async Task<List<(string id, string name)>> LoadByType(string stageDir, string type, CancellationToken ct)
    {
        var list = new List<(string, string)>();
        var path = Path.Combine(stageDir, "resources.json");
        if (!File.Exists(path)) return list;
        using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("resources", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in arr.EnumerateArray())
            if (string.Equals(Str(r, "type"), type, StringComparison.OrdinalIgnoreCase)
                && Str(r, "id") is { } id)
                list.Add((id, Str(r, "name") ?? ""));
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
