#:property PublishAot=false
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;

// =========================================================================
// Stage 9 — pure-CPU aggregation. Joins every staged JSON into the light
// usage report: 6 sections (costos, seguridad, fiabilidad, disponibilidad,
// rendimiento, operacion) + a deterministic `signals` block that grounds the
// agent-authored narrative. All classification (zombie / oversized /
// saturated, FP candidates, cert expiry buckets, backup gaps, zone/regional
// redundancy posture, scaling gaps) is decided HERE, in code — the narrative
// only phrases it. Findings are ENVIRONMENT-WEIGHTED: each resource is
// classified Prod / Dev/Test / unclassified (tags, then naming), and the same
// gap that is a real finding on Prod degrades to Informativo on Dev/Test.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var forceOption = new Option<bool>("--force") { Description = "Overwrite usage-report.json." };

var rootCommand = new RootCommand("Stage 9 — aggregate staged JSONs into the light usage report (sections + signals).")
{
    stageDirOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var force = parseResult.GetValue(forceOption);

    var outputPath = Path.Combine(stageDir.FullName, "usage-report.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[build-usage-report] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var resources = await TryLoad(Path.Combine(stageDir.FullName, "resources.json"), ct);
    var costs = await TryLoad(Path.Combine(stageDir.FullName, "costs.json"), ct);
    var metrics = await TryLoad(Path.Combine(stageDir.FullName, "metrics.json"), ct);
    var wafLogs = await TryLoad(Path.Combine(stageDir.FullName, "waf-logs.json"), ct);
    var advisor = await TryLoad(Path.Combine(stageDir.FullName, "advisor.json"), ct);
    var security = await TryLoad(Path.Combine(stageDir.FullName, "security.json"), ct);
    var resilience = await TryLoad(Path.Combine(stageDir.FullName, "resilience.json"), ct);

    if (resources is null)
    {
        await Console.Error.WriteLineAsync("[build-usage-report] resources.json missing. Run discover-resources first.");
        return 2;
    }

    var model = Model.Build(resources.Value, costs, metrics, wafLogs, advisor, security, resilience);

    var sections = new List<object>
    {
        Builders.Costos(model),
        Builders.Seguridad(model),
        Builders.Fiabilidad(model),
        Builders.Disponibilidad(model),
        Builders.Rendimiento(model),
        Builders.Operacion(model),
    };

    var payload = new
    {
        generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        subscriptionId = model.SubscriptionId,
        period = new { start = model.PeriodStart, end = model.PeriodEnd },
        months = model.Months,
        analysisMonth = model.AnalysisMonth,
        currency = "USD",
        signals = Builders.Signals(model),
        sections,
    };

    await WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Json.Opts), ct);
    await Console.Error.WriteLineAsync(
        $"[build-usage-report] wrote {outputPath} ({sections.Count} sections, {model.Months.Count} months, " +
        $"{model.Utilization.Count} metered resources, waf={(model.WafSources.Count > 0 ? "yes" : "no")}, " +
        $"defender={(model.DefenderPresent ? "yes" : "no")})");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task<JsonElement?> TryLoad(string path, CancellationToken ct)
{
    if (!File.Exists(path)) return null;
    using var stream = File.OpenRead(path);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    return doc.RootElement.Clone();
}

static async Task WriteAtomic(string path, string content, CancellationToken ct)
{
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, content, ct);
    File.Move(tmp, path, overwrite: true);
}

// =====================================================================
// Domain model.
// =====================================================================

sealed record ResourceInfo(
    string Id, string IdLower, string Name, string Type, string FriendlyType, string? Kind,
    string Region, string ResourceGroup, string? SkuName, string? SkuTier, int? SkuCapacity,
    bool HasTags, List<string> TagKeys, Dictionary<string, string> Tags, string Environment, JsonElement Properties);

sealed record CostRow(string CloudId, string Month, double Value, string ChargeType);

// Per-resource utilization derived from the CPU-family metric series.
sealed record Utilization(
    string Label, string FriendlyType, string? Sku, string Environment, double AvgCpu, double P95Cpu,
    double IdlePct, double SatPct, int Samples, double MonthlyCost, string Classification);

sealed record UnusedResource(string Label, string FriendlyType, string Finding, double MonthlyCost);

// Backup coverage per family + per-resource gaps (severity env-weighted).
sealed record BackupFamily(string Family, int Total, int Protected, List<string> Unprotected);
sealed record BackupGap(string Label, string FriendlyType, string Environment, string Finding, string Severity);

// TLS certificates merged from App Service + App Gateway listeners.
sealed record CertRow(string Name, string Origin, string Hosts, string? Expires, int? Days, string Status);

// Redundancy posture rows. Level: Local < Zona < Regional. Prod (and
// unclassified) must reach at least Zona; Dev/Test is deliberately satisfied
// by Local — a nonprod resource never raises a redundancy finding.
sealed record RedundancyRow(string Label, string FriendlyType, string Environment, string Level, string Config, string Status);

// Scaling posture across compute + databases (Severity: OK / Advertencia / Informativo).
sealed record ScaleRow(string Label, string FriendlyType, string Environment, string Config, string Finding, string Severity);

// WAF coverage per app: does public traffic pass through a WAF-fronted gateway,
// and is the direct endpoint locked down (no bypass)? Env-weighted severity.
sealed record ExposureRow(string Label, string FriendlyType, string Environment, string Exposure, string Finding, string Severity);

sealed class Model
{
    public required string? SubscriptionId { get; init; }
    public required string? PeriodStart { get; init; }
    public required string? PeriodEnd { get; init; }
    public required List<ResourceInfo> Resources { get; init; }
    public required Dictionary<string, ResourceInfo> ResourceByIdLower { get; init; }
    public required List<CostRow> Costs { get; init; }
    public required List<string> Months { get; init; }
    public required string? AnalysisMonth { get; init; }
    public required string? CurrentMonth { get; init; }
    public required Dictionary<string, double> CurrentMonthCostById { get; init; }
    public required List<Utilization> Utilization { get; init; }
    public required List<UnusedResource> Unused { get; init; }
    public required List<BackupFamily> BackupFamilies { get; init; }
    public required List<BackupGap> BackupGaps { get; init; }
    public required List<CertRow> Certificates { get; init; }
    public required List<RedundancyRow> Redundancy { get; init; }
    public required List<ScaleRow> Scaling { get; init; }
    public required List<ExposureRow> WafExposure { get; init; }
    public required List<ExposureRow> InfraExposure { get; init; }
    public required List<JsonElement> WafSources { get; init; }
    public string? WafStart { get; init; }
    public string? WafEnd { get; init; }
    public required List<JsonElement> AdvisorRecs { get; init; }
    public required bool DefenderPresent { get; init; }
    public required JsonElement? Security { get; init; }
    public required JsonElement? Resilience { get; init; }

    public static Model Build(
        JsonElement resources, JsonElement? costs, JsonElement? metrics,
        JsonElement? wafLogs, JsonElement? advisor, JsonElement? security, JsonElement? resilience)
    {
        var subId = resources.GetProperty("subscriptionId").GetString();
        string? periodStart = costs?.GetProperty("start").GetString() ?? metrics?.GetProperty("start").GetString();
        string? periodEnd = costs?.GetProperty("end").GetString() ?? metrics?.GetProperty("end").GetString();

        var resourceList = new List<ResourceInfo>();
        foreach (var r in resources.GetProperty("resources").EnumerateArray())
        {
            var id = r.GetProperty("id").GetString() ?? "";
            var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var region = Helpers.NormalizeRegion(r.TryGetProperty("region", out var rg) ? rg.GetString() : null);
            var resourceGroup = r.TryGetProperty("resourceGroup", out var rgp) ? rgp.GetString() ?? "" : "";
            string? skuName = null, skuTier = null; int? skuCapacity = null;
            if (r.TryGetProperty("sku", out var sku) && sku.ValueKind == JsonValueKind.Object)
            {
                skuName = Helpers.Str(sku, "name");
                skuTier = Helpers.Str(sku, "tier");
                if (sku.TryGetProperty("capacity", out var cap) && cap.ValueKind == JsonValueKind.Number)
                    skuCapacity = cap.GetInt32();
            }
            var tagKeys = new List<string>();
            var tagMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (r.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                foreach (var p in tags.EnumerateObject())
                {
                    tagKeys.Add(p.Name);
                    if (p.Value.ValueKind == JsonValueKind.String) tagMap[p.Name] = p.Value.GetString() ?? "";
                }
            var props = r.TryGetProperty("properties", out var pr) && pr.ValueKind == JsonValueKind.Object ? pr : default;
            var environment = Helpers.ClassifyEnvironment(name, resourceGroup, tagMap);
            var kind = r.TryGetProperty("kind", out var kd) && kd.ValueKind == JsonValueKind.String ? kd.GetString() : null;
            resourceList.Add(new ResourceInfo(
                id, id.ToLowerInvariant(), name, type, Helpers.FriendlyType(type), kind,
                region, resourceGroup, skuName, skuTier, skuCapacity, tagKeys.Count > 0, tagKeys, tagMap, environment, props));
        }

        var byIdLower = new Dictionary<string, ResourceInfo>(StringComparer.Ordinal);
        foreach (var r in resourceList) byIdLower[r.IdLower] = r;

        var costRows = new List<CostRow>();
        if (costs is not null && costs.Value.TryGetProperty("costs", out var cArr))
        {
            foreach (var c in cArr.EnumerateArray())
            {
                var cloudId = (Helpers.Str(c, "cloudId") ?? "Unassigned").ToLowerInvariant();
                var day = Helpers.Str(c, "day") ?? "";
                if (day.Length < 7) continue;
                var value = c.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                costRows.Add(new CostRow(cloudId, day[..7], value, Helpers.Str(c, "chargeType") ?? "Usage"));
            }
        }
        var months = costRows.Select(c => c.Month).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();
        var currentMonth = months.Count > 0 ? months[^1] : null;

        var currentCostById = costRows
            .Where(c => currentMonth is null || c.Month == currentMonth)
            .GroupBy(c => c.CloudId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Value), StringComparer.Ordinal);

        var (utilization, analysisMonth) = BuildUtilization(metrics, byIdLower, currentCostById);
        analysisMonth ??= currentMonth;

        var unused = BuildUnused(resourceList, currentCostById);
        var (backupFamilies, backupGaps) = BuildBackup(resourceList, resilience);
        var certificates = BuildCertificates(resourceList, resilience);
        var redundancy = BuildRedundancy(resourceList, resilience);
        var scaling = BuildScaling(resourceList, resilience);
        var wafExposure = BuildWafExposure(resourceList, security);
        var infraExposure = BuildInfraExposure(resourceList, security);

        var wafSources = new List<JsonElement>();
        if (wafLogs is not null && wafLogs.Value.TryGetProperty("sources", out var srcArr) && srcArr.ValueKind == JsonValueKind.Array)
            foreach (var s in srcArr.EnumerateArray()) wafSources.Add(s);

        var advisorRecs = new List<JsonElement>();
        if (advisor is not null && advisor.Value.TryGetProperty("recommendations", out var recArr) && recArr.ValueKind == JsonValueKind.Array)
            foreach (var rec in recArr.EnumerateArray()) advisorRecs.Add(rec);

        var defenderPresent = security is not null
            && security.Value.TryGetProperty("present", out var dp) && dp.ValueKind == JsonValueKind.True;

        return new Model
        {
            SubscriptionId = subId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Resources = resourceList,
            ResourceByIdLower = byIdLower,
            Costs = costRows,
            Months = months,
            AnalysisMonth = analysisMonth,
            CurrentMonth = currentMonth,
            CurrentMonthCostById = currentCostById,
            Utilization = utilization,
            Unused = unused,
            BackupFamilies = backupFamilies,
            BackupGaps = backupGaps,
            Certificates = certificates,
            Redundancy = redundancy,
            Scaling = scaling,
            WafExposure = wafExposure,
            InfraExposure = infraExposure,
            WafSources = wafSources,
            WafStart = wafLogs is { } wl0 ? Helpers.Str(wl0, "start") : null,
            WafEnd = wafLogs is { } wl1 ? Helpers.Str(wl1, "end") : null,
            AdvisorRecs = advisorRecs,
            DefenderPresent = defenderPresent,
            Security = security,
            Resilience = resilience,
        };
    }

    // Classify each metered resource from its CPU-family series (memory series inform nothing
    // classification-wise in the light report; they stay available in metrics.json).
    static (List<Utilization>, string? analysisMonth) BuildUtilization(
        JsonElement? metrics, Dictionary<string, ResourceInfo> byIdLower, Dictionary<string, double> costById)
    {
        var results = new List<Utilization>();
        string? metricMonth = null;
        if (metrics is null || !metrics.Value.TryGetProperty("metrics", out var mArr))
            return (results, null);

        var cpuByResource = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var m in mArr.EnumerateArray())
        {
            var name = Helpers.Str(m, "name") ?? "";
            var isCpu = name.Equals("CpuPercentage", StringComparison.OrdinalIgnoreCase)
                || name.Equals("cpu_percent", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Percentage CPU", StringComparison.OrdinalIgnoreCase);
            if (!isCpu) continue;
            var cloudId = (Helpers.Str(m, "cloudId") ?? "").ToLowerInvariant();
            if (cloudId.Length == 0) continue;
            var values = cpuByResource.TryGetValue(cloudId, out var list) ? list : cpuByResource[cloudId] = new();
            if (m.TryGetProperty("values", out var vArr))
            {
                foreach (var v in vArr.EnumerateArray())
                {
                    if (v.TryGetProperty("value", out var vv) && vv.ValueKind == JsonValueKind.Number)
                        values.Add(vv.GetDouble());
                    if (metricMonth is null && v.TryGetProperty("timestamp", out var ts)
                        && ts.GetString() is { Length: >= 7 } tsStr)
                        metricMonth = tsStr[..7];
                }
            }
        }

        foreach (var (cloudId, values) in cpuByResource)
        {
            if (values.Count == 0) continue;
            byIdLower.TryGetValue(cloudId, out var res);
            var label = res?.Name ?? Helpers.ShortLabel(cloudId);
            if (res is not null && res.Type.Contains("databases", StringComparison.OrdinalIgnoreCase))
                label = Helpers.SqlLabel(res.Id);

            var sorted = values.OrderBy(v => v).ToList();
            var avg = Math.Round(values.Average(), 1);
            var p95 = Math.Round(sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))], 1);
            var idlePct = Math.Round(values.Count(v => v < 10) * 100.0 / values.Count, 1);
            var satPct = Math.Round(values.Count(v => v >= 90) * 100.0 / values.Count, 1);
            var cost = costById.TryGetValue(cloudId, out var c) ? Math.Round(c, 2) : 0;

            // Deterministic classification thresholds (documented in pipeline.md):
            // Zombie >= 99% idle; Sobredimensionado >= 80% idle OR (avg < 15 AND p95 < 40);
            // Saturado >= 5% of samples at >= 90% OR avg >= 80; else Correcto.
            var classification =
                idlePct >= 99 ? "Zombie" :
                (idlePct >= 80 || (avg < 15 && p95 < 40)) ? "Sobredimensionado" :
                (satPct >= 5 || avg >= 80) ? "Saturado" :
                "Correcto";

            results.Add(new Utilization(
                label, res?.FriendlyType ?? Helpers.FriendlyTypeFromArmId(cloudId),
                res?.SkuName, res?.Environment ?? "—", avg, p95, idlePct, satPct, values.Count, cost, classification));
        }

        // Worst first: Zombie, Sobredimensionado, Saturado, Correcto — then by cost desc.
        var order = new Dictionary<string, int> { ["Zombie"] = 0, ["Sobredimensionado"] = 1, ["Saturado"] = 2, ["Correcto"] = 3 };
        results = results.OrderBy(u => order[u.Classification]).ThenByDescending(u => u.MonthlyCost).ToList();
        return (results, metricMonth);
    }

    // Concrete no-use findings readable straight from ARM properties.
    static List<UnusedResource> BuildUnused(List<ResourceInfo> resources, Dictionary<string, double> costById)
    {
        var unused = new List<UnusedResource>();
        foreach (var r in resources)
        {
            double Cost() => costById.TryGetValue(r.IdLower, out var c) ? Math.Round(c, 2) : 0;
            var type = r.Type.ToLowerInvariant();
            if (type == "microsoft.compute/disks")
            {
                if (string.Equals(Helpers.Str(r.Properties, "diskState"), "Unattached", StringComparison.OrdinalIgnoreCase))
                    unused.Add(new(r.Name, r.FriendlyType, "Disco sin adjuntar a ninguna VM", Cost()));
            }
            else if (type == "microsoft.network/publicipaddresses")
            {
                var associated = r.Properties.ValueKind == JsonValueKind.Object
                    && (r.Properties.TryGetProperty("ipConfiguration", out _) || r.Properties.TryGetProperty("natGateway", out _));
                if (!associated)
                    unused.Add(new(r.Name, r.FriendlyType, "IP pública sin asociar a ningún recurso", Cost()));
            }
            else if (type == "microsoft.web/serverfarms")
            {
                if (r.Properties.ValueKind == JsonValueKind.Object
                    && r.Properties.TryGetProperty("numberOfSites", out var ns)
                    && ns.ValueKind == JsonValueKind.Number && ns.GetInt32() == 0)
                    unused.Add(new(r.Name, r.FriendlyType, "App Service Plan sin aplicaciones alojadas", Cost()));
            }
        }
        return unused.OrderByDescending(u => u.MonthlyCost).ToList();
    }

    // Backup / data-protection coverage per family. VMs need a Recovery Services
    // vault; SQL has PITR built in (the verified gap is a missing LTR policy);
    // Cosmos and the flexible DB servers carry integrated backups; storage
    // recoverability = blob soft delete. App Service content backup is not
    // verifiable with Reader-only access and is deliberately out of scope.
    static (List<BackupFamily>, List<BackupGap>) BuildBackup(List<ResourceInfo> resources, JsonElement? resilience)
    {
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        var protectedShares = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "{accountIdLower}|{shareName}"
        var sqlPolicies = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var blobServices = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var webBackupById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileShares = new List<(string accountIdLower, string shareName)>();
        if (resilience is { } res)
        {
            if (res.TryGetProperty("protectedItems", out var pi) && pi.ValueKind == JsonValueKind.Array)
                foreach (var item in pi.EnumerateArray())
                    if (Helpers.Str(item, "sourceResourceId") is { } sid)
                    {
                        protectedIds.Add(sid.ToLowerInvariant());
                        if (Helpers.Str(item, "friendlyName") is { } fn)
                            protectedShares.Add(sid.ToLowerInvariant() + "|" + fn);
                    }
            if (res.TryGetProperty("sqlBackupPolicies", out var sq) && sq.ValueKind == JsonValueKind.Array)
                foreach (var p in sq.EnumerateArray())
                    if (Helpers.Str(p, "databaseId") is { } did) sqlPolicies[did] = p;
            if (res.TryGetProperty("storageBlobServices", out var sb) && sb.ValueKind == JsonValueKind.Array)
                foreach (var b in sb.EnumerateArray())
                    if (Helpers.Str(b, "accountId") is { } aid) blobServices[aid] = b;
            if (res.TryGetProperty("webAppBackups", out var wb) && wb.ValueKind == JsonValueKind.Array)
                foreach (var w in wb.EnumerateArray())
                    if (Helpers.Str(w, "siteId") is { } wid) webBackupById[wid] = Helpers.Str(w, "status") ?? "NotVerifiable";
            if (res.TryGetProperty("fileShares", out var fs) && fs.ValueKind == JsonValueKind.Array)
                foreach (var f in fs.EnumerateArray())
                    if (Helpers.Str(f, "accountId") is { } fid && Helpers.Str(f, "shareName") is { } sn)
                        fileShares.Add((fid.ToLowerInvariant(), sn));
        }

        var families = new List<BackupFamily>();
        var gaps = new List<BackupGap>();

        void Family(string name, List<ResourceInfo> members, Func<ResourceInfo, bool> isProtected,
            Func<ResourceInfo, string> finding, string prodSeverity)
        {
            if (members.Count == 0) return;
            var unprotected = members.Where(m => !isProtected(m)).ToList();
            families.Add(new(name, members.Count, members.Count - unprotected.Count,
                unprotected.Select(u => u.Name).ToList()));
            foreach (var u in unprotected)
            {
                var text = finding(u);
                if (text.Length == 0) continue;
                gaps.Add(new(u.Name, u.FriendlyType, u.Environment, text, Helpers.EnvSeverity(u.Environment, prodSeverity)));
            }
        }

        List<ResourceInfo> Of(string type) =>
            resources.Where(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();

        Family("Máquinas virtuales (Azure Backup)", Of("microsoft.compute/virtualmachines"),
            v => protectedIds.Contains(v.IdLower), _ => "Sin backup en Recovery Services", "Crítico");

        var sqlDbs = Of("microsoft.sql/servers/databases")
            .Where(d => !d.Name.Equals("master", StringComparison.OrdinalIgnoreCase)).ToList();
        if (sqlPolicies.Count > 0 || sqlDbs.Count > 0)
            Family("SQL Databases (PITR integrado; LTR)", sqlDbs,
                d => sqlPolicies.TryGetValue(d.Id, out var pol) && HasLtr(pol),
                d => sqlPolicies.TryGetValue(d.Id, out var pol)
                    && pol.TryGetProperty("retentionDays", out var rd) && rd.ValueKind == JsonValueKind.Number
                        ? $"PITR {rd.GetInt32()}d activo; sin retención a largo plazo (LTR)"
                        : "Sin retención a largo plazo (LTR) configurada",
                "Advertencia");

        Family("Cosmos DB (backup integrado)", Of("microsoft.documentdb/databaseaccounts"),
            _ => true, _ => "", "Informativo");

        var flexible = Of("microsoft.dbformysql/flexibleservers")
            .Concat(Of("microsoft.dbforpostgresql/flexibleservers")).ToList();
        Family("MySQL/PostgreSQL Flexible (backup integrado)", flexible, _ => true, _ => "", "Informativo");

        if (blobServices.Count > 0)
            Family("Storage Accounts (blob soft delete)", Of("microsoft.storage/storageaccounts"),
                s => blobServices.TryGetValue(s.Id, out var b) && Helpers.BoolProp(b, "blobSoftDeleteEnabled"),
                _ => "Blob soft delete deshabilitado", "Advertencia");

        // Azure Files — shares protected when a Recovery Services protected item
        // matches (accountId, shareName). Environment inherited from the account.
        // Function-App CONTENT shares (WEBSITE_CONTENTSHARE: "{site name}" +
        // optional hex suffix, auto-created per app) are runtime artifacts that
        // redeploy with the app — they report as their own family and are never
        // a backup gap. Only user-managed shares can raise findings.
        if (fileShares.Count > 0)
        {
            var siteNamesLower = resources
                .Where(r => r.Type.Equals("microsoft.web/sites", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Name.ToLowerInvariant())
                .Where(n => n.Length >= 4)
                .ToList();
            bool IsAppContentShare(string shareName)
            {
                var s = shareName.ToLowerInvariant();
                foreach (var site in siteNamesLower)
                {
                    if (!s.StartsWith(site, StringComparison.Ordinal)) continue;
                    var rest = s[site.Length..].TrimStart('-');
                    if (rest.Length == 0
                        || (rest.Length <= 16 && rest.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or '-')))
                        return true;
                }
                return false;
            }

            var contentShares = fileShares.Where(fs => IsAppContentShare(fs.shareName)).ToList();

            // Second pass: accounts that host app-content shares accumulate
            // ORPHAN content shares from deleted/renamed apps (same hex-suffix
            // pattern, no live site to match). Only applies inside those
            // accounts so genuinely user-managed shares elsewhere are untouched.
            var contentAccountIds = new HashSet<string>(contentShares.Select(c => c.accountIdLower), StringComparer.Ordinal);
            static bool HasHexSuffix(string shareName)
            {
                var s = shareName.ToLowerInvariant().Replace("-", "");
                if (s.Length < 4) return false;
                return s[^4..].All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
            }
            var orphanShares = fileShares.Except(contentShares)
                .Where(fs => contentAccountIds.Contains(fs.accountIdLower) && HasHexSuffix(fs.shareName)).ToList();
            var userShares = fileShares.Except(contentShares).Except(orphanShares).ToList();

            if (contentShares.Count + orphanShares.Count > 0)
                families.Add(new("Azure Files — contenido de apps (redespliegue; incl. huérfanas)",
                    contentShares.Count + orphanShares.Count, contentShares.Count + orphanShares.Count, new()));

            if (userShares.Count > 0)
            {
                var accountById = resources.ToDictionary(r => r.IdLower, r => r, StringComparer.Ordinal);
                var unprotectedShares = userShares
                    .Where(fs => !protectedShares.Contains(fs.accountIdLower + "|" + fs.shareName)).ToList();
                families.Add(new("Azure Files (Recovery Services)", userShares.Count,
                    userShares.Count - unprotectedShares.Count, unprotectedShares.Select(u => u.shareName).ToList()));
                foreach (var (accountIdLower, shareName) in unprotectedShares)
                {
                    accountById.TryGetValue(accountIdLower, out var acc);
                    var env = acc?.Environment ?? "—";
                    gaps.Add(new(acc is null ? shareName : $"{shareName} ({acc.Name})", "Azure Files", env,
                        "File share sin backup en Recovery Services", Helpers.EnvSeverity(env, "Advertencia")));
                }
            }
        }

        // Container Apps — stateless by design (config as code, image in the
        // registry): visible in the coverage table, never a gap.
        var containerApps = Of("microsoft.app/containerapps");
        if (containerApps.Count > 0)
            families.Add(new("Container Apps (sin estado persistente)", containerApps.Count, containerApps.Count, new()));

        // App Services — config/backup/list is ALWAYS attempted (Contributor
        // assumed); sites where access was denied degrade to an Informativo
        // "no verificable" row. Function apps are excluded (redeployable by
        // design; the backup feature does not apply the same way).
        if (webBackupById.Count > 0)
        {
            var sites = resources.Where(r =>
                r.Type.Equals("microsoft.web/sites", StringComparison.OrdinalIgnoreCase)
                && !(r.Kind ?? "").Contains("functionapp", StringComparison.OrdinalIgnoreCase)).ToList();
            if (sites.Count > 0)
            {
                var unprotectedSites = sites
                    .Where(s => webBackupById.GetValueOrDefault(s.Id, "NotVerifiable") != "Configured").ToList();
                families.Add(new("App Services (App Service Backup)", sites.Count,
                    sites.Count - unprotectedSites.Count, unprotectedSites.Select(u => u.Name).ToList()));
                foreach (var s in unprotectedSites)
                {
                    if (webBackupById.GetValueOrDefault(s.Id, "NotVerifiable") == "NotVerifiable")
                        gaps.Add(new(s.Name, s.FriendlyType, s.Environment,
                            "Backup no verificable (requiere Contributor)", "Informativo"));
                    else
                        gaps.Add(new(s.Name, s.FriendlyType, s.Environment,
                            "Sin App Service Backup configurado", Helpers.EnvSeverity(s.Environment, "Advertencia")));
                }
            }
        }

        return (families, gaps
            .OrderBy(g => Helpers.FindingRank(g.Severity))
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList());
    }

    static bool HasLtr(JsonElement policy) =>
        IsLtrValue(Helpers.Str(policy, "weeklyRetention"))
        || IsLtrValue(Helpers.Str(policy, "monthlyRetention"))
        || IsLtrValue(Helpers.Str(policy, "yearlyRetention"));

    static bool IsLtrValue(string? v) => v is not null && !v.Equals("PT0S", StringComparison.OrdinalIgnoreCase);

    // TLS certificates from two sources: App Service certificates (staged by
    // fetch-resilience) and Application Gateway listener certificates read from
    // the staged ARM properties — embedded certs are parsed as X509 for their
    // real expiry; Key Vault references resolve against the expiry staged by
    // fetch-resilience (data-plane read, always attempted) and only degrade to
    // "No verificable" when that access was denied.
    static List<CertRow> BuildCertificates(List<ResourceInfo> resources, JsonElement? resilience)
    {
        var rows = new List<CertRow>();
        var now = DateTimeOffset.UtcNow;

        var kvExpiryBySecretId = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        if (resilience is { } kvRes && kvRes.TryGetProperty("keyVaultCertificates", out var kvArr) && kvArr.ValueKind == JsonValueKind.Array)
            foreach (var k in kvArr.EnumerateArray())
                if (Helpers.Str(k, "secretId") is { } kSid && Helpers.Str(k, "expirationDate") is { } kExp
                    && DateTimeOffset.TryParse(kExp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var kvDate))
                    kvExpiryBySecretId[kSid] = kvDate;

        void Add(string name, string origin, string hosts, DateTimeOffset? exp)
        {
            if (exp is null)
            {
                rows.Add(new(name, origin, hosts, null, null, "No verificable"));
                return;
            }
            var days = (int)Math.Floor((exp.Value - now).TotalDays);
            var status = days < 0 ? "Vencido" : days <= 30 ? "Crítico" : days <= 60 ? "Advertencia" : "OK";
            rows.Add(new(name, origin, hosts, exp.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days, status));
        }

        if (resilience is { } res && res.TryGetProperty("webCertificates", out var wc) && wc.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in wc.EnumerateArray())
            {
                var name = Helpers.Str(c, "name") ?? "—";
                var hosts = "";
                if (c.TryGetProperty("hostNames", out var hn) && hn.ValueKind == JsonValueKind.Array)
                    hosts = string.Join(", ", hn.EnumerateArray().Select(h => h.GetString()).Where(h => h is not null).Take(3));
                if (Helpers.Str(c, "expirationDate") is { } es
                    && DateTimeOffset.TryParse(es, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exp))
                    Add(name, "App Service", hosts, exp);
            }
        }

        foreach (var gw in resources.Where(r =>
            r.Type.Equals("microsoft.network/applicationgateways", StringComparison.OrdinalIgnoreCase)))
        {
            if (gw.Properties.ValueKind != JsonValueKind.Object) continue;

            // Listener hostnames keyed by the ssl certificate name they reference.
            var hostsByCert = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (gw.Properties.TryGetProperty("httpListeners", out var listeners) && listeners.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in listeners.EnumerateArray())
                {
                    if (!l.TryGetProperty("properties", out var lp) || lp.ValueKind != JsonValueKind.Object) continue;
                    if (!lp.TryGetProperty("sslCertificate", out var sc) || Helpers.Str(sc, "id") is not { } certId) continue;
                    var certName = Helpers.ShortLabel(certId);
                    var hosts = new List<string>();
                    if (Helpers.Str(lp, "hostName") is { } single) hosts.Add(single);
                    if (lp.TryGetProperty("hostNames", out var hns) && hns.ValueKind == JsonValueKind.Array)
                        foreach (var h in hns.EnumerateArray())
                            if (h.ValueKind == JsonValueKind.String) hosts.Add(h.GetString()!);
                    var list = hostsByCert.TryGetValue(certName, out var existing) ? existing : hostsByCert[certName] = new();
                    list.AddRange(hosts);
                }
            }

            if (!gw.Properties.TryGetProperty("sslCertificates", out var certs) || certs.ValueKind != JsonValueKind.Array) continue;
            foreach (var c in certs.EnumerateArray())
            {
                var certName = Helpers.Str(c, "name") ?? "—";
                var cp = c.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                var hosts = hostsByCert.TryGetValue(certName, out var hl)
                    ? string.Join(", ", hl.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
                    : "";
                DateTimeOffset? exp = null;
                var origin = $"App Gateway {gw.Name}";
                if (cp.ValueKind == JsonValueKind.Object)
                {
                    if (Helpers.Str(cp, "publicCertData") is { } data)
                        exp = Helpers.TryParseCertExpiry(data);
                    else if (Helpers.Str(cp, "keyVaultSecretId") is { } sid)
                    {
                        origin = $"App Gateway {gw.Name} (Key Vault)";
                        if (kvExpiryBySecretId.TryGetValue(sid, out var kvExp)) exp = kvExp;
                    }
                }
                Add(certName, origin, hosts, exp);
            }
        }

        return rows.OrderBy(r => r.Days ?? int.MaxValue).ToList();
    }

    // Redundancy posture: each resource gets a LEVEL (Local < Zona < Regional)
    // and the ask is environment-driven — Prod (and unclassified) must reach at
    // least Zona; Dev/Test is deliberately satisfied by Local. `resourceZones`
    // (staged by fetch-resilience) covers the types whose zones live outside
    // `properties` (VMs, VMSS, Redis, App Gateway); the rest read staged ARM
    // properties. The Config column shows WHY the level is what it is.
    static List<RedundancyRow> BuildRedundancy(List<ResourceInfo> resources, JsonElement? resilience)
    {
        var zonesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var avSetById = new HashSet<string>(StringComparer.Ordinal);
        if (resilience is { } res && res.TryGetProperty("resourceZones", out var rz) && rz.ValueKind == JsonValueKind.Array)
        {
            foreach (var z in rz.EnumerateArray())
            {
                if (Helpers.Str(z, "id") is not { } id) continue;
                var idLower = id.ToLowerInvariant();
                var zones = new List<string>();
                if (z.TryGetProperty("zones", out var za) && za.ValueKind == JsonValueKind.Array)
                    foreach (var v in za.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.String) zones.Add(v.GetString()!);
                zonesById[idLower] = zones;
                if (Helpers.Str(z, "availabilitySetId") is not null) avSetById.Add(idLower);
            }
        }

        var rows = new List<RedundancyRow>();
        void Row(ResourceInfo r, string level, string config)
        {
            var required = r.Environment == "Dev/Test" ? "Local" : "Zona";
            var status = Helpers.LevelRank(level) >= Helpers.LevelRank(required) ? "OK" : "Advertencia";
            rows.Add(new(r.Name, r.FriendlyType, r.Environment, level, config, status));
        }

        foreach (var r in resources)
        {
            var type = r.Type.ToLowerInvariant();
            var props = r.Properties;
            switch (type)
            {
                case "microsoft.storage/storageaccounts":
                {
                    var sku = r.SkuName ?? "—";
                    var level = sku.Contains("GZRS", StringComparison.OrdinalIgnoreCase)
                            || sku.Contains("GRS", StringComparison.OrdinalIgnoreCase) ? "Regional"
                        : sku.Contains("ZRS", StringComparison.OrdinalIgnoreCase) ? "Zona"
                        : "Local";
                    Row(r, level, $"SKU {sku}");
                    break;
                }
                case "microsoft.web/serverfarms":
                {
                    var zr = Helpers.BoolProp(props, "zoneRedundant");
                    Row(r, zr ? "Zona" : "Local", $"SKU {r.SkuName ?? "—"}{(zr ? ", zoneRedundant" : "")}");
                    break;
                }
                case "microsoft.app/managedenvironments":
                {
                    var zr = Helpers.BoolProp(props, "zoneRedundant");
                    Row(r, zr ? "Zona" : "Local", zr ? "zoneRedundant habilitado" : "Sin zoneRedundant");
                    break;
                }
                case "microsoft.sql/servers/databases":
                {
                    if (r.Name.Equals("master", StringComparison.OrdinalIgnoreCase)) break;
                    var backupRedundancy = Helpers.Str(props, "currentBackupStorageRedundancy")
                        ?? Helpers.Str(props, "requestedBackupStorageRedundancy") ?? "—";
                    Row(r, Helpers.BoolProp(props, "zoneRedundant") ? "Zona" : "Local",
                        $"{r.SkuName ?? r.SkuTier ?? "—"}, backup {backupRedundancy}");
                    break;
                }
                case "microsoft.documentdb/databaseaccounts":
                {
                    var anyZr = false;
                    var locs = 0;
                    if (props.ValueKind == JsonValueKind.Object
                        && props.TryGetProperty("locations", out var la) && la.ValueKind == JsonValueKind.Array)
                        foreach (var loc in la.EnumerateArray())
                        {
                            locs++;
                            if (Helpers.BoolProp(loc, "isZoneRedundant")) anyZr = true;
                        }
                    var level = locs > 1 ? "Regional" : anyZr ? "Zona" : "Local";
                    Row(r, level, $"{Math.Max(locs, 1)} región(es){(anyZr ? ", zonas" : "")}");
                    break;
                }
                case "microsoft.cache/redis":
                {
                    var zones = zonesById.GetValueOrDefault(r.IdLower);
                    Row(r, zones is { Count: > 0 } ? "Zona" : "Local",
                        $"SKU {Helpers.EffectiveSkuName(r) ?? "—"}{(zones is { Count: > 0 } ? $", zonas {string.Join(",", zones)}" : "")}");
                    break;
                }
                case "microsoft.compute/virtualmachines":
                {
                    var zones = zonesById.GetValueOrDefault(r.IdLower);
                    if (zones is { Count: > 0 }) Row(r, "Zona", $"Zona {string.Join(",", zones)}");
                    else Row(r, "Local", avSetById.Contains(r.IdLower) ? "Availability Set (sin zona)" : "Instancia única");
                    break;
                }
                case "microsoft.compute/virtualmachinescalesets":
                {
                    var zones = zonesById.GetValueOrDefault(r.IdLower);
                    Row(r, zones is { Count: > 0 } ? "Zona" : "Local",
                        zones is { Count: > 0 } ? $"Zonas {string.Join(",", zones)}" : "Sin zonas");
                    break;
                }
                case "microsoft.network/applicationgateways":
                {
                    var zones = zonesById.GetValueOrDefault(r.IdLower);
                    Row(r, zones is { Count: > 0 } ? "Zona" : "Local",
                        $"SKU {Helpers.EffectiveSkuName(r) ?? "—"}{(zones is { Count: > 0 } ? $", zonas {string.Join(",", zones)}" : "")}");
                    break;
                }
                case "microsoft.dbformysql/flexibleservers":
                case "microsoft.dbforpostgresql/flexibleservers":
                {
                    string? haMode = null, geoBackup = null;
                    if (props.ValueKind == JsonValueKind.Object)
                    {
                        if (props.TryGetProperty("highAvailability", out var ha)) haMode = Helpers.Str(ha, "mode");
                        if (props.TryGetProperty("backup", out var b)) geoBackup = Helpers.Str(b, "geoRedundantBackup");
                    }
                    var zonal = string.Equals(haMode, "ZoneRedundant", StringComparison.OrdinalIgnoreCase);
                    Row(r, zonal ? "Zona" : "Local",
                        $"HA {haMode ?? "Disabled"}, geo-backup {geoBackup ?? "—"}");
                    break;
                }
            }
        }

        return rows
            .OrderBy(x => Helpers.FindingRank(x.Status))
            .ThenBy(x => Helpers.LevelRank(x.Level))
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Scaling posture across compute + databases: App Service plans and VMSS
    // need an autoscale setting, Container Apps need scale rules with headroom,
    // SQL fixed provisioning is informational (serverless / elastic pool are
    // the autoscaling shapes).
    static List<ScaleRow> BuildScaling(List<ResourceInfo> resources, JsonElement? resilience)
    {
        var autoscaleByTarget = new Dictionary<string, (bool enabled, string? min, string? max)>(StringComparer.Ordinal);
        if (resilience is { } res && res.TryGetProperty("autoscaleSettings", out var asArr) && asArr.ValueKind == JsonValueKind.Array)
            foreach (var a in asArr.EnumerateArray())
                if (Helpers.Str(a, "targetResourceUri") is { } uri)
                    autoscaleByTarget[uri.ToLowerInvariant()] = (
                        a.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
                        Helpers.Str(a, "minCapacity"), Helpers.Str(a, "maxCapacity"));

        var rows = new List<ScaleRow>();
        void Row(ResourceInfo r, string config, string finding, string severity) =>
            rows.Add(new(r.Name, r.FriendlyType, r.Environment, config, finding, severity));

        foreach (var r in resources)
        {
            var type = r.Type.ToLowerInvariant();
            switch (type)
            {
                case "microsoft.web/serverfarms":
                {
                    var tier = r.SkuTier ?? "";
                    if (tier.Length == 0
                        || tier.Contains("Free", StringComparison.OrdinalIgnoreCase)
                        || tier.Contains("Shared", StringComparison.OrdinalIgnoreCase)
                        || tier.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                        || tier.Contains("Dynamic", StringComparison.OrdinalIgnoreCase)) break;
                    if (autoscaleByTarget.TryGetValue(r.IdLower, out var a))
                    {
                        if (a.enabled) Row(r, $"Autoscale {a.min ?? "?"}–{a.max ?? "?"} instancias", "OK", "OK");
                        else Row(r, $"Autoscale {a.min ?? "?"}–{a.max ?? "?"} (deshabilitado)",
                            "Autoscale deshabilitado", Helpers.EnvSeverity(r.Environment, "Advertencia"));
                    }
                    else Row(r, $"SKU {r.SkuName ?? tier}, {r.SkuCapacity?.ToString(CultureInfo.InvariantCulture) ?? "?"} instancias fijas",
                        "Sin autoscale", Helpers.EnvSeverity(r.Environment, "Advertencia"));
                    break;
                }
                case "microsoft.compute/virtualmachinescalesets":
                {
                    if (autoscaleByTarget.TryGetValue(r.IdLower, out var a) && a.enabled)
                        Row(r, $"Autoscale {a.min ?? "?"}–{a.max ?? "?"} instancias", "OK", "OK");
                    else
                        Row(r, $"{r.SkuCapacity?.ToString(CultureInfo.InvariantCulture) ?? "?"} instancias fijas",
                            "Sin autoscale", Helpers.EnvSeverity(r.Environment, "Advertencia"));
                    break;
                }
                case "microsoft.app/containerapps":
                {
                    string? min = null, max = null;
                    var ruleCount = 0;
                    if (r.Properties.ValueKind == JsonValueKind.Object
                        && r.Properties.TryGetProperty("template", out var t) && t.ValueKind == JsonValueKind.Object
                        && t.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Object)
                    {
                        min = Helpers.NumStr(sc, "minReplicas");
                        max = Helpers.NumStr(sc, "maxReplicas");
                        if (sc.TryGetProperty("rules", out var ru) && ru.ValueKind == JsonValueKind.Array)
                            ruleCount = ru.GetArrayLength();
                    }
                    var fixedScale = ruleCount == 0 && (max is null || max == min);
                    Row(r, $"Réplicas {min ?? "0"}–{max ?? "?"}, {ruleCount} regla(s)",
                        fixedScale ? "Sin reglas de escalado" : "OK",
                        fixedScale ? Helpers.EnvSeverity(r.Environment, "Advertencia") : "OK");
                    break;
                }
                case "microsoft.sql/servers/databases":
                {
                    if (r.Name.Equals("master", StringComparison.OrdinalIgnoreCase)) break;
                    var sku = r.SkuName ?? r.SkuTier ?? "—";
                    var elasticPool = r.Properties.ValueKind == JsonValueKind.Object
                        && Helpers.Str(r.Properties, "elasticPoolId") is not null;
                    if (sku.Contains("_S_", StringComparison.OrdinalIgnoreCase))
                        Row(r, $"{sku} (serverless)", "OK", "OK");
                    else if (elasticPool)
                        Row(r, "Elastic Pool", "OK", "OK");
                    else
                        Row(r, $"{sku} aprovisionado fijo", "Escala fija (evaluar serverless/elastic pool)", "Informativo");
                    break;
                }
            }
        }

        return rows
            .OrderBy(s => Helpers.FindingRank(s.Severity))
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // WAF coverage per application. MANDATORY rule: every Prod app must publish
    // through a WAF-fronted gateway AND keep its direct endpoint restricted.
    // Detection: an app is "behind the WAF" when one of its hostnames/FQDN
    // appears in a backend pool of a WAF-enabled Application Gateway; the
    // direct endpoint is "open" per fetch-security's config/web summary
    // (access restrictions + publicNetworkAccess) or the Container App
    // ingress restrictions. Container Apps whose managed environment is
    // internal (vnetConfiguration.internal) have NO internet exposure even
    // with external ingress — the environment's load balancer is private and
    // the FQDN only resolves inside the VNet. Env-weighted: direct traffic on
    // Prod → Crítico, bypassable WAF → Advertencia, Dev/Test → Informativo.
    static List<ExposureRow> BuildWafExposure(List<ResourceInfo> resources, JsonElement? security)
    {
        var internalEnvs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in resources.Where(r =>
            r.Type.Equals("microsoft.app/managedenvironments", StringComparison.OrdinalIgnoreCase)))
            if (env.Properties.ValueKind == JsonValueKind.Object
                && env.Properties.TryGetProperty("vnetConfiguration", out var vc) && vc.ValueKind == JsonValueKind.Object
                && Helpers.BoolProp(vc, "internal"))
                internalEnvs.Add(env.Id);

        var wafFqdns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nonWafFqdns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gw in resources.Where(r =>
            r.Type.Equals("microsoft.network/applicationgateways", StringComparison.OrdinalIgnoreCase)))
        {
            if (gw.Properties.ValueKind != JsonValueKind.Object) continue;
            var hasWaf = gw.Properties.TryGetProperty("firewallPolicy", out _)
                || (gw.Properties.TryGetProperty("webApplicationFirewallConfiguration", out var wc)
                    && Helpers.BoolProp(wc, "enabled"));
            if (!gw.Properties.TryGetProperty("backendAddressPools", out var pools) || pools.ValueKind != JsonValueKind.Array) continue;
            foreach (var pool in pools.EnumerateArray())
            {
                if (!pool.TryGetProperty("properties", out var pp) || pp.ValueKind != JsonValueKind.Object
                    || !pp.TryGetProperty("backendAddresses", out var addrs) || addrs.ValueKind != JsonValueKind.Array) continue;
                foreach (var a in addrs.EnumerateArray())
                    if (Helpers.Str(a, "fqdn") is { Length: > 0 } fqdn)
                        (hasWaf ? wafFqdns : nonWafFqdns).Add(fqdn);
            }
        }

        var openById = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (security is { } sec && sec.TryGetProperty("webAccessRestrictions", out var war) && war.ValueKind == JsonValueKind.Array)
            foreach (var w in war.EnumerateArray())
                if (Helpers.Str(w, "siteId") is { } sid)
                    openById[sid] = w.TryGetProperty("openToAll", out var op) && op.ValueKind == JsonValueKind.True;

        var rows = new List<ExposureRow>();
        void Classify(ResourceInfo r, bool behindWaf, bool behindNonWaf, bool directOpen, bool directKnown)
        {
            if (behindWaf && !directOpen)
                rows.Add(new(r.Name, r.FriendlyType, r.Environment, "WAF + endpoint directo restringido", "OK", "OK"));
            else if (behindWaf)
                rows.Add(new(r.Name, r.FriendlyType, r.Environment,
                    directKnown ? "WAF, pero endpoint directo abierto" : "WAF; restricciones no verificadas",
                    "Bypass posible del WAF", Helpers.EnvSeverity(r.Environment, "Advertencia")));
            else if (directKnown && !directOpen)
                rows.Add(new(r.Name, r.FriendlyType, r.Environment, "Sin WAF; acceso restringido/privado", "OK", "OK"));
            else if (behindNonWaf)
                rows.Add(new(r.Name, r.FriendlyType, r.Environment, "Detrás de App Gateway sin WAF",
                    "Tráfico no inspeccionado por WAF", Helpers.EnvSeverity(r.Environment, "Advertencia")));
            else
                rows.Add(new(r.Name, r.FriendlyType, r.Environment,
                    directKnown ? "Endpoint público abierto" : "Endpoint público (restricciones no verificadas)",
                    "Tráfico directo sin WAF", Helpers.EnvSeverity(r.Environment, "Crítico")));
        }

        foreach (var site in resources.Where(r => r.Type.Equals("microsoft.web/sites", StringComparison.OrdinalIgnoreCase)))
        {
            var props = site.Properties;
            var hostnames = new List<string>();
            var pnaDisabled = false;
            if (props.ValueKind == JsonValueKind.Object)
            {
                if (Helpers.Str(props, "defaultHostName") is { } dh) hostnames.Add(dh);
                foreach (var prop in new[] { "hostNames", "enabledHostNames" })
                    if (props.TryGetProperty(prop, out var hn) && hn.ValueKind == JsonValueKind.Array)
                        foreach (var h in hn.EnumerateArray())
                            if (h.ValueKind == JsonValueKind.String) hostnames.Add(h.GetString()!);
                pnaDisabled = string.Equals(Helpers.Str(props, "publicNetworkAccess"), "Disabled", StringComparison.OrdinalIgnoreCase);
            }
            var behindWaf = hostnames.Any(wafFqdns.Contains);
            var behindNonWaf = hostnames.Any(nonWafFqdns.Contains);
            var directKnown = pnaDisabled || openById.ContainsKey(site.Id);
            var directOpen = !pnaDisabled && (!openById.TryGetValue(site.Id, out var open) || open);
            Classify(site, behindWaf, behindNonWaf, directOpen, directKnown);
        }

        foreach (var ca in resources.Where(r => r.Type.Equals("microsoft.app/containerapps", StringComparison.OrdinalIgnoreCase)))
        {
            var external = false;
            string? fqdn = null;
            var restricted = false;
            if (ca.Properties.ValueKind == JsonValueKind.Object
                && ca.Properties.TryGetProperty("configuration", out var cfg) && cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("ingress", out var ingress) && ingress.ValueKind == JsonValueKind.Object)
            {
                external = Helpers.BoolProp(ingress, "external");
                fqdn = Helpers.Str(ingress, "fqdn");
                restricted = ingress.TryGetProperty("ipSecurityRestrictions", out var isr)
                    && isr.ValueKind == JsonValueKind.Array && isr.GetArrayLength() > 0;
            }
            if (!external)
            {
                rows.Add(new(ca.Name, ca.FriendlyType, ca.Environment, "Ingress interno", "OK", "OK"));
                continue;
            }
            var envId = ca.Properties.ValueKind == JsonValueKind.Object
                ? Helpers.Str(ca.Properties, "managedEnvironmentId") ?? Helpers.Str(ca.Properties, "environmentId")
                : null;
            if (envId is not null && internalEnvs.Contains(envId))
            {
                rows.Add(new(ca.Name, ca.FriendlyType, ca.Environment,
                    "Entorno interno (sin exposición a internet)", "OK", "OK"));
                continue;
            }
            var behindWaf = fqdn is not null && wafFqdns.Contains(fqdn);
            var behindNonWaf = fqdn is not null && nonWafFqdns.Contains(fqdn);
            Classify(ca, behindWaf, behindNonWaf, directOpen: !restricted, directKnown: true);
        }

        return rows
            .OrderBy(x => Helpers.FindingRank(x.Severity))
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Infra resources (DB servers, storage, Key Vault, Cosmos, Redis) that
    // accept traffic from ANY origin. Deliberately laxer than the app WAF rule:
    // open-to-any on Prod (or unclassified) is Advertencia, Dev/Test degrades
    // to Informativo. "Any origin" markers: the 0.0.0.0-255.255.255.255
    // firewall rule (DB servers, staged by fetch-security), networkAcls
    // defaultAction Allow (storage / Key Vault), no ip/VNet rules (Cosmos),
    // publicNetworkAccess enabled (Redis — auth by key still applies).
    static List<ExposureRow> BuildInfraExposure(List<ResourceInfo> resources, JsonElement? security)
    {
        var rulesByServer = new Dictionary<string, List<(string? start, string? end)>>(StringComparer.OrdinalIgnoreCase);
        var rulesStaged = false;
        if (security is { } sec && sec.TryGetProperty("dbFirewallRules", out var fw) && fw.ValueKind == JsonValueKind.Array)
            foreach (var rule in fw.EnumerateArray())
                if (Helpers.Str(rule, "serverId") is { } sid)
                {
                    rulesStaged = true;
                    var list = rulesByServer.TryGetValue(sid, out var existing) ? existing : rulesByServer[sid] = new();
                    list.Add((Helpers.Str(rule, "startIpAddress"), Helpers.Str(rule, "endIpAddress")));
                }

        var rows = new List<ExposureRow>();
        void Open(ResourceInfo r, string exposure) =>
            rows.Add(new(r.Name, r.FriendlyType, r.Environment, exposure,
                "Acepta tráfico de cualquier origen", Helpers.EnvSeverity(r.Environment, "Advertencia")));
        void Ok(ResourceInfo r, string exposure) =>
            rows.Add(new(r.Name, r.FriendlyType, r.Environment, exposure, "OK", "OK"));

        static bool AclsDeny(JsonElement props, out int exceptions)
        {
            exceptions = 0;
            if (props.ValueKind != JsonValueKind.Object
                || !props.TryGetProperty("networkAcls", out var acls) || acls.ValueKind != JsonValueKind.Object)
                return false;   // no ACLs configured = default Allow
            if (acls.TryGetProperty("ipRules", out var ip) && ip.ValueKind == JsonValueKind.Array) exceptions += ip.GetArrayLength();
            if (acls.TryGetProperty("virtualNetworkRules", out var vn) && vn.ValueKind == JsonValueKind.Array) exceptions += vn.GetArrayLength();
            return string.Equals(Helpers.Str(acls, "defaultAction"), "Deny", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var r in resources)
        {
            var type = r.Type.ToLowerInvariant();
            var props = r.Properties;
            switch (type)
            {
                case "microsoft.sql/servers":
                case "microsoft.dbformysql/flexibleservers":
                case "microsoft.dbforpostgresql/flexibleservers":
                {
                    var pna = Helpers.Str(props, "publicNetworkAccess");
                    if (pna is null && props.ValueKind == JsonValueKind.Object && props.TryGetProperty("network", out var net))
                        pna = Helpers.Str(net, "publicNetworkAccess");
                    if (string.Equals(pna, "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        Ok(r, "Acceso público deshabilitado");
                        break;
                    }
                    var rules = rulesByServer.GetValueOrDefault(r.Id);
                    if (rules?.Any(x => x.start == "0.0.0.0" && x.end == "255.255.255.255") == true)
                        Open(r, "Regla de firewall 0.0.0.0–255.255.255.255");
                    else if (rules is { Count: > 0 })
                        Ok(r, $"Público con {rules.Count} regla(s) de firewall específicas");
                    else if (rulesStaged)
                        Ok(r, "Público sin reglas de firewall (ninguna IP permitida)");
                    else
                        rows.Add(new(r.Name, r.FriendlyType, r.Environment,
                            "Público; reglas de firewall no verificadas", "Exposición no verificada", "Informativo"));
                    break;
                }
                case "microsoft.storage/storageaccounts":
                case "microsoft.keyvault/vaults":
                {
                    if (string.Equals(Helpers.Str(props, "publicNetworkAccess"), "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        Ok(r, "Acceso público deshabilitado");
                        break;
                    }
                    if (AclsDeny(props, out var exceptions))
                        Ok(r, $"networkAcls Deny + {exceptions} excepción(es)");
                    else
                        Open(r, "networkAcls con defaultAction Allow");
                    break;
                }
                case "microsoft.documentdb/databaseaccounts":
                {
                    if (string.Equals(Helpers.Str(props, "publicNetworkAccess"), "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        Ok(r, "Acceso público deshabilitado");
                        break;
                    }
                    var ipRuleCount = props.ValueKind == JsonValueKind.Object
                        && props.TryGetProperty("ipRules", out var ipr) && ipr.ValueKind == JsonValueKind.Array
                            ? ipr.GetArrayLength() : 0;
                    var vnetFilter = Helpers.BoolProp(props, "isVirtualNetworkFilterEnabled");
                    if (ipRuleCount > 0 || vnetFilter)
                        Ok(r, $"Restringido ({ipRuleCount} IP rule(s){(vnetFilter ? ", VNet filter" : "")})");
                    else
                        Open(r, "Sin restricciones de red");
                    break;
                }
                case "microsoft.cache/redis":
                {
                    var pna = Helpers.Str(props, "publicNetworkAccess") ?? "Enabled";
                    if (string.Equals(pna, "Disabled", StringComparison.OrdinalIgnoreCase))
                        Ok(r, "Acceso público deshabilitado");
                    else
                        Open(r, "Acceso público habilitado (autenticación por clave)");
                    break;
                }
            }
        }

        return rows
            .OrderBy(x => Helpers.FindingRank(x.Severity))
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

// =====================================================================
// Section builders.
// =====================================================================

static class Builders
{
    // FIXED TEMPLATE: every section always emits its canonical blocks in this
    // exact order, every run — an empty block renders "Sin hallazgos" instead
    // of disappearing, so consecutive monthly reports are structurally identical.

    // ---- 1. costos ----
    // Everything money-related consolidated in one section: the spend trend, the
    // top consumers, and the recoverable cost (zombies, oversized, unused
    // resources) with Advisor Cost. Saturation is a performance risk, not a
    // savings opportunity — it reports under `rendimiento`.
    public static object Costos(Model m)
    {
        var blocks = new List<object>();

        var monthlyTotals = m.Months.Select(month => new
        {
            month,
            value = m.Costs.Where(c => c.Month == month).Sum(c => c.Value),
        }).ToList();
        var trendTotals = new List<object>();
        for (var i = 0; i < monthlyTotals.Count; i++)
        {
            var cur = monthlyTotals[i].value;
            var (delta, dir) = Helpers.Delta(cur, i > 0 ? monthlyTotals[i - 1].value : (double?)null);
            trendTotals.Add(new { month = monthlyTotals[i].month, value = Math.Round(cur, 2), delta, direction = dir });
        }
        blocks.Add(Block("cost-trend", "Tendencia de Costos por Mes", "MonthlyTrend",
            "Evolución del gasto total de la suscripción por mes; la variación bajo cada columna compara contra el mes anterior.",
            new { months = m.Months, totals = trendTotals }));

        blocks.Add(GroupedMonthly(m, "cost-by-service", "Costos por Tipo de Servicio (Top 5)",
            "Costo mensual agrupado por tipo de servicio, ordenado por el gasto del mes actual; los meses previos son referencia comparativa.",
            c => m.ResourceByIdLower.TryGetValue(c.CloudId, out var r) ? r.FriendlyType : Helpers.FriendlyTypeFromArmId(c.CloudId),
            topN: 5));

        blocks.Add(GroupedMonthly(m, "cost-top-resources", "Recursos más Costosos (Top 5)",
            "Los recursos individuales con mayor costo del mes actual; los meses previos son referencia comparativa.",
            c => Helpers.LabelFor(m.ResourceByIdLower, c.CloudId), topN: 5));

        var counts = m.Utilization.GroupBy(u => u.Classification).ToDictionary(g => g.Key, g => g.Count());
        blocks.Add(Block("sizing-summary", "Clasificación de Recursos Medidos (CPU)", "BarList",
            "Clasificación por uso de CPU de los recursos medidos durante el mes de análisis; el detalle de los saturados se presenta en la sección Rendimiento.",
            new
            {
                format = "count",
                rows = new object[]
                {
                    new { label = "Zombie (sin uso real)", value = counts.GetValueOrDefault("Zombie", 0), color = "rojo" },
                    new { label = "Sobredimensionado", value = counts.GetValueOrDefault("Sobredimensionado", 0), color = "naranja" },
                    new { label = "Saturado", value = counts.GetValueOrDefault("Saturado", 0), color = "naranja" },
                    new { label = "Dimensionamiento correcto", value = counts.GetValueOrDefault("Correcto", 0), color = "verde" },
                },
            }));

        // Compactness: only cost-recoverable problems (Zombie + Sobredimensionado)
        // with real monthly cost make the table, ranked by cost; zero-cost
        // problems are rolled up into one closing row.
        var problems = m.Utilization.Where(u => u.Classification is "Zombie" or "Sobredimensionado")
            .OrderByDescending(u => u.MonthlyCost).ToList();
        var costly = problems.Where(u => u.MonthlyCost >= 0.5).Take(12).ToList();
        var residual = problems.Where(u => !costly.Contains(u)).ToList();
        var rows = costly.Select(u => new object?[]
        {
            u.Label, u.FriendlyType, u.Sku ?? "—", u.Environment,
            $"{u.AvgCpu.ToString("0.0", CultureInfo.InvariantCulture)}%",
            $"{u.IdlePct.ToString("0.0", CultureInfo.InvariantCulture)}%",
            Helpers.Usd(u.MonthlyCost), u.Classification,
        }).ToList();
        foreach (var g in residual
            .GroupBy(u => (u.FriendlyType, u.Environment, u.Classification))
            .OrderByDescending(g => g.Sum(u => u.MonthlyCost)))
            rows.Add(new object?[]
            {
                Helpers.NameList(g.Select(u => u.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, "—", g.Key.Environment, "—", "—",
                Helpers.Usd(Math.Round(g.Sum(u => u.MonthlyCost), 2)), g.Key.Classification,
            });
        blocks.Add(Block("utilization", "Recursos Zombie y Sobredimensionados", "Table",
            $"Recursos con costo recuperable — zombies (sin uso real) y sobredimensionados — ordenados por costo mensual; los de costo cercano a $0 se agrupan al final por tipo y clasificación. {counts.GetValueOrDefault("Correcto", 0)} recursos están correctamente dimensionados.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "SKU", "Entorno", "CPU prom.", "% Inactivo", "Costo mes", "Clasificación" },
                align = new[] { "l", "l", "c", "c", "r", "r", "r", "c" },
                rows,
                dense = true,
            }));

        var unusedRows = m.Unused.Take(10)
            .Select(u => new object?[] { u.Label, u.FriendlyType, u.Finding, Helpers.Usd(u.MonthlyCost) }).ToList();
        foreach (var g in m.Unused.Skip(10)
            .GroupBy(u => (u.FriendlyType, u.Finding))
            .OrderByDescending(g => g.Sum(u => u.MonthlyCost)))
            unusedRows.Add(new object?[]
            {
                Helpers.NameList(g.Select(u => u.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, g.Key.Finding,
                Helpers.Usd(Math.Round(g.Sum(u => u.MonthlyCost), 2)),
            });
        blocks.Add(Block("unused", "Recursos sin Uso", "Table",
            "Recursos detectablemente sin uso según su estado en Azure (discos sin asociar, IPs públicas libres, planes de App Service vacíos) y el costo mensual que siguen generando.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "Hallazgo", "Costo mes" },
                align = new[] { "l", "l", "l", "r" },
                rows = unusedRows,
            }));

        blocks.Add(AdvisorTable(m, "Cost", "advisor-cost", "Recomendaciones de Costo (Azure Advisor)", includeSavings: true));

        return Section("costos", "Costos", blocks);
    }

    // ---- WAF sub-report (blocks inside `seguridad`) ----
    // Fixed skeleton with sources MERGED — block ids never vary with the log mode
    // (AzureDiagnostics vs dedicated tables) or the number of WAF products.
    // Unit discipline: "peticiones" = distinct transactions (what a reader calls
    // an attack); "eventos" = rule-match log rows (one request fires N rules).
    // Every figure comes from WafFacts, which signals reuse 1:1.
    static void AddWafBlocks(Model m, WafFacts w, List<object> blocks)
    {
        // 1. Month summary — the leadership view: how much, how much stopped, how much data.
        var coverage = !w.HasLogs ? "—" : $"{w.ActiveDays} de {w.MonthDays} días";
        var coverageNote = !w.HasLogs
            ? "Sin logs de firewall en el workspace para este mes"
            : w.ActiveDays < w.MonthDays
                ? "Cobertura parcial: el workspace solo retiene (o solo recibe) logs de parte del mes; las cifras cubren esos días"
                : "Días del mes con logs de firewall disponibles";
        var policyLine = w.Policies.Count == 0 ? "0"
            : $"{w.Policies.Count} (Prevention {w.PreventionCount} · Detection {w.DetectionCount})";
        object V(long value) => w.HasLogs ? value : "—";
        var summaryRows = new List<object?[]>
        {
            new object?[] { "Cobertura de datos", coverage, coverageNote },
            new object?[] { "Peticiones con detección", V(w.Requests), "Peticiones que activaron al menos una regla del WAF (una petición puede generar varios eventos)" },
            new object?[] { "Peticiones bloqueadas", w.HasLogs ? $"{w.Blocked:#,0} ({w.BlockedPct:0.0}%)" : "—", "Rechazadas por políticas en Prevention: el WAF cumpliendo su función" },
            new object?[] { "Peticiones solo detectadas", V(w.Detected), "Registradas por políticas en Detection: habrían sido bloqueadas en Prevention" },
            new object?[] { "Permitidas tras evaluación", V(w.Allowed + w.Challenged), "Bots verificados permitidos y desafíos JavaScript superados" },
            new object?[] { "IPs de origen distintas", V(w.DistinctIps), "Orígenes únicos con actividad detectada" },
            new object?[] { "Aplicaciones con ataques", V(w.DistinctHosts), "Hosts distintos que recibieron peticiones detectadas" },
            new object?[] { "Políticas WAF", policyLine, "Políticas desplegadas por modo de operación" },
            new object?[] { "Falsos positivos candidatos", w.HasLogs ? w.FpCandidates.Count : "—", "Reglas concentradas en una sola URI; revisar antes de crear exclusiones" },
        };
        blocks.Add(Block("waf-summary", "WAF — Resumen del Mes", "Table",
            w.HasLogs
                ? "Lectura rápida del Web Application Firewall en el mes de análisis: volumen de peticiones con detección, cuántas se bloquearon, cobertura de datos y tamaño del inventario."
                : "Hay WAF desplegado pero no se recolectaron logs de firewall (sin workspace o diagnósticos no conectados a Log Analytics); habilitarlos es el prerrequisito de los bloques de actividad.",
            new { headers = new[] { "Indicador", "Valor", "Lectura" }, align = new[] { "l", "r", "l" }, widths = new[] { "1.4fr", "auto", "2fr" }, rows = summaryRows }));

        // 2. Inventory tied to activity — each policy with scope, rules, and this month's requests.
        var inventoryRows = w.Policies.Select(p => (object?[])new object?[]
        {
            p.Name, p.Scope, p.Mode, p.RuleSets, p.CustomAndExclusions,
            w.HasLogs ? p.Requests : "—",
            !w.HasLogs ? "—" : p.IsDetection ? $"{p.Detected:#,0} (detect.)" : p.Blocked,
        }).ToList();
        blocks.Add(Block("waf-inventory", "WAF — Inventario y Actividad por Política", "Table",
            "Políticas WAF y configuraciones integradas de App Gateway con su alcance (gateway · listeners), modo, conjunto de reglas, reglas personalizadas / exclusiones y la actividad del mes; en políticas en Detection la última columna muestra las peticiones detectadas, que habrían sido bloqueadas en Prevention.",
            new
            {
                headers = new[] { "Política", "Alcance", "Modo", "Conjunto de reglas", "Custom / Excl.", "Peticiones", "Bloqueadas" },
                align = new[] { "l", "l", "c", "l", "c", "r", "r" },
                rows = inventoryRows,
                dense = true,
            }));

        // 3. Cost — what the WAF costs vs. how many apps it protects.
        var costRows = w.Gateways.Select(g => (object?[])new object?[]
        {
            g.Label, g.Sku, g.ProtectedApps is null ? "—" : g.ProtectedApps, w.HasLogs ? g.Requests : "—",
            Helpers.Usd(g.Cost), Helpers.DeltaPct(g.Cost, g.PrevCost),
        }).ToList();
        if (w.Gateways.Count > 1)
            costRows.Add(new object?[]
            {
                "Total", "—", w.ProtectedApps, w.HasLogs ? w.Gateways.Sum(g => g.Requests) : "—",
                Helpers.Usd(w.MonthlyCost), Helpers.DeltaPct(w.MonthlyCost, w.PrevMonthlyCost),
            });
        var costNote = w.Gateways.Count == 0
            ? "No hay gateways con WAF (App Gateway WAF / Front Door con política WAF) en la suscripción."
            : $"El WAF se factura dentro del gateway (las políticas WAF no tienen costo propio). Costo total {Helpers.Usd(w.MonthlyCost)}/mes" +
              (w.ProtectedApps > 0 ? $", {Helpers.Usd(w.CostPerProtectedApp)} por aplicación protegida ({w.ProtectedApps})" : ", sin aplicaciones detectadas detrás del WAF") +
              (w.DirectProdApps > 0 ? $"; {w.DirectProdApps} aplicaciones Prod siguen recibiendo tráfico directo sin WAF." : ".");
        blocks.Add(Block("waf-cost", "WAF — Costo del Servicio", "Table", costNote,
            new
            {
                headers = new[] { "Recurso", "SKU", "Apps protegidas", "Peticiones con detección", "Costo mes", "Variación" },
                align = new[] { "l", "c", "r", "r", "r", "r" },
                rows = costRows,
                dense = true,
            }));

        // 4. Daily trend — bursts vs. background noise, and the data-coverage gap made visible.
        var peak = w.Days.Where(d => d.HasData).OrderByDescending(d => d.Requests).FirstOrDefault();
        blocks.Add(Block("waf-daily", "WAF — Peticiones por Día", "DailyBars",
            w.HasLogs
                ? $"Peticiones con detección por día del mes (rojo = bloqueadas); los días sin barra no tienen logs en el workspace. Pico: {peak?.Requests ?? 0:#,0} peticiones el {Helpers.DayLabel(peak?.Day)}."
                : "Peticiones con detección por día del mes; sin logs de firewall en el período.",
            new
            {
                days = w.Days.Select(d => new { day = d.Day, label = d.Day.Length >= 10 ? d.Day[8..].TrimStart('0') : d.Day, requests = d.Requests, blocked = d.Blocked, detected = d.Detected, hasData = d.HasData }).ToList(),
                daysWithoutData = w.Days.Count(d => !d.HasData),
            }));

        // 5. Attack families — rule ids translated into what a reader understands.
        blocks.Add(Block("waf-categories", "WAF — Ataques por Categoría", "BarList",
            "Peticiones con detección agrupadas por familia de reglas (OWASP CRS / DRS / Bot Manager); rojo = ataques de inyección o explotación, naranja = anomalías de protocolo, escáneres y bots no verificados, azul = bots legítimos verificados.",
            new
            {
                format = "count",
                rows = w.Categories.Take(10).Select(c => new { label = c.Label, value = c.Requests, color = c.Color }).ToList(),
            }));

        // 6. Attacked applications.
        blocks.Add(Block("waf-top-hosts", "WAF — Aplicaciones más Atacadas (Top 8)", "Table",
            "Hosts con más peticiones detectadas en el mes, con la proporción bloqueada y la cantidad de orígenes distintos; un host con muchas IPs y poca proporción bloqueada suele estar detrás de una política en Detection o de un falso positivo.",
            new
            {
                headers = new[] { "Host", "Peticiones", "Bloqueadas", "% bloqueado", "IPs distintas" },
                align = new[] { "l", "r", "r", "r", "r" },
                rows = w.Hosts.Take(8).Select(h => (object?[])new object?[] { h.Host, h.Requests, h.Blocked, Helpers.Pct(h.Blocked, h.Requests), h.DistinctIps }).ToList(),
            }));

        // 7. Rules — with category and friendly rule-set name.
        blocks.Add(Block("waf-top-rules", "WAF — Reglas más Activadas (Top 8)", "Table",
            "Reglas con más peticiones detectadas en el mes; la categoría traduce el identificador y la descripción es el mensaje que registra el WAF.",
            new
            {
                headers = new[] { "Regla", "Categoría", "Conjunto", "Peticiones", "Bloqueadas", "Descripción" },
                align = new[] { "l", "l", "l", "r", "r", "l" },
                rows = w.Rules.Take(8).Select(r => (object?[])new object?[]
                    { r.Rule, r.Category, r.RuleSet, r.Requests, r.Blocked, Helpers.TruncateText(r.Sample, 70) }).ToList(),
                dense = true,
            }));

        // 8. Source IPs — with persistence profile.
        blocks.Add(Block("waf-top-ips", "WAF — IPs de Origen más Activas (Top 8)", "Table",
            "Direcciones IP con más peticiones detectadas; Perfil distingue orígenes persistentes (5 o más días activos, candidatos a regla personalizada de bloqueo) de ráfagas de un día.",
            new
            {
                headers = new[] { "IP", "Peticiones", "Bloqueadas", "Días activos", "Hosts", "Perfil" },
                align = new[] { "l", "r", "r", "r", "r", "l" },
                rows = w.Ips.Take(8).Select(i => (object?[])new object?[] { i.Ip, i.Requests, i.Blocked, i.ActiveDays, i.Hosts, i.Profile }).ToList(),
            }));

        // 9. False-positive candidates — prioritized by real impact.
        blocks.Add(Block("waf-fp-candidates", "WAF — Posibles Falsos Positivos", "Table",
            "Reglas cuyos eventos se concentran (≥80%) en una sola URI con ≥50 eventos, la firma típica de un falso positivo; muchas IPs distintas sobre esa URI refuerzan que es tráfico legítimo. Prioridad Alta = la regla está bloqueando ese tráfico; Media = lo bloquearía al pasar a Prevention; Baja = solo suma a la puntuación de anomalía.",
            new
            {
                headers = new[] { "Regla", "URI", "Acción", "Eventos", "% de la regla", "IPs", "Prioridad" },
                align = new[] { "l", "l", "c", "r", "r", "r", "c" },
                rows = w.FpCandidates.Take(10).Select(f => (object?[])new object?[]
                    { f.Rule, Helpers.TruncateText(f.Uri, 60), f.Action, f.Hits, $"{f.SharePct:0}%", f.DistinctIps, f.Priority }).ToList(),
                dense = true,
            }));
    }

    internal static long Num64(JsonElement row, string prop) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt64(out var l) ? l : (long)v.GetDouble()) : 0;

    // ---- 2. seguridad ----
    // Defender posture first, then the WAF sub-report (the firewall is part of
    // the security story), closing with Advisor Security.
    public static object Seguridad(Model m)
    {
        var blocks = new List<object>();

        // Severity split as a bar list; the description carries the secure score
        // (or its unavailability) so the block id/shape never varies.
        long high = 0, medium = 0, low = 0;
        string scoreNote = "Secure Score no disponible con el acceso actual.";
        var findings = new List<(string name, string severity, int count)>();
        if (m.DefenderPresent && m.Security is { } sec)
        {
            if (sec.TryGetProperty("secureScore", out var score) && score.ValueKind == JsonValueKind.Object)
                scoreNote = $"Secure Score (Defender for Cloud): {Num(score, "percentage").ToString("0.0", CultureInfo.InvariantCulture)}%.";
            if (sec.TryGetProperty("assessmentCounts", out var ac) && ac.ValueKind == JsonValueKind.Object
                && ac.TryGetProperty("bySeverity", out var bySev) && bySev.ValueKind == JsonValueKind.Object)
            {
                high = (long)Num(bySev, "High");
                medium = (long)Num(bySev, "Medium");
                low = (long)Num(bySev, "Low");
            }
            if (sec.TryGetProperty("unhealthyAssessments", out var ua) && ua.ValueKind == JsonValueKind.Array)
            {
                findings = ua.EnumerateArray()
                    .GroupBy(a => (name: Helpers.Str(a, "displayName") ?? "—", severity: Helpers.Str(a, "severity") ?? "Unknown"))
                    .Select(g => (g.Key.name, g.Key.severity, g.Count()))
                    .OrderBy(f => SeverityRank(f.Item2)).ThenByDescending(f => f.Item3)
                    .ToList();
            }
        }
        else
        {
            scoreNote = "Microsoft Defender for Cloud no disponible (o acceso denegado); habilitar al menos Foundational CSPM.";
        }
        blocks.Add(Block("security-severity", "Evaluaciones Incumplidas por Severidad", "BarList",
            $"Evaluaciones de Defender for Cloud en estado no saludable, agrupadas por severidad. {scoreNote}",
            new
            {
                format = "count",
                rows = new object[]
                {
                    new { label = "Alta", value = high, color = "rojo" },
                    new { label = "Media", value = medium, color = "naranja" },
                    new { label = "Baja", value = low, color = "verde" },
                },
            }));

        var findingRows = findings.Take(10)
            .Select(f => new object?[] { TranslateSeverity(f.severity), f.name, f.count }).ToList();
        foreach (var g in findings.Skip(10).GroupBy(f => f.severity).OrderBy(g => SeverityRank(g.Key)))
            findingRows.Add(new object?[]
            {
                TranslateSeverity(g.Key),
                Helpers.NameList(g.Select(f => f.name).ToList()),
                g.Sum(f => f.count),
            });
        blocks.Add(Block("security-findings", "Hallazgos de Seguridad Principales", "Table",
            "Detalle de las evaluaciones de Defender incumplidas más relevantes (severidad Alta primero) con el número de recursos afectados por cada una.",
            new
            {
                headers = new[] { "Severidad", "Hallazgo", "Recursos" },
                align = new[] { "c", "l", "r" },
                rows = findingRows,
                dense = true,
            }));

        var surface = new List<(string, int)>
        {
            ("IPs Públicas", m.Resources.Count(r => r.Type.Equals("microsoft.network/publicipaddresses", StringComparison.OrdinalIgnoreCase))),
            ("Grupos de Seguridad de Red (NSG)", m.Resources.Count(r => r.Type.Equals("microsoft.network/networksecuritygroups", StringComparison.OrdinalIgnoreCase))),
            ("Key Vaults", m.Resources.Count(r => r.Type.Equals("microsoft.keyvault/vaults", StringComparison.OrdinalIgnoreCase))),
            ("Cuentas de Storage con acceso público de blobs", m.Resources.Count(r =>
                r.Type.Equals("microsoft.storage/storageaccounts", StringComparison.OrdinalIgnoreCase)
                && r.Properties.ValueKind == JsonValueKind.Object
                && r.Properties.TryGetProperty("allowBlobPublicAccess", out var ab) && ab.ValueKind == JsonValueKind.True)),
        };
        var (sqlAuditTotal, sqlNoAudit) = SqlAuditing(m);
        surface.Add((sqlAuditTotal > 0
            ? $"Servidores SQL sin auditoría habilitada (de {sqlAuditTotal})"
            : "Servidores SQL sin auditoría habilitada", sqlNoAudit.Count));
        blocks.Add(CountTable("security-surface", "Superficie Expuesta",
            "Inventario de la superficie expuesta de la suscripción: puntos de entrada públicos y controles de acceso relevantes.",
            surface));

        var exposureOk = m.WafExposure.Count(e => e.Severity == "OK");
        var exposureRows = GroupedExposureRows(m.WafExposure);
        blocks.Add(Block("waf-exposure", "Aplicaciones con Tráfico Directo (sin WAF)", "Table",
            $"Evalúa que toda aplicación productiva publique su tráfico a través de un gateway con WAF y mantenga restringido su endpoint directo. Solo se listan hallazgos, agrupados por características compartidas; {exposureOk} aplicaciones están cubiertas correctamente, son privadas o viven en un entorno interno sin exposición a internet.",
            new
            {
                headers = new[] { "Aplicación", "Tipo", "Entorno", "Exposición", "Hallazgo", "Severidad" },
                align = new[] { "l", "l", "c", "l", "l", "c" },
                rows = exposureRows,
                dense = true,
            }));

        var infraOk = m.InfraExposure.Count(e => e.Severity == "OK");
        var infraRows = GroupedExposureRows(m.InfraExposure);
        blocks.Add(Block("infra-exposure", "Infraestructura que Acepta Tráfico de Cualquier Origen", "Table",
            $"Evalúa la configuración de red de la infraestructura de datos (servidores de BD, storage, Key Vault, Cosmos, Redis): aceptar tráfico de cualquier origen es un hallazgo, con una regla más laxa que la de aplicaciones. {infraOk} recursos están restringidos o privados.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "Entorno", "Exposición", "Hallazgo", "Severidad" },
                align = new[] { "l", "l", "c", "l", "l", "c" },
                rows = infraRows,
                dense = true,
            }));

        AddWafBlocks(m, WafFacts.Build(m), blocks);

        blocks.Add(AdvisorTable(m, "Security", "advisor-security", "Recomendaciones de Seguridad (Azure Advisor)"));

        return Section("seguridad", "Seguridad", blocks);
    }

    // Exposure findings grouped by their full characteristics tuple: one row
    // per (type, environment, exposure, finding, severity) with the member
    // names inlined via Helpers.NameList — never a separate "… y N más" row.
    static List<object?[]> GroupedExposureRows(IEnumerable<ExposureRow> exposure) =>
        exposure.Where(e => e.Severity != "OK")
            .GroupBy(e => (e.FriendlyType, e.Environment, e.Exposure, e.Finding, e.Severity))
            .OrderBy(g => Helpers.FindingRank(g.Key.Severity))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key.FriendlyType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new object?[]
            {
                Helpers.NameList(g.Select(e => e.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, g.Key.Environment, g.Key.Exposure, g.Key.Finding, g.Key.Severity,
            }).ToList();

    // SQL server auditing state staged by fetch-security: (total evaluated,
    // names of servers whose auditing is not Enabled).
    static (int total, List<string> withoutAuditing) SqlAuditing(Model m)
    {
        var total = 0;
        var without = new List<string>();
        if (m.Security is { } sec && sec.TryGetProperty("sqlAuditing", out var sa) && sa.ValueKind == JsonValueKind.Array)
            foreach (var s in sa.EnumerateArray())
            {
                total++;
                if (!string.Equals(Helpers.Str(s, "state"), "Enabled", StringComparison.OrdinalIgnoreCase))
                    without.Add(Helpers.Str(s, "name") ?? "—");
            }
        return (total, without);
    }

    static int SeverityRank(string s) => s.ToLowerInvariant() switch { "high" => 0, "medium" => 1, "low" => 2, _ => 3 };
    static string TranslateSeverity(string s) => s.ToLowerInvariant() switch
    { "high" => "Alta", "medium" => "Media", "low" => "Baja", _ => s };

    // ---- 3. fiabilidad ----
    // Backup / recoverability coverage for every family that can and should be
    // protected — not only VMs. Gap severity is environment-weighted. The
    // per-family coverage table was retired: the family totals stay in
    // signals.reliability.families and the block description summarizes them —
    // the section goes straight to the resources lacking protection.
    public static object Fiabilidad(Model m)
    {
        var blocks = new List<object>();

        var protectable = m.BackupFamilies.Sum(f => f.Total);
        var covered = m.BackupFamilies.Sum(f => f.Protected);
        var gapRows = m.BackupGaps
            .GroupBy(g => (g.FriendlyType, g.Environment, g.Finding, g.Severity))
            .OrderBy(g => Helpers.FindingRank(g.Key.Severity))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key.FriendlyType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new object?[]
            {
                Helpers.NameList(g.Select(x => x.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, g.Key.Environment, g.Key.Finding, g.Key.Severity,
            }).ToList();
        blocks.Add(Block("backup-gaps", "Recursos sin Protección Adecuada", "Table",
            $"Evalúa la protección de datos por recurso (Recovery Services, SQL LTR, soft delete de blobs, App Service Backup); solo se listan los recursos sin protección adecuada, con severidad ponderada por entorno. {covered} de {protectable} recursos protegibles ya están cubiertos.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "Entorno", "Hallazgo", "Severidad" },
                align = new[] { "l", "l", "c", "l", "c" },
                rows = gapRows,
                dense = true,
            }));

        blocks.Add(AdvisorTable(m, "HighAvailability", "advisor-reliability", "Recomendaciones de Confiabilidad (Azure Advisor)"));

        return Section("fiabilidad", "Fiabilidad", blocks);
    }

    // ---- 4. disponibilidad ----
    // TLS certificates (App Service + App Gateway listeners) and the unified
    // redundancy posture (levels Local < Zona < Regional; Prod asks for at
    // least Zona, Dev/Test is satisfied by Local). Capped tables close with
    // roll-up rows so the counts always reconcile with the signals.
    public static object Disponibilidad(Model m)
    {
        var blocks = new List<object>();

        var certRows = m.Certificates.Take(12).Select(c => new object?[]
        {
            c.Name, c.Origin, c.Hosts, c.Expires ?? "—", (object?)c.Days ?? "—", c.Status,
        }).ToList();
        foreach (var g in m.Certificates.Skip(12)
            .GroupBy(c => c.Status)
            .OrderBy(g => Helpers.FindingRank(g.Key)))
            certRows.Add(new object?[]
            {
                Helpers.NameList(g.Select(c => c.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                "—", "—", "—", "—", g.Key,
            });
        blocks.Add(Block("certificates", "Certificados TLS (App Service y App Gateway)", "Table",
            "Evalúa la vigencia de los certificados TLS de App Service y de los listeners de App Gateway, ordenados por proximidad de expiración; los referenciados en Key Vault se resuelven contra su fecha real y solo aparecen como 'No verificable' cuando el acceso fue denegado.",
            new
            {
                headers = new[] { "Certificado", "Origen", "Hosts", "Expira", "Días", "Estado" },
                align = new[] { "l", "l", "l", "c", "r", "c" },
                rows = certRows,
                dense = true,
            }));

        var redundancyRows = m.Redundancy
            .GroupBy(x => (x.FriendlyType, x.Environment, x.Level, x.Config, x.Status))
            .OrderBy(g => Helpers.FindingRank(g.Key.Status))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key.FriendlyType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new object?[]
            {
                Helpers.NameList(g.Select(x => x.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, g.Key.Environment, g.Key.Level, g.Key.Config, g.Key.Status,
            }).ToList();
        blocks.Add(Block("redundancy", "Redundancia", "Table",
            "Evalúa el nivel de redundancia de cada recurso (Local < Zona < Regional): Prod y sin clasificar requieren al menos Zona; en Dev/Test el nivel Local se considera adecuado. Hallazgos primero, agrupados por características compartidas.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "Entorno", "Nivel", "Configuración", "Estado" },
                align = new[] { "l", "l", "c", "c", "l", "c" },
                rows = redundancyRows,
                dense = true,
            }));

        return Section("disponibilidad", "Disponibilidad", blocks);
    }

    // ---- 5. rendimiento ----
    // Saturation (from the CPU classification) + scaling rules across compute
    // and databases, closing with Advisor Performance.
    public static object Rendimiento(Model m)
    {
        var blocks = new List<object>();

        var saturated = m.Utilization.Where(u => u.Classification == "Saturado").ToList();
        blocks.Add(Block("saturated", "Recursos Saturados", "Table",
            "Recursos cuya CPU del mes de análisis muestra saturación sostenida (≥5% de muestras sobre 90%, o promedio ≥80%) — riesgo de throttling y degradación de servicio.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "SKU", "Entorno", "CPU prom.", "CPU p95", "Costo mes" },
                align = new[] { "l", "l", "c", "c", "r", "r", "r" },
                rows = saturated.Select(u => new object?[]
                {
                    u.Label, u.FriendlyType, u.Sku ?? "—", u.Environment,
                    $"{u.AvgCpu.ToString("0.0", CultureInfo.InvariantCulture)}%",
                    $"{u.P95Cpu.ToString("0.0", CultureInfo.InvariantCulture)}%",
                    Helpers.Usd(u.MonthlyCost),
                }).ToList(),
            }));

        var scalingOk = m.Scaling.Count(s => s.Severity == "OK");
        var scalingRows = m.Scaling
            .GroupBy(s => (s.FriendlyType, s.Environment, s.Config, s.Finding, s.Severity))
            .OrderBy(g => Helpers.FindingRank(g.Key.Severity))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key.FriendlyType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new object?[]
            {
                Helpers.NameList(g.Select(s => s.Label).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()),
                g.Key.FriendlyType, g.Key.Environment, g.Key.Config, g.Key.Finding, g.Key.Severity,
            }).ToList();
        blocks.Add(Block("scaling", "Reglas de Escalamiento", "Table",
            $"Evalúa la capacidad de escalar ante demanda (autoscale en planes y VMSS, scale rules en Container Apps, serverless/elastic pool en SQL), hallazgos primero; {scalingOk} recursos ya escalan adecuadamente.",
            new
            {
                headers = new[] { "Recurso", "Tipo", "Entorno", "Configuración", "Hallazgo", "Severidad" },
                align = new[] { "l", "l", "c", "l", "l", "c" },
                rows = scalingRows,
                dense = true,
            }));

        blocks.Add(AdvisorTable(m, "Performance", "advisor-performance", "Recomendaciones de Rendimiento (Azure Advisor)"));

        return Section("rendimiento", "Rendimiento", blocks);
    }

    // ---- 6. operacion ----
    public static object Operacion(Model m)
    {
        var blocks = new List<object>();

        var tagged = m.Resources.Count(r => r.HasTags);
        var untagged = m.Resources.Count - tagged;
        blocks.Add(CountTable("tagging", "Cobertura de Etiquetado",
            "Cobertura de etiquetas (tags) sobre el inventario — la base de la atribución de costos y de la clasificación por entorno.",
            new[] { ("Recursos etiquetados", tagged), ("Recursos sin etiquetar", untagged) }));

        // Tag keys that differ only by case (env/Env/ENV) — inconsistency worth normalizing.
        var tagVariants = m.Resources.SelectMany(r => r.TagKeys)
            .GroupBy(k => k.ToLowerInvariant())
            .Select(g => new { key = g.Key, variants = g.Distinct(StringComparer.Ordinal).ToList() })
            .Where(g => g.variants.Count > 1)
            .ToList();
        blocks.Add(Block("tag-keys", "Claves de Etiqueta Inconsistentes", "Table",
            "Claves de etiqueta que aparecen con varias grafías que difieren solo en mayúsculas/minúsculas — una inconsistencia que conviene normalizar.",
            new
            {
                headers = new[] { "Clave", "Variantes encontradas" },
                align = new[] { "l", "l" },
                rows = tagVariants.Select(v => new object?[] { v.key, string.Join(", ", v.variants) }).ToList(),
            }));

        blocks.Add(CountTable("fragmentation", "Fragmentación",
            "Dimensión del despliegue: grupos de recursos, regiones, tipos de servicio y SKUs en uso.",
            new[]
            {
                ("Grupos de recursos", m.Resources.Select(r => r.ResourceGroup).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                ("Regiones en uso", m.Resources.Select(r => r.Region).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                ("Tipos de servicio", m.Resources.Select(r => r.FriendlyType).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                ("SKUs distintas", m.Resources.Where(r => r.SkuName is not null).Select(r => r.SkuName!).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            }));

        var namingRows = m.Resources
            .GroupBy(r => Helpers.NamingPattern(r.Name))
            .Select(g => new { pattern = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Select(x => new object?[]
            {
                x.pattern, x.count,
                $"{Math.Round(x.count * 100.0 / Math.Max(1, m.Resources.Count), 0).ToString(CultureInfo.InvariantCulture)}%",
            }).ToList();
        blocks.Add(Block("naming", "Patrones de Nomenclatura", "Table",
            "Distribución de convenciones de nomenclatura de recursos; una distribución fragmentada indica la ausencia de un estándar (CAF recomienda definir uno).",
            new { headers = new[] { "Patrón", "Recursos", "%" }, align = new[] { "l", "r", "r" }, rows = namingRows }));

        blocks.Add(AdvisorTable(m, "OperationalExcellence", "advisor-operational", "Recomendaciones Operativas (Azure Advisor)"));

        return Section("operacion", "Operación", blocks);
    }

    // ---- signals ----
    public static object Signals(Model m)
    {
        var totalByMonth = m.Months.Select(month => new
        {
            month,
            value = Math.Round(m.Costs.Where(c => c.Month == month).Sum(c => c.Value), 2),
        }).ToList();
        double momGrowth = 0;
        if (totalByMonth.Count >= 2 && totalByMonth[^2].value != 0)
            momGrowth = Math.Round((totalByMonth[^1].value - totalByMonth[^2].value) / totalByMonth[^2].value * 100, 1);

        double CurMonthSum(IEnumerable<CostRow> g) =>
            Math.Round(g.Where(c => m.CurrentMonth is null || c.Month == m.CurrentMonth).Sum(c => c.Value), 2);
        var topServiceTypes = m.Costs
            .GroupBy(c => m.ResourceByIdLower.TryGetValue(c.CloudId, out var r) ? r.FriendlyType : Helpers.FriendlyTypeFromArmId(c.CloudId))
            .Select(g => new { label = g.Key, cost = CurMonthSum(g) })
            .OrderByDescending(x => x.cost).Take(5).ToList();
        var topResources = m.Costs
            .GroupBy(c => c.CloudId)
            .Select(g => new { label = Helpers.LabelFor(m.ResourceByIdLower, g.Key), cost = CurMonthSum(g) })
            .OrderByDescending(x => x.cost).Take(5).ToList();

        var zombies = m.Utilization.Where(u => u.Classification == "Zombie")
            .Select(u => new { label = u.Label, type = u.FriendlyType, sku = u.Sku, environment = u.Environment, monthlyCost = u.MonthlyCost }).ToList();
        var oversized = m.Utilization.Where(u => u.Classification == "Sobredimensionado")
            .Select(u => new { label = u.Label, type = u.FriendlyType, sku = u.Sku, environment = u.Environment, avgCpu = u.AvgCpu, monthlyCost = u.MonthlyCost }).ToList();
        var saturated = m.Utilization.Where(u => u.Classification == "Saturado")
            .Select(u => new { label = u.Label, type = u.FriendlyType, sku = u.Sku, environment = u.Environment, p95Cpu = u.P95Cpu, monthlyCost = u.MonthlyCost }).ToList();

        var w = WafFacts.Build(m);

        // Security signals.
        double? secureScorePct = null;
        long secHigh = 0, secMedium = 0, secLow = 0;
        if (m.DefenderPresent && m.Security is { } sec)
        {
            if (sec.TryGetProperty("secureScore", out var score) && score.ValueKind == JsonValueKind.Object)
                secureScorePct = Math.Round(Num(score, "percentage"), 1);
            if (sec.TryGetProperty("assessmentCounts", out var ac) && ac.TryGetProperty("bySeverity", out var bySev))
            {
                secHigh = (long)Num(bySev, "High");
                secMedium = (long)Num(bySev, "Medium");
                secLow = (long)Num(bySev, "Low");
            }
        }

        // Advisor rollup.
        var advisorByCategory = m.AdvisorRecs
            .GroupBy(r => Helpers.Str(r, "category") ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        double estAnnualSavings = 0;
        foreach (var rec in m.AdvisorRecs)
            if (Helpers.Str(rec, "annualSavingsAmount") is { } s
                && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                estAnnualSavings += v;

        // Operations signals.
        var tagged = m.Resources.Count(r => r.HasTags);
        var namingGroups = m.Resources.GroupBy(r => Helpers.NamingPattern(r.Name)).Select(g => new { pattern = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToList();

        return new
        {
            currentMonth = m.CurrentMonth,
            currentMonthCost = totalByMonth.Count > 0 ? totalByMonth[^1].value : 0,
            momGrowthPct = momGrowth,
            priorMonths = totalByMonth.Count > 1 ? totalByMonth.Take(totalByMonth.Count - 1).ToList() : new(),
            totalCostByMonth = totalByMonth,
            topServiceTypes,
            topResources,
            resourceCounts = new { total = m.Resources.Count },
            environments = new
            {
                prod = m.Resources.Count(r => r.Environment == "Prod"),
                devTest = m.Resources.Count(r => r.Environment == "Dev/Test"),
                unclassified = m.Resources.Count(r => r.Environment == "—"),
            },
            sizing = new
            {
                zombies,
                oversized,
                correctCount = m.Utilization.Count(u => u.Classification == "Correcto"),
                meteredCount = m.Utilization.Count,
                zombieMonthlyCost = Math.Round(zombies.Sum(z => z.monthlyCost), 2),
                oversizedMonthlyCost = Math.Round(oversized.Sum(o => o.monthlyCost), 2),
                unused = m.Unused.Select(u => new { label = u.Label, type = u.FriendlyType, finding = u.Finding, monthlyCost = u.MonthlyCost }).ToList(),
                unusedMonthlyCost = Math.Round(m.Unused.Sum(u => u.MonthlyCost), 2),
            },
            waf = new
            {
                present = w.HasLogs,
                coverage = new { activeDays = w.ActiveDays, monthDays = w.MonthDays, firstEvent = w.FirstEvent, lastEvent = w.LastEvent },
                totalEvents = w.Events,
                requests = w.Requests,
                blockedRequests = w.Blocked,
                blockedPct = w.BlockedPct,
                detectedRequests = w.Detected,
                allowedRequests = w.Allowed,
                challengedRequests = w.Challenged,
                distinctIps = w.DistinctIps,
                distinctHosts = w.DistinctHosts,
                preventionCount = w.PreventionCount,
                detectionCount = w.DetectionCount,
                policies = w.Policies.Select(p => new
                {
                    name = p.Name, type = p.Type, mode = p.Mode, state = p.State, scope = p.Scope, ruleSets = p.RuleSets,
                    customRules = p.CustomRules, exclusions = p.Exclusions,
                    requests = p.Requests, blockedRequests = p.Blocked, detectedRequests = p.Detected,
                }).ToList(),
                cost = new
                {
                    monthlyCost = w.MonthlyCost,
                    previousMonthlyCost = w.PrevMonthlyCost,
                    protectedApps = w.ProtectedApps,
                    costPerProtectedApp = w.CostPerProtectedApp,
                    directProdApps = w.DirectProdApps,
                    gateways = w.Gateways.Select(g => new
                    {
                        label = g.Label, type = g.Type, sku = g.Sku, protectedApps = g.ProtectedApps, requests = g.Requests,
                        monthlyCost = g.Cost, previousMonthlyCost = g.PrevCost,
                    }).ToList(),
                },
                peakDay = w.Days.Where(d => d.HasData).OrderByDescending(d => d.Requests).Select(d => new { day = d.Day, requests = d.Requests, blockedRequests = d.Blocked }).FirstOrDefault(),
                categories = w.Categories.Select(c => new { key = c.Key, label = c.Label, requests = c.Requests, blockedRequests = c.Blocked, detectedRequests = c.Detected }).ToList(),
                topHosts = w.Hosts.Take(8).Select(h => new { host = h.Host, requests = h.Requests, blockedRequests = h.Blocked, distinctIps = h.DistinctIps }).ToList(),
                topRules = w.Rules.Take(8).Select(r => new { rule = r.Rule, category = r.Category, ruleSet = r.RuleSet, requests = r.Requests, blockedRequests = r.Blocked, detectedRequests = r.Detected, sample = r.Sample }).ToList(),
                topIps = w.Ips.Take(8).Select(i => new { ip = i.Ip, requests = i.Requests, blockedRequests = i.Blocked, activeDays = i.ActiveDays, hosts = i.Hosts, profile = i.Profile }).ToList(),
                fpCandidates = w.FpCandidates.Select(f => new { rule = f.Rule, uri = f.Uri, host = f.Host, action = f.Action, hits = f.Hits, sharePct = f.SharePct, distinctIps = f.DistinctIps, priority = f.Priority }).ToList(),
                fpCandidateCount = w.FpCandidates.Count,
            },
            security = new
            {
                defenderPresent = m.DefenderPresent,
                secureScorePct,
                unhealthyBySeverity = new { high = secHigh, medium = secMedium, low = secLow },
                sqlServersEvaluated = SqlAuditing(m).total,
                sqlServersWithoutAuditing = SqlAuditing(m).withoutAuditing,
                wafExposure = new
                {
                    directCount = m.WafExposure.Count(e => e.Finding == "Tráfico directo sin WAF"),
                    bypassCount = m.WafExposure.Count(e => e.Finding == "Bypass posible del WAF"),
                    nonWafGatewayCount = m.WafExposure.Count(e => e.Finding == "Tráfico no inspeccionado por WAF"),
                    okCount = m.WafExposure.Count(e => e.Severity == "OK"),
                    findings = m.WafExposure.Where(e => e.Severity != "OK").Select(e => new
                    {
                        label = e.Label, type = e.FriendlyType, environment = e.Environment,
                        exposure = e.Exposure, finding = e.Finding, severity = e.Severity,
                    }).ToList(),
                },
                infraExposure = new
                {
                    openCount = m.InfraExposure.Count(e => e.Finding == "Acepta tráfico de cualquier origen"),
                    okCount = m.InfraExposure.Count(e => e.Severity == "OK"),
                    findings = m.InfraExposure.Where(e => e.Severity != "OK").Select(e => new
                    {
                        label = e.Label, type = e.FriendlyType, environment = e.Environment,
                        exposure = e.Exposure, finding = e.Finding, severity = e.Severity,
                    }).ToList(),
                },
                advisorSecurityCount = advisorByCategory.GetValueOrDefault("Security", 0),
            },
            reliability = new
            {
                families = m.BackupFamilies.Select(f => new
                {
                    family = f.Family, total = f.Total, protectedCount = f.Protected, unprotected = f.Unprotected,
                }).ToList(),
                gaps = m.BackupGaps.Select(g => new
                {
                    label = g.Label, type = g.FriendlyType, environment = g.Environment, finding = g.Finding, severity = g.Severity,
                }).ToList(),
                criticalGapCount = m.BackupGaps.Count(g => g.Severity == "Crítico"),
                advisorReliabilityCount = advisorByCategory.GetValueOrDefault("HighAvailability", 0),
            },
            availability = new
            {
                certsExpired = m.Certificates.Count(c => c.Status == "Vencido"),
                certsCritical = m.Certificates.Count(c => c.Status == "Crítico"),
                certsWarning = m.Certificates.Count(c => c.Status == "Advertencia"),
                certsUnverifiable = m.Certificates.Count(c => c.Status == "No verificable"),
                redundancy = new
                {
                    localCount = m.Redundancy.Count(x => x.Level == "Local"),
                    zonaCount = m.Redundancy.Count(x => x.Level == "Zona"),
                    regionalCount = m.Redundancy.Count(x => x.Level == "Regional"),
                    okCount = m.Redundancy.Count(x => x.Status == "OK"),
                    warnings = m.Redundancy.Where(x => x.Status == "Advertencia")
                        .Select(x => new { label = x.Label, type = x.FriendlyType, environment = x.Environment, level = x.Level, config = x.Config }).ToList(),
                },
            },
            performance = new
            {
                saturated,
                scalingFindings = m.Scaling.Where(s => s.Severity != "OK")
                    .Select(s => new { label = s.Label, type = s.FriendlyType, environment = s.Environment, finding = s.Finding, severity = s.Severity }).ToList(),
                scalingOkCount = m.Scaling.Count(s => s.Severity == "OK"),
                advisorPerformanceCount = advisorByCategory.GetValueOrDefault("Performance", 0),
            },
            operations = new
            {
                tagCoveragePct = m.Resources.Count > 0 ? Math.Round(tagged * 100.0 / m.Resources.Count, 1) : 0,
                untaggedCount = m.Resources.Count - tagged,
                inconsistentTagKeyCount = m.Resources.SelectMany(r => r.TagKeys)
                    .GroupBy(k => k.ToLowerInvariant()).Count(g => g.Distinct(StringComparer.Ordinal).Count() > 1),
                resourceGroups = m.Resources.Select(r => r.ResourceGroup).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                regions = m.Resources.Select(r => r.Region).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                namingPatterns = namingGroups,
            },
            advisor = new
            {
                total = m.AdvisorRecs.Count,
                byCategory = advisorByCategory,
                estAnnualSavingsUsd = Math.Round(estAnnualSavings, 2),
            },
        };
    }

    // ---- shared shapes ----

    // Advisor recommendations for one category, grouped by problem text, impact-ranked.
    // ALWAYS returns a block (fixed template); empty renders "Sin hallazgos".
    static object AdvisorTable(Model m, string category, string id, string title, bool includeSavings = false)
    {
        var recs = m.AdvisorRecs
            .Where(r => string.Equals(Helpers.Str(r, "category"), category, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (problem: Helpers.Str(r, "problem") ?? "—", impact: Helpers.Str(r, "impact") ?? "—"))
            .Select(g => new
            {
                g.Key.problem,
                g.Key.impact,
                count = g.Count(),
                savings = g.Sum(r => Helpers.Str(r, "annualSavingsAmount") is { } s
                    && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0),
            })
            .OrderBy(x => ImpactRank(x.impact)).ThenByDescending(x => x.count)
            .ToList();

        var headers = includeSavings
            ? new[] { "Impacto", "Recomendación", "Recursos", "Ahorro anual est." }
            : new[] { "Impacto", "Recomendación", "Recursos" };
        var align = includeSavings ? new[] { "c", "l", "r", "r" } : new[] { "c", "l", "r" };
        var rows = recs.Take(8).Select(r =>
        {
            var baseRow = new List<object?> { TranslateImpact(r.impact), r.problem, r.count };
            if (includeSavings) baseRow.Add(r.savings > 0 ? Helpers.Usd(Math.Round(r.savings, 0)) : "—");
            return baseRow.ToArray();
        }).ToList();
        foreach (var g in recs.Skip(8).GroupBy(r => r.impact).OrderBy(g => ImpactRank(g.Key)))
        {
            var residualRow = new List<object?>
            {
                TranslateImpact(g.Key),
                Helpers.NameList(g.Select(r => r.problem).ToList()),
                g.Sum(r => r.count),
            };
            if (includeSavings)
            {
                var residualSavings = g.Sum(r => r.savings);
                residualRow.Add(residualSavings > 0 ? Helpers.Usd(Math.Round(residualSavings, 0)) : "—");
            }
            rows.Add(residualRow.ToArray());
        }

        return Block(id, title, "Table",
            "Recomendaciones oficiales de Azure Advisor para esta categoría, agrupadas por problema y ordenadas por impacto; la columna Recursos cuenta los recursos afectados.",
            new { headers, align, rows, dense = true });
    }

    static int ImpactRank(string s) => s.ToLowerInvariant() switch { "high" => 0, "medium" => 1, "low" => 2, _ => 3 };
    static string TranslateImpact(string s) => s.ToLowerInvariant() switch
    { "high" => "Alta", "medium" => "Media", "low" => "Baja", _ => s };

    static object GroupedMonthly(Model m, string id, string title, string description, Func<CostRow, string> keySelector, int topN)
    {
        var groups = m.Costs
            .GroupBy(keySelector)
            .Select(g => new
            {
                label = g.Key,
                total = g.Sum(c => c.Value),
                perMonth = m.Months.ToDictionary(mo => mo, mo => g.Where(c => c.Month == mo).Sum(c => c.Value)),
            })
            .OrderByDescending(x => m.CurrentMonth != null ? x.perMonth[m.CurrentMonth] : x.total)
            .Take(topN)
            .ToList();
        var rows = groups.Select(x => new
        {
            label = x.label,
            values = m.Months.Select(mo => Math.Round(x.perMonth[mo], 2)).ToList(),
        }).ToList();
        var total = m.Months.Select((mo, i) => Math.Round(rows.Sum(r => r.values[i]), 2)).ToList();
        return Block(id, title, "GroupedMonthly", description, new { months = m.Months, rows, total });
    }

    static object CountTable(string id, string title, string description, IEnumerable<(string label, int count)> pairs)
    {
        var rows = pairs.Select(p => new { label = p.label, count = p.count }).ToList();
        var total = rows.Sum(x => x.count);
        return Block(id, title, "CountTable", description, new { rows, total });
    }

    static object Section(string id, string title, List<object> blocks) => new
    {
        id,
        title,
        blocks,
        analysis = new { resumen = "", observaciones = Array.Empty<string>(), recomendaciones = Array.Empty<string>() },
    };

    static object Block(string id, string title, string displayType, string description, object data) => new
    {
        id, title, displayType, description, data,
    };

    // ---- JSON row helpers for waf-logs aggregates ----
    internal static List<JsonElement> Rows(JsonElement src, string prop)
    {
        if (src.ValueKind == JsonValueKind.Object && src.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().ToList();
        return new List<JsonElement>();
    }

    static double Num(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;
}

// =====================================================================
// Pure helpers.
// =====================================================================

static class Helpers
{
    public const string UnknownRegion = "Otros (sin región)";

    public static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static string NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ? UnknownRegion : region.Trim().ToLowerInvariant();

    public static string Usd(double v) => "$" + v.ToString("#,0.00", CultureInfo.InvariantCulture);

    public static string TruncateText(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public static bool BoolProp(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    // Number-or-string property as a display string (Container Apps replica counts etc.).
    public static string? NumStr(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.String => v.GetString(),
                _ => null,
            }
            : null;

    // Some types (Redis, App Gateway) carry their SKU inside `properties`, not top-level.
    public static string? EffectiveSkuName(ResourceInfo r)
    {
        if (r.SkuName is not null) return r.SkuName;
        if (r.Properties.ValueKind == JsonValueKind.Object && r.Properties.TryGetProperty("sku", out var sku))
            return Str(sku, "name");
        return null;
    }

    // Environment classification: tags first (environment/env/entorno/ambiente/stage),
    // then tokens in the resource name and resource-group name, then a flat name
    // suffix. Returns "Prod" | "Dev/Test" | "—" (unclassified).
    public static string ClassifyEnvironment(string name, string resourceGroup, Dictionary<string, string> tags)
    {
        foreach (var key in new[] { "environment", "env", "entorno", "ambiente", "stage" })
            if (tags.TryGetValue(key, out var v) && ClassifyEnvToken(v) is { } fromTag) return fromTag;
        foreach (var source in new[] { name, resourceGroup })
        {
            // "non-prod"/"no-prod" tokenize into ["non","prod"] and would match
            // Prod — resolve the compound forms on the full string first.
            var flatSource = (source ?? "").ToLowerInvariant();
            if (flatSource.Contains("nonprod") || flatSource.Contains("non-prod") || flatSource.Contains("noprod"))
                return "Dev/Test";
            foreach (var token in flatSource.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries))
                if (ClassifyEnvToken(token) is { } fromToken) return fromToken;
        }
        var flat = (name ?? "").ToLowerInvariant();
        if (flat.EndsWith("prod") || flat.EndsWith("prd")) return "Prod";
        foreach (var suffix in new[] { "dev", "qa", "uat", "test", "stg", "demo" })
            if (flat.EndsWith(suffix)) return "Dev/Test";
        return "—";
    }

    static string? ClassifyEnvToken(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "prod" or "prd" or "production" or "produccion" or "producción" or "productivo" or "live" => "Prod",
        "dev" or "development" or "desarrollo" or "test" or "testing" or "pruebas" or "prueba" or "qa" or "uat"
            or "stg" or "staging" or "preprod" or "nonprod" or "nonprd" or "sandbox" or "demo" or "lab" or "poc" => "Dev/Test",
        _ => null,
    };

    // Inline compaction for grouped table rows: resources sharing the same
    // characteristics collapse into ONE row whose first cell lists their names
    // up to a character budget, then closes with "… y N más" IN THE SAME CELL.
    // A different characteristics tuple always starts a new row with its own
    // counter — never a separate "… y N más" row.
    public static string NameList(IReadOnlyList<string> names, int budget = 96)
    {
        var sb = new StringBuilder();
        var listed = 0;
        foreach (var name in names)
        {
            var next = listed == 0 ? name : ", " + name;
            if (listed > 0 && sb.Length + next.Length > budget) break;
            sb.Append(next);
            listed++;
        }
        var rest = names.Count - listed;
        if (rest > 0) sb.Append($" … y {rest} más");
        return sb.ToString();
    }

    // Env-weighted severity: Prod carries the real severity, Dev/Test degrades to
    // Informativo (a gap there is a deliberate trade-off, not a grave finding),
    // and unclassified gets the conservative middle ground.
    public static string EnvSeverity(string environment, string prodSeverity, string unknownSeverity = "Advertencia") =>
        environment switch
        {
            "Prod" => prodSeverity,
            "Dev/Test" => "Informativo",
            _ => unknownSeverity,
        };

    // Redundancy level ordering: Local < Zona < Regional.
    public static int LevelRank(string level) => level switch
    {
        "Regional" => 2,
        "Zona" => 1,
        _ => 0,
    };

    // ---- WAF helpers ----

    // Public hostnames of an app (App Service: default + custom host names;
    // Container App: ingress FQDN) — the key that matches App Gateway backend pools.
    public static List<string> AppHostnames(ResourceInfo r)
    {
        var hostnames = new List<string>();
        var props = r.Properties;
        if (props.ValueKind != JsonValueKind.Object) return hostnames;
        if (r.Type.Equals("microsoft.app/containerapps", StringComparison.OrdinalIgnoreCase))
        {
            if (props.TryGetProperty("configuration", out var cfg) && cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("ingress", out var ing) && ing.ValueKind == JsonValueKind.Object
                && Str(ing, "fqdn") is { Length: > 0 } fqdn)
                hostnames.Add(fqdn);
            return hostnames;
        }
        if (Str(props, "defaultHostName") is { Length: > 0 } dh) hostnames.Add(dh);
        foreach (var prop in new[] { "hostNames", "enabledHostNames" })
            if (props.TryGetProperty(prop, out var hn) && hn.ValueKind == JsonValueKind.Array)
                foreach (var h in hn.EnumerateArray())
                    if (h.ValueKind == JsonValueKind.String && h.GetString() is { Length: > 0 } s) hostnames.Add(s);
        return hostnames;
    }

    // Month-over-month delta as a signed percentage; "—" when there is no baseline.
    public static string DeltaPct(double current, double previous)
    {
        if (previous <= 0) return "—";
        var p = (current - previous) / previous * 100;
        return (p >= 0 ? "+" : "") + p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    public static string Pct(long part, long total) =>
        total <= 0 ? "—" : (part * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    static readonly string[] SpanishMonths =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    // "2026-09-21" → "21 de septiembre".
    public static string DayLabel(string? day) =>
        day is { Length: >= 10 }
        && DateTime.TryParseExact(day[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"{d.Day} de {SpanishMonths[d.Month - 1]}" : "—";

    public static int PriorityRank(string priority) => priority switch { "Alta" => 0, "Media" => 1, "Baja" => 2, _ => 3 };

    // Shared ordering for finding/status tokens: worst first, OK last.
    public static int FindingRank(string status) => status switch
    {
        "Vencido" or "Crítico" => 0,
        "Advertencia" => 1,
        "Informativo" => 2,
        "No verificable" => 3,
        "No aplica" => 4,
        "OK" or "Correcto" => 5,
        _ => 6,
    };

    // Expiry from App Gateway publicCertData (base64 DER certificate). Null when
    // not parseable (e.g. a PKCS7 chain or garbage) — the row degrades to
    // "No verificable" instead of failing the stage.
    public static DateTimeOffset? TryParseCertExpiry(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(bytes);
            return new DateTimeOffset(cert.NotAfter.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    // Resource-name convention classifier for the naming-fragmentation table.
    public static string NamingPattern(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Vacío";
        var hasHyphen = name.Contains('-');
        var hasUnderscore = name.Contains('_');
        var hasUpper = name.Any(char.IsUpper);
        var hasLower = name.Any(char.IsLower);
        if (hasHyphen && !hasUpper && !hasUnderscore) return "kebab-case";
        if (hasUnderscore) return "snake_case";
        if (hasUpper && hasLower && !hasHyphen) return "camelCase/PascalCase";
        if (!hasUpper && !hasHyphen) return "minúsculas planas";
        return "Mixto";
    }

    public static string LabelFor(Dictionary<string, ResourceInfo> byIdLower, string idLower)
    {
        if (byIdLower.TryGetValue(idLower, out var r))
        {
            if (r.Type.Contains("databases", StringComparison.OrdinalIgnoreCase))
                return SqlLabel(r.Id);
            return string.IsNullOrEmpty(r.Name) ? ShortLabel(r.Id) : r.Name;
        }
        return ShortLabel(idLower);
    }

    public static string ShortLabel(string armId)
    {
        var idx = armId.LastIndexOf('/');
        return idx >= 0 && idx < armId.Length - 1 ? armId[(idx + 1)..] : armId;
    }

    public static string SqlLabel(string armId)
    {
        var parts = armId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return parts[^3].Equals("databases", StringComparison.OrdinalIgnoreCase)
            ? parts[^1]
            : $"{parts[^3]}/{parts[^1]}";
        return ShortLabel(armId);
    }

    public static (double delta, string direction) Delta(double current, double? previous)
    {
        if (previous is null) return (0, "flat");
        var prev = previous.Value;
        var delta = Math.Round(current - prev, 2);
        if ((Math.Abs(prev) < 1e-6 && Math.Abs(current) < 1e-6) ||
            (Math.Abs(prev) > 1e-6 && Math.Abs(current - prev) < 0.005 * Math.Abs(prev)))
            return (delta, "flat");
        return (delta, current > prev ? "up" : "down");
    }

    public static string FriendlyTypeFromArmId(string armId)
    {
        var type = TypeFromArmId(armId);
        return type is null ? "Desconocido" : FriendlyType(type);
    }

    public static string? TypeFromArmId(string armId)
    {
        var parts = armId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var provIdx = Array.FindIndex(parts, p => p.Equals("providers", StringComparison.OrdinalIgnoreCase));
        if (provIdx < 0 || provIdx + 2 >= parts.Length) return null;
        var provider = parts[provIdx + 1];
        var segments = new List<string> { parts[provIdx + 2] };
        for (var i = provIdx + 4; i < parts.Length; i += 2) segments.Add(parts[i]);
        return provider + "/" + string.Join("/", segments);
    }

    public static string FriendlyType(string type)
    {
        var key = type.ToLowerInvariant();
        return key switch
        {
            "microsoft.web/serverfarms" => "App Service Plan",
            "microsoft.web/sites" => "App Service",
            "microsoft.web/sites/slots" => "Deployment Slot",
            "microsoft.web/staticsites" => "Static Web App",
            "microsoft.web/certificates" => "App Service Certificate",
            "microsoft.sql/servers" => "SQL Server",
            "microsoft.sql/servers/databases" => "SQL Database",
            "microsoft.sql/servers/elasticpools" => "SQL Elastic Pool",
            "microsoft.network/applicationgateways" => "Application Gateway",
            "microsoft.network/applicationgatewaywebapplicationfirewallpolicies" => "WAF Policy (App Gateway)",
            "microsoft.network/frontdoorwebapplicationfirewallpolicies" => "WAF Policy (Front Door)",
            "microsoft.network/virtualnetworkgateways" => "VPN Gateway",
            "microsoft.network/virtualnetworks" => "Virtual Network",
            "microsoft.network/networksecuritygroups" => "Network Security Group",
            "microsoft.network/publicipaddresses" => "Public IP",
            "microsoft.network/networkinterfaces" => "Network Interface",
            "microsoft.network/privateendpoints" => "Private Endpoint",
            "microsoft.network/privatednszones" => "Private DNS Zone",
            "microsoft.network/dnszones" => "DNS Zone",
            "microsoft.network/loadbalancers" => "Load Balancer",
            "microsoft.network/natgateways" => "NAT Gateway",
            "microsoft.documentdb/databaseaccounts" => "Cosmos DB",
            "microsoft.dbformysql/flexibleservers" => "MySQL Flexible Server",
            "microsoft.dbforpostgresql/flexibleservers" => "PostgreSQL Flexible Server",
            "microsoft.storage/storageaccounts" => "Storage Account",
            "microsoft.insights/components" => "Application Insights",
            "microsoft.operationalinsights/workspaces" => "Log Analytics Workspace",
            "microsoft.keyvault/vaults" => "Key Vault",
            "microsoft.cache/redis" => "Redis Cache",
            "microsoft.servicebus/namespaces" => "Service Bus",
            "microsoft.eventhub/namespaces" => "Event Hub",
            "microsoft.containerregistry/registries" => "Container Registry",
            "microsoft.containerservice/managedclusters" => "AKS Cluster",
            "microsoft.compute/virtualmachines" => "Virtual Machine",
            "microsoft.compute/disks" => "Managed Disk",
            "microsoft.compute/virtualmachinescalesets" => "VM Scale Set",
            "microsoft.managedidentity/userassignedidentities" => "Managed Identity",
            "microsoft.signalrservice/signalr" => "SignalR Service",
            "microsoft.apimanagement/service" => "API Management",
            "microsoft.cdn/profiles" => "CDN / Front Door",
            "microsoft.app/containerapps" => "Container App",
            "microsoft.app/managedenvironments" => "Container Apps Environment",
            "microsoft.app/jobs" => "Container App Job",
            "microsoft.recoveryservices/vaults" => "Recovery Services Vault",
            "microsoft.cognitiveservices/accounts" => "Cognitive Services",
            "microsoft.insights/autoscalesettings" => "Autoscale Setting",
            _ => HumanizeType(type),
        };
    }

    static string HumanizeType(string type)
    {
        var key = (type ?? "").ToLowerInvariant();
        if (key.StartsWith("microsoft.resources/") || key == "resources") return "Otros recursos";
        var segment = LastSegment(type ?? "");
        if (string.IsNullOrWhiteSpace(segment)) return "Otros recursos";
        var sb = new System.Text.StringBuilder(segment.Length + 8);
        for (var i = 0; i < segment.Length; i++)
        {
            var ch = segment[i];
            if (!char.IsLetterOrDigit(ch)) { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(ch) && (char.IsLower(segment[i - 1]) || char.IsDigit(segment[i - 1])))
                sb.Append(' ');
            sb.Append(ch);
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "Otros recursos";
        for (var i = 0; i < words.Length; i++)
            words[i] = char.ToUpperInvariant(words[i][0]) + (words[i].Length > 1 ? words[i][1..].ToLowerInvariant() : "");
        return string.Join(' ', words);
    }

    public static string LastSegment(string s)
    {
        var idx = s.LastIndexOf('/');
        return idx >= 0 && idx < s.Length - 1 ? s[(idx + 1)..] : s;
    }
}

// =====================================================================
// WAF facts — one deterministic view of the firewall month, consumed by the
// Seguridad blocks AND by signals.waf so both always agree. Merges every
// staged log source, joins activity to the ARM inventory (policy id /
// gateway resource id), prices the gateways from costs.json, and classifies
// false-positive candidates by their real impact.
// =====================================================================

sealed class WafPolicyRow(
    string name, string idLower, string type, string mode, string state, string scope, string ruleSets,
    int customRules, int exclusions, List<string> gatewayIdsLower)
{
    public string Name { get; } = name;
    public string IdLower { get; } = idLower;
    public string Type { get; } = type;
    public string Mode { get; set; } = mode;          // ARM mode; filled from logs (Front Door) when ARM lacks it
    public string State { get; } = state;
    public string Scope { get; } = scope;
    public string RuleSets { get; } = ruleSets;
    public int CustomRules { get; } = customRules;
    public int Exclusions { get; } = exclusions;
    public List<string> GatewayIdsLower { get; } = gatewayIdsLower;
    public long Requests { get; set; }
    public long Blocked { get; set; }
    public long Detected { get; set; }
    public bool IsDetection => Mode.Equals("Detection", StringComparison.OrdinalIgnoreCase);
    public string CustomAndExclusions => $"{CustomRules} / {Exclusions}";
}

sealed record WafGateway(string Label, string Type, string Sku, int? ProtectedApps, double Cost, double PrevCost)
{
    public long Requests { get; set; }
}

sealed record WafDay(string Day, long Requests, long Blocked, long Detected, bool HasData);
sealed record WafCategory(string Key, string Label, string? Color, long Requests, long Blocked, long Detected);
sealed record WafHost(string Host, long Requests, long Blocked, long DistinctIps);
sealed record WafRule(string Rule, string Category, string RuleSet, long Requests, long Blocked, long Detected, long DistinctIps, string Sample);
sealed record WafIp(string Ip, long Requests, long Blocked, int ActiveDays, long Hosts, string Profile);
sealed record WafFp(string Rule, string Uri, string Host, string Action, long Hits, double SharePct, long DistinctIps, string Priority);

sealed class WafFacts
{
    public bool HasLogs { get; init; }
    public int MonthDays { get; init; }
    public int ActiveDays { get; init; }
    public string? FirstEvent { get; init; }
    public string? LastEvent { get; init; }
    public long Events { get; init; }
    public long Requests { get; init; }
    public long Blocked { get; init; }
    public long Detected { get; init; }
    public long Allowed { get; init; }
    public long Challenged { get; init; }
    public long DistinctIps { get; init; }
    public long DistinctHosts { get; init; }
    public double BlockedPct => Requests > 0 ? Math.Round(Blocked * 100.0 / Requests, 1) : 0;
    public required List<WafPolicyRow> Policies { get; init; }
    public int PreventionCount => Policies.Count(p => p.Mode.Equals("Prevention", StringComparison.OrdinalIgnoreCase));
    public int DetectionCount => Policies.Count(p => p.IsDetection);
    public required List<WafGateway> Gateways { get; init; }
    public double MonthlyCost => Math.Round(Gateways.Sum(g => g.Cost), 2);
    public double PrevMonthlyCost => Math.Round(Gateways.Sum(g => g.PrevCost), 2);
    public int ProtectedApps { get; init; }
    public int DirectProdApps { get; init; }
    public double CostPerProtectedApp => ProtectedApps > 0 ? Math.Round(MonthlyCost / ProtectedApps, 2) : 0;
    public required List<WafDay> Days { get; init; }
    public required List<WafCategory> Categories { get; init; }
    public required List<WafHost> Hosts { get; init; }
    public required List<WafRule> Rules { get; init; }
    public required List<WafIp> Ips { get; init; }
    public required List<WafFp> FpCandidates { get; init; }

    public static WafFacts Build(Model m)
    {
        var srcs = m.WafSources;
        var hasLogs = srcs.Count > 0;

        // ---- month window + daily series (every day of the window, gaps kept) ----
        var (winStart, winEnd) = Window(m);
        var monthDays = Math.Max(1, (int)Math.Round((winEnd - winStart).TotalDays));
        var dayAgg = new Dictionary<string, (long req, long blk, long det)>(StringComparer.Ordinal);
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "dailyTrend"))
            {
                var day = Helpers.Str(r, "day") ?? "";
                if (day.Length < 10) continue;
                day = day[..10];
                var prev = dayAgg.GetValueOrDefault(day);
                dayAgg[day] = (prev.req + Builders.Num64(r, "requests"), prev.blk + Builders.Num64(r, "blockedRequests"), prev.det + Builders.Num64(r, "detectedRequests"));
            }
        var days = new List<WafDay>();
        for (var d = winStart; d < winEnd; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            days.Add(dayAgg.TryGetValue(key, out var v) ? new WafDay(key, v.req, v.blk, v.det, true) : new WafDay(key, 0, 0, 0, false));
        }
        var activeDays = days.Count(d => d.HasData);

        // ---- summary totals (additive across sources: different tables = different gateways) ----
        long events = 0, requests = 0, blocked = 0, detected = 0, allowed = 0, challenged = 0, distinctIps = 0, distinctHosts = 0;
        string? first = null, last = null;
        foreach (var src in srcs)
        {
            if (!src.TryGetProperty("summary", out var s) || s.ValueKind != JsonValueKind.Object) continue;
            events += Builders.Num64(s, "events");
            requests += Builders.Num64(s, "requests");
            blocked += Builders.Num64(s, "blockedRequests");
            detected += Builders.Num64(s, "detectedRequests");
            allowed += Builders.Num64(s, "allowedRequests");
            challenged += Builders.Num64(s, "challengedRequests");
            distinctIps += Builders.Num64(s, "distinctIps");
            distinctHosts += Builders.Num64(s, "distinctHosts");
            var f = Helpers.Str(s, "firstEvent"); var l = Helpers.Str(s, "lastEvent");
            if (f is not null && (first is null || string.CompareOrdinal(f, first) < 0)) first = f;
            if (l is not null && (last is null || string.CompareOrdinal(l, last) > 0)) last = l;
        }

        // ---- inventory from ARM state ----
        var policies = BuildPolicies(m);
        var byGateway = new Dictionary<string, (long req, long blk, long det)>(StringComparer.Ordinal);
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "byPolicy"))
            {
                var req = Builders.Num64(r, "requests"); var blk = Builders.Num64(r, "blockedRequests"); var det = Builders.Num64(r, "detectedRequests");
                var gwId = (Helpers.Str(r, "resourceId") ?? "").ToLowerInvariant();
                if (gwId.Length > 0)
                {
                    var prev = byGateway.GetValueOrDefault(gwId);
                    byGateway[gwId] = (prev.req + req, prev.blk + blk, prev.det + det);
                }
                var policyId = (Helpers.Str(r, "policyId") ?? "").Trim();
                var target = MatchPolicy(policies, policyId, gwId);
                if (target is null) continue;
                target.Requests += req; target.Blocked += blk; target.Detected += det;
                if (target.Mode == "—" && Helpers.Str(r, "mode") is { Length: > 0 } logMode) target.Mode = logMode;
            }

        // ---- gateways priced from costs.json + protected apps from backend pools ----
        var gateways = BuildGateways(m, policies);
        foreach (var g in gateways)
            if (g.Label is { } && m.ResourceByIdLower.Values.FirstOrDefault(r => r.Name == g.Label && Helpers.FriendlyType(r.Type) == g.Type) is { } res
                && byGateway.TryGetValue(res.IdLower, out var act))
                g.Requests = act.req;
        var protectedApps = m.WafExposure.Count(e => e.Exposure.StartsWith("WAF", StringComparison.Ordinal));
        var directProd = m.WafExposure.Count(e => e.Finding == "Tráfico directo sin WAF" && e.Environment == "Prod");

        // ---- categories (decision rows 949/980 are not attack families) ----
        var catAgg = new Dictionary<string, (long req, long blk, long det)>(StringComparer.Ordinal);
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "byCategory"))
            {
                var key = Helpers.Str(r, "category") ?? "unknown";
                if (key is "949" or "980") continue;
                var prev = catAgg.GetValueOrDefault(key);
                catAgg[key] = (prev.req + Builders.Num64(r, "requests"), prev.blk + Builders.Num64(r, "blockedRequests"), prev.det + Builders.Num64(r, "detectedRequests"));
            }
        var categories = catAgg.Select(kv =>
        {
            var (label, color) = CategoryInfo(kv.Key);
            return new WafCategory(kv.Key, label, color, kv.Value.req, kv.Value.blk, kv.Value.det);
        }).OrderByDescending(c => c.Requests).ThenBy(c => c.Label, StringComparer.Ordinal).ToList();

        // ---- hosts ----
        var hostAgg = new Dictionary<string, (long req, long blk, long ips)>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "byHost"))
            {
                var host = Helpers.Str(r, "host") is { Length: > 0 } h ? h : "—";
                var prev = hostAgg.GetValueOrDefault(host);
                hostAgg[host] = (prev.req + Builders.Num64(r, "requests"), prev.blk + Builders.Num64(r, "blockedRequests"), Math.Max(prev.ips, Builders.Num64(r, "distinctIps")));
            }
        var hosts = hostAgg.Select(kv => new WafHost(kv.Key, kv.Value.req, kv.Value.blk, kv.Value.ips))
            .OrderByDescending(h => h.Requests).ThenBy(h => h.Host, StringComparer.Ordinal).ToList();

        // ---- rules ----
        var ruleAgg = new Dictionary<(string rule, string set), (long req, long blk, long det, long ips, string sample, string cat)>();
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "topRules"))
            {
                var rule = Helpers.Str(r, "rule") ?? "—";
                var set = FriendlyRuleSet(Helpers.Str(r, "ruleSet"), Helpers.Str(r, "ruleSetVersion"), rule);
                var key = (rule, set);
                var prev = ruleAgg.GetValueOrDefault(key, (0, 0, 0, 0, "", ""));
                var cat = prev.cat.Length > 0 ? prev.cat : CategoryInfo(Helpers.Str(r, "category") ?? CategoryKey(rule, Helpers.Str(r, "ruleSet"))).label;
                ruleAgg[key] = (prev.req + Builders.Num64(r, "requests"), prev.blk + Builders.Num64(r, "blockedRequests"), prev.det + Builders.Num64(r, "detectedRequests"),
                    Math.Max(prev.ips, Builders.Num64(r, "distinctIps")), prev.sample.Length > 0 ? prev.sample : Helpers.Str(r, "sample") ?? "", cat);
            }
        var rules = ruleAgg.Select(kv => new WafRule(kv.Key.rule, kv.Value.cat, kv.Key.set, kv.Value.req, kv.Value.blk, kv.Value.det, kv.Value.ips, kv.Value.sample))
            .OrderByDescending(r => r.Requests).ThenBy(r => r.Rule, StringComparer.Ordinal).ToList();

        // ---- IPs ----
        var ipAgg = new Dictionary<string, (long req, long blk, int days, long hosts)>(StringComparer.Ordinal);
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "topClientIps"))
            {
                var ip = Helpers.Str(r, "ip") ?? Helpers.Str(r, "clientIp") ?? "—";
                var prev = ipAgg.GetValueOrDefault(ip);
                ipAgg[ip] = (prev.req + Builders.Num64(r, "requests"), prev.blk + Builders.Num64(r, "blockedRequests"),
                    Math.Max(prev.days, (int)Builders.Num64(r, "activeDays")), Math.Max(prev.hosts, Builders.Num64(r, "distinctHosts")));
            }
        var ips = ipAgg.Select(kv => new WafIp(kv.Key, kv.Value.req, kv.Value.blk, kv.Value.days, kv.Value.hosts,
                kv.Value.days >= 5 ? "Persistente" : kv.Value.days >= 2 ? "Intermitente" : "Ráfaga (1 día)"))
            .OrderByDescending(i => i.Requests).ThenBy(i => i.Ip, StringComparer.Ordinal).ToList();

        // ---- false-positive candidates (server-side heuristic, impact decided here) ----
        var fpAgg = new Dictionary<(string rule, string uri), (long hits, long total, long blk, long det, long ips, string host, string action)>();
        foreach (var src in srcs)
            foreach (var r in Builders.Rows(src, "fpCandidates"))
            {
                var rule = Helpers.Str(r, "rule") ?? "—";
                var uri = Helpers.Str(r, "uri") ?? "—";
                var key = (rule, uri);
                var prev = fpAgg.GetValueOrDefault(key, (0, 0, 0, 0, 0, "", ""));
                fpAgg[key] = (prev.hits + Builders.Num64(r, "hits"), prev.total + Builders.Num64(r, "ruleTotal"),
                    prev.blk + Builders.Num64(r, "blockedHits"), prev.det + Builders.Num64(r, "detectedHits"),
                    Math.Max(prev.ips, Builders.Num64(r, "distinctIps")),
                    prev.host.Length > 0 ? prev.host : Helpers.Str(r, "host") ?? "", prev.action.Length > 0 ? prev.action : Helpers.Str(r, "action") ?? "—");
            }
        var fp = new List<WafFp>();
        foreach (var kv in fpAgg)
        {
            var v = kv.Value;
            var action = v.blk > 0 ? "Blocked" : v.det > 0 ? "Detected" : v.action;
            if (action.Contains("allow", StringComparison.OrdinalIgnoreCase)) continue;   // allowed traffic is not a false positive
            var share = v.total > 0 ? Math.Round(v.hits * 100.0 / v.total, 0) : 0;
            if (v.hits < 50 || share < 80) continue;
            var priority = v.blk > 0 ? "Alta" : v.det > 0 ? "Media" : "Baja";
            fp.Add(new WafFp(kv.Key.rule, kv.Key.uri, v.host, action, v.hits, share, v.ips, priority));
        }
        fp = fp.OrderBy(f => Helpers.PriorityRank(f.Priority)).ThenByDescending(f => f.Hits).ThenBy(f => f.Rule, StringComparer.Ordinal).ToList();

        return new WafFacts
        {
            HasLogs = hasLogs,
            MonthDays = monthDays,
            ActiveDays = activeDays,
            FirstEvent = first,
            LastEvent = last,
            Events = events,
            Requests = requests,
            Blocked = blocked,
            Detected = detected,
            Allowed = allowed,
            Challenged = challenged,
            DistinctIps = distinctIps,
            DistinctHosts = distinctHosts,
            Policies = policies,
            Gateways = gateways,
            ProtectedApps = protectedApps,
            DirectProdApps = directProd,
            Days = days,
            Categories = categories,
            Hosts = hosts,
            Rules = rules,
            Ips = ips,
            FpCandidates = fp,
        };
    }

    // Analysis window: waf-logs.json start/end, else the analysis month, else 30 days.
    static (DateTime start, DateTime end) Window(Model m)
    {
        if (DateTime.TryParse(m.WafStart, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var s)
            && DateTime.TryParse(m.WafEnd, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var e) && e > s)
            return (s.Date, e.Date > s.Date ? e.Date : s.Date.AddDays(1));
        if (m.AnalysisMonth is { Length: >= 7 } am && int.TryParse(am[..4], out var y) && int.TryParse(am[5..7], out var mo))
        {
            var start = new DateTime(y, mo, 1, 0, 0, 0, DateTimeKind.Utc);
            return (start, start.AddMonths(1));
        }
        var today = DateTime.UtcNow.Date;
        return (today.AddDays(-30), today);
    }

    static WafPolicyRow? MatchPolicy(List<WafPolicyRow> policies, string policyId, string gatewayIdLower)
    {
        if (policyId.Length > 0)
        {
            var lower = policyId.ToLowerInvariant();
            var byId = policies.FirstOrDefault(p => p.IdLower == lower);
            if (byId is not null) return byId;
            var name = lower.Contains('/') ? lower[(lower.LastIndexOf('/') + 1)..] : lower;
            var byName = policies.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }
        if (gatewayIdLower.Length == 0) return null;
        // Embedded (classic) config rows carry the gateway itself as id; otherwise the single policy on that gateway.
        return policies.FirstOrDefault(p => p.IdLower == gatewayIdLower)
            ?? (policies.Where(p => p.GatewayIdsLower.Contains(gatewayIdLower)).ToList() is { Count: 1 } one ? one[0] : null);
    }

    static List<WafPolicyRow> BuildPolicies(Model m)
    {
        var rows = new List<WafPolicyRow>();
        var gatewayNames = m.Resources
            .Where(r => r.Type.Equals("microsoft.network/applicationgateways", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r.IdLower, r => r.Name, StringComparer.Ordinal);

        foreach (var r in m.Resources)
        {
            var type = r.Type.ToLowerInvariant();
            var props = r.Properties;
            if (type == "microsoft.network/applicationgatewaywebapplicationfirewallpolicies")
            {
                var (mode, state) = PolicySettings(props);
                var listenerIds = IdList(props, "httpListeners").Concat(IdList(props, "pathBasedRules")).ToList();
                var listeners = listenerIds.Count;
                // Gateway-level attachments list the gateway; listener/path attachments only carry
                // the listener id, whose prefix (â¦/applicationGateways/<name>/â¦) names the gateway.
                var gwIds = IdList(props, "applicationGateways")
                    .Concat(listenerIds.Select(id => id.IndexOf("/httplisteners/", StringComparison.Ordinal) is var li && li > 0 ? id[..li]
                        : id.IndexOf("/urlpathmaps/", StringComparison.Ordinal) is var pi && pi > 0 ? id[..pi] : ""))
                    .Where(id => id.Length > 0).Distinct().ToList();
                var gwLabel = gwIds.Count == 0 ? "Sin asociar"
                    : Helpers.NameList(gwIds.Select(id => gatewayNames.TryGetValue(id, out var n) ? n : Helpers.LastSegment(id)).Distinct().ToList(), 40);
                var scope = listeners > 0 ? $"{gwLabel} · {listeners} listener(s)" : gwIds.Count > 0 ? $"{gwLabel} · global" : gwLabel;
                rows.Add(new WafPolicyRow(r.Name, r.IdLower, r.FriendlyType, mode, state, scope, ManagedRuleSets(props),
                    ArrayCount(props, "customRules"), ExclusionCount(props), gwIds));
            }
            else if (type == "microsoft.network/frontdoorwebapplicationfirewallpolicies")
            {
                var (mode, state) = PolicySettings(props);
                var links = IdList(props, "securityPolicyLinks").Count + IdList(props, "frontendEndpointLinks").Count;
                var custom = props.ValueKind == JsonValueKind.Object && props.TryGetProperty("customRules", out var cr) && cr.ValueKind == JsonValueKind.Object
                    ? ArrayCount(cr, "rules") : 0;
                rows.Add(new WafPolicyRow(r.Name, r.IdLower, r.FriendlyType, mode, state,
                    links > 0 ? $"Front Door · {links} endpoint(s)" : "Sin asociar", ManagedRuleSets(props), custom, ExclusionCount(props), new List<string>()));
            }
            else if (type == "microsoft.network/applicationgateways"
                && props.ValueKind == JsonValueKind.Object
                && props.TryGetProperty("webApplicationFirewallConfiguration", out var wc) && wc.ValueKind == JsonValueKind.Object)
            {
                var enabled = Helpers.BoolProp(wc, "enabled");
                var set = $"{Helpers.Str(wc, "ruleSetType") ?? "—"} {Helpers.Str(wc, "ruleSetVersion") ?? ""}".Trim();
                rows.Add(new WafPolicyRow(r.Name, r.IdLower, "App Gateway (WAF integrado)", Helpers.Str(wc, "firewallMode") ?? "—",
                    enabled ? "Enabled" : "Disabled", "Configuración integrada", FriendlyRuleSet(Helpers.Str(wc, "ruleSetType"), Helpers.Str(wc, "ruleSetVersion"), ""),
                    0, ArrayCount(wc, "exclusions"), new List<string> { r.IdLower }));
            }
        }
        return rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static (string mode, string state) PolicySettings(JsonElement props)
    {
        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("policySettings", out var ps) || ps.ValueKind != JsonValueKind.Object)
            return ("—", "—");
        return (Helpers.Str(ps, "mode") ?? "—", Helpers.Str(ps, "state") ?? Helpers.Str(ps, "enabledState") ?? "—");
    }

    static List<string> IdList(JsonElement props, string prop)
    {
        var list = new List<string>();
        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
            if (Helpers.Str(e, "id") is { Length: > 0 } id) list.Add(id.ToLowerInvariant());
        return list;
    }

    static int ArrayCount(JsonElement props, string prop) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.GetArrayLength() : 0;

    static int ExclusionCount(JsonElement props) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty("managedRules", out var mr) && mr.ValueKind == JsonValueKind.Object
            ? ArrayCount(mr, "exclusions") : 0;

    static string ManagedRuleSets(JsonElement props)
    {
        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("managedRules", out var mr) || mr.ValueKind != JsonValueKind.Object
            || !mr.TryGetProperty("managedRuleSets", out var sets) || sets.ValueKind != JsonValueKind.Array)
            return "—";
        var names = sets.EnumerateArray()
            .Select(s => FriendlyRuleSet(Helpers.Str(s, "ruleSetType"), Helpers.Str(s, "ruleSetVersion"), ""))
            .Where(n => n != "—").Distinct().ToList();
        return names.Count == 0 ? "—" : string.Join(" + ", names);
    }

    static List<WafGateway> BuildGateways(Model m, List<WafPolicyRow> policies)
    {
        var prevMonth = m.Months.Count >= 2 ? m.Months[^2] : null;
        double Cost(string idLower) => m.CurrentMonthCostById.GetValueOrDefault(idLower, 0);
        double PrevCost(string idLower) => prevMonth is null ? 0 : m.Costs.Where(c => c.CloudId == idLower && c.Month == prevMonth).Sum(c => c.Value);

        var appHostnames = m.Resources
            .Where(r => r.Type.Equals("microsoft.web/sites", StringComparison.OrdinalIgnoreCase)
                     || r.Type.Equals("microsoft.app/containerapps", StringComparison.OrdinalIgnoreCase))
            .Select(r => Helpers.AppHostnames(r)).Where(h => h.Count > 0).ToList();

        var list = new List<WafGateway>();
        foreach (var gw in m.Resources.Where(r => r.Type.Equals("microsoft.network/applicationgateways", StringComparison.OrdinalIgnoreCase)))
        {
            if (gw.Properties.ValueKind != JsonValueKind.Object) continue;
            var hasWaf = gw.Properties.TryGetProperty("firewallPolicy", out _)
                || (gw.Properties.TryGetProperty("webApplicationFirewallConfiguration", out var wc) && Helpers.BoolProp(wc, "enabled"));
            if (!hasWaf) continue;
            var fqdns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gw.Properties.TryGetProperty("backendAddressPools", out var pools) && pools.ValueKind == JsonValueKind.Array)
                foreach (var pool in pools.EnumerateArray())
                    if (pool.TryGetProperty("properties", out var pp) && pp.ValueKind == JsonValueKind.Object
                        && pp.TryGetProperty("backendAddresses", out var addrs) && addrs.ValueKind == JsonValueKind.Array)
                        foreach (var a in addrs.EnumerateArray())
                            if (Helpers.Str(a, "fqdn") is { Length: > 0 } fqdn) fqdns.Add(fqdn);
            var protectedApps = appHostnames.Count(h => h.Any(fqdns.Contains));
            var sku = gw.SkuName ?? gw.SkuTier ?? "—";
            if (gw.SkuCapacity is { } cap && cap > 0) sku += $" ×{cap}";
            list.Add(new WafGateway(gw.Name, gw.FriendlyType, sku, protectedApps, Math.Round(Cost(gw.IdLower), 2), Math.Round(PrevCost(gw.IdLower), 2)));
        }

        // Front Door: Standard/Premium profiles linked from a FD WAF policy, plus classic Front Doors with a policy link.
        var fdProfileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pol in m.Resources.Where(r => r.Type.Equals("microsoft.network/frontdoorwebapplicationfirewallpolicies", StringComparison.OrdinalIgnoreCase)))
            foreach (var link in IdList(pol.Properties, "securityPolicyLinks"))
            {
                var idx = link.IndexOf("/securitypolicies/", StringComparison.Ordinal);
                if (idx > 0) fdProfileIds.Add(link[..idx]);
            }
        foreach (var r in m.Resources)
        {
            var type = r.Type.ToLowerInvariant();
            var isProfile = type == "microsoft.cdn/profiles" && fdProfileIds.Contains(r.IdLower);
            var isClassic = type == "microsoft.network/frontdoors" && r.Properties.ValueKind == JsonValueKind.Object
                && r.Properties.TryGetProperty("frontendEndpoints", out var fes) && fes.ValueKind == JsonValueKind.Array
                && fes.EnumerateArray().Any(fe => fe.TryGetProperty("properties", out var fp) && fp.ValueKind == JsonValueKind.Object && fp.TryGetProperty("webApplicationFirewallPolicyLink", out _));
            if (!isProfile && !isClassic) continue;
            list.Add(new WafGateway(r.Name, isProfile ? "Front Door" : "Front Door (clásico)", r.SkuName ?? r.SkuTier ?? "—", null,
                Math.Round(Cost(r.IdLower), 2), Math.Round(PrevCost(r.IdLower), 2)));
        }
        return list.OrderByDescending(g => g.Cost).ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Same key derivation as the KQL stage, for rows that predate the `category` column.
    static string CategoryKey(string rule, string? ruleSet)
    {
        var digits = System.Text.RegularExpressions.Regex.Match(rule ?? "", @"(\d{6,8})\s*$").Groups[1].Value;
        if (string.IsNullOrEmpty(rule)) return "unknown";
        if (digits.Length == 0) return "custom:" + rule;
        var isBot = (ruleSet ?? "").Contains("Bot", StringComparison.OrdinalIgnoreCase) || rule.Contains("Bot", StringComparison.OrdinalIgnoreCase);
        return (isBot ? "bot:" : "") + (digits.Length == 8 ? digits[..5] : digits[..3]);
    }

    public static (string label, string? color) CategoryInfo(string key) => key switch
    {
        "200" => ("Validación general de la petición", "naranja"),
        "910" => ("Reputación de IP", "naranja"),
        "911" => ("Método HTTP no permitido", "naranja"),
        "912" => ("Protección contra DoS", "naranja"),
        "913" => ("Escáneres y herramientas de ataque", "naranja"),
        "920" => ("Violaciones del protocolo HTTP", "naranja"),
        "921" => ("Ataques al protocolo (smuggling, splitting)", "naranja"),
        "930" => ("Inclusión de archivos locales (LFI)", "rojo"),
        "931" => ("Inclusión de archivos remotos (RFI)", "rojo"),
        "932" => ("Ejecución remota de comandos (RCE)", "rojo"),
        "933" => ("Inyección PHP", "rojo"),
        "934" => ("Ataques a Node.js y otros runtimes", "rojo"),
        "941" => ("Cross-Site Scripting (XSS)", "rojo"),
        "942" => ("Inyección SQL (SQLi)", "rojo"),
        "943" => ("Fijación de sesión", "rojo"),
        "944" => ("Ataques a Java", "rojo"),
        "949" => ("Decisión por puntuación de anomalía", null),
        "980" => ("Correlación de anomalía", null),
        "99001" => ("Vulnerabilidades conocidas (CVE, Microsoft Threat Intel)", "rojo"),
        "99005" => ("Web shells (Microsoft Threat Intel)", "rojo"),
        "99030" => ("Ataques a aplicaciones (Microsoft Threat Intel)", "rojo"),
        "99031" => ("Inyección SQL (Microsoft Threat Intel)", "rojo"),
        "bot:100" => ("Bots maliciosos (threat intelligence)", "rojo"),
        "bot:200" => ("Bots legítimos verificados", "azul"),
        "bot:300" => ("Bots desconocidos / no verificados", "naranja"),
        "unknown" => ("Sin clasificar", null),
        _ when key.StartsWith("custom:", StringComparison.Ordinal) => ($"Regla personalizada: {key[7..]}", "naranja"),
        _ => ($"Categoría {key}", null),
    };

    // Friendly rule-set label from the set type/version; falls back to the rule name
    // (Front Door encodes the set in the rule name, e.g. Microsoft_DefaultRuleSet-2.1-SQLI-942100).
    public static string FriendlyRuleSet(string? set, string? version, string rule)
    {
        static string? Known(string s, string v) =>
            s.Contains("BotManager", StringComparison.OrdinalIgnoreCase) ? "Bot Manager" + v
            : s.Contains("DefaultRuleSet", StringComparison.OrdinalIgnoreCase) ? "DRS" + v
            : s.Contains("OWASP", StringComparison.OrdinalIgnoreCase) ? "OWASP CRS" + v
            : null;
        var v = string.IsNullOrWhiteSpace(version) ? "" : " " + version.Trim();
        if (Known(set ?? "", v) is { } fromSet) return fromSet;
        if (Known(rule ?? "", "") is { } fromRule)
        {
            var ver = System.Text.RegularExpressions.Regex.Match(rule!, @"-(\d+\.\d+)-").Groups[1].Value;
            return ver.Length > 0 ? fromRule + " " + ver : fromRule;
        }
        return string.IsNullOrWhiteSpace(set) ? "—" : (set + v).Trim();
    }
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
