# Process-wide Exception Events and COM Interop

## `AppDomain.CurrentDomain.UnhandledException`

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    Log.Fatal(ex, "Unhandled exception. Terminating={IsTerminating}", e.IsTerminating);
};
```

Last-chance hook for **logging only**. Cannot prevent termination on .NET Core+. May fire on multiple threads concurrently — handler must be thread-safe.

## `TaskScheduler.UnobservedTaskException`

Raised when a faulted `Task` is being finalized without being observed (no `await`/`.Result`/`.Wait`/`.Exception`). On .NET Core/5+ the process is **not** torn down even if the handler does nothing.

```csharp
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
```

Use as a safety net to log fire-and-forget failures; don't rely on it for correctness — fix the missing `await`.

## `AppDomain.CurrentDomain.FirstChanceException`

Raised the moment the runtime starts searching for a handler — even for exceptions that will be caught and swallowed. Fires for **every** managed throw. Useful for diagnostics; never for control flow. **Handler must not throw.**

```csharp
AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
{
    if (e.Exception is OperationCanceledException) return;
    Diagnostics.Counter("first-chance").Add(1, ("type", e.Exception.GetType().Name));
};
```

## Host-specific equivalents

| Host | Event |
|---|---|
| WinForms | `Application.ThreadException` (UI thread); `AppDomain.UnhandledException` (others). Switch via `Application.SetUnhandledExceptionMode`. |
| WPF | `Application.DispatcherUnhandledException`, `Dispatcher.UnhandledException` (`e.Handled = true` to swallow). |
| ASP.NET Core | `UseExceptionHandler`, `UseDeveloperExceptionPage`, `IExceptionHandler`, `ProblemDetails`. |
| MAUI | `Application.Current.UnhandledException`. |

## COM interop exceptions

The CLR translates between managed exceptions and COM `HRESULT`s automatically.

- **Managed → COM:** managed throw becomes a failure HRESULT (from the exception's `HResult` or type mapping).
- **COM → Managed:** failure HRESULT becomes a managed exception. Well-known codes map to specific types: `E_ACCESSDENIED` → `UnauthorizedAccessException`, `E_OUTOFMEMORY` → `OutOfMemoryException`, `E_INVALIDARG` → `ArgumentException`, `E_POINTER` → `NullReferenceException`, `E_NOTIMPL` → `NotImplementedException`, `DISP_E_TYPEMISMATCH` → `InvalidCastException`. Unknown HRESULTs surface as `COMException`; inspect `COMException.ErrorCode`.

```csharp
try { comObject.DoSomething(); }
catch (COMException cex)
{
    int hr = cex.ErrorCode;
    Console.WriteLine($"HRESULT 0x{hr:X8}: {cex.Message}");
}

// Manual conversion
var ex = Marshal.GetExceptionForHR(unchecked((int)0x80070005)); // E_ACCESSDENIED -> UnauthorizedAccessException
Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));        // throws COMException
```

If the COM object implements `IErrorInfo`, `Message`/`Source`/`HelpLink` are populated from the rich COM error. `IDispatch` errors arrive as `TargetInvocationException` wrapping the mapped exception.
