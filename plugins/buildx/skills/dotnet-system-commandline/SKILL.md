---
name: dotnet-system-commandline
description: Complete `System.CommandLine` 2.0.0-beta5+ reference for building CLI apps in .NET 10 / C# 14. Covers `RootCommand` / `Command` / `Option<T>` / `Argument<T>`, sync vs async `SetAction`, `ParseResult.GetValue`, recursive options, aliases, defaults, custom parsers, validators (`AcceptOnlyFromAmong`, `AcceptExistingOnly`, `AcceptLegalFileNamesOnly`), `ParserConfiguration` (POSIX bundling, response files), `InvocationConfiguration` (Output/Error writers, default exception handler, process-termination timeout), tab completion via `dotnet-suggest` + `CompletionSources`, help customization (`HelpOption`, `HelpName`, `HelpAction`), `Microsoft.Extensions.Hosting` integration without the deprecated `System.CommandLine.Hosting`, and the pre-beta5 → beta5+ migration delta. Load the sub-file matching the trigger — not the full index.
when_to_use: |
  - Trigger keywords: System.CommandLine, RootCommand, Option<T>, Argument<T>, SetAction, ParseResult, GetValue, ParserConfiguration, InvocationConfiguration, dotnet-suggest, CompletionSources, AcceptOnlyFromAmong, HelpOption, HelpAction, Recursive option, ProcessTerminationTimeout, response files, POSIX bundling.
  - Task shapes: scaffold a CLI app, design a verb/option graph, wire validators, write a custom parser, customize help output, enable tab completion, port a beta4 app to beta5+, integrate with `Microsoft.Extensions.Hosting`, replace removed companion packages (`System.CommandLine.Hosting`, `NamingConventionBinder`, `DragonFruit`, `Rendering`).
allowed-tools: Glob, Grep, Read
user-invocable: false
---

# System.CommandLine 2.0 — Authoring Reference

L1 dispatcher. Substantive content lives in L2 sub-files; load the one matching the trigger.

## Mental model

`System.CommandLine` is a parser + invocation library. You build a **symbol graph** (`RootCommand` → `Command` → `Option<T>` / `Argument<T>`), call `Parse(args)` to get a `ParseResult`, then either `Invoke()` to run the registered action or read values directly. The 2.0 beta5+ surface is intentionally small (no builder, no `IConsole`, no middleware, no companion packages) and is pre-release: pin `System.CommandLine` and watch the migration guide. The library is trim/AOT-friendly — Native AOT startup is roughly 17 ms vs 76 ms JIT.

## Non-negotiable rules (must survive compaction)

1. **Construction.** Use `new("--name", "-alias")` constructor. Set metadata via object initializer (`Description`, `DefaultValueFactory`, `Required`, `HelpName`, `Recursive`, `AllowMultipleArgumentsPerToken`). The two-string `Option(name, description)` constructor is gone — passing a description as the second arg is silently treated as an alias.
2. **Graph mutation.** Add via collection: `command.Subcommands.Add(...)`, `command.Options.Add(...)`, `command.Arguments.Add(...)`, `command.Validators.Add(...)`, `option.Aliases.Add(...)`, `option.CompletionSources.Add(...)`. The pre-beta5 `AddOption` / `AddArgument` / `AddCommand` / `AddValidator` / `AddAlias` / `AddCompletions` methods were all removed.
3. **Action shape.** `SetAction(parseResult => ...)` for sync (`Action<ParseResult>` or `Func<ParseResult, int>`); `SetAction(async (parseResult, ct) => ...)` for async (`Func<ParseResult, CancellationToken, Task>` / `Task<int>`). **Never mix sync and async in the same app.** If any action is async the whole app is async — invoke via `await parseResult.InvokeAsync()`.
4. **CancellationToken.** Forward the async action's `CancellationToken` to every downstream cancellable call. Failing to forward triggers analyzer warning CA2016. The token is wired by `InvocationConfiguration.ProcessTerminationTimeout` (default 2 s) to Ctrl+C / SIGINT / SIGTERM.
5. **Reading values.** Prefer the strongly-typed `parseResult.GetValue(option)` / `GetValue(argument)` overloads over the by-name `GetValue<T>(string)` overload (the latter takes the symbol's primary **name**, not an alias).
6. **No `IConsole`.** Pipe through `InvocationConfiguration.Output` / `.Error` (`TextWriter`) — the `IConsole` / `IStandardOut` / `IStandardError` / `IStandardIn` abstractions were removed.
7. **No `CommandLineBuilder`.** `RootCommand` is the entry point. `HelpOption` and `VersionOption` are auto-added; the `[suggest]` directive is auto-included.
8. **Companion packages are deprecated.** `System.CommandLine.Hosting`, `NamingConventionBinder`, `DragonFruit`, `Rendering` are all marked deprecated on NuGet — do not introduce them. See [hosting.md](hosting.md) for the manual `Microsoft.Extensions.Hosting` glue that replaces them.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Symbol hierarchy, constructors, `SetAction` sync/async, `Parse → Invoke` split | [types-and-construction.md](types-and-construction.md) | Authoring the command graph; deciding sync vs async; understanding `ParseResult` / `CommandLineParser` / `CommandLineAction`. |
| Built-in types, aliases, recursive options, required options, default values, custom parsers, validators, arity, tokens & bundling, unmatched tokens, reading values | [parsing.md](parsing.md) | Designing options/arguments; writing validators or `CustomParser`; debugging argument arity, bundling, or response-file expansion. |
| `ParserConfiguration` and `InvocationConfiguration` (POSIX bundling, response files, `Output`/`Error` writers, default exception handler, process termination), the canonical testing pattern | [configuration.md](configuration.md) | Tuning the parser/invoker; redirecting stdout/stderr for tests; adjusting Ctrl+C / SIGTERM behaviour; disabling response files. |
| `HelpOption`, `HelpName`, `Hidden`, parse-error reporting & typo correction, `[diagram]` / `[suggest]` / `[env]` directives, tab completion via `dotnet-suggest` + `CompletionSources` | [help-and-completion.md](help-and-completion.md) | Customizing help output; wrapping `HelpAction` with prologue/epilogue; wiring `dotnet-suggest`; adding dynamic `CompletionItem` lists. |
| Manual integration with `Microsoft.Extensions.Hosting` (DI, configuration, logging) after the `System.CommandLine.Hosting` deprecation; replacements for the other deprecated companion packages | [hosting.md](hosting.md) | Building a CLI that needs DI / configuration / logging; replacing legacy `NamingConventionBinder` or `DragonFruit` code. |
| Full pre-beta5 → beta5+ rename and rewiring table | [migration.md](migration.md) | Porting code from beta4 or earlier; explaining why an old API name no longer compiles. |

## Quick decision matrix

| Need | Pick |
|---|---|
| One-shot CLI binary, no DI | Idiomatic `RootCommand` + `Option<T>` graph (this skill). |
| CLI binary with DI / config / logging | This skill + the manual hosting pattern in [hosting.md](hosting.md). |
| Single `.cs` file CLI utility | This skill + `dotnet-file-based-apps` + `dotnet-scripting`. |
| Convention-based `Main(string foo, int bar)` binding | Not supported in 2.0 — use `Option<T>` + `parseResult.GetValue(...)`. |
| Render coloured tables / progress bars in stdout | Not in `System.CommandLine` (the `Rendering` companion was deprecated) — use `Spectre.Console` or write directly to `Output`. |

## Status & versioning

- Package: [`System.CommandLine`](https://www.nuget.org/packages/System.CommandLine), **2.0.0-beta** (prerelease). Pin the dated beta version explicitly in scripts and projects to avoid silent breaks.
- API namespace reference is rendered against `view=net-10.0-pp` (preview) and carries the prerelease warning.
- The official tutorial scaffolds with `dotnet new console --framework net9.0`; on .NET 10, the noun-first verb `dotnet package add System.CommandLine` is the preferred install (alongside the legacy `dotnet add package System.CommandLine`).
- 2.0 surface counts vs older betas: public interfaces 11 → 0, classes/structs 56 → 38, methods 378 → 235, properties 118 → 99, referenced assemblies 11 → 6.

## Cross-references

- Live overview — https://learn.microsoft.com/en-us/dotnet/standard/commandline/
- API namespace reference — https://learn.microsoft.com/en-us/dotnet/api/system.commandline
- Migration guide (2.0.0-beta5+) — https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5
- Design guidance (POSIX, naming) — https://learn.microsoft.com/en-us/dotnet/standard/commandline/design-guidance
- Project repo (status, issues) — https://github.com/dotnet/command-line-api
- Related skill: `dotnet-file-based-apps` — running `.cs` files without a `.csproj`.
- Related skill: `dotnet-scripting` — the integrated recipe (file-based app + this library).
