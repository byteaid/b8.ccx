#:property PublishAot=false
#:package Azure.Identity@1.13.1
#:package Azure.Monitor.Query@1.5.0
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var startOption = new Option<DateTimeOffset>("--start") { Required = true, Description = "Period start.", HelpName = "DATETIME" };
var endOption = new Option<DateTimeOffset>("--end") { Required = true, Description = "Period end.", HelpName = "DATETIME" };
var rulesOption = new Option<FileInfo?>("--rules") { Description = "Path to metric-rules.json. Falls back to {stage-dir}/metric-rules.json or built-in defaults.", HelpName = "PATH" };
var maxParallelOption = new Option<int>("--max-parallel") { Description = "Max concurrent queries.", DefaultValueFactory = _ => 4 };
var forceOption = new Option<bool>("--force") { Description = "Overwrite metrics.json." };

var rootCommand = new RootCommand("Stage 4 — query Azure Monitor metrics for resources matching the supplied rules.")
{
    stageDirOption, startOption, endOption, rulesOption, maxParallelOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var start = parseResult.GetValue(startOption);
    var end = parseResult.GetValue(endOption);
    var rulesFile = parseResult.GetValue(rulesOption);
    var maxParallel = parseResult.GetValue(maxParallelOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "metrics.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-metrics] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var resourcesPath = Path.Combine(stageDir.FullName, "resources.json");
    if (!File.Exists(resourcesPath))
    {
        await Console.Error.WriteLineAsync("[fetch-metrics] resources.json missing. Run discover-resources first.");
        return 2;
    }

    var rules = await LoadRules(rulesFile, stageDir.FullName, ct);
    if (rules.Count == 0)
    {
        await Console.Error.WriteLineAsync("[fetch-metrics] No metric rules supplied. Provide --rules or {stage-dir}/metric-rules.json.");
        return 2;
    }

    var (subscriptionId, resources) = await LoadResources(resourcesPath, ct);

    MetricsQueryClient metricsClient;
    try { metricsClient = new MetricsQueryClient(new DefaultAzureCredential()); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-metrics] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    var queue = new Queue<(string cloudId, MetricRule rule)>();
    foreach (var rule in rules)
    {
        var match = resources
            .Where(r => string.Equals(r.TypeKey, rule.CloudType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var r in match) queue.Enqueue((r.CloudId, rule));
    }

    await Console.Error.WriteLineAsync(
        $"[fetch-metrics] {queue.Count} (resource, rule) series to query over {start:yyyy-MM-dd}..{end:yyyy-MM-dd} with max-parallel={maxParallel}.");

    using var sem = new SemaphoreSlim(maxParallel);
    var tasks = new List<Task>();
    var collected = new List<object>();
    var collectedLock = new object();

    while (queue.Count > 0)
    {
        var (cloudId, rule) = queue.Dequeue();
        await sem.WaitAsync(ct);
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                var granularity = TimeSpan.Parse(rule.Granularity, CultureInfo.InvariantCulture);
                var values = new List<object>();

                // Azure Monitor caps datapoints per query response; a 1-minute granularity
                // over a full month (≈44,640 points) is silently truncated. Split the window
                // into 1-day sub-queries per (resource, rule) and concatenate the values so
                // the full month is preserved.
                foreach (var (chunkStart, chunkEnd) in DayChunks(start, end))
                {
                    var ts = await QueryChunkWithRetryAsync(metricsClient, cloudId, rule, granularity, chunkStart, chunkEnd, ct);
                    if (ts is null) continue;

                    foreach (var v in ts.Values)
                    {
                        values.Add(new
                        {
                            timestamp = v.TimeStamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                            value = ExtractValue(v, rule.Aggregation),
                        });
                    }
                }

                lock (collectedLock)
                {
                    collected.Add(new
                    {
                        cloudId,
                        name = rule.MetricName,
                        aggregation = rule.Aggregation,
                        granularity = rule.Granularity,
                        values,
                    });
                }

                await Console.Error.WriteLineAsync($"[fetch-metrics] {ShortId(cloudId)} {rule.MetricName}: {values.Count} samples.");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                await Console.Error.WriteLineAsync($"[fetch-metrics] {ShortId(cloudId)} {rule.MetricName}: 429 throttling; series skipped (re-run to backfill).");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[fetch-metrics] {ShortId(cloudId)} {rule.MetricName}: {ex.Message}");
            }
            finally
            {
                sem.Release();
            }
        }, ct));
    }

    await Task.WhenAll(tasks);

    var payload = new
    {
        subscriptionId,
        start = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        end = end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        metrics = collected,
    };

    await WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Json.Opts), ct);
    await Console.Error.WriteLineAsync($"[fetch-metrics] wrote {outputPath} ({collected.Count} metric series)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

// Split [start, end) into 1-day sub-windows. The last window ends exactly at `end`.
static IEnumerable<(DateTimeOffset start, DateTimeOffset end)> DayChunks(DateTimeOffset start, DateTimeOffset end)
{
    var cursor = start;
    while (cursor < end)
    {
        var next = cursor.AddDays(1);
        if (next > end) next = end;
        yield return (cursor, next);
        cursor = next;
    }
}

// One day-chunk query with bounded 429 retry/backoff (3 attempts, 10s/20s/40s).
static async Task<MetricTimeSeriesElement?> QueryChunkWithRetryAsync(
    MetricsQueryClient client, string cloudId, MetricRule rule, TimeSpan granularity,
    DateTimeOffset chunkStart, DateTimeOffset chunkEnd, CancellationToken ct)
{
    const int maxAttempts = 3;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var options = new MetricsQueryOptions
            {
                TimeRange = new QueryTimeRange(chunkStart, chunkEnd),
                Granularity = granularity,
            };
            options.Aggregations.Add(MapAggregation(rule.Aggregation));

            var response = await client.QueryResourceAsync(cloudId, new[] { rule.MetricName }, options, ct);
            return response.Value.Metrics.FirstOrDefault()?.TimeSeries.FirstOrDefault();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 429 && attempt < maxAttempts)
        {
            var backoff = TimeSpan.FromSeconds(10 * Math.Pow(2, attempt - 1));
            await Console.Error.WriteLineAsync(
                $"[fetch-metrics] {ShortId(cloudId)} {rule.MetricName} {chunkStart:yyyy-MM-dd}: 429, backing off {backoff.TotalSeconds:0}s (attempt {attempt}/{maxAttempts}).");
            await Task.Delay(backoff, ct);
        }
    }
}

static string ShortId(string cloudId)
{
    var idx = cloudId.LastIndexOf('/');
    return idx >= 0 && idx < cloudId.Length - 1 ? cloudId[(idx + 1)..] : cloudId;
}

static MetricAggregationType MapAggregation(string agg) => agg.ToLowerInvariant() switch
{
    "average" => MetricAggregationType.Average,
    "count" => MetricAggregationType.Count,
    "minimum" => MetricAggregationType.Minimum,
    "maximum" => MetricAggregationType.Maximum,
    "total" or "sum" => MetricAggregationType.Total,
    _ => MetricAggregationType.Average,
};

static double ExtractValue(MetricValue v, string agg) => agg.ToLowerInvariant() switch
{
    "average" => v.Average ?? 0,
    "count" => v.Count ?? 0,
    "minimum" => v.Minimum ?? 0,
    "maximum" => v.Maximum ?? 0,
    "total" or "sum" => v.Total ?? 0,
    _ => v.Average ?? 0,
};

static async Task<List<MetricRule>> LoadRules(FileInfo? explicitFile, string stageDir, CancellationToken ct)
{
    string? path = explicitFile?.FullName;
    if (path is null)
    {
        var fromStage = Path.Combine(stageDir, "metric-rules.json");
        if (File.Exists(fromStage)) path = fromStage;
    }

    if (path is null)
    {
        // Built-in defaults for the light usage report: 5-minute granularity (a 31-day
        // month yields ≈8,928 samples per series — enough for avg/p95/idle classification
        // at a fraction of the 1-minute query cost). Container Apps and other families
        // can be added per run via metric-rules.json without touching this script.
        return new List<MetricRule>
        {
            new("microsoft.web/serverfarms", "CpuPercentage", "00:05:00", "Average"),
            new("microsoft.web/serverfarms", "MemoryPercentage", "00:05:00", "Average"),
            new("microsoft.sql/servers/databases", "cpu_percent", "00:05:00", "Average"),
            new("microsoft.sql/servers/elasticpools", "cpu_percent", "00:05:00", "Average"),
            new("microsoft.compute/virtualmachines", "Percentage CPU", "00:05:00", "Average"),
        };
    }

    using var stream = File.OpenRead(path);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    var rules = new List<MetricRule>();
    foreach (var r in doc.RootElement.GetProperty("rules").EnumerateArray())
    {
        rules.Add(new MetricRule(
            r.GetProperty("cloudType").GetString()!,
            r.GetProperty("metricName").GetString()!,
            r.GetProperty("granularity").GetString()!,
            r.GetProperty("aggregation").GetString()!));
    }
    return rules;
}

static async Task<(string subscriptionId, List<ResourceRef> resources)> LoadResources(string path, CancellationToken ct)
{
    using var stream = File.OpenRead(path);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    var sub = doc.RootElement.GetProperty("subscriptionId").GetString()!;
    var list = new List<ResourceRef>();
    foreach (var r in doc.RootElement.GetProperty("resources").EnumerateArray())
    {
        // resources.json carries a flat `type` STRING (e.g. "Microsoft.Web/serverfarms")
        // and the ARM id under `id` — not the legacy `type.key` / `cloudId` shape.
        var typeKey = r.GetProperty("type").GetString()!;
        list.Add(new ResourceRef(r.GetProperty("id").GetString()!, typeKey));
    }
    return (sub, list);
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

internal record MetricRule(string CloudType, string MetricName, string Granularity, string Aggregation);
internal record ResourceRef(string CloudId, string TypeKey);
