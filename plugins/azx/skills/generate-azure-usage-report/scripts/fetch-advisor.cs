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
// Stage 6 — fetch Azure Advisor recommendations for the subscription.
// Advisor is the native "current best practices" engine: its Cost /
// HighAvailability / Security / OperationalExcellence / Performance
// recommendations ground the report's recommendation prose so advice is
// aligned with Microsoft's own guidance, not invented.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var subscriptionOption = new Option<string?>("--subscription") { Description = "Subscription id. Falls back to resources.json or subscriptions.json.", HelpName = "ID" };
var forceOption = new Option<bool>("--force") { Description = "Overwrite advisor.json." };

var rootCommand = new RootCommand("Stage 6 — fetch Azure Advisor recommendations (all categories).")
{
    stageDirOption, subscriptionOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var subscriptionArg = parseResult.GetValue(subscriptionOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "advisor.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-advisor] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var subscriptionId = subscriptionArg ?? await Arm.ResolveSubscription(stageDir.FullName, ct);
    if (subscriptionId is null)
    {
        await Console.Error.WriteLineAsync("[fetch-advisor] No subscription resolvable. Pass --subscription or run earlier stages first.");
        return 2;
    }

    TokenCredential credential;
    try { credential = new DefaultAzureCredential(); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-advisor] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Advisor/recommendations?api-version=2023-01-01&$top=1000";

    var recommendations = new List<object>();
    try
    {
        await foreach (var item in Arm.GetPagedAsync(http, credential, url, "fetch-advisor", ct))
        {
            if (!item.TryGetProperty("properties", out var p)) continue;
            var shortDesc = p.TryGetProperty("shortDescription", out var sd) ? sd : default;
            // Advisor cost recommendations surface estimated savings in extendedProperties.
            string? savings = null, savingsCurrency = null;
            if (p.TryGetProperty("extendedProperties", out var xp) && xp.ValueKind == JsonValueKind.Object)
            {
                savings = Arm.Str(xp, "annualSavingsAmount") ?? Arm.Str(xp, "savingsAmount");
                savingsCurrency = Arm.Str(xp, "savingsCurrency");
            }
            recommendations.Add(new
            {
                category = Arm.Str(p, "category"),
                impact = Arm.Str(p, "impact"),
                impactedField = Arm.Str(p, "impactedField"),
                impactedValue = Arm.Str(p, "impactedValue"),
                problem = Arm.Str(shortDesc, "problem"),
                solution = Arm.Str(shortDesc, "solution"),
                resourceId = p.TryGetProperty("resourceMetadata", out var rm) ? Arm.Str(rm, "resourceId") : null,
                lastUpdated = Arm.Str(p, "lastUpdated"),
                annualSavingsAmount = savings,
                savingsCurrency,
            });
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-advisor] Failed to enumerate recommendations: {ex.Message}");
        return 1;
    }

    var payload = new
    {
        subscriptionId,
        generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        recommendations,
    };

    await Arm.WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Arm.JsonOpts), ct);
    await Console.Error.WriteLineAsync($"[fetch-advisor] wrote {outputPath} ({recommendations.Count} recommendations)");
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

    // GET an ARM collection, following value[] pages via nextLink, with bounded 429 retry.
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
