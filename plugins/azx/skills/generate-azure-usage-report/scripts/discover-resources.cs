#:property PublishAot=false
#:package Azure.Identity@1.13.1
#:package Azure.ResourceManager@1.13.1
#:package Azure.ResourceManager.AppService@1.3.0
#:package Azure.ResourceManager.Dns@1.1.1
#:package Azure.ResourceManager.PrivateDns@1.2.1
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.PrivateDns;

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var subscriptionOption = new Option<string?>("--subscription") { Description = "Subscription id. Falls back to subscriptions.json.", HelpName = "ID" };
var maxParallelOption = new Option<int>("--max-parallel") { Description = "Max concurrent enrichment calls.", DefaultValueFactory = _ => 6 };
var limitOption = new Option<int?>("--limit") { Description = "Process only the first N resources after filtering.", HelpName = "N" };
var includeTypeOption = new Option<string[]>("--include-type") { Description = "Keep only ARM types matching one of the given values (case-insensitive). Repeatable.", HelpName = "TYPE" };
var filterSystemOption = new Option<bool>("--filter-system-resources") { DefaultValueFactory = _ => true, Description = "Drop master DBs and other system-managed resources." };
var forceOption = new Option<bool>("--force") { Description = "Overwrite resources.json." };

var rootCommand = new RootCommand("Stage 2 — discover Azure resources and pull full ARM properties + sub-resource enrichment.")
{
    stageDirOption, subscriptionOption, maxParallelOption, limitOption, includeTypeOption, filterSystemOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var subscriptionArg = parseResult.GetValue(subscriptionOption);
    var maxParallel = parseResult.GetValue(maxParallelOption);
    var limit = parseResult.GetValue(limitOption);
    var includeTypes = parseResult.GetValue(includeTypeOption);
    var filterSystem = parseResult.GetValue(filterSystemOption);
    var force = parseResult.GetValue(forceOption);

    stageDir.Create();
    var outputPath = Path.Combine(stageDir.FullName, "resources.json");
    if (File.Exists(outputPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[discover-resources] {outputPath} exists. Use --force to overwrite.");
        return 1;
    }

    var subscriptionId = subscriptionArg ?? await ResolveSubscriptionFromStage(stageDir.FullName, ct);
    if (subscriptionId is null)
    {
        await Console.Error.WriteLineAsync("[discover-resources] No --subscription provided and subscriptions.json missing or empty.");
        return 2;
    }

    ArmClient arm;
    try { arm = new ArmClient(new DefaultAzureCredential()); }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[discover-resources] Azure credential setup failed: {ex.Message}");
        return 3;
    }

    var includeSet = (includeTypes is null || includeTypes.Length == 0)
        ? null
        : new HashSet<string>(includeTypes, StringComparer.OrdinalIgnoreCase);

    var subscription = arm.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
    var ids = new List<ResourceIdentifier>();
    var droppedSystem = 0;
    var droppedTypeFilter = 0;

    try
    {
        await foreach (var resource in subscription.GetGenericResourcesAsync(cancellationToken: ct))
        {
            var typeKey = resource.Data.ResourceType.ToString();
            if (filterSystem && IsSystemResource(typeKey, resource.Data.Name, resource.Data.Tags)) { droppedSystem++; continue; }
            if (includeSet is not null && !includeSet.Contains(typeKey)) { droppedTypeFilter++; continue; }
            ids.Add(resource.Id);
            if (limit is not null && ids.Count >= limit.Value) break;
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[discover-resources] enumeration failed: {ex.Message}");
        return 1;
    }

    await Console.Error.WriteLineAsync(
        $"[discover-resources] discovered {ids.Count} resource ids " +
        $"(dropped {droppedSystem} system, {droppedTypeFilter} type-filtered). Fetching details with max-parallel={maxParallel}.");

    var entries = new Dictionary<string, object?>?[ids.Count];
    using var sem = new SemaphoreSlim(maxParallel);
    var tasks = new List<Task>();
    var fetchErrors = 0;
    var enrichErrors = 0;
    var enrichCount = 0;

    for (int i = 0; i < ids.Count; i++)
    {
        var index = i;
        var id = ids[i];
        await sem.WaitAsync(ct);
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                var generic = await arm.GetGenericResource(id).GetAsync(cancellationToken: ct);
                var d = generic.Value.Data;
                var typeKey = d.ResourceType.ToString();
                var entry = new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = d.Name,
                    ["type"] = typeKey,
                    ["region"] = d.Location.ToString(),
                    ["resourceGroup"] = id.ResourceGroupName,
                };
                if (!string.IsNullOrEmpty(d.Kind)) entry["kind"] = d.Kind;
                if (d.Sku is not null) entry["sku"] = new Dictionary<string, object?>
                {
                    ["name"] = d.Sku.Name,
                    ["tier"] = d.Sku.Tier,
                    ["size"] = d.Sku.Size,
                    ["family"] = d.Sku.Family,
                    ["model"] = d.Sku.Model,
                    ["capacity"] = d.Sku.Capacity,
                };
                if (d.Tags is not null && d.Tags.Count > 0)
                    entry["tags"] = new Dictionary<string, string>(d.Tags);
                var props = SerializeProperties(d.Properties);
                if (props is not null) entry["properties"] = props;

                try
                {
                    var enrichment = await Enrich.RunAsync(arm, typeKey, id, ct);
                    if (enrichment is not null && enrichment.Count > 0)
                    {
                        entry["enrichment"] = enrichment;
                        Interlocked.Increment(ref enrichCount);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref enrichErrors);
                    await Console.Error.WriteLineAsync($"[discover-resources] enrich {id}: {ex.Message}");
                }

                entries[index] = entry;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref fetchErrors);
                await Console.Error.WriteLineAsync($"[discover-resources] fetch {id}: {ex.Message}");
            }
            finally
            {
                sem.Release();
            }
        }, ct));
    }
    await Task.WhenAll(tasks);

    var resourceList = entries.Where(e => e is not null).Cast<object>().ToList();
    var perType = resourceList
        .Cast<Dictionary<string, object?>>()
        .GroupBy(e => (string)e["type"]!)
        .OrderByDescending(g => g.Count())
        .ToDictionary(g => g.Key, g => g.Count());

    var summary = new Dictionary<string, object?>
    {
        ["total"] = resourceList.Count,
        ["perType"] = perType,
    };
    if (droppedSystem > 0) summary["droppedSystem"] = droppedSystem;
    if (droppedTypeFilter > 0) summary["droppedTypeFilter"] = droppedTypeFilter;
    if (fetchErrors > 0) summary["fetchErrors"] = fetchErrors;
    if (enrichErrors > 0) summary["enrichErrors"] = enrichErrors;
    summary["enrichedCount"] = enrichCount;

    var payload = new Dictionary<string, object?>
    {
        ["subscriptionId"] = subscriptionId,
        ["generatedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        ["summary"] = summary,
        ["resources"] = resourceList,
    };

    await WriteAtomic(outputPath, JsonSerializer.Serialize(payload, Json.Opts), ct);
    await Console.Error.WriteLineAsync(
        $"[discover-resources] wrote {outputPath} ({resourceList.Count} resources, {enrichCount} enriched, " +
        $"{fetchErrors} fetch errors, {enrichErrors} enrich errors)");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task<string?> ResolveSubscriptionFromStage(string stageDir, CancellationToken ct)
{
    var path = Path.Combine(stageDir, "subscriptions.json");
    if (!File.Exists(path)) return null;
    using var stream = File.OpenRead(path);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    if (!doc.RootElement.TryGetProperty("subscriptions", out var arr) || arr.GetArrayLength() == 0) return null;
    return arr[0].GetProperty("id").GetString();
}

static bool IsSystemResource(string type, string name, IDictionary<string, string>? tags)
{
    var t = type.ToLowerInvariant();
    var n = name.ToLowerInvariant();

    string[] systemTypes =
    [
        "microsoft.sql/servers/systemdatabases",
        "microsoft.insights/diagnosticsettings",
        "microsoft.operationalinsights/workspaces/savedsearches",
        "microsoft.insights/autoscalesettings",
        "microsoft.insights/actiongroups",
        "microsoft.operationalinsights/workspaces/datasources",
        "microsoft.servicefabric/clusters/applications/services",
    ];
    foreach (var st in systemTypes) if (t == st) return true;

    string[] systemPatterns =
    [
        @"master$", @"tempdb$", @"model$", @"msdb$",
        @"^master-", @"^system", @"^AzureBackup", @"^DefaultFreeStorage",
        @"-diag$", @"-wad$", @"-lad$", @"^__"
    ];
    foreach (var pat in systemPatterns)
        if (Regex.IsMatch(n, pat, RegexOptions.IgnoreCase)) return true;

    if (tags is not null)
    {
        if (tags.TryGetValue("ms-resource-usage", out var v1) && v1.Equals("system", StringComparison.OrdinalIgnoreCase)) return true;
        if (tags.TryGetValue("isSystemManaged", out var v2) && v2.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (tags.TryGetValue("hidden-resourceType", out var v3) && v3.Equals("systemOnly", StringComparison.OrdinalIgnoreCase)) return true;
        if (tags.TryGetValue("createdBy", out var v4) && v4.Equals("Microsoft.Azure", StringComparison.OrdinalIgnoreCase)) return true;
    }

    if (t.Contains("microsoft.sql/servers/databases"))
    {
        var db = name.Split('/').LastOrDefault()?.ToLowerInvariant();
        if (db is "master" or "tempdb" or "model" or "msdb") return true;
    }

    return false;
}

static object? SerializeProperties(BinaryData? properties)
{
    if (properties is null) return null;
    try
    {
        using var doc = JsonDocument.Parse(properties);
        return doc.RootElement.Clone();
    }
    catch
    {
        return null;
    }
}

static async Task WriteAtomic(string path, string content, CancellationToken ct)
{
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, content, ct);
    File.Move(tmp, path, overwrite: true);
}

static class Json
{
    public static readonly JsonSerializerOptions Opts = BuildOpts();

    static JsonSerializerOptions BuildOpts()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 128,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        };
        o.Converters.Add(new IPAddressConverter());
        o.Converters.Add(new UriConverter());
        return o;
    }
}

sealed class IPAddressConverter : System.Text.Json.Serialization.JsonConverter<System.Net.IPAddress>
{
    public override System.Net.IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() is { } s ? System.Net.IPAddress.Parse(s) : null;
    public override void Write(Utf8JsonWriter writer, System.Net.IPAddress value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

sealed class UriConverter : System.Text.Json.Serialization.JsonConverter<Uri>
{
    public override Uri? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() is { } s ? new Uri(s) : null;
    public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

// =====================================================================
// Sub-resource enrichment. ONLY for types whose full picture requires
// additional API calls beyond the resource's GET (siteConfig, DNS record
// sets, vnet links). Anything reachable from the GenericResource.Properties
// payload is intentionally NOT duplicated here — consumers read from
// `properties` for that.
// =====================================================================
static class Enrich
{
    public static async Task<Dictionary<string, object?>?> RunAsync(
        ArmClient arm, string typeKey, ResourceIdentifier id, CancellationToken ct)
        => typeKey.ToLowerInvariant() switch
        {
            "microsoft.web/sites" => await AppServiceAsync(arm, id, ct),
            "microsoft.network/dnszones" => await DnsZoneAsync(arm, id, ct),
            "microsoft.network/privatednszones" => await PrivateDnsZoneAsync(arm, id, ct),
            _ => null,
        };

    static async Task<Dictionary<string, object?>?> AppServiceAsync(ArmClient arm, ResourceIdentifier id, CancellationToken ct)
    {
        var web = arm.GetWebSiteResource(id);
        var info = new Dictionary<string, object?>();

        try
        {
            var configRes = await web.GetWebSiteConfig().GetAsync(cancellationToken: ct);
            var c = configRes.Value.Data;
            info["siteConfig"] = new Dictionary<string, object?>
            {
                ["alwaysOn"] = c.IsAlwaysOn,
                ["use32BitWorkerProcess"] = c.Use32BitWorkerProcess,
                ["managedPipelineMode"] = c.ManagedPipelineMode?.ToString(),
                ["minTlsVersion"] = c.MinTlsVersion?.ToString(),
                ["scmMinTlsVersion"] = c.ScmMinTlsVersion?.ToString(),
                ["ftpsState"] = c.FtpsState?.ToString(),
                ["http20Enabled"] = c.IsHttp20Enabled,
                ["remoteDebuggingEnabled"] = c.IsRemoteDebuggingEnabled,
                ["healthCheckPath"] = c.HealthCheckPath,
                ["ipSecurityRestrictionsDefaultAction"] = c.IPSecurityRestrictionsDefaultAction?.ToString(),
                ["scmIpSecurityRestrictionsDefaultAction"] = c.ScmIPSecurityRestrictionsDefaultAction?.ToString(),
                ["ipSecurityRestrictions"] = c.IPSecurityRestrictions?.Select(r => new
                {
                    name = r.Name,
                    priority = r.Priority,
                    action = r.Action,
                    ipAddress = r.IPAddressOrCidr,
                    subnetMask = r.SubnetMask,
                    vnetSubnetResourceId = r.VnetSubnetResourceId?.ToString(),
                    tag = r.Tag?.ToString(),
                    description = r.Description,
                }).Cast<object>().ToList(),
                ["scmIpSecurityRestrictions"] = c.ScmIPSecurityRestrictions?.Select(r => new
                {
                    name = r.Name,
                    priority = r.Priority,
                    action = r.Action,
                    ipAddress = r.IPAddressOrCidr,
                }).Cast<object>().ToList(),
                ["cors"] = c.Cors is null ? null : new
                {
                    allowedOrigins = c.Cors.AllowedOrigins?.ToList(),
                    supportCredentials = c.Cors.IsCredentialsSupported,
                },
            };
        }
        catch
        {
            // siteConfig is unreachable for some site shapes (read-only / restricted slots).
        }

        try
        {
            var pe = web.GetSitePrivateEndpointConnections();
            var list = new List<object>();
            await foreach (var p in pe.GetAllAsync(cancellationToken: ct))
            {
                list.Add(new
                {
                    name = p.Data.Name,
                    state = p.Data.PrivateLinkServiceConnectionState?.Status?.ToString(),
                    description = p.Data.PrivateLinkServiceConnectionState?.Description,
                    provisioningState = p.Data.ProvisioningState,
                });
            }
            if (list.Count > 0) info["privateEndpointConnections"] = list;
        }
        catch
        {
            // Optional.
        }

        return info.Count == 0 ? null : info;
    }

    static async Task<Dictionary<string, object?>?> DnsZoneAsync(ArmClient arm, ResourceIdentifier id, CancellationToken ct)
    {
        var zone = arm.GetDnsZoneResource(id);
        var sets = new Dictionary<string, object>();

        await TryAddAsync(sets, "a", () => CollectAsync(zone.GetDnsARecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ipAddresses = r.Data.DnsARecords?.Select(x => x.IPv4Address?.ToString()).ToList(),
        }));
        await TryAddAsync(sets, "aaaa", () => CollectAsync(zone.GetDnsAaaaRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ipv6Addresses = r.Data.DnsAaaaRecords?.Select(x => x.IPv6Address?.ToString()).ToList(),
        }));
        await TryAddAsync(sets, "cname", () => CollectAsync(zone.GetDnsCnameRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds, cname = r.Data.Cname,
        }));
        await TryAddAsync(sets, "mx", () => CollectAsync(zone.GetDnsMXRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            mxRecords = r.Data.DnsMXRecords?.Select(x => new { preference = x.Preference, exchange = x.Exchange }).Cast<object>().ToList(),
        }));
        await TryAddAsync(sets, "ns", () => CollectAsync(zone.GetDnsNSRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            nameServers = r.Data.DnsNSRecords?.Select(x => x.DnsNSDomainName).ToList(),
        }));
        await TryAddAsync(sets, "ptr", () => CollectAsync(zone.GetDnsPtrRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ptrDomains = r.Data.DnsPtrRecords?.Select(x => x.DnsPtrDomainName).ToList(),
        }));
        await TryAddAsync(sets, "srv", () => CollectAsync(zone.GetDnsSrvRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            srvRecords = r.Data.DnsSrvRecords?.Select(x => new { priority = x.Priority, weight = x.Weight, port = x.Port, target = x.Target }).Cast<object>().ToList(),
        }));
        await TryAddAsync(sets, "txt", () => CollectAsync(zone.GetDnsTxtRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            values = r.Data.DnsTxtRecords?.SelectMany(x => x.Values).ToList(),
        }));
        await TryAddAsync(sets, "caa", () => CollectAsync(zone.GetDnsCaaRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            caaRecords = r.Data.DnsCaaRecords?.Select(x => new { flags = x.Flags, tag = x.Tag, value = x.Value }).Cast<object>().ToList(),
        }));

        var info = new Dictionary<string, object?>();
        if (sets.Count > 0) info["recordSets"] = sets;
        try
        {
            var soa = await zone.GetDnsSoaRecordAsync("@", cancellationToken: ct);
            if (soa.Value?.Data?.DnsSoaRecord is { } s)
            {
                info["soaRecord"] = new
                {
                    name = soa.Value.Data.Name,
                    fqdn = soa.Value.Data.Fqdn,
                    ttl = soa.Value.Data.TtlInSeconds,
                    host = s.Host,
                    email = s.Email,
                    serialNumber = s.SerialNumber,
                    refreshSeconds = s.RefreshTimeInSeconds,
                    retrySeconds = s.RetryTimeInSeconds,
                    expireSeconds = s.ExpireTimeInSeconds,
                    minimumTtlSeconds = s.MinimumTtlInSeconds,
                };
            }
        }
        catch { }
        return info.Count == 0 ? null : info;
    }

    static async Task<Dictionary<string, object?>?> PrivateDnsZoneAsync(ArmClient arm, ResourceIdentifier id, CancellationToken ct)
    {
        var zone = arm.GetPrivateDnsZoneResource(id);
        var info = new Dictionary<string, object?>();

        try
        {
            var links = new List<object>();
            await foreach (var l in zone.GetVirtualNetworkLinks().GetAllAsync(cancellationToken: ct))
            {
                links.Add(new
                {
                    name = l.Data.Name,
                    virtualNetworkId = l.Data.VirtualNetworkId?.ToString(),
                    registrationEnabled = l.Data.RegistrationEnabled,
                    provisioningState = l.Data.PrivateDnsProvisioningState?.ToString(),
                    linkState = l.Data.VirtualNetworkLinkState?.ToString(),
                });
            }
            if (links.Count > 0) info["virtualNetworkLinks"] = links;
        }
        catch { }

        var sets = new Dictionary<string, object>();
        await TryAddAsync(sets, "a", () => CollectAsync(zone.GetPrivateDnsARecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ipAddresses = r.Data.PrivateDnsARecords?.Select(x => x.IPv4Address?.ToString()).ToList(),
        }));
        await TryAddAsync(sets, "aaaa", () => CollectAsync(zone.GetPrivateDnsAaaaRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ipv6Addresses = r.Data.PrivateDnsAaaaRecords?.Select(x => x.IPv6Address?.ToString()).ToList(),
        }));
        await TryAddAsync(sets, "cname", () => CollectAsync(zone.GetPrivateDnsCnameRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds, cname = r.Data.Cname,
        }));
        await TryAddAsync(sets, "mx", () => CollectAsync(zone.GetPrivateDnsMXRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            mxRecords = r.Data.PrivateDnsMXRecords?.Select(x => new { preference = x.Preference, exchange = x.Exchange }).Cast<object>().ToList(),
        }));
        await TryAddAsync(sets, "ptr", () => CollectAsync(zone.GetPrivateDnsPtrRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            ptrDomains = r.Data.PrivateDnsPtrRecords?.Select(x => x.PtrDomainName).ToList(),
        }));
        await TryAddAsync(sets, "srv", () => CollectAsync(zone.GetPrivateDnsSrvRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            srvRecords = r.Data.PrivateDnsSrvRecords?.Select(x => new { priority = x.Priority, weight = x.Weight, port = x.Port, target = x.Target }).Cast<object>().ToList(),
        }));
        await TryAddAsync(sets, "txt", () => CollectAsync(zone.GetPrivateDnsTxtRecords().GetAllAsync(cancellationToken: ct), r => new
        {
            name = r.Data.Name, fqdn = r.Data.Fqdn, ttl = r.Data.TtlInSeconds,
            values = r.Data.PrivateDnsTxtRecords?.SelectMany(x => x.Values).ToList(),
        }));
        if (sets.Count > 0) info["recordSets"] = sets;

        try
        {
            var soa = await zone.GetPrivateDnsSoaRecordAsync("@", cancellationToken: ct);
            if (soa.Value?.Data?.PrivateDnsSoaRecord is { } s)
            {
                info["soaRecord"] = new
                {
                    name = soa.Value.Data.Name,
                    fqdn = soa.Value.Data.Fqdn,
                    ttl = soa.Value.Data.TtlInSeconds,
                    host = s.Host,
                    email = s.Email,
                    serialNumber = s.SerialNumber,
                    refreshSeconds = s.RefreshTimeInSeconds,
                    retrySeconds = s.RetryTimeInSeconds,
                    expireSeconds = s.ExpireTimeInSeconds,
                    minimumTtlSeconds = s.MinimumTtlInSeconds,
                };
            }
        }
        catch { }

        return info.Count == 0 ? null : info;
    }

    static async Task TryAddAsync(Dictionary<string, object> dest, string key, Func<Task<List<object>>> producer)
    {
        try
        {
            var list = await producer();
            if (list.Count > 0) dest[key] = list;
        }
        catch
        {
            // Sub-resource collection may be empty or restricted.
        }
    }

    static async Task<List<object>> CollectAsync<T>(IAsyncEnumerable<T> source, Func<T, object> select)
    {
        var list = new List<object>();
        await foreach (var item in source) list.Add(select(item));
        return list;
    }
}
