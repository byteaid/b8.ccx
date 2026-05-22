# Types & Construction — Symbol Graph, Actions, Parse → Invoke

## Symbol hierarchy

`System.CommandLine` namespace exposes:

| Type | Role |
|---|---|
| `Symbol` (abstract) | Base of `Command`, `Option`, `Argument`. Has `Name`, `Description`, `Hidden`, `Aliases`. |
| `Command` | A specific action; holds `Subcommands`, `Options`, `Arguments`, `Validators`. |
| `RootCommand` | App entry point. Inherits from `Command`. Auto-adds `HelpOption`, `VersionOption`, `[suggest]` directive; exposes mutable `Directives` collection. |
| `Option`, `Option<T>` | Named parameter (e.g. `--delay 5`). |
| `Argument`, `Argument<T>` | Positional value passed to a command or option. |
| `Directive` | Cross-cutting `[bracket]` token before the command. Built-ins: `DiagramDirective`, `EnvironmentVariablesDirective`, plus the auto-included `[suggest]`. |
| `VersionOption` | Auto-added on `RootCommand`; responds to `--version`. |
| `ArgumentArity` (struct) | Canonical arities: `Zero`, `ZeroOrOne`, `ExactlyOne`, `ZeroOrMore`, `OneOrMore`. |
| `ParseResult` | Output of `Command.Parse(...)`. Source of values, errors, configuration, and `Action`. |
| `ParserConfiguration` | Parse-time config (POSIX bundling, response files). |
| `InvocationConfiguration` | Invoke-time config (`Output`, `Error`, exception handler, termination timeout). **Not sealed** — derive to add custom properties. |
| `CommandLineAction` (abstract) → `SynchronousCommandLineAction`, `AsynchronousCommandLineAction` | Action base classes. You normally use `SetAction` instead of subclassing; subclass only for advanced cases (e.g. wrapping `HelpAction`). |
| `CommandLineParser` (static) | Underlying parser — `Parse(IReadOnlyList<string>)`, `Parse(string)`, `SplitCommandLine(string)`. Renamed from `Parser` in beta5. |

There is **no longer** a `CommandLineConfiguration` type — it was split into `ParserConfiguration` + `InvocationConfiguration` in 2.0.0-beta7.

## Construction patterns

### Mandatory positional name

Symbol constructors require a `name`. For `Option`, the constructor is `new(string name, params string[] aliases)`. The pre-beta5 `(name, description)` overload is gone — pre-existing code that passed a description as the second arg now silently treats it as an alias.

```csharp
Option<int> delayOption = new("--delay", "-d")
{
    Description = "An option whose argument is parsed as an int.",
    DefaultValueFactory = parseResult => 42,
};
```

### Object-initializer for metadata

Set everything else through property assignment:

| Property | Notes |
|---|---|
| `Description` | Free-form help text. |
| `DefaultValueFactory` | `Func<ArgumentResult, T>`. Replaces the removed `SetDefaultValue(object)`. |
| `Required` | Renamed from `IsRequired`. |
| `HelpName` | Display name for the option's argument (metavar). Renamed from `ArgumentHelpName`. |
| `Hidden` | Hide from help / completion (still callable). Renamed from `IsHidden`. |
| `Recursive` | Make the option available to all descendant subcommands. Replaces the older "global option". |
| `AllowMultipleArgumentsPerToken` | Permit `--items one two three` without repeating the flag. |
| `CustomParser` | `Func<ArgumentResult, T>` overriding the built-in parser. |
| `Action` | Both `Command.Action` and `Option.Action` exist (so options like help carry their own action). |
| `Aliases`, `Validators`, `CompletionSources` | Mutable collections — `Add`, `Remove`, `Contains`. |

### Adding to the graph

```csharp
RootCommand root = new("CLI description.");
root.Subcommands.Add(syncOrders);
root.Options.Add(verbosity);
root.Arguments.Add(targetArg);
syncOrders.Validators.Add(parseResult =>
{
    if (...) parseResult.AddError("Invalid combo");
});
```

Or via collection-initializer:

```csharp
RootCommand root = new("desc") { fileOption, delayOption, importCommand };
```

The pre-beta5 `command.AddOption(...)` / `AddArgument(...)` / `AddCommand(...)` / `AddValidator(...)` / `AddAlias(...)` / `AddCompletions(...)` are all gone. Use `command.Options.Add(...)` etc.

## SetAction — sync vs async

```csharp
// Sync: Action<ParseResult> or Func<ParseResult, int>
syncOrders.SetAction(parseResult =>
{
    var since = parseResult.GetValue(sinceOption);
    DoWork(since);
    return 0;
});

// Async: Func<ParseResult, CancellationToken, Task> or Task<int>
syncOrders.SetAction(async (parseResult, ct) =>
{
    var since = parseResult.GetValue(sinceOption);
    await DoWorkAsync(since, ct);
    return 0;
});
```

Renames vs pre-beta5:

| Old | New |
|---|---|
| `SetHandler` | `SetAction` |
| `Command.Handler` | `Command.Action` |
| `ICommandHandler` | replaced by `CommandLineAction` hierarchy |
| `InvocationContext` (carrier) | removed — action receives `ParseResult` directly |

**Sync and async actions must not be mixed within the same app.** If any action is async, the whole app is async, and you must invoke via `await parseResult.InvokeAsync(...)`. The async signature's `CancellationToken` is mandatory in the type so that compilers warn (CA2016) if you fail to forward it.

## Parse → Invoke split

```csharp
ParseResult result = rootCommand.Parse(args);                   // Always returns a ParseResult
return await result.InvokeAsync();                              // or result.Invoke() for sync
```

After parsing you have two valid paths:

1. **Invoke the action.** `parseResult.Invoke(InvocationConfiguration?)` / `await parseResult.InvokeAsync(InvocationConfiguration?, CancellationToken)`. This runs the matched command's registered action and returns the exit code.
2. **Use `ParseResult` directly.** Read `parseResult.Errors`, `parseResult.GetValue(option)`, `parseResult.GetResult(symbol)`, `parseResult.UnmatchedTokens` without invoking. Useful in tests and for non-action workflows.

`Command.Parse(string[] args, ParserConfiguration? config = null)` is the entry point. The pre-beta5 `CommandExtensions.Parse / Invoke / InvokeAsync` extension methods were removed.

## Reading parsed values

| API | Use |
|---|---|
| `parseResult.GetValue(option)` | Strongly-typed read — preferred. |
| `parseResult.GetValue(argument)` | Same, for arguments. |
| `parseResult.GetValue<T>(string symbolName)` | By-name read. The string is the symbol's primary **name** (not an alias), scoped to the parsed command. |
| `parseResult.GetResult(symbol)` | Returns the `SymbolResult` for a symbol. Renamed from pre-beta5 `FindResultFor`. Useful inside validators. |
| `parseResult.Errors` | `List<ParseError>` — iterate when not invoking. |
| `parseResult.UnmatchedTokens` | Tokens not bound to any symbol. Set `Command.TreatUnmatchedTokensAsErrors = false` for `sudo`-style wrappers. |
| `parseResult.Configuration` | The `ParserConfiguration` that produced this result. |
| `parseResult.InvocationConfiguration` | The `InvocationConfiguration` (if `Invoke` ran). |
| `parseResult.Action` | The `CommandLineAction` selected (`HelpAction`, `ParseErrorAction`, etc.); cast to inspect or override behaviour. |

## Idiomatic async skeleton

```csharp
using System.CommandLine;

var urlOption = new Option<Uri>("--url", "-u")
{
    Description = "Endpoint to probe.",
    Required = true,
};

var rootCommand = new RootCommand("Probe a URL.") { urlOption };

rootCommand.SetAction((parseResult, ct) =>
    DoWorkAsync(parseResult.GetValue(urlOption)!, ct));

return await rootCommand.Parse(args).InvokeAsync();

static async Task<int> DoWorkAsync(Uri url, CancellationToken ct)
{
    using var http = new HttpClient();
    using var resp = await http.GetAsync(url, ct);
    Console.WriteLine((int)resp.StatusCode);
    return resp.IsSuccessStatusCode ? 0 : 1;
}
```

## Cross-references

- [parsing.md](parsing.md) — defaults, validators, custom parsers, arity, tokens.
- [configuration.md](configuration.md) — `InvocationConfiguration` properties, testing pattern.
- [help-and-completion.md](help-and-completion.md) — `HelpOption`, `HelpAction`, directives.
- [migration.md](migration.md) — full beta4 → beta5+ rename table.
- API: https://learn.microsoft.com/en-us/dotnet/api/system.commandline
- Parsing & invocation: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-parse-and-invoke
