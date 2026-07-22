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

// =========================================================================
// Stage 5 — aggregate Web Application Firewall logs (the FIREWALL product,
// not the Well-Architected Framework) from a Log Analytics workspace.
// Aggregation happens IN KQL so an attack flood of millions of events still
// stages a few hundred rows: totals by action, top rules, top client IPs,
// top URIs, rule×URI concentration (false-positive heuristic input), and a
// daily trend. Three sources are probed independently — Application Gateway
// (AzureDiagnostics), Front Door classic (AzureDiagnostics), and the Front
// Door resource-specific table; only sources that yield rows are emitted.
// Without --workspace-id the stage writes an empty payload and exits 0.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var workspaceOption = new Option<string?>("--workspace-id") { Description = "Log Analytics workspace GUID.", HelpName = "GUID" };
var startOption = new Option<DateTimeOffset>("--start") { Required = true, Description = "Period start.", HelpName = "DATETIME" };
var endOption = new Option<DateTimeOffset>("--end") { Required = true, Description = "Period end.", HelpName = "DATETIME" };
var topOption = new Option<int>("--top") { Description = "Rows kept per top-N aggregate.", DefaultValueFactory = _ => 15 };
var forceOption = new Option<bool>("--force") { Description = "Overwrite waf-logs.json." };

var rootCommand = new RootCommand("Stage 5 — aggregate WAF firewall logs (App Gateway + Front Door) from Log Analytics.")
{
    stageDirOption, workspaceOption, startOption, endOption, topOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var workspace = parseResult.GetValue(workspaceOption);
    var start = parseResult.GetValue(startOption);
    var end = parseResult.GetValue(endOption);
    var top = parseResult.GetValue(topOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "waf-logs.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[fetch-waf-logs] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    var endStr = end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    if (string.IsNullOrEmpty(workspace))
    {
        await Console.Error.WriteLineAsync("[fetch-waf-logs] --workspace-id not set; emitting an empty waf-logs.json (the WAF blocks in Seguridad degrade gracefully).");
        var empty = new { workspaceId = (string?)null, start = startStr, end = endStr, sources = Array.Empty<object>() };
        await WriteAtomic(outputPath, JsonSerializer.Serialize(empty, Json.Opts), ct);
        return 0;
    }

    LogsQueryClient logs;
    try { logs = new LogsQueryClient(new DefaultAzureCredential()); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-waf-logs] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    // Column mapping per source. `msgCol` null = source has no per-rule description column.
    var sources = new[]
    {
        new SourceDef("application-gateway",
            "AzureDiagnostics | where Category == 'ApplicationGatewayFirewallLog'",
            ActionCol: "action_s", RuleCol: "ruleId_s", RuleSetCol: "ruleSetType_s",
            IpCol: "clientIp_s", UriCol: "requestUri_s", HostCol: "hostname_s", MsgCol: "Message"),
        // Same App Gateway logs when the workspace uses resource-specific (dedicated) tables.
        new SourceDef("application-gateway-dedicated",
            "AGWFirewallLogs",
            ActionCol: "Action", RuleCol: "RuleId", RuleSetCol: "RuleSetType",
            IpCol: "ClientIp", UriCol: "RequestUri", HostCol: "Hostname", MsgCol: "Message"),
        new SourceDef("front-door-classic",
            "AzureDiagnostics | where Category == 'FrontdoorWebApplicationFirewallLog'",
            ActionCol: "action_s", RuleCol: "ruleName_s", RuleSetCol: "policy_s",
            IpCol: "clientIP_s", UriCol: "requestUri_s", HostCol: "host_s", MsgCol: null),
        new SourceDef("front-door",
            "FrontDoorWebApplicationFirewallLog",
            ActionCol: "Action", RuleCol: "RuleName", RuleSetCol: "Policy",
            IpCol: "ClientIP", UriCol: "RequestUri", HostCol: "Host", MsgCol: null),
    };

    var range = new QueryTimeRange(start, end);
    var emitted = new List<object>();

    foreach (var src in sources)
    {
        // Presence probe = the byAction aggregate; a missing table or empty result skips the source.
        var byAction = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | summarize hits=count() by action=tostring({src.ActionCol}) | order by hits desc", src.Key, ct);
        if (byAction is null || byAction.Count == 0)
        {
            await Console.Error.WriteLineAsync($"[fetch-waf-logs] {src.Key}: no data.");
            continue;
        }

        var ruleProjection = src.MsgCol is null
            ? $"summarize hits=count() by rule=tostring({src.RuleCol}), ruleSet=tostring({src.RuleSetCol})"
            : $"summarize hits=count(), sample=take_any(tostring({src.MsgCol})) by rule=tostring({src.RuleCol}), ruleSet=tostring({src.RuleSetCol})";

        var topRules = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | {ruleProjection} | top {top} by hits", src.Key, ct) ?? new();
        var topClientIps = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | summarize hits=count() by clientIp=tostring({src.IpCol}) | top {top} by hits", src.Key, ct) ?? new();
        var topUris = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | summarize hits=count() by host=tostring({src.HostCol}), uri=tostring({src.UriCol}) | top {top} by hits", src.Key, ct) ?? new();
        var topRuleUris = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | summarize hits=count() by rule=tostring({src.RuleCol}), uri=tostring({src.UriCol}), action=tostring({src.ActionCol}) | top 30 by hits", src.Key, ct) ?? new();
        var dailyTrend = await TryQuery(logs, workspace, range,
            $"{src.BaseKql} | summarize hits=count() by day=format_datetime(bin(TimeGenerated, 1d), 'yyyy-MM-dd') | order by day asc", src.Key, ct) ?? new();

        emitted.Add(new
        {
            source = src.Key,
            byAction,
            topRules,
            topClientIps,
            topUris,
            topRuleUris,
            dailyTrend,
        });
        var totalHits = byAction.Sum(r => r.TryGetValue("hits", out var h) && h is long l ? l : 0);
        await Console.Error.WriteLineAsync($"[fetch-waf-logs] {src.Key}: {totalHits} events aggregated.");
    }

    var payload = new
    {
        workspaceId = workspace,
        start = startStr,
        end = endStr,
        sources = emitted,
    };

    await WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Json.Opts), ct);
    await Console.Error.WriteLineAsync($"[fetch-waf-logs] wrote {outputPath} ({emitted.Count} source(s) with data)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

// Run one aggregate query; null on error (table absent, permissions), empty list on no rows.
// Rows come back as generic {column: value} dictionaries — the aggregates are small by design.
static async Task<List<Dictionary<string, object?>>?> TryQuery(
    LogsQueryClient logs, string workspace, QueryTimeRange range, string kql, string sourceKey, CancellationToken ct)
{
    try
    {
        var response = await logs.QueryWorkspaceAsync(workspace, kql, range, cancellationToken: ct);
        if (response.Value.Status != LogsQueryResultStatus.Success && response.Value.Status != LogsQueryResultStatus.PartialFailure)
        {
            await Console.Error.WriteLineAsync($"[fetch-waf-logs] {sourceKey}: {response.Value.Error?.Message}");
            return null;
        }
        var table = response.Value.Table;
        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in table.Rows)
        {
            var dict = new Dictionary<string, object?>();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var name = table.Columns[i].Name;
                var raw = row[i];
                dict[name] = raw switch
                {
                    null => null,
                    int n => (long)n,
                    long l => l,
                    double d => d,
                    _ => raw.ToString(),
                };
            }
            rows.Add(dict);
        }
        return rows;
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[fetch-waf-logs] {sourceKey}: {FirstLine(ex.Message)}");
        return null;
    }
}

static string FirstLine(string s)
{
    var idx = s.IndexOf('\n');
    return idx >= 0 ? s[..idx] : s;
}

static async Task WriteAtomic(string path, string content, CancellationToken ct)
{
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, content, ct);
    File.Move(tmp, path, overwrite: true);
}

internal sealed record SourceDef(
    string Key, string BaseKql,
    string ActionCol, string RuleCol, string RuleSetCol,
    string IpCol, string UriCol, string HostCol, string? MsgCol);

static class Json
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
