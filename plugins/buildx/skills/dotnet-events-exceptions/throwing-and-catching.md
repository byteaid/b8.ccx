# Throwing & Catching — `throw`, `ThrowIf*`, `try`/`catch`/`finally`/`when`

## `throw;` vs `throw ex;`

| Form | StackTrace | When |
|---|---|---|
| `throw;` | **Preserves** original (with rethrown marker). | Inside `catch` to bubble up after side-effects/logging. |
| `throw ex;` | **Resets** to current method. Loses original frames. | Almost never. CA2200. |
| `throw new T(msg, ex);` | New exception, original as `InnerException`. | Wrapping a low-level cause. |

## `throw` expression (C# 7+)

Allowed wherever an expression is needed:

```csharp
string first = args.Length >= 1 ? args[0] : throw new ArgumentException("Pass at least one arg.");

public string Name
{
    get => _name;
    set => _name = value ?? throw new ArgumentNullException(nameof(value));
}

DateTime ToDateTime(IFormatProvider p) =>
    throw new InvalidCastException("Conversion to DateTime not supported.");
```

## Builder helpers (.NET 7+)

```csharp
ArgumentNullException.ThrowIfNull(arg);
ArgumentException.ThrowIfNullOrEmpty(s);
ArgumentException.ThrowIfNullOrWhiteSpace(s);
ArgumentOutOfRangeException.ThrowIfZero(n);
ArgumentOutOfRangeException.ThrowIfNegative(n);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
ArgumentOutOfRangeException.ThrowIfEqual(a, b);
ArgumentOutOfRangeException.ThrowIfNotEqual(a, b);
ArgumentOutOfRangeException.ThrowIfLessThan(a, b);
ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(a, b);
ArgumentOutOfRangeException.ThrowIfGreaterThan(a, b);
ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(a, b);
ObjectDisposedException.ThrowIf(_disposed, this);
cancellationToken.ThrowIfCancellationRequested();
```

## `try` / `catch` / `finally` / `when`

### Multiple `catch` clauses

- Evaluated top to bottom; first matching type wins; at most one runs.
- Order **most-derived to least-derived** types. The compiler errors if a base-type clause shadows a more-derived one.
- A type-less `catch { ... }` (or `catch (Exception)`) catches everything; if present, must be last (unless later clauses are differentiated by `when` filter).

### `when` filters

- Boolean expression after the catch type. The clause matches only if the type matches **and** the filter returns `true`.
- The filter runs **before** the stack is unwound — original throw point remains on the stack and locals are still inspectable in a debugger.
- If the filter throws, the thrown exception is **swallowed** and the filter is treated as `false`.

```csharp
try { Process(input); }
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
catch (HttpRequestException ex) when (IsTransient(ex))                          { Retry(); }
catch (HttpRequestException)                                                    { /* terminal */ }

// Pure-logging filter that never handles
catch (Exception ex) when (LogAndContinue(ex)) { /* never reached */ }
static bool LogAndContinue(Exception ex) { Logger.Error(ex); return false; }

// Combine multiple types via filter
catch (Exception ex) when (ex is TimeoutException or HttpRequestException) { /* … */ }
```

Filters beat `if`-then-`throw;` because they avoid stack unwinding and preserve the rethrow.

### `finally`

- Runs whether the `try` exits normally, by `return`/`break`/`continue`/`goto`, or by exception. Runs even when a `catch` itself throws.
- **Does not run** on immediate process termination (`Environment.FailFast`, fatal stack overflow / invalid program in some hosts, process kill).
- Don't `throw` from `finally` (CA2219).
- Prefer `using` / `using var` / `await using`.

### Async + iterator semantics

- Exceptions in an `async` method are stored in the returned `Task` and rethrown on `await` (or wrapped in `AggregateException` via `Wait`/`.Result`).
- Argument validation **before the first `await`** — split into a sync wrapper + nested async core if necessary:

```csharp
public Task<T> GetAsync(string id)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(id);
    return GetCore(id);
    static async Task<T> GetCore(string id) { /* ... */ }
}
```

- In an iterator (`yield return`) method, exceptions surface only when the consumer advances the enumerator.

## Code-analysis cheat sheet

| Rule | Subject |
|---|---|
| CA1031 | Do not catch general exception types. |
| CA1064 | Exceptions should be public. |
| CA1065 | Do not raise exceptions in unexpected locations (Equals/GetHashCode/ToString/static ctor/ops/Dispose/finalizer/event accessors/getters). |
| CA2200 | Rethrow to preserve stack — `throw;` not `throw ex;`. |
| CA2201 | Do not raise reserved exception types. |
| CA2219 | Do not raise exceptions in `finally`. |
| CA2250 | Use `ThrowIfCancellationRequested`. |
| CA1510 – CA1513 | Use the `ThrowIf*` argument-validation helpers. |
| SYSLIB0050 / SYSLIB0051 | Binary serialization for exceptions is obsolete. |
