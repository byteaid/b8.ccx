#:property PublishAot=false
#:package Azure.Identity@1.13.1
#:package Azure.ResourceManager@1.13.1
#:package Azure.ResourceManager.CostManagement@1.0.0
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var subscriptionOption = new Option<string?>("--subscription") { Description = "Subscription id. Falls back to resources.json or subscriptions.json.", HelpName = "ID" };
var startOption = new Option<DateTimeOffset>("--start") { Required = true, Description = "Period start (ISO-8601).", HelpName = "DATETIME" };
var endOption = new Option<DateTimeOffset>("--end") { Required = true, Description = "Period end (ISO-8601).", HelpName = "DATETIME" };
var granularityOption = new Option<string>("--granularity") { Description = "Daily | Monthly.", DefaultValueFactory = _ => "Daily" };
var forceOption = new Option<bool>("--force") { Description = "Overwrite costs.json." };

granularityOption.AcceptOnlyFromAmong("Daily", "Monthly");

var rootCommand = new RootCommand("Stage 3 — fetch Azure costs grouped by resource id and charge type.")
{
    stageDirOption, subscriptionOption, startOption, endOption, granularityOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var subscriptionArg = parseResult.GetValue(subscriptionOption);
    var start = parseResult.GetValue(startOption);
    var end = parseResult.GetValue(endOption);
    var granularity = parseResult.GetValue(granularityOption)!;
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "costs.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-costs] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var subscriptionId = subscriptionArg ?? await ResolveSubscription(stageDir.FullName, ct);
    if (subscriptionId is null)
    {
        await Console.Error.WriteLineAsync("[fetch-costs] No subscription resolvable. Pass --subscription or run earlier stages first.");
        return 2;
    }

    TokenCredential credential;
    try { credential = new DefaultAzureCredential(); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-costs] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    var scope = $"/subscriptions/{subscriptionId}";
    var monthly = granularity.Equals("Monthly", StringComparison.OrdinalIgnoreCase);

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var costs = new List<object>();
    var currentStart = start;

    // The Cost Management query API caps a single response page at ~15,000 rows and
    // surfaces the remainder via `nextLink`. We chunk the window into <=30-day slices
    // (which keeps each first page small) AND follow `nextLink` to completion so no
    // day is ever silently dropped.
    while (currentStart < end)
    {
        var currentEnd = currentStart.AddDays(30);
        if (currentEnd > end) currentEnd = end;

        // Cost Management treats the time period as an inclusive day range; subtract one
        // tick from the exclusive chunk end so we don't double-count the boundary day in
        // the next chunk.
        var inclusiveEnd = currentEnd.AddTicks(-1);
        if (inclusiveEnd < currentStart) inclusiveEnd = currentStart;

        var body = BuildQueryBody(monthly, currentStart.UtcDateTime, inclusiveEnd.UtcDateTime);
        var nextLink = $"https://management.azure.com{scope}/providers/Microsoft.CostManagement/query?api-version=2023-11-01";
        var pageBody = body;
        var chunkRows = 0;
        var pageIndex = 0;

        while (nextLink is not null)
        {
            JsonDocument? doc;
            try
            {
                doc = await PostQueryAsync(http, credential, nextLink, pageBody, ct);
            }
            catch (HttpRequestException ex) when (IsThrottled(ex))
            {
                await Console.Error.WriteLineAsync("[fetch-costs] 429 throttling, sleeping 10s.");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[fetch-costs] chunk {currentStart:yyyy-MM-dd}..{currentEnd:yyyy-MM-dd} page {pageIndex} failed: {ex.Message}");
                break;
            }

            using (doc)
            {
                var props = doc.RootElement.GetProperty("properties");
                var columnIndex = MapColumns(props.GetProperty("columns"));

                foreach (var row in props.GetProperty("rows").EnumerateArray())
                {
                    var (cloudId, day, value, chargeType) = ParseRow(row, columnIndex, monthly, currentStart);
                    costs.Add(new
                    {
                        cloudId,
                        day,
                        value,
                        chargeType,
                    });
                    chunkRows++;
                }

                // The Cost Management query API paginates via a `nextLink` URL that carries a
                // $skiptoken; each subsequent page is fetched by POSTing the SAME query body
                // to that URL (a GET returns HTTP 405). The field may be present-but-empty on
                // the last page, so treat empty/whitespace as "no more pages".
                var rawNext = props.TryGetProperty("nextLink", out var nl) && nl.ValueKind == JsonValueKind.String
                    ? nl.GetString()
                    : null;
                nextLink = string.IsNullOrWhiteSpace(rawNext) ? null : rawNext;
                pageBody = body;
                pageIndex++;
            }
        }

        await Console.Error.WriteLineAsync(
            $"[fetch-costs] chunk {currentStart:yyyy-MM-dd}..{currentEnd:yyyy-MM-dd}: {chunkRows} rows across {pageIndex} page(s).");

        currentStart = currentEnd;
    }

    var payload = new
    {
        subscriptionId,
        start = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        end = end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        granularity,
        costs,
    };

    await WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Json.Opts), ct);
    await Console.Error.WriteLineAsync($"[fetch-costs] wrote {outputPath} ({costs.Count} cost rows)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

// Builds the Cost Management `query` request body: ResourceId + ChargeType grouping,
// PreTaxCost sum aggregation, custom timeframe. Mirrors the SDK QueryDefinition the
// previous version sent, but as raw JSON so we can follow `nextLink` ourselves.
static string BuildQueryBody(bool monthly, DateTime fromUtc, DateTime toUtc)
{
    var payload = new
    {
        type = "ActualCost",
        timeframe = "Custom",
        timePeriod = new
        {
            from = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            to = toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        },
        dataset = new
        {
            granularity = monthly ? "Monthly" : "Daily",
            aggregation = new
            {
                totalCost = new { name = "PreTaxCost", function = "Sum" },
            },
            grouping = new[]
            {
                new { type = "Dimension", name = "ResourceId" },
                new { type = "Dimension", name = "ChargeType" },
            },
        },
    };
    return JsonSerializer.Serialize(payload);
}

static async Task<JsonDocument> PostQueryAsync(HttpClient http, TokenCredential credential, string url, string? body, CancellationToken ct)
{
    var token = await credential.GetTokenAsync(
        new TokenRequestContext(new[] { "https://management.azure.com/.default" }), ct);

    // Both the first page and every nextLink page are POSTs carrying the query body.
    using var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    if (body is not null)
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

    using var response = await http.SendAsync(request, ct);
    if ((int)response.StatusCode == 429)
        throw new HttpRequestException("429 Too Many Requests", null, response.StatusCode);

    var json = await response.Content.ReadAsStringAsync(ct);
    if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(json, 400)}");

    return JsonDocument.Parse(json);
}

static bool IsThrottled(HttpRequestException ex)
    => ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
       || (ex.Message?.Contains("429", StringComparison.Ordinal) ?? false);

static Dictionary<string, int> MapColumns(JsonElement columns)
{
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var i = 0;
    foreach (var c in columns.EnumerateArray())
    {
        var name = c.GetProperty("name").GetString();
        if (name is not null) map[name] = i;
        i++;
    }
    return map;
}

static (string cloudId, string day, double value, string chargeType) ParseRow(
    JsonElement row, Dictionary<string, int> columns, bool monthly, DateTimeOffset chunkStart)
{
    // Column order is stable for our query (Cost, UsageDate, ResourceId, ChargeType, Currency)
    // but resolve by name so a future column re-order can't silently corrupt the join.
    double value = GetDoubleAt(row, ColIndex(columns, 0, "PreTaxCost", "totalCost", "Cost"));
    var resId = GetStringAt(row, ColIndex(columns, 2, "ResourceId"));
    var chargeType = GetStringAt(row, ColIndex(columns, 3, "ChargeType")) ?? "Usage";

    string day;
    if (!monthly)
    {
        var dateRaw = GetRawAt(row, ColIndex(columns, 1, "UsageDate", "BillingMonth"));
        var dateInt = dateRaw.ValueKind == JsonValueKind.Number ? dateRaw.GetInt32() : 0;
        day = dateInt > 0
            ? new DateOnly(dateInt / 10000, (dateInt % 10000) / 100, dateInt % 100)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(chunkStart.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    else
    {
        day = DateOnly.FromDateTime(chunkStart.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    return (resId ?? "Unassigned", day, value, chargeType);
}

static int ColIndex(Dictionary<string, int> columns, int fallback, params string[] names)
{
    foreach (var n in names)
        if (columns.TryGetValue(n, out var idx)) return idx;
    return fallback;
}

static double GetDoubleAt(JsonElement row, int idx)
{
    if (idx < 0 || idx >= row.GetArrayLength()) return 0;
    var el = row[idx];
    return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0;
}

static string? GetStringAt(JsonElement row, int idx)
{
    if (idx < 0 || idx >= row.GetArrayLength()) return null;
    var el = row[idx];
    return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
}

static JsonElement GetRawAt(JsonElement row, int idx)
    => idx >= 0 && idx < row.GetArrayLength() ? row[idx] : default;

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

static async Task<string?> ResolveSubscription(string stageDir, CancellationToken ct)
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

static async Task WriteAtomic(string path, string content, CancellationToken ct)
{
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, content, ct);
    File.Move(tmp, path, overwrite: true);
}

static class Json
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
