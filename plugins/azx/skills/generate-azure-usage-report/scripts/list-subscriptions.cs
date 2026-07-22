#:property PublishAot=false
#:package Azure.Identity@1.13.1
#:package Azure.ResourceManager@1.13.1
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.ResourceManager;

var JsonOpts = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};

var stageDirOption = new Option<DirectoryInfo?>("--stage-dir")
{
    Description = "Staging directory. When set, writes subscriptions.json into it.",
    HelpName = "PATH",
};

var filterOption = new Option<string?>("--filter")
{
    Description = "Optional regex applied to the subscription display name.",
    HelpName = "REGEX",
};

var outputOption = new Option<string?>("--output")
{
    Description = "Override output file path. Use '-' to write to stdout.",
    HelpName = "PATH",
};

var forceOption = new Option<bool>("--force")
{
    Description = "Overwrite an existing subscriptions.json.",
};

var rootCommand = new RootCommand("Stage 1 — list Azure subscriptions reachable by the current credential.")
{
    stageDirOption,
    filterOption,
    outputOption,
    forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption);
    var filter = parseResult.GetValue(filterOption);
    var outputOverride = parseResult.GetValue(outputOption);
    var force = parseResult.GetValue(forceOption);

    string? targetPath = outputOverride;
    if (targetPath is null && stageDir is not null)
    {
        stageDir.Create();
        targetPath = Path.Combine(stageDir.FullName, "subscriptions.json");
    }

    if (targetPath is not null && targetPath != "-" && File.Exists(targetPath) && !force)
    {
        await Console.Error.WriteLineAsync($"[list-subscriptions] {targetPath} exists. Use --force to overwrite.");
        return 1;
    }

    Regex? regex = filter is null ? null : new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    ArmClient arm;
    try
    {
        arm = new ArmClient(new DefaultAzureCredential());
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[list-subscriptions] Azure credential setup failed: {ex.Message}");
        await Console.Error.WriteLineAsync("[list-subscriptions] Run `az login` and retry.");
        return 3;
    }

    var subs = new List<Subscription>();
    try
    {
        await foreach (var s in arm.GetSubscriptions().GetAllAsync(cancellationToken: ct))
        {
            var name = s.Data.DisplayName;
            if (regex is not null && !regex.IsMatch(name))
                continue;
            subs.Add(new Subscription(s.Data.SubscriptionId, name));
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[list-subscriptions] Failed to enumerate subscriptions: {ex.Message}");
        return 1;
    }

    var payload = new
    {
        generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        subscriptions = subs.OrderBy(x => x.Name).Select(s => new { id = s.Id, name = s.Name }).ToArray(),
    };

    var json = JsonSerializer.Serialize(payload, JsonOpts);
    if (targetPath is null || targetPath == "-")
    {
        Console.Out.Write(json);
        Console.Out.WriteLine();
    }
    else
    {
        await WriteAtomic(targetPath, json, ct);
        await Console.Error.WriteLineAsync($"[list-subscriptions] wrote {targetPath} ({subs.Count} subscriptions)");
    }
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task WriteAtomic(string path, string content, CancellationToken ct)
{
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, content, ct);
    File.Move(tmp, path, overwrite: true);
}

internal record Subscription(string Id, string Name);
