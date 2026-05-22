# Parsing — Types, Aliases, Validators, Arity, Tokens

## Built-in types

The default parser supports:

- `bool`
- `byte` / `sbyte`, `short` / `ushort`, `int` / `uint`, `long` / `ulong`
- `float` / `double`, `decimal`
- `DateTime` / `DateTimeOffset`, `DateOnly` / `TimeOnly`
- `Guid`
- `FileSystemInfo` / `FileInfo` / `DirectoryInfo`
- All enums
- Arrays and `List<T>` of any of the above

Anything else needs a `CustomParser` (see § Custom parsers).

## Aliases

- Aliases must be specified explicitly — there is **no** GNU-style automatic prefix-matched alias.
- `Option` constructor takes them as `params string[]`:
  ```csharp
  Option helpOption = new("--help", "-h", "/h", "-?", "/?");
  ```
- Both `Command` and `Option` expose a mutable `Aliases` collection.
- Aliases no longer include the symbol's primary name (pre-beta5 the collection redundantly contained the name).
- `Aliases.Remove(...)` and `Aliases.Contains(...)` replace the removed `RemoveAlias` / `HasAlias` methods.

## Recursive (formerly "global") options

```csharp
Option<string> verbosity = new("--verbosity", "-v") { Recursive = true };
rootCommand.Options.Add(verbosity);
```

`Recursive = true` makes the option available to all of the command's subcommands. This is the supported replacement for the older "global option" concept; useful for verbosity, output format, configuration paths, etc.

## Required

`Option.Required = true` makes the option mandatory. If a `DefaultValueFactory` is also set, the default satisfies the requirement and the user does not need to pass the option.

`Argument<T>` defined without a `DefaultValueFactory` is treated as required.

The pre-beta5 property name was `IsRequired` — renamed to `Required`.

## Default values

Both `Argument<T>` and `Option<T>` expose `DefaultValueFactory: Func<ArgumentResult, T>`, invoked when the symbol is absent from the parsed input. Replaces the removed `SetDefaultValue(object)` (which was not type-safe).

```csharp
Option<int> number = new("--number") { DefaultValueFactory = _ => 42 };

Option<DirectoryInfo> outputDir = new("--output-dir")
{
    DefaultValueFactory = _ => new DirectoryInfo(Environment.CurrentDirectory),
};
```

## Custom parsers

`Argument<T>.CustomParser` and `Option<T>.CustomParser` are `Func<ArgumentResult, T>` delegates that override the built-in parser. Combine custom parsing with validation by calling `result.AddError(...)` from inside the parser:

```csharp
Option<TimeSpan> timeoutOption = new("--timeout")
{
    CustomParser = result =>
    {
        var token = result.Tokens.Single().Value;
        if (!TimeSpan.TryParse(token, out var ts))
        {
            result.AddError($"'{token}' is not a valid TimeSpan.");
            return TimeSpan.Zero;
        }
        return ts;
    },
};
```

`ArgumentResult.OnlyTake(int)` lets you dynamically split incoming tokens between multiple arguments (dynamic arity).

## Validators

Every symbol type (`Command`, `Option`, `Argument`) has a mutable `Validators` collection. Validators run after parsing and call `SymbolResult.AddError(string)` on failure. **Multiple errors per symbol are supported** — this is why `ErrorMessage` (single-string property) became `AddError` (method) in beta5.

```csharp
fileOption.Validators.Add(result =>
{
    var file = result.GetValue(fileOption);
    if (file is not null && file.Length > 1_000_000)
        result.AddError("File too large.");
});
```

### Built-in fluent validators

| Method | Effect |
|---|---|
| `AcceptOnlyFromAmong(params string[])` | Restrict to a fixed list of values. Doubles as a tab-completion source and shows up in help. |
| `AcceptExistingOnly()` | Restrict to existing files / directories (paired with `FileInfo` / `DirectoryInfo` / `FileSystemInfo`). |
| `AcceptLegalFileNamesOnly()` | Restrict to legal filename strings (no path separators, no reserved names). |

```csharp
var format = new Option<string>("--format")
    .AcceptOnlyFromAmong("json", "yaml", "table");

var input = new Option<FileInfo>("--input")
    .AcceptExistingOnly();
```

(The fluent extensions are presumably on the `OptionValidation` / `ArgumentValidation` static classes exposed by the namespace.)

## Arity

`ArgumentArity` is a struct with named values:

| Value | Effect |
|---|---|
| `Zero` | No values. |
| `ZeroOrOne` | At most one. Default for `bool`. |
| `ExactlyOne` | Exactly one. Default for most scalar types. |
| `ZeroOrMore` | Any count. Default for arrays / lists. |
| `OneOrMore` | At least one. |

Default arity is inferred from the bound type:

- `bool` → `ZeroOrOne`
- Collection types → `ZeroOrMore`
- Everything else → `ExactlyOne`

### Option arity quirks

- When arity max is 1 but the option appears multiple times, **the last wins**: `--delay 3 --delay 2` → `2`.
- `Option.AllowMultipleArgumentsPerToken = true` lets you pass several values without repeating the option name (`--items one two three`). When arity max is 1 this property still allows repetition, but only the last value is kept.

## Tokens, delimiters, bundling

- Tokens are space-delimited; quote with `"` to embed spaces.
- Option–argument delimiter: any of space, `=`, or `:`.
- POSIX bundling of single-char options is supported by default: `-fdx` ≡ `-f -d -x`. Disable with `ParserConfiguration.EnablePosixBundling = false`.
- POSIX `--` escape: tokens after `--` are not parsed as options.
- Boolean flags: arity `ZeroOrOne` by default; bare `--flag` ⇒ `true`, absent ⇒ `false`, explicit `--flag false` parses as `false`.

## Unmatched tokens / wrapper commands

`ParseResult.UnmatchedTokens` exposes tokens that didn't match any configured symbol. Set `Command.TreatUnmatchedTokensAsErrors = false` to make this non-fatal, which is the right shape for `sudo`-style wrappers that pass everything after the wrapper's options to a child process.

The pre-beta5 `EnableLegacyDoubleDashBehavior` switch was removed — unmatched tokens are now uniformly accessible via this property.

## Response files

- Token-replace mechanism: `@<filename>` anywhere on the command line expands to the contents of that file.
- The `.rsp` extension is conventional but not required.
- Syntax: tokens space-delimited; multi-token values must be quoted; `#` to end-of-line is a comment; lines are concatenated; nested `@otherfile` references are resolved.
- Enabled by default; disable via `ParserConfiguration.ResponseFileTokenReplacer = null`. Supply a custom replacer to change how `@file` tokens expand.
- **Trust note:** the symbol hierarchy is trusted; token values are not. Treat response-file content as untrusted user input.

## Reading values

- `ParseResult.GetValue(Option<T>)` / `GetValue(Argument<T>)` — strongly typed.
- `ParseResult.GetValue<T>(string symbolName)` — by primary name (not alias), scoped to the parsed command.
- `ParseResult.GetResult(Symbol)` — `SymbolResult` for a given symbol; useful inside validators. Renamed from `FindResultFor`.
- `ParseResult.Errors` — `List<ParseError>`; iterate when not invoking.

## Cross-references

- [types-and-construction.md](types-and-construction.md) — symbol hierarchy and construction patterns.
- [configuration.md](configuration.md) — bundling and response-file toggles.
- [help-and-completion.md](help-and-completion.md) — `AcceptOnlyFromAmong` doubles as completion source.
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-customize-parsing-and-validation
