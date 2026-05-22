# Canonical Script Blueprint

The shape every new script starts from. This file owns only what is **specific to scripts**: the shebang, the `#:package` line, the implicit `args` entry point, and the script-flavoured action wiring. For everything else, defer to the parents:

- `dotnet-file-based-apps` § directives — every `#:` directive (`#:package`, `#:project`, `#:property`, `#:sdk`), the SDK lifecycle (`dotnet run file.cs`, `dotnet build`, `dotnet publish`, `dotnet pack`, `dotnet project convert`), Native AOT opt-out, build cache, layout, secrets, launch profiles.
- `dotnet-system-commandline` § types-and-construction — `RootCommand`, `Command`, `Option<T>`, `Argument<T>`, `SetAction`, `ParseResult.GetValue`, validators (`AcceptOnlyFromAmong`, `AcceptExistingOnly`), recursive options, custom parsers, help, completion, hosting glue.

Copy the starter below, then customize via the parent skills.

## Minimal starter (sync)

```csharp
#!/usr/bin/env dotnet
#:package System.CommandLine@*

using System.CommandLine;

var input = new Option<FileInfo>("--input", "-i") { Required = true }.AcceptExistingOnly();
var root = new RootCommand("Describe what the script does.") { input };
root.SetAction(parseResult =>
{
    var f = parseResult.GetValue(input)!;
    Console.WriteLine(f.FullName);
    return 0;
});
return root.Parse(args).Invoke();
```

`args` is the implicit array provided to top-level statements. Run with `dotnet run file.cs -- --input data.csv` (or `chmod +x file.cs && ./file.cs --input data.csv` on Unix).

## Async variant

When any action awaits, the entry point returns `await root.Parse(args).InvokeAsync()` and each action takes `(parseResult, CancellationToken ct)`. Forward `ct` to every cancellable downstream call (CA2016). Return `130` (`128 + SIGTERM`) when `OperationCanceledException` is observed and `ct.IsCancellationRequested` is true. Construction of the option/command graph is identical — see `dotnet-system-commandline` § types-and-construction for `SetAction` overloads.

## Subcommands

Build verbs as `Command` instances and add them to the root alongside (not instead of) options. Cross-cutting flags become recursive (`Recursive = true`) so children inherit them without redeclaration. Full subcommand patterns: `dotnet-system-commandline` § types-and-construction.

## Sharing code

When a script grows past a single file, extract helpers into a class library `.csproj` and reference it via `#:project ../Shared/Shared.csproj`. Several scripts in the same directory can share the same library. Directive details: `dotnet-file-based-apps` § directives.

## Native AOT opt-out

Native AOT is on by default for file-based apps. If a dependency uses reflection-emit, add `#:property PublishAot=false`. Only `dotnet publish` is affected. Implications: `dotnet-file-based-apps` § native-aot (or the equivalent section of that skill's index).

## What goes where

| Lives in the `.cs` script | Lives in a class library | Lives in a real project |
|---|---|---|
| Command graph (`RootCommand`, options, args) | Domain types | Anything tested with `dotnet test` |
| Action delegate bodies (small) | Pure helpers, parsers, formatters | Multi-file evolving codebase |
| Direct I/O (read file, call HTTP, write stdout) | Reusable I/O abstractions | DI-heavy startup, hosted services |

## Scaffolding checklist

- [ ] File starts with `#!/usr/bin/env dotnet` if Unix-runnable.
- [ ] `#:package System.CommandLine@*` (or pinned version) at the top.
- [ ] Single `RootCommand` with a `Description`.
- [ ] Every option / argument has a `Description`.
- [ ] Async actions take `(parseResult, CancellationToken ct)` and forward `ct`.
- [ ] Exit codes returned: `0` success, non-zero failure, `130` cancellation.
- [ ] On Unix, `chmod +x` the file (LF line endings, no BOM).
- [ ] To install as a tool: leave `PackAsTool=true` (file-based default) and verify `dotnet pack file.cs` produces a `.nupkg`. See [distribution.md](distribution.md).

## Cross-references

- [cli-design.md](cli-design.md) — naming, validation, help text conventions.
- [distribution.md](distribution.md) — packaging the script as a tool.
- `dotnet-file-based-apps` § directives — full `#:` directive syntax and SDK lifecycle.
- `dotnet-system-commandline` § types-and-construction — full API for the command graph.
