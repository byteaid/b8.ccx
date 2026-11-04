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
// stages a few hundred rows. Every source is normalized to the same column
// set (action / rule / ruleSet / ip / uri / host / transaction / policy) and
// counted in two units: EVENTS (one log row per rule match) and REQUESTS
// (distinct transaction ids — what a reader understands as "an attack").
// Per source: summary, byAction, byPolicy (ties activity to the WAF
// inventory), byCategory (rule-id prefix → attack family), byHost (attacked
// applications), topRules, topClientIps (with active days), topUris,
// fpCandidates (rule×URI concentration computed server-side, with distinct
// IPs), dailyTrend. Four sources are probed independently — Application
// Gateway (AzureDiagnostics + dedicated table) and Front Door (classic +
// resource-specific) — and only sources that yield rows are emitted.
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

    // Column mapping per source. Every column is read through column_ifexists so a
    // workspace whose schema never saw a given field (older gateways, no WAF policy)
    // still answers instead of failing the whole source. `null` = the source has no
    // such concept; it projects as an empty string.
    var sources = new[]
    {
        new SourceDef("application-gateway",
            "AzureDiagnostics | where Category == 'ApplicationGatewayFirewallLog'",
            ActionCol: "action_s", RuleCol: "ruleId_s", RuleSetCol: "ruleSetType_s", RuleSetVersionCol: "ruleSetVersion_s",
            IpCol: "clientIp_s", UriCol: "requestUri_s", HostCol: "hostname_s", TxCol: "transactionId_g",
            PolicyCol: "policyId_s", PolicyScopeCol: "policyScope_s", PolicyScopeNameCol: "policyScopeName_s", PolicyModeCol: null,
            MsgCol: "Message"),
        // Same App Gateway logs when the workspace uses resource-specific (dedicated) tables.
        new SourceDef("application-gateway-dedicated",
            "AGWFirewallLogs",
            ActionCol: "Action", RuleCol: "RuleId", RuleSetCol: "RuleSetType", RuleSetVersionCol: "RuleSetVersion",
            IpCol: "ClientIp", UriCol: "RequestUri", HostCol: "Hostname", TxCol: "TransactionId",
            PolicyCol: "PolicyId", PolicyScopeCol: "PolicyScope", PolicyScopeNameCol: "PolicyScopeName", PolicyModeCol: null,
            MsgCol: "Message"),
        new SourceDef("front-door-classic",
            "AzureDiagnostics | where Category == 'FrontdoorWebApplicationFirewallLog'",
            ActionCol: "action_s", RuleCol: "ruleName_s", RuleSetCol: "policy_s", RuleSetVersionCol: null,
            IpCol: "clientIP_s", UriCol: "requestUri_s", HostCol: "host_s", TxCol: "trackingReference_s",
            PolicyCol: "policy_s", PolicyScopeCol: null, PolicyScopeNameCol: null, PolicyModeCol: "policyMode_s",
            MsgCol: "details_msg_s"),
        new SourceDef("front-door",
            "FrontDoorWebApplicationFirewallLog",
            ActionCol: "Action", RuleCol: "RuleName", RuleSetCol: "Policy", RuleSetVersionCol: null,
            IpCol: "ClientIP", UriCol: "RequestUri", HostCol: "Host", TxCol: "TrackingReference",
            PolicyCol: "Policy", PolicyScopeCol: null, PolicyScopeNameCol: null, PolicyModeCol: "PolicyMode",
            MsgCol: null),
    };

    var range = new QueryTimeRange(start, end);
    var emitted = new List<object>();

    foreach (var src in sources)
    {
        var norm = src.Normalized();

        // Presence probe = the summary aggregate; a missing table or empty result skips the source.
        var summaryRows = await TryQuery(logs, workspace, range, norm + Kql.Summary, src.Key, ct);
        if (summaryRows is null || summaryRows.Count == 0 || GetLong(summaryRows[0], "events") == 0)
        {
            await Console.Error.WriteLineAsync($"[fetch-waf-logs] {src.Key}: no data.");
            continue;
        }

        var byAction = await TryQuery(logs, workspace, range, norm + Kql.ByAction, src.Key, ct) ?? new();
        var byPolicy = await TryQuery(logs, workspace, range, norm + Kql.ByPolicy, src.Key, ct) ?? new();
        var byCategory = await TryQuery(logs, workspace, range, norm + Kql.ByCategory, src.Key, ct) ?? new();
        var byHost = await TryQuery(logs, workspace, range, norm + Kql.ByHost(top), src.Key, ct) ?? new();
        var topRules = await TryQuery(logs, workspace, range, norm + Kql.TopRules(top), src.Key, ct) ?? new();
        var topClientIps = await TryQuery(logs, workspace, range, norm + Kql.TopClientIps(top), src.Key, ct) ?? new();
        var topUris = await TryQuery(logs, workspace, range, norm + Kql.TopUris(top), src.Key, ct) ?? new();
        var fpCandidates = await TryQuery(logs, workspace, range, Kql.FpCandidates(norm), src.Key, ct) ?? new();
        var dailyTrend = await TryQuery(logs, workspace, range, norm + Kql.DailyTrend, src.Key, ct) ?? new();

        emitted.Add(new
        {
            source = src.Key,
            summary = summaryRows[0],
            byAction,
            byPolicy,
            byCategory,
            byHost,
            topRules,
            topClientIps,
            topUris,
            fpCandidates,
            dailyTrend,
        });
        await Console.Error.WriteLineAsync(
            $"[fetch-waf-logs] {src.Key}: {GetLong(summaryRows[0], "events")} events / {GetLong(summaryRows[0], "requests")} requests " +
            $"({GetLong(summaryRows[0], "blockedRequests")} blocked) over {GetLong(summaryRows[0], "activeDays")} day(s).");
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
                    bool b => b,
                    DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
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

static long GetLong(Dictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var v) ? v switch { long l => l, double d => (long)d, _ => 0 } : 0;

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
    string ActionCol, string RuleCol, string RuleSetCol, string? RuleSetVersionCol,
    string IpCol, string UriCol, string HostCol, string TxCol,
    string PolicyCol, string? PolicyScopeCol, string? PolicyScopeNameCol, string? PolicyModeCol,
    string? MsgCol)
{
    static string Col(string? name) => name is null ? "''" : $"tostring(column_ifexists('{name}', ''))";

    // Shared normalization prefix: same column names for every source so the
    // aggregates below are source-agnostic. `tx` falls back to a synthetic
    // request key when the transaction id is missing (very old log schemas).
    public string Normalized() =>
        BaseKql +
        $" | extend action={Col(ActionCol)}, rule={Col(RuleCol)}, ruleSet={Col(RuleSetCol)}, ruleSetVersion={Col(RuleSetVersionCol)}," +
        $" ip={Col(IpCol)}, uri={Col(UriCol)}, host={Col(HostCol)}, txRaw={Col(TxCol)}," +
        $" policyId={Col(PolicyCol)}, policyScope=trim(' ', strcat({Col(PolicyScopeCol)}, ' ', {Col(PolicyScopeNameCol)})), policyMode={Col(PolicyModeCol)}, msg={Col(MsgCol)}," +
        " resourceId=tolower(coalesce(tostring(column_ifexists('_ResourceId', '')), tostring(column_ifexists('ResourceId', ''))))" +
        " | extend tx=iff(isempty(txRaw), strcat(ip, '|', tostring(TimeGenerated), '|', uri), txRaw)" +
        @" | extend ruleNum=extract(@'(\d{6,8})\s*$', 1, rule)" +
        // Category key = rule-id prefix (CRS/DRS family: 942 SQLi, 941 XSS, …; 8-digit Microsoft
        // threat-intel rules keep 5 chars: 99001 CVEs, 99005 web shells, …), prefixed 'bot:' for
        // Bot Manager ids (100/200/300 collide with DRS General 200xxx), 'custom:<name>' otherwise.
        " | extend category=case(isempty(rule), 'unknown', isempty(ruleNum), strcat('custom:', rule)," +
        " strcat(iff(ruleSet contains 'Bot' or rule contains 'Bot', 'bot:', ''), iff(strlen(ruleNum) == 8, substring(ruleNum, 0, 5), substring(ruleNum, 0, 3))))" +
        " | extend blocked=action in~ ('Blocked', 'Block'), detected=action in~ ('Detected', 'Log'), allowed=action in~ ('Allowed', 'Allow'), challenged=action contains 'Challenge'";
}

static class Kql
{
    const string Counters =
        "events=count(), requests=dcount(tx), blockedRequests=dcountif(tx, blocked), detectedRequests=dcountif(tx, detected)";

    public const string Summary =
        " | summarize " + Counters + ", allowedRequests=dcountif(tx, allowed), challengedRequests=dcountif(tx, challenged)," +
        " distinctIps=dcount(ip), distinctHosts=dcount(host), activeDays=dcount(bin(TimeGenerated, 1d))," +
        " firstEvent=min(TimeGenerated), lastEvent=max(TimeGenerated)";

    public const string ByAction =
        " | summarize events=count(), requests=dcount(tx) by action | order by events desc";

    public const string ByPolicy =
        " | summarize " + Counters + ", mode=take_anyif(policyMode, isnotempty(policyMode)) by resourceId, policyId, policyScope | order by requests desc";

    public const string ByCategory =
        " | summarize " + Counters + ", ruleSet=take_anyif(ruleSet, isnotempty(ruleSet)) by category | order by requests desc";

    public static string ByHost(int top) =>
        " | summarize " + Counters + ", distinctIps=dcount(ip) by host | top " + top + " by requests";

    public static string TopRules(int top) =>
        " | summarize " + Counters + ", distinctIps=dcount(ip), distinctUris=dcount(uri), sample=take_anyif(msg, isnotempty(msg))" +
        " by rule, ruleSet, ruleSetVersion, category | top " + top + " by requests";

    public static string TopClientIps(int top) =>
        " | summarize " + Counters + ", activeDays=dcount(bin(TimeGenerated, 1d)), distinctHosts=dcount(host), distinctUris=dcount(uri)," +
        " firstEvent=min(TimeGenerated), lastEvent=max(TimeGenerated) by ip | top " + top + " by requests";

    public static string TopUris(int top) =>
        " | summarize " + Counters + ", distinctIps=dcount(ip) by host, uri | top " + top + " by requests";

    public const string DailyTrend =
        " | summarize " + Counters + " by day=format_datetime(bin(TimeGenerated, 1d), 'yyyy-MM-dd') | order by day asc";

    // False-positive candidates computed server-side: rule×URI combos holding >= 80% of the
    // rule's events with >= 50 events. Anomaly-score decision rules (949xxx / 980xxx) are not
    // rules a reader can exclude, so they are left out. distinctIps is the tie-breaker the
    // build stage uses: many origins on one URI = legitimate traffic, one origin = targeted probe.
    public static string FpCandidates(string normalized) =>
        "let base = " + normalized + " | where not(category in ('949', '980'));" +
        " let totals = base | summarize ruleTotal=count() by rule;" +
        " base | summarize hits=count(), requests=dcount(tx), distinctIps=dcount(ip), blockedHits=countif(blocked), detectedHits=countif(detected)," +
        " host=take_anyif(host, isnotempty(host)), action=take_any(action), sample=take_anyif(msg, isnotempty(msg)), ruleSet=take_anyif(ruleSet, isnotempty(ruleSet)) by rule, uri, category" +
        " | join kind=inner totals on rule" +
        " | extend sharePct = round(hits * 100.0 / ruleTotal, 1)" +
        " | where hits >= 50 and sharePct >= 80" +
        " | project rule, category, ruleSet, uri, host, action, hits, ruleTotal, sharePct, requests, distinctIps, blockedHits, detectedHits, sample" +
        " | top 30 by hits";
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
