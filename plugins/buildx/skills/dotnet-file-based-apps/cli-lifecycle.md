# CLI Lifecycle — Run, Build, Restore, Clean, Publish, Pack, Convert

All seven SDK verbs accept a `.cs` file in place of a `.csproj`. The synthesized project is built into a temp cache; output paths can be overridden per-file or per-invocation.

## `dotnet run` — execute

```bash
dotnet run --file file.cs        # explicit; preferred
dotnet run file.cs               # positional (see backwards-compat note)
dotnet file.cs                   # shorthand
```

**Backwards-compat trap.** When a `.csproj` exists in cwd, `dotnet run file.cs` (no `--file`) runs the **project** and passes `file.cs` as an argument. Use `--file` to disambiguate, or `cd` out of the project cone.

Pass arguments to the program via `--`:

```bash
dotnet run file.cs -- arg1 arg2
```

Pipe code via stdin (`-` argument). Skips launch-profile lookup; cwd remains the working dir:

```bash
echo 'Console.WriteLine("hi");' | dotnet run -        # bash
'Console.WriteLine("hi");' | dotnet run -             # PowerShell
```

## `dotnet build` — compile only

```bash
dotnet build file.cs
```

Default output: `<temp>/dotnet/runfile/<appname>-<appfilesha>/bin/<configuration>/`. Override with `--output` or `#:property OutputPath=./output`.

`--no-restore` is honored on both `build` and `run`.

## `dotnet restore` — fetch dependencies

```bash
dotnet restore file.cs
```

Implicit on `build` / `run` unless `--no-restore` is passed.

## `dotnet clean` — clear cache

```bash
dotnet clean file.cs                   # one app
dotnet clean file-based-apps           # whole cache
dotnet clean file-based-apps --days 30 # only artifacts unused N days (default 30)
```

The `file-based-apps` literal is the cache target — type it verbatim.

## `dotnet publish` — Native AOT by default

```bash
dotnet publish file.cs
```

Default output: `artifacts/` next to the `.cs` file, in a subdir named for the app. Override with `--output`. Native AOT is on by default for file-based apps — disable per file with `#:property PublishAot=false`.

For full implications and supported targets, see [native-aot.md](native-aot.md).

## `dotnet pack` — global tool by default

```bash
dotnet pack file.cs
```

`PackAsTool=true` is the file-based default, so `dotnet pack` produces a `.nupkg` ready to install via `dotnet tool install -g`. Disable via `#:property PackAsTool=false`.

## `dotnet project convert` — promote to a real `.csproj`

```bash
dotnet project convert <FILE> [--dry-run] [--force] [--interactive] [-o|--output <DIR>]
```

### Behavior

1. Creates a directory named after the file (no extension), or `--output <DIR>`.
2. Scaffolds a `.csproj` with SDK + properties.
3. Moves the source into a `.cs` of the same name.
4. Strips `#:` directives.
5. Translates: first `#:sdk` → `<Project Sdk="..."/>`; additional `#:sdk` → `<Sdk Name="..." Version="..."/>` elements; `#:package` → `<PackageReference>`; `#:property` → MSBuild properties.
6. Sets typical defaults: `OutputType=Exe`, `TargetFramework=net10.0`, `ImplicitUsings=enable`, `Nullable=enable`, `PublishAot=true`, `PackAsTool=true`.

The original `.cs` is **left untouched**; the converted copy lives in the new directory.

### Options

| Flag | Effect |
|---|---|
| `--dry-run` | Preview only — no files written. |
| `--force` | Convert even on malformed directives (default fails). |
| `--interactive` | Allow auth/UI prompts during restore. |
| `-o`, `--output <DIR>` | Override the generated directory name. |

### Worked example

Before (`api.cs`):

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.AspNetCore.OpenApi@10.*-*

var builder = WebApplication.CreateBuilder();
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapGet("/", () => "Hello, world!");
app.Run();
```

After (`api/api.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
    <PackAsTool>true</PackAsTool>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.*-*" />
  </ItemGroup>
</Project>
```

## .NET 10 noun-first CLI aliases

The classic verb-first commands still work; the noun-first form is preferred on .NET 10:

| Noun-first (.NET 10) | Verb-first alias |
|---|---|
| `dotnet package add` | `dotnet add package` |
| `dotnet package list` | `dotnet list package` |
| `dotnet package remove` | `dotnet remove package` |
| `dotnet reference add` | `dotnet add reference` |
| `dotnet reference list` | `dotnet list reference` |
| `dotnet reference remove` | `dotnet remove reference` |

## One-shot tool execution

`dotnet tool exec` and the `dnx` script run a tool one-shot without install. Useful for invoking a script-as-tool published from a `.cs` file:

```bash
dnx my-cli-tool -- --arg=value
dotnet tool exec my-cli-tool -- --arg=value
```

## Container images

Console apps can build container images via `dotnet publish /t:PublishContainer` without setting `EnableSdkContainerSupport`. Works for file-based apps too.

## Shell / IDE tooling

- `dotnet completions script [bash|fish|nushell|powershell|zsh]` (.NET 10) emits native tab-completion for the `dotnet` CLI itself.
- `--cli-schema` is available on every `dotnet` command for machine-readable command trees.
- `--interactive` is on by default in interactive terminals (.NET 10); pass `--interactive false` for CI.
- IDE behaviour, `dotnet watch`, and debugger support for file-based apps are not separately documented in the official reference at the time of this write-up — treat as best-effort and verify per-IDE.

## Cross-references

- [directives.md](directives.md) — directives that drive what the verbs do.
- [layout-and-build.md](layout-and-build.md) — output paths, build cache, concurrency.
- [native-aot.md](native-aot.md) — `dotnet publish` default and how to opt out.
- [troubleshooting.md](troubleshooting.md) — common verb-related gotchas.
- Live: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-project-convert
