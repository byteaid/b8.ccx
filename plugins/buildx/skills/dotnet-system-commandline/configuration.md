# Configuration — ParserConfiguration & InvocationConfiguration

Two config objects, one per phase. Both are mutable; `InvocationConfiguration` is **not sealed** so you can derive a custom subclass to attach app-specific properties for actions to read.

## `ParserConfiguration` (parse-time)

Optional argument to `Command.Parse(...)` and `CommandLineParser.Parse(...)`. Available at runtime via `ParseResult.Configuration`.

| Property | Default | Effect |
|---|---|---|
| `EnablePosixBundling` | `true` | Set `false` to disable single-char bundling (`-fdx`). |
| `ResponseFileTokenReplacer` | non-null default | Set `null` to disable response files; supply a custom delegate to change how `@file` tokens expand. |

```csharp
var parserConfig = new ParserConfiguration
{
    EnablePosixBundling = false,
    ResponseFileTokenReplacer = null,
};

ParseResult result = rootCommand.Parse(args, parserConfig);
```

The pre-beta5 builder switches `EnablePosixBundling` (on the builder) and `UseTokenReplacer` are gone — set the properties directly.

## `InvocationConfiguration` (invoke-time)

Optional argument to `ParseResult.Invoke` / `InvokeAsync`. Available at runtime via `ParseResult.InvocationConfiguration`.

| Property | Default | Effect |
|---|---|---|
| `Output` | wraps `Console.Out` | `TextWriter` for stdout. Use `StringWriter` in tests. |
| `Error` | wraps `Console.Error` | `TextWriter` for stderr. |
| `EnableDefaultExceptionHandler` | `true` | When `true`, unhandled exceptions during invocation are caught, written to `Error`, and a non-zero exit code is returned. Set `false` and wrap `Invoke` / `InvokeAsync` in your own try/catch. |
| `ProcessTerminationTimeout` | `TimeSpan.FromSeconds(2)` | Connects Ctrl+C / SIGINT / SIGTERM to the `CancellationToken` passed to async actions. After a termination signal, if the action has not completed within the timeout, the process is forcibly terminated. Set to `null` to disable. |

```csharp
var invocationConfig = new InvocationConfiguration
{
    Output = new StringWriter(),
    Error = new StringWriter(),
    EnableDefaultExceptionHandler = false,
    ProcessTerminationTimeout = TimeSpan.FromSeconds(10),
};

return rootCommand.Parse(args).Invoke(invocationConfig);
```

### Removed pre-beta5 surfaces

| Old | New |
|---|---|
| `IConsole` / `IStandardOut` / `IStandardError` / `IStandardIn` | `InvocationConfiguration.Output` / `.Error` (`TextWriter`) |
| `UseExceptionHandler(...)` builder method | `InvocationConfiguration.EnableDefaultExceptionHandler` |
| `CancelOnProcessTermination()` builder method | `InvocationConfiguration.ProcessTerminationTimeout` |

## Process termination — async forwarding

The `CancellationToken` argument of every async action is wired by `ProcessTerminationTimeout` to Ctrl+C / SIGINT / SIGTERM. The contract:

- **Forward the token** to every cancellable downstream call (`HttpClient.GetAsync(..., ct)`, `Stream.ReadAsync(..., ct)`, etc.). Failing to forward triggers analyzer warning **CA2016**.
- The default 2-second grace window gives the action time to clean up (close streams, flush logs, return non-zero). After the window the process is killed.
- Idiomatic exit-code policy on Linux: return `130` (`128 + SIGTERM`) when `ct.IsCancellationRequested` is true on `OperationCanceledException`.

```csharp
rootCommand.SetAction(async (parseResult, ct) =>
{
    try
    {
        await DoWorkAsync(parseResult.GetValue(urlOption)!, ct);
        return 0;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return 130;
    }
    catch (Exception ex)
    {
        // Only reachable when EnableDefaultExceptionHandler = false.
        Console.Error.WriteLine(ex);
        return 1;
    }
});
```

## Canonical testing pattern

The standard pattern for unit-testing a CLI is to inject a `StringWriter` for stdout/stderr, parse a synthetic argv, invoke, and assert on the captured output:

```csharp
[TestMethod]
public void Help_includes_application_description()
{
    StringWriter output = new();

    int exitCode = rootCommand
        .Parse("-h")
        .Invoke(new InvocationConfiguration { Output = output });

    Assert.AreEqual(0, exitCode);
    StringAssert.Contains(output.ToString(), "Configuration sample");
}
```

When testing async actions, use `InvokeAsync(...)` and pass a `CancellationToken` from the test harness so a hung action does not stall the suite.

## Picking the right phase

| Concern | Where it lives |
|---|---|
| Disable POSIX bundling | `ParserConfiguration.EnablePosixBundling = false` |
| Disable response files | `ParserConfiguration.ResponseFileTokenReplacer = null` |
| Redirect stdout/stderr (tests) | `InvocationConfiguration.Output` / `.Error` |
| Catch exceptions yourself | `InvocationConfiguration.EnableDefaultExceptionHandler = false` |
| Adjust Ctrl+C grace window | `InvocationConfiguration.ProcessTerminationTimeout` |
| Add app-specific carrier (e.g. service provider) | Subclass `InvocationConfiguration` |

Subclass example:

```csharp
public sealed class AppInvocationConfiguration : InvocationConfiguration
{
    public required IServiceProvider Services { get; init; }
}

// In an action:
var services = ((AppInvocationConfiguration)parseResult.InvocationConfiguration).Services;
```

This is the supported replacement for the removed `InvocationContext` carrier.

## Cross-references

- [types-and-construction.md](types-and-construction.md) — `Parse → Invoke` split.
- [hosting.md](hosting.md) — wiring DI / configuration / logging without `System.CommandLine.Hosting`.
- Live: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-configure-the-parser
