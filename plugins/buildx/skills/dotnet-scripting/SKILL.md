---
name: dotnet-scripting
description: End-to-end recipe for building **scripts** in .NET 10 / C# 14 — small, single-purpose utilities authored as a single `.cs` file (file-based app) whose CLI surface (verbs, options, arguments, help, validation) is defined with `System.CommandLine` 2.0.0-beta5+. Covers the canonical script blueprint (directives, command graph, action shape), CLI design conventions (verbs, options, arguments, exit codes, help text), packaging as a global tool (`dotnet pack` / `dotnet tool install -g` / `dnx` / `dotnet tool exec`), and one-shot distribution. Defers all surface-level detail to its two parents: `dotnet-file-based-apps` for `#:` directives and the SDK lifecycle, `dotnet-system-commandline` for the API surface. Load this skill when the task is "write a CLI script in .NET" — when the task is purely about file-based-app mechanics or purely about the `System.CommandLine` API, load the appropriate parent skill instead.
when_to_use: |
  - Trigger keywords: dotnet script, .NET script, CLI script, single-file CLI utility, scripting in C#, `#:package System.CommandLine`, "small CLI tool", "one-off command", `dotnet tool install` from a `.cs` file, `dnx` script, packing a global tool from a single file.
  - Task shapes: scaffold a new `.cs` script with verbs/options, port a bash / pwsh script to .NET, decide whether a job belongs in a script or a project, design the command surface of a small utility, package a script as a global tool, distribute a one-shot CLI.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET Scripting — Single-File CLIs Built on `System.CommandLine`

## Mental model

A *script* in .NET is a single `.cs` file (file-based app) that owns its CLI surface through `System.CommandLine`. The unit is one file, one entry point, one well-defined command tree. Anything more elaborate belongs in a project.

This skill is a **recipe**, not a reference: the canonical blueprint, the conventions, the packaging story. The exhaustive content lives in the two parent skills:

- **`dotnet-file-based-apps`** owns: `#:package`, `#:project`, `#:property`, `#:sdk`, `#!` shebang, `dotnet run` / `build` / `publish` / `pack` / `project convert`, Native AOT defaults, build cache, layout, secrets, launch profiles.
- **`dotnet-system-commandline`** owns: `RootCommand` / `Command` / `Option<T>` / `Argument<T>`, `SetAction`, `ParseResult`, validators, custom parsers, help, completion, hosting glue, migration.

**Load both** when authoring a non-trivial script. This file orchestrates them.

## Non-negotiable rules (must survive compaction)

1. **Single `.cs` file.** No sibling `.cs` files (file-based apps don't compile them). For shared code, extract a class library `.csproj` and reference via `#:project`, or convert.
2. **`#:package System.CommandLine@*`** at the top — pin a version once the script ships (`@2.0.0-beta5` or later).
3. **Use the strongly-typed graph.** `Option<T>` / `Argument<T>` with object-initializer metadata. Read values via `parseResult.GetValue(option)`. Never the deprecated `DragonFruit` / `NamingConventionBinder` shape.
4. **Async-by-default for I/O scripts.** If anything in the script is async, every action is async — return `await rootCommand.Parse(args).InvokeAsync()`. Forward the `CancellationToken` to every cancellable downstream call (CA2016).
5. **Exit codes are part of the contract.** `0` = success; non-zero = failure; `130` (`128 + SIGTERM`) for a clean cancellation. Return them from the action delegate.
6. **Help text is part of the script.** Every command, option, and argument gets a `Description`. Options get a `HelpName` when the metavar matters (`FILEPATH`, `URL`).
7. **Native AOT default is on.** Most pure-stdlib scripts publish cleanly. If a dependency uses reflection-emit, set `#:property PublishAot=false` for that script. See `dotnet-file-based-apps` § native-aot.
8. **`PackAsTool=true` is the file-based default.** A script can be installed as a global tool with `dotnet pack file.cs && dotnet tool install -g <id> --add-source ./bin/...` straight away — no extra knobs needed.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Canonical script blueprint — file shape, directives, command graph, sync vs async examples | [scaffold.md](scaffold.md) | Starting a new script from zero, or auditing the structure of an existing one. |
| CLI design conventions — verbs vs options vs arguments, naming, validation, help text, exit codes | [cli-design.md](cli-design.md) | Designing the command surface, choosing between an option and a positional, deciding when a script needs subcommands. |
| Distribution — pack as global tool, install / update / uninstall, `dnx` and `dotnet tool exec` for one-shot, container images | [distribution.md](distribution.md) | Shipping a script to other machines or to CI. |

## Quick decision matrix

| Need | Pick |
|---|---|
| One-off command, no args | Top-level statements in a `.cs` file. No `System.CommandLine` needed. |
| One verb, a few flags | Single-file script, `RootCommand` only. **This skill.** |
| Multiple verbs (`sync`, `cleanup`, `report`) | Single-file script, `RootCommand` + `Subcommands`. **This skill.** |
| Verbs that share a lot of helper code | Class library `.csproj` + several `.cs` scripts referencing it via `#:project`. |
| DI / `IHostedService` / structured logging at scale | Real `.csproj`. Use `dotnet-system-commandline` § hosting. |
| Multi-file evolving codebase | Convert with `dotnet project convert` (see `dotnet-file-based-apps/cli-lifecycle.md`). |

## Stay-or-leave checklist

A script *should* graduate to a real project when **any** of these is true:

- More than ~300 lines of meaningful logic in the `.cs`.
- Helper types that other scripts also need (extract a library, but watch the count).
- Tests beyond a smoke run (file-based apps don't compose with `dotnet test` cleanly).
- Multiple `IHostedService` / background workers with non-trivial lifetimes.
- A growing set of conditional `#:property` lines that approximate a small `.csproj`.

If none of these is true, stay file-based.

## Cross-references

- `dotnet-file-based-apps` — the file-based-app substrate (directives, lifecycle, layout, AOT).
- `dotnet-system-commandline` — the CLI library (API, parsing, help, hosting, migration).
- Live: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/
- Live: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools
