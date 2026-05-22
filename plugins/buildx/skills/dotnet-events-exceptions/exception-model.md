# Exception Model — Hierarchy, Properties, AggregateException, ExceptionDispatchInfo

## Hierarchy

`Object` → `Exception` → (`SystemException` for runtime errors | custom user exceptions). `ApplicationException` is **superseded** — derive directly from `Exception`. Catching/throwing non-`Exception` is allowed by some languages but not CLS-compliant.

## `Exception` properties

| Property | Notes |
|---|---|
| `Message` | Human-readable. Localize via resource files. End sentences with a period. |
| `StackTrace` | Frames from throw to catch. Captured at throw or via `ExceptionDispatchInfo.SetCurrentStackTrace`. |
| `InnerException` | The cause. Walk via `GetBaseException()` or a loop. |
| `Data` | `IDictionary` for context that does not justify a typed property. |
| `HResult` | COM-style code; settable. Default for `Exception` is `0x80131500`. |
| `Source` | Assembly name by default; settable. |
| `TargetSite` | Throwing method (reflection). |
| `HelpLink` | URL/URN to documentation. |

## `InnerException` chaining

```csharp
try { return ParseConfig(text); }
catch (FormatException ex)
{
    throw new ConfigurationException("Invalid configuration file.", ex);
}

// Walk
for (var cur = ex; cur is not null; cur = cur.InnerException)
    Console.WriteLine($"{cur.GetType().Name}: {cur.Message}");
```

`GetBaseException()` returns the deepest non-`AggregateException` cause.

## `AggregateException`

Bundles multiple inner exceptions. Used by TPL (`Task.Wait`, `Task.Result`, `Task.WaitAll`, `Parallel.For/ForEach`, PLINQ). `await` unwraps and rethrows only the **first** inner; the synchronous APIs rethrow the wrapped aggregate.

| Member | Description |
|---|---|
| `InnerExceptions` | All causes. |
| `Flatten()` | Recursively un-nests inner aggregates. |
| `Handle(Func<Exception,bool>)` | `true` = handled; `false` = re-aggregated and rethrown. |
| `GetBaseException()` | Returns root if exactly one inner; otherwise the aggregate itself. |

```csharp
try { Task.WaitAll(t1, t2, t3); }
catch (AggregateException ae)
{
    ae.Flatten().Handle(e =>
    {
        if (e is OperationCanceledException) return true;
        if (e is HttpRequestException) { Log(e); return true; }
        return false;
    });
}
```

## `ExceptionDispatchInfo` (cross-thread rethrow)

Captures an exception's full state (stack, Watson info) so it can be rethrown later — possibly on a different thread, after the original `catch` has exited. New frames are **augmented**, not replaced.

| Member | Purpose |
|---|---|
| `Capture(Exception)` | Snapshot inside a `catch`. |
| `Throw()` | Rethrow with original state. |
| `Throw(Exception)` (static) | Rethrow a never-thrown exception while augmenting its trace. |
| `SetCurrentStackTrace(Exception)` (static) | Stamp current call site as the throw point onto an unthrown exception. |
| `SetRemoteStackTrace(Exception, string)` (static) | Attach a string trace originating elsewhere (RPC). |
| `SourceException` | The captured exception. |

```csharp
ExceptionDispatchInfo? edi = null;
try { File.ReadAllText(@"C:\temp\file.txt"); }
catch (FileNotFoundException ex) { edi = ExceptionDispatchInfo.Capture(ex); }

// ... arbitrary code, possibly hops thread ...
edi?.Throw();
```

`ExceptionDispatchInfo` is **not serializable** and not designed to cross AppDomain/process boundaries.
