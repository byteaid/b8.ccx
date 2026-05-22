# Migration — Pre-beta5 → 2.0.0-beta5+

The 2.0 surface intentionally collapsed the older builder-based API. Use this table to port code mechanically; the remaining files in this skill describe the target shape.

## Surface counts

| Metric | Old (~beta4) | New (2.0) |
|---|---|---|
| Public interfaces | 11 | 0 |
| Public classes / structs | 56 | 38 |
| Public methods | 378 | 235 |
| Public properties | 118 | 99 |
| Referenced assemblies | 11 | 6 |

## Renames & replacements

| Old (beta4) | New (beta5+) |
|---|---|
| `Parser` (instance class with builder) | `CommandLineParser` (static class) |
| `OptionResult.IsImplicit` | `Implicit` |
| `Option.IsRequired` | `Required` |
| `Symbol.IsHidden` | `Hidden` |
| `Option.ArgumentHelpName` | `HelpName` |
| `OptionResult.Token` | `IdentifierToken` |
| `ParseResult.FindResultFor` | `GetResult` |
| `SymbolResult.ErrorMessage` (property) | `AddError(string)` (method, multi-error) |
| `Command.AddOption` / `AddArgument` / `AddCommand` / `AddValidator` / `AddAlias` / `AddCompletions` etc. | `Command.Options.Add` / `Arguments.Add` / `Subcommands.Add` / `Validators.Add` / `Aliases.Add` / `CompletionSources.Add` |
| `RemoveAlias` / `HasAlias` | `Aliases.Remove` / `Aliases.Contains` |
| `IdentifierSymbol` (base of Command/Option) | removed |
| `Option(name, description)` ctor | only `Option(name, params aliases)` — silent semantic change: second arg now treated as alias |
| `SetDefaultValue(object)` | `DefaultValueFactory` (typed delegate) |
| `parse` delegate + `isDefault` bool ctor | `CustomParser` typed delegate |
| `CommandExtensions.Parse / Invoke / InvokeAsync` | `Command.Parse(...)` returns `ParseResult`; `ParseResult.Invoke` / `InvokeAsync` |
| `CommandLineConfiguration` (immutable) + `CommandLineBuilder` | `ParserConfiguration` + `InvocationConfiguration` (mutable, split in 2.0.0-beta7) |
| Builder methods `EnableDirectives`, `UseEnvironmentVariableDirective`, `UseParseDirective`, `UseSuggestDirective` | `RootCommand.Directives` collection of `Directive` instances; `[suggest]` auto-included |
| `EnableLegacyDoubleDashBehavior` | removed; uniform `ParseResult.UnmatchedTokens` |
| `EnablePosixBundling` (builder) | `ParserConfiguration.EnablePosixBundling` |
| `RegisterWithDotnetSuggest` | removed; user runs `dotnet-suggest register` |
| `UseExceptionHandler` | `InvocationConfiguration.EnableDefaultExceptionHandler` |
| `UseHelp` / `UseVersion` / `UseHelpBuilder` | `HelpOption` / `VersionOption` types auto-added to `RootCommand`; help customized by wrapping `HelpAction` |
| `AddMiddleware` | removed |
| `UseParseErrorReporting` / `UseTypoCorrections` | always-on; configure via `ParseErrorAction` cast on `ParseResult.Action` |
| `UseLocalizationResources` / `LocalizationResources` | removed; translations folded into the library |
| `UseTokenReplacer` | `ParserConfiguration.ResponseFileTokenReplacer` |
| `IConsole` / `IStandardOut` / `IStandardError` / `IStandardIn` | removed; use `InvocationConfiguration.Output` / `.Error` (`TextWriter`) |
| `ICommandHandler`, `Command.SetHandler`, `Command.Handler`, `InvocationContext` | replaced by `CommandLineAction` / `SynchronousCommandLineAction` / `AsynchronousCommandLineAction`; `Command.SetAction`; `Command.Action`; `Option.Action`; `ParseResult` passed directly |
| `CancelOnProcessTermination` | `InvocationConfiguration.ProcessTerminationTimeout` |

## Companion packages

| Package | Status | Replacement |
|---|---|---|
| `System.CommandLine.Hosting` | Deprecated | Manual `Microsoft.Extensions.Hosting` glue — see [hosting.md](hosting.md). |
| `System.CommandLine.NamingConventionBinder` | Deprecated | `Option<T>` + `parseResult.GetValue(...)`. |
| `System.CommandLine.DragonFruit` | Deprecated | Idiomatic `RootCommand` + option graph; convention-based `Main(...)` binding is gone. |
| `System.CommandLine.Rendering` | Deprecated | Direct `TextWriter` writes or third-party rendering (e.g. Spectre.Console). |

## Subtle pitfalls during migration

- **The `Option(name, description)` ctor silently became `Option(name, alias)`.** Code that compiled against beta4 still compiles against beta5+ but means something else. Audit every `new Option<T>("--foo", "some text")` — that "some text" is now an alias. Move human-readable text into `Description = "..."` via the object initializer.
- **`Aliases` no longer contains the primary name.** Code that iterated `option.Aliases` expecting to find `--foo` will silently miss it. Use `option.Name` for the primary name.
- **Sync/async cannot be mixed.** If you migrate one action to async, every action must be async; switch the entry point to `InvokeAsync`.
- **`IConsole` is gone.** Tests that injected an `IConsole` need to switch to `InvocationConfiguration.Output` / `.Error`.
- **`UseExceptionHandler` is gone.** If a beta4 app depended on `try { app.Invoke() } catch { ... }` working because exceptions bubbled, set `EnableDefaultExceptionHandler = false` to restore that behaviour.
- **`CancellationToken` is mandatory in async signatures.** Compilers warn (CA2016) when you don't forward it. Treat the warning as an error during migration.
- **`RegisterWithDotnetSuggest` is gone.** Apps that called this on startup just stop registering with completion. Document the one-time `dotnet-suggest register --command-path <executable>` step in your README / install script.
- **`AddMiddleware` is gone with no replacement.** Middleware-style cross-cutting work (logging, metrics, auth) moves into a wrapper around `parseResult.InvokeAsync(...)` or a derived `InvocationConfiguration` carrying the necessary services.

## Mechanical port checklist

1. Replace every `new Option<T>("--name", "description")` with `new Option<T>("--name") { Description = "description" }`.
2. Replace every `command.AddOption / AddArgument / AddCommand / AddValidator / AddAlias / AddCompletions(...)` with `command.<Plural>.Add(...)`.
3. Replace `SetHandler` → `SetAction`; `Command.Handler` → `Command.Action`.
4. Drop `InvocationContext` from action signatures; read from `parseResult` directly.
5. Replace `IsRequired` / `IsHidden` / `ArgumentHelpName` / `FindResultFor` with `Required` / `Hidden` / `HelpName` / `GetResult`.
6. Replace `SetDefaultValue(x)` with `DefaultValueFactory = _ => x`.
7. Replace `parse` delegate ctors with `CustomParser`.
8. Drop `CommandLineBuilder` / `CommandLineConfiguration`; pick `ParserConfiguration` / `InvocationConfiguration` per phase.
9. Replace `IConsole` consumers with `InvocationConfiguration.Output` / `.Error`.
10. Replace `CancelOnProcessTermination()` with `InvocationConfiguration.ProcessTerminationTimeout`.
11. Replace `UseExceptionHandler` with `InvocationConfiguration.EnableDefaultExceptionHandler`.
12. Replace `UseParseErrorReporting` / `UseTypoCorrections` with the `ParseErrorAction` cast.
13. Replace `Builder.UseHelp(...)` customization with the `CustomHelpAction` wrapping pattern (see [help-and-completion.md](help-and-completion.md)).
14. Drop deprecated companion packages; replace per the table above.
15. Add `dotnet-suggest register` to your install / README documentation.

## Cross-references

- [types-and-construction.md](types-and-construction.md) — target shape for ctors, `SetAction`, graph mutation.
- [configuration.md](configuration.md) — replacements for builder-time switches.
- [hosting.md](hosting.md) — replacement for `System.CommandLine.Hosting`.
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5
