# Help, Directives & Tab Completion

## Help system

`RootCommand` ships with `HelpOption` (under `System.CommandLine.Help`) included in its `Options` collection by default. The pre-beta5 `UseHelp` builder switch is gone — help is always wired.

`HelpOption` recognizes the multi-platform conventions: `--help`, `-h`, `-?`, `/h`, `/?`.

### What feeds help output

Help is generated from each symbol's:

- `Name` and `Aliases`
- `HelpName` — the display name for the option's argument (metavar). Renamed from pre-beta5 `ArgumentHelpName`.
- `Description`
- `DefaultValueFactory` — default value shown alongside.
- `CompletionSources` / `AcceptOnlyFromAmong(...)` — the constrained value list shows up in help.
- `Hidden = true` — hide from help and completion (still callable on the command line). Renamed from `IsHidden`.

```csharp
Option<FileInfo> input = new("--input", "-i")
{
    Description = "Path to the source file.",
    HelpName = "FILEPATH",
    Required = true,
};
```

### Custom help action — prologue / epilogue

The pre-beta5 `UseHelpBuilder` is gone. To wrap the help output, subclass `SynchronousCommandLineAction`, hold the original `HelpAction`, and replace `HelpOption.Action`:

```csharp
internal sealed class CustomHelpAction(HelpAction defaultHelp) : SynchronousCommandLineAction
{
    public override int Invoke(ParseResult parseResult)
    {
        // Prologue
        parseResult.InvocationConfiguration.Output.WriteLine("== Acme CLI ==");

        int result = defaultHelp.Invoke(parseResult);

        // Epilogue
        parseResult.InvocationConfiguration.Output.WriteLine("Docs: https://acme.example/cli");
        return result;
    }
}

// Wire it on the existing HelpOption:
var helpOption = rootCommand.Options.OfType<HelpOption>().First();
helpOption.Action = new CustomHelpAction((HelpAction)helpOption.Action!);
```

`HelpAction` itself is a `SynchronousCommandLineAction`.

### Version

`VersionOption` is auto-added to `RootCommand` and responds to `--version`. The pre-beta5 opt-in `UseVersion()` was removed.

### Parse-error reporting

Always-on. When `Invoke` is called and `ParseResult.Errors` is non-empty:

- Errors are written to the `Error` `TextWriter`.
- Help is written to `Output`.
- Exit code `1` is returned.

To configure typo correction or whether to print help on error, cast `ParseResult.Action` to `ParseErrorAction`:

```csharp
ParseResult result = rootCommand.Parse(args, parserConfig);
if (result.Action is ParseErrorAction parseError)
{
    parseError.ShowTypoCorrections = true;
    parseError.ShowHelp = false;
}
return result.Invoke();
```

The pre-beta5 `UseParseErrorReporting` and `UseTypoCorrections` builder switches are gone — the cast is the supported entry point.

## Directives

A directive is a token in `[brackets]` that appears between the app name and any subcommand/option, providing cross-cutting features. Unrecognized directives are silently ignored.

`RootCommand` exposes a mutable `Directives` collection. The pre-beta5 builder switches `EnableDirectives`, `UseEnvironmentVariableDirective`, `UseParseDirective`, `UseSuggestDirective` are all replaced by adding/removing `Directive` instances on this collection.

| Directive | Effect |
|---|---|
| `[suggest]` (auto-included on `RootCommand`) | Returns suggestion candidates for a partial input — used by `dotnet-suggest` for tab completion. |
| `[diagram]` (`DiagramDirective`) | Prints a parse-tree diagram instead of invoking the command. `!` marks parse errors; `*` marks default-supplied values. |
| `[env:KEY=VAL]` (`EnvironmentVariablesDirective`) | Set environment variables for the invocation. |

```csharp
rootCommand.Directives.Add(new DiagramDirective());
rootCommand.Directives.Add(new EnvironmentVariablesDirective());
```

## Tab completion

Tab completion is automatic for **static** value sets (enums and `AcceptOnlyFromAmong`) once the shell is wired up. It also fires for any dynamic `CompletionSources` you attach.

### Per-end-user wiring (one-time)

1. Install `dotnet-suggest`:
   ```bash
   dotnet tool install -g dotnet-suggest
   ```
2. Source the shell shim from the [`dotnet/command-line-api`](https://github.com/dotnet/command-line-api) repo:
   - `dotnet-suggest-shim.bash` (bash)
   - `dotnet-suggest-shim.zsh` (zsh)
   - `dotnet-suggest-shim.ps1` (PowerShell)
3. Register the executable per app:
   ```bash
   dotnet-suggest register --command-path /path/to/myapp
   ```

The pre-beta5 `RegisterWithDotnetSuggest` extension was removed because it ran an expensive operation at every startup. Apps now must rely on the user (or installer) running `dotnet-suggest register` once.

`cmd.exe` on Windows has no completion plug-in mechanism, so tab completion is not available there. Use PowerShell.

### Dynamic completions

Every symbol exposes a mutable `CompletionSources` collection. Each source is `Func<CompletionContext, IEnumerable<CompletionItem>>`:

```csharp
dateOption.CompletionSources.Add(ctx =>
{
    var dates = new List<CompletionItem>();
    foreach (var i in Enumerable.Range(1, 7))
    {
        dates.Add(new CompletionItem(
            label:    DateTime.Today.AddDays(i).ToShortDateString(),
            sortText: $"{i:D2}"));
    }
    return dates;
});
```

`CompletionItem` properties used:

| Property | Purpose |
|---|---|
| `Label` | The displayed value. |
| `SortText` | Used to sort. Required when `Label` does not sort lexicographically into the desired order. |
| `Documentation`, `Detail` | Exist on the type but are not yet consumed by the runtime. |

## Quick checklist for a polished CLI

1. Set `Description` on every command, option, and argument.
2. Set `HelpName` on options whose argument display name benefits from a metavar (`FILEPATH`, `URL`, `LEVEL`).
3. Use `AcceptOnlyFromAmong(...)` for closed enumerations — it auto-feeds help and completion.
4. Mark internal/diagnostic options `Hidden = true`.
5. Set `Recursive = true` on cross-cutting options (`--verbosity`, `--config`, `--output-format`).
6. Add a `CustomHelpAction` only when you genuinely need prologue/epilogue.
7. Document the one-time `dotnet-suggest register` step in your README.

## Cross-references

- [types-and-construction.md](types-and-construction.md) — `HelpOption` lives in the auto-added `Options` collection.
- [parsing.md](parsing.md) — `AcceptOnlyFromAmong` doubles as completion + help.
- [configuration.md](configuration.md) — redirecting `Output` / `Error` for tests.
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-customize-help
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-enable-tab-completion
