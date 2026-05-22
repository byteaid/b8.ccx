---
name: dotnet-file-based-apps
description: Reference for file-based apps in .NET 10+ and C# 14+ — single `.cs` files runnable without a `.csproj`. Covers the `#:` directive set (`#:package`, `#:project`, `#:property`, `#:sdk`) and the Unix `#!` shebang; the full SDK lifecycle for a `.cs` file (`dotnet run file.cs`, `dotnet build`, `dotnet restore`, `dotnet clean`, `dotnet publish`, `dotnet pack`, `dotnet project convert`); file-based defaults (Native AOT on, `PackAsTool` on, `Microsoft.NET.Sdk`); user secrets keyed off the file path; launch profiles via `<App>.run.json`; the implicit-build-file ladder (`Directory.Build.props/targets`, `Directory.Packages.props`, `nuget.config`, `global.json`); the build cache and its quirks; folder-layout recommendations; multi-file composition via `#:project`; and the .NET 10 noun-first CLI aliases. Load this skill whenever a `.cs` file is intended to run without an explicit `.csproj`.
when_to_use: |
  - Trigger keywords: file-based app, single-file C# script, `dotnet run file.cs`, `dotnet run --file`, `dotnet file.cs`, `dotnet project convert`, `#:package`, `#:project`, `#:property`, `#:sdk`, shebang `#!/usr/bin/env dotnet`, `<App>.run.json`, file-based-apps cache, PublishAot default, PackAsTool default.
  - Task shapes: scaffold a single-file C# script, decide between file-based vs project-based, promote a `.cs` script to a project, debug `Directory.Build.props` interfering with a script, manage launch profiles for `.cs` files, configure user secrets for a script, set up a global tool from a single `.cs`.
allowed-tools: Bash, Glob, Grep, Monitor, PowerShell, Read
user-invocable: false
paths: ["**/*.cs", "**/Directory.Build.props", "**/Directory.Packages.props", "**/global.json", "**/*.run.json"]
---

# .NET File-Based Apps — Authoring Reference

L1 dispatcher. Concrete content lives in L2 sub-files.

## Mental model

A *file-based app* is a runnable C# program contained in a single `*.cs` file with **no `.csproj`**. The .NET SDK reads `#:` directives from the source and synthesizes a virtual project at build/run time. Introduced in **.NET 10 / C# 14**.

| Aspect | File-based | Project-based |
|---|---|---|
| Manifest | `#:` directives at top of `.cs` | `.csproj` |
| Source | Single `.cs` file | One or more files under the `.csproj` cone |
| Entry point | Top-level statements OR explicit `Main` | Top-level statements OR explicit `Main` |
| Default `PublishAot` | `true` | `false` |
| Default `PackAsTool` | `true` | `false` |
| Default SDK | `Microsoft.NET.Sdk` | Per `<Project Sdk=...>` |
| Conversion path | `dotnet project convert app.cs` → project | n/a |

Use for: scripts, CLI utilities, prototypes, glue code, shell-scriptable C#. **Avoid** for anything that spans multiple loose `.cs` files (no such mode exists — use `#:project` to a class library or convert).

## Non-negotiable rules (must survive compaction)

1. **One `.cs` file per app.** There is no "multiple loose `.cs` files in one file-based app" mode. To grow, move shared code into a class library `.csproj` and reference it via `#:project`, or run `dotnet project convert`.
2. **`#:package Id` (no version) requires `Directory.Packages.props`** with Central Package Management. Otherwise restore fails — pin a version or use `@*`.
3. **`dotnet run file.cs` from a directory containing a `.csproj` runs the project**, passing `file.cs` as an argument. To force file-based execution, use `dotnet run --file file.cs` (or, unambiguously, `dotnet file.cs` in a directory with no `.csproj`).
4. **Native AOT is on by default.** Packages that rely on `Assembly.LoadFile`, `System.Reflection.Emit`, or built-in COM will fail to publish until you set `#:property PublishAot=false`.
5. **`PackAsTool=true` is the file-based default.** `dotnet pack file.cs` produces a global tool out of the box. Disable with `#:property PackAsTool=false`.
6. **Don't nest a `.cs` script inside a `.csproj` cone.** The project's implicit files override file-based defaults. Keep scripts in a peer directory.
7. **Watch ambient `Directory.Build.props`.** A parent-dir props file silently affects every `.cs` underneath. Drop a local `Directory.Build.props` in the scripts dir to isolate when the ambient one is hostile.
8. **Shebangs are LF, no BOM, `chmod +x`.** Anything else and the OS won't dispatch.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `#:` directive set (`#:package`, `#:project`, `#:property`, `#:sdk`) and the `#!` shebang | [directives.md](directives.md) | Authoring or auditing the directive block of a `.cs` file; making a script Unix-executable. |
| SDK lifecycle: `dotnet run`, `build`, `restore`, `clean`, `publish`, `pack`, `project convert` | [cli-lifecycle.md](cli-lifecycle.md) | Picking the right verb; understanding default output paths; converting a script to a project. |
| Implicit build files, build cache, folder layout, multi-file composition | [layout-and-build.md](layout-and-build.md) | Diagnosing `Directory.Build.props` bleed-through; cache invalidation surprises; structuring a scripts folder. |
| Native AOT defaults, opt-out, implications, supported targets | [native-aot.md](native-aot.md) | A package fails AOT publish; deciding whether to opt out; understanding deployment size and startup. |
| User secrets, launch profiles (`<App>.run.json` vs `Properties/launchSettings.json`), runtime path access, .NET 10 CLI aliases | [configuration.md](configuration.md) | Setting up secrets for a script; choosing/launching a profile; reading the script's own path at runtime. |
| Gotchas checklist | [troubleshooting.md](troubleshooting.md) | Anything misbehaving — start here. |

## Quick decision matrix

| Need | Pick |
|---|---|
| One-off C# script, single file | File-based, default everything. |
| Small CLI tool with verbs/options | File-based + `#:package System.CommandLine@*` (load `dotnet-scripting`). |
| Web sample (`MapGet`, `MapPost`) | File-based + `#:sdk Microsoft.NET.Sdk.Web`. |
| Multi-file, evolving codebase | Convert to `.csproj` with `dotnet project convert`. |
| Shared library used by several scripts | A class library `.csproj`, referenced from each `.cs` via `#:project`. |
| Aspire AppHost | Either shape works; a real `.csproj` AppHost has better testability today. |

## Status

- File-based apps require **.NET 10 SDK** and **C# 14**.
- Earlier SDKs do not parse `#:` directives — they treat them as ordinary comments and the build fails because the package/property/SDK metadata never reaches MSBuild.
- The C# compiler ignores `#:` and `#!`; only the SDK build system parses them. Using `#:` in a project-based compilation **emits warnings**.

## Cross-references

- Live: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
- Live: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk
- Live: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-project-convert
- Live (Native AOT): https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- Live (preprocessor directives — note `#:` is SDK, not compiler): https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/preprocessor-directives
- Related skill: `dotnet-system-commandline` — designing the CLI surface of a script.
- Related skill: `dotnet-scripting` — combined recipe (file-based + `System.CommandLine`).
