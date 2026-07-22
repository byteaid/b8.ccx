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
    static void AddWafBlocks(Model m, List<object> blocks)
    {
        // Inventory from ARM state: WAF policies + App Gateways with embedded (classic) WAF config.
        var inventory = new List<object?[]>();
        foreach (var r in m.Resources)
        {
            var type = r.Type.ToLowerInvariant();
            if (type == "microsoft.network/applicationgatewaywebapplicationfirewallpolicies"
                || type == "microsoft.network/frontdoorwebapplicationfirewallpolicies")
            {
                string mode = "—", state = "—";
                if (r.Properties.ValueKind == JsonValueKind.Object && r.Properties.TryGetProperty("policySettings", out var ps))
                {
                    mode = Helpers.Str(ps, "mode") ?? "—";
                    state = Helpers.Str(ps, "state") ?? Helpers.Str(ps, "enabledState") ?? "—";
                }
                inventory.Add(new object?[] { r.Name, r.FriendlyType, mode, state });
            }
            else if (type == "microsoft.network/applicationgateways"
                && r.Properties.ValueKind == JsonValueKind.Object
                && r.Properties.TryGetProperty("webApplicationFirewallConfiguration", out var wc))
            {
                var enabled = wc.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                inventory.Add(new object?[] { r.Name, "App Gateway (WAF integrado)", Helpers.Str(wc, "firewallMode") ?? "—", enabled ? "Enabled" : "Disabled" });
            }
        }
        blocks.Add(Block("waf-inventory", "WAF — Inventario", "Table",
            m.WafSources.Count == 0 && inventory.Count > 0
                ? "Hay WAF desplegado pero no se recolectaron logs de firewall (sin workspace o diagnósticos no conectados a Log Analytics) — habilitarlos es el prerrequisito para los bloques de actividad siguientes."
                : "Políticas WAF y configuraciones WAF clásicas de App Gateway desplegadas, con su modo de operación (Prevention/Detection) y estado.",
            new { headers = new[] { "Recurso", "Tipo", "Modo", "Estado" }, align = new[] { "l", "l", "c", "c" }, rows = inventory }));

        // Events by action, merged across sources, as a bar list.
        var actionTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in m.WafSources)
            foreach (var r in Rows(src, "byAction"))
            {
                var action = Helpers.Str(r, "action") ?? "—";
                actionTotals[action] = actionTotals.GetValueOrDefault(action, 0) + Hits(r);
            }
        blocks.Add(Block("waf-actions", "WAF — Eventos por Acción", "BarList",
            "Total de eventos WAF por acción durante el mes de análisis, consolidado entre todas las fuentes de logs.",
            new
            {
                format = "count",
                rows = actionTotals.OrderByDescending(kv => kv.Value).Select(kv => new
                {
                    label = kv.Key,
                    value = kv.Value,
                    color = ActionColor(kv.Key),
                }).ToList(),
            }));

        // Top rules merged across sources (rule+ruleSet key), capped at 8.
        var ruleAgg = new Dictionary<(string rule, string set), (long hits, string sample)>();
        foreach (var src in m.WafSources)
            foreach (var r in Rows(src, "topRules"))
            {
                var key = (Helpers.Str(r, "rule") ?? "—", Helpers.Str(r, "ruleSet") ?? "—");
                var prev = ruleAgg.GetValueOrDefault(key, (0, ""));
                ruleAgg[key] = (prev.hits + Hits(r), prev.sample.Length > 0 ? prev.sample : Helpers.Str(r, "sample") ?? "");
            }
        blocks.Add(Block("waf-top-rules", "WAF — Reglas más Activadas (Top 8)", "Table",
            "Las reglas WAF con más activaciones en el período; la columna Ejemplo muestra un mensaje representativo de la regla.",
            new
            {
                headers = new[] { "Regla", "Conjunto", "Eventos", "Ejemplo" },
                align = new[] { "l", "l", "r", "l" },
                rows = ruleAgg.OrderByDescending(kv => kv.Value.hits).Take(8)
                    .Select(kv => (object?[])new object?[] { kv.Key.rule, kv.Key.set, kv.Value.hits, Helpers.TruncateText(kv.Value.sample, 90) }).ToList(),
                dense = true,
            }));

        // Top client IPs merged, capped at 8.
        var ipAgg = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var src in m.WafSources)
            foreach (var r in Rows(src, "topClientIps"))
            {
                var ip = Helpers.Str(r, "clientIp") ?? "—";
                ipAgg[ip] = ipAgg.GetValueOrDefault(ip, 0) + Hits(r);
            }
        blocks.Add(Block("waf-top-ips", "WAF — IPs de Origen más Frecuentes (Top 8)", "Table",
            "Las direcciones IP de origen con más eventos WAF en el período, consolidadas entre fuentes.",
            new
            {
                headers = new[] { "IP Cliente", "Eventos" },
                align = new[] { "l", "r" },
                rows = ipAgg.OrderByDescending(kv => kv.Value).Take(8)
                    .Select(kv => (object?[])new object?[] { kv.Key, kv.Value }).ToList(),
            }));

        // False-positive candidates across sources.
        var fp = m.WafSources.SelectMany(FpCandidates).ToList();
        blocks.Add(Block("waf-fp-candidates", "WAF — Posibles Falsos Positivos", "Table",
            "Reglas cuyos eventos se concentran (≥80%) en una sola URI con ≥50 eventos — la firma típica de un falso positivo; revisar antes de crear exclusiones o pasar a Prevention.",
            new { headers = new[] { "Regla", "URI", "Acción", "Eventos", "% de la regla" }, align = new[] { "l", "l", "c", "r", "r" }, rows = fp }));
    }

    // Blocked = the WAF doing its job (verde); detect-only actions naranja; else neutral.
    static string? ActionColor(string action)
    {
        var a = action.ToLowerInvariant();
        if (a.Contains("block")) return "verde";
        if (a.Contains("match") || a.Contains("detect") || a.Contains("log")) return "naranja";
        if (a.Contains("allow")) return null;
        return "azul";
    }

    // FP heuristic: rule×URI combos holding >=80% of the rule's total hits with >=50 events.
    static List<object?[]> FpCandidates(JsonElement src)
    {
        var ruleTotals = Rows(src, "topRules")
            .Where(r => Helpers.Str(r, "rule") is not null)
            .ToDictionary(r => Helpers.Str(r, "rule")!, Hits, StringComparer.Ordinal);
        var result = new List<object?[]>();
        foreach (var combo in Rows(src, "topRuleUris"))
        {
            var rule = Helpers.Str(combo, "rule");
            var hits = Hits(combo);
            if (rule is null || hits < 50 || !ruleTotals.TryGetValue(rule, out var total) || total <= 0) continue;
            var share = hits * 100.0 / total;
            if (share < 80) continue;
            result.Add(new object?[]
            {
                rule, Helpers.TruncateText(Helpers.Str(combo, "uri") ?? "—", 60),
                Helpers.Str(combo, "action") ?? "—", hits,
                $"{Math.Round(share, 0).ToString(CultureInfo.InvariantCulture)}%",
            });
        }
        return result;
    }

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

        AddWafBlocks(m, blocks);

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

        // WAF signals aggregated across sources.
        long totalWafEvents = 0, blockedWafEvents = 0;
        var wafTopRules = new List<object>();
        var fpCandidateCount = 0;
        foreach (var src in m.WafSources)
        {
            foreach (var row in Rows(src, "byAction"))
            {
                var hits = Hits(row);
                totalWafEvents += hits;
                var action = (Helpers.Str(row, "action") ?? "").ToLowerInvariant();
                if (action.Contains("block")) blockedWafEvents += hits;
            }
            wafTopRules.AddRange(Rows(src, "topRules").Take(5).Select(r => (object)new
            {
                source = Helpers.Str(src, "source"),
                rule = Helpers.Str(r, "rule"),
                ruleSet = Helpers.Str(r, "ruleSet"),
                hits = Hits(r),
            }));
            fpCandidateCount += FpCandidates(src).Count;
        }

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
                present = m.WafSources.Count > 0,
                totalEvents = totalWafEvents,
                blockedEvents = blockedWafEvents,
                topRules = wafTopRules,
                fpCandidateCount,
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

    internal static long Hits(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty("hits", out var h) && h.ValueKind == JsonValueKind.Number
            ? h.GetInt64() : 0;

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

    static string LastSegment(string s)
    {
        var idx = s.LastIndexOf('/');
        return idx >= 0 && idx < s.Length - 1 ? s[(idx + 1)..] : s;
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
