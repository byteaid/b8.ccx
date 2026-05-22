# CLI Design Conventions for Scripts

A script's command surface is what users see. The `System.CommandLine` API supports more shapes than you should use; these conventions narrow the choice set.

For the underlying API see `dotnet-system-commandline`. This file is opinion, not reference.

## Verbs vs options vs arguments

| Use a **verb** (subcommand) when | Use an **option** when | Use an **argument** when |
|---|---|---|
| The action itself differs (e.g. `sync` vs `cleanup`). | The action is the same but parameterized (e.g. `--format json`). | The parameter is the obvious "subject" of the action and naming it would be redundant (e.g. `mytool path/to/file`). |
| The set of valid options changes per verb. | The flag is optional or has a default. | A single positional is required and there is exactly one obvious thing it refers to. |
| You expect the surface to grow. | The flag is cross-cutting (`--verbosity`, `--config`) — pair with `Recursive = true`. | n/a |

Heuristics:

- **Single-verb script** → `RootCommand` only, no `Subcommands`. Easier to use, easier to document.
- **Two verbs** → split. Don't try to cram both into one verb with a `--mode` option — users won't discover it.
- **Five+ verbs** → consider grouping or graduating to a real project.
- **Mostly one positional plus a few flags** → use an `Argument<T>` for the positional. `mytool foo.txt --format json` reads better than `mytool --input foo.txt --format json`.

## Naming

| Element | Convention | Examples |
|---|---|---|
| Verbs | Lowercase, hyphen-separated, **imperative** (verb form). | `sync`, `cleanup`, `init`, `add-user`, `list-users`. |
| Long option | `--kebab-case`. Spell things out. | `--retention-days`, not `--rd`. |
| Short option | Single letter, lowercase. Reserve `-h` / `-?` / `/h` / `/?` for help; `-v` is conventional for verbose **or** version — pick one and document it. | `-i` (input), `-o` (output), `-f` (format). |
| Boolean flags | Phrase as a positive (`--dry-run`, `--no-color`). Avoid double negatives. | `--watch`, `--force`. |
| Argument | Lowercase, descriptive noun. Help name in caps. | `path` (with `HelpName = "PATH"`). |

Avoid:
- `--`-prefixed args meant to be positional. Confuses users and the parser.
- Different conventions in the same tool (`--retention-days` and `--maxRetries` together).
- Single-letter long options (`--v` instead of `-v`).

## Required vs default

- **Mandatory inputs** → `Required = true`. The script fails with help text if absent.
- **Optional with a sensible default** → `DefaultValueFactory = _ => <default>`. The default appears in help.
- **Optional, no default** → omit both. Reading the value yields the default of `T` (`null` for nullable types). Use sparingly — explicit defaults are clearer.
- **Default that depends on another flag** → resolve inside the action delegate, not in `DefaultValueFactory`. Keep the default factory pure.

## Validation

Use the built-in fluent validators when they fit (see `dotnet-system-commandline/parsing.md` for the full list):

| Validator | When |
|---|---|
| `AcceptOnlyFromAmong(...)` | Closed enum-like sets. Bonus: doubles as a tab-completion source and shows up in help. |
| `AcceptExistingOnly()` | `FileInfo` / `DirectoryInfo` arguments where the script reads from disk. |
| `AcceptLegalFileNamesOnly()` | The script writes a file whose name comes from the user. |

For anything else, write a custom validator on the symbol's `Validators` collection. Multiple errors per symbol are supported — call `result.AddError("...")` once per problem.

For value transforms beyond what the built-in parser does, set `CustomParser` (typed delegate). Keep the parser pure: validate inside it, and emit `result.AddError(...)` on bad input.

## Help text

- **Every** symbol gets a `Description`. Help is the script's documentation.
- Set `HelpName` when the option's argument display name is non-obvious (`FILEPATH`, `URL`, `LEVEL`, `KEY=VALUE`).
- Mark internal/diagnostic options `Hidden = true`. They still work on the command line but stay out of `--help`.
- For prologue / epilogue (banner, links, footer), use the `CustomHelpAction` wrapping pattern — see `dotnet-system-commandline/help-and-completion.md`.

## Exit codes

A script's exit code is part of its contract — CI pipelines and shells branch on it.

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Generic failure. |
| `2` | Misuse / parse error (the parser already returns this when `ParseResult.Errors` is non-empty). |
| `130` | Clean cancellation on SIGTERM (`128 + SIGTERM`). |
| `>1`, `<128` | App-specific. Document them in help if you use them. |

Return the code from the `SetAction` delegate. The default exception handler sets a non-zero code automatically when an exception escapes; if you need to control it, set `EnableDefaultExceptionHandler = false` (see `dotnet-system-commandline/configuration.md`) and catch yourself.

## Output discipline

- **stdout** for the script's product (data, table, JSON). A consumer should be able to `mytool ... | jq`.
- **stderr** for diagnostics, errors, progress. A consumer that pipes stdout shouldn't see chatter.
- Write to `parseResult.InvocationConfiguration.Output` / `.Error` instead of `Console.Out` / `Console.Error` if you intend to test the script — otherwise `Console` is fine.
- Don't mix progress bars and structured stdout output. If progress matters, send it to stderr or use a `--quiet` flag.

## When subcommands need shared state

Cross-cutting options (`--config`, `--verbosity`, `--output-format`) belong on the `RootCommand` with `Recursive = true`. Read them from `parseResult.GetValue(option)` inside any subcommand's action — recursive options bind once and propagate down.

For shared helper objects (DB connection, HTTP client) created once and reused across actions, the cleanest pattern is:
- Create them inside the action when there's only one verb.
- Hoist them above the command graph (top-level statements) when multiple actions use them, capturing in the action lambda.
- For non-trivial DI, graduate to a real project with `Microsoft.Extensions.Hosting` (see `dotnet-system-commandline/hosting.md`).

## Cross-references

- [scaffold.md](scaffold.md) — the four canonical script shapes.
- [distribution.md](distribution.md) — packaging considerations that flow back into the design.
- `dotnet-system-commandline/parsing.md` — validators and custom parsers in full.
- `dotnet-system-commandline/help-and-completion.md` — `HelpAction`, directives, completion.
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/design-guidance
