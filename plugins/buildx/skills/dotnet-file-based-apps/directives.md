# `#:` Directives & `#!` Shebang

All directives go at the top of the file, before any C# code. The C# compiler ignores `#:` and `#!`; only the SDK build system parses them. Using `#:` in a project-based compilation **emits warnings**.

## `#:package` — NuGet reference

Syntax: `#:package <Id>[@<Version>]`

```csharp
#:package Newtonsoft.Json
#:package Serilog@3.1.1
#:package Spectre.Console@*
#:package Microsoft.AspNetCore.OpenApi@10.*-*
```

| Form | Meaning |
|---|---|
| `Id` | Version omitted. **Only valid with `Directory.Packages.props`** (Central Package Management). Otherwise restore fails. |
| `Id@1.2.3` | Pin to exact version. |
| `Id@*` | Use latest stable. |
| `Id@10.*-*` | NuGet floating range, including prerelease — works as in any project. |

## `#:project` — Project / project-folder reference

```csharp
#:project ../SharedLibrary/SharedLibrary.csproj
#:project ../ClassLib                            // directory containing a single .csproj
```

Path may target a `.csproj` directly or a directory containing one. This is how you split a file-based "main" across additional code: keep shared code as a class library `.csproj` and reference it from one or more `.cs` scripts.

## `#:property` — MSBuild property

```csharp
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property OutputPath=./output
#:property PackAsTool=false
```

MSBuild expressions and property functions work — read environment variables with defaults, set conditional values:

```csharp
#:property LogLevel=$([MSBuild]::ValueOrDefault('$(LOG_LEVEL)', 'Information'))
#:property EnableLogging=$([System.Convert]::ToBoolean($([MSBuild]::ValueOrDefault('$(ENABLE_LOGGING)', 'true'))))
```

`$(VAR)` references env vars directly with **no fallback** — use `[MSBuild]::ValueOrDefault` for defaults.

### Common knobs

| Property | File-based default | When to override |
|---|---|---|
| `TargetFramework` | `net10.0` | Targeting a different TFM. |
| `PublishAot` | `true` | Packages incompatible with AOT (reflection, Emit, COM). |
| `PackAsTool` | `true` | Output is not a global tool. |
| `OutputPath` | temp cache (see [layout-and-build.md](layout-and-build.md)) | Predictable output for CI. |
| `OutputType` | `Exe` (set automatically on convert) | Library output (rare for file-based). |
| `ImplicitUsings` | `enable` | Disable to be explicit. |
| `Nullable` | `enable` | Disable nullable annotations. |

## `#:sdk` — MSBuild SDK selection

Default SDK: `Microsoft.NET.Sdk`. Override or stack additional SDKs:

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:sdk Aspire.AppHost.Sdk@13.0.2
```

### Conversion semantics (`dotnet project convert`)

- The **first** `#:sdk` becomes the project's `Sdk` attribute: `<Project Sdk="Id"/>` or `<Project Sdk="Id/version"/>` if a version is pinned.
- Subsequent `#:sdk` lines become standalone `<Sdk Name="Id" Version="..."/>` elements inside the converted `.csproj`.

## `#!` — Shebang (Unix shell exec)

```csharp
#!/usr/bin/env dotnet
Console.WriteLine("Hello");
```

- Use **LF** line endings, **no BOM**.
- Set executable: `chmod +x file.cs`.
- Run: `./file.cs`.
- **Extensionless dispatch is supported** (.NET 10): copy `file.cs` to e.g. `~/utils/hello`, `chmod +x`, run `hello`.

## Default included items

- The single `.cs` file is always included.
- `Microsoft.NET.Sdk.Web` additionally includes `*.json` config files.
- Non-default SDKs additionally include `.resx` files.

There is no glob to pull in sibling `.cs` files. The unit is a single `.cs`.

## Worked example — minimal CLI script

```csharp
#!/usr/bin/env dotnet
#:package System.CommandLine@*

using System.CommandLine;

var nameOption = new Option<string>("--name", "-n")
{
    Description = "The name to greet.",
    Required = true,
};
var root = new RootCommand("Greet a person.") { nameOption };
root.SetAction(parseResult =>
{
    Console.WriteLine($"Hello, {parseResult.GetValue(nameOption)}!");
    return 0;
});
return root.Parse(args).Invoke();
```

Save as `greet.cs`, `chmod +x greet.cs`, run `./greet.cs --name Ada`.

## Worked example — minimal web app

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.AspNetCore.OpenApi@10.*-*

var builder = WebApplication.CreateBuilder();
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapGet("/", () => "Hello, world!");
app.Run();
```

`Microsoft.NET.Sdk.Web` automatically pulls in any sibling `appsettings*.json`.

## Cross-references

- [cli-lifecycle.md](cli-lifecycle.md) — `dotnet run / build / publish / pack / project convert`.
- [layout-and-build.md](layout-and-build.md) — implicit build files that interact with these defaults.
- [native-aot.md](native-aot.md) — when to set `#:property PublishAot=false`.
- Live: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
