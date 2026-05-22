# Custom Exception Types and Common .NET Exceptions

## Custom exception types

```csharp
public class EmployeeListNotFoundException : Exception
{
    public EmployeeListNotFoundException() { }
    public EmployeeListNotFoundException(string message) : base(message) { }
    public EmployeeListNotFoundException(string message, Exception inner) : base(message, inner) { }
}
```

Conventions:
- Derive from `Exception`. Name ends in `Exception`. `public` if it should be caught from outside the assembly (CA1064).
- Provide the three canonical constructors.
- Add typed properties only when callers programmatically need that data.
- Do **not** implement `[Serializable]` / `ISerializable` / the `(SerializationInfo, StreamingContext)` constructor in new code targeting .NET 5+ — `BinaryFormatter` is removed/disabled by default and these APIs are obsolete (`SYSLIB0050` / `SYSLIB0051`). For cross-process transport use a contract serializer (System.Text.Json, MessagePack, gRPC error details).

With typed payload:

```csharp
public sealed class TransferFundsException : Exception
{
    public TransferFundsException() { }
    public TransferFundsException(string message) : base(message) { }
    public TransferFundsException(string message, Exception inner) : base(message, inner) { }

    public required Account From { get; init; }
    public required Account To   { get; init; }
    public required decimal Amount { get; init; }
}
```

## Common .NET exception types

| Exception | Throw conditions |
|---|---|
| `ArgumentException` | Argument invalid in some way. Set `ParamName`. |
| `ArgumentNullException` | Argument is `null` and shouldn't be. `ThrowIfNull`. |
| `ArgumentOutOfRangeException` | Argument outside valid range. `ThrowIf*` helpers. |
| `InvalidOperationException` | State forbids the operation now. |
| `NotSupportedException` | Member exists but unsupported in this context. |
| `NotImplementedException` | Stub. Production code should not throw. |
| `ObjectDisposedException` | Member used after `Dispose`. `ThrowIf(_disposed, this)`. |
| `NullReferenceException` / `IndexOutOfRangeException` | **Runtime-only**, don't throw. |
| `OverflowException` / `DivideByZeroException` | Arithmetic. |
| `FormatException` | Argument format invalid (parsing). |
| `InvalidCastException` | Bad reference cast or `IConvertible` failure. |
| `KeyNotFoundException` | Dictionary key absent. |
| `OutOfMemoryException` / `StackOverflowException` / `AccessViolationException` | **Reserved**, don't throw. `StackOverflowException` is uncatchable in modern CLR. |
| `IOException` (+ `FileNotFoundException`, `DirectoryNotFoundException`, `PathTooLongException`) | I/O failures. |
| `UnauthorizedAccessException` | OS-level access denied. |
| `OperationCanceledException` (+ `TaskCanceledException`) | Cooperative cancellation. Catch the base, not the derivative. |
| `TimeoutException` | Operation timed out. |
| `AggregateException` | Multiple inner exceptions (TPL/PLINQ). |
| `HttpRequestException` | HTTP failure (`StatusCode` since .NET 5). |
| `JsonException` | `System.Text.Json` parse failure. |
| `SocketException` | `SocketErrorCode`. |
| `COMException` / `Win32Exception` / `ExternalException` / `SEHException` | Native interop. |
| `PlatformNotSupportedException` | API not supported on this platform. |
| `CryptographicException` | Crypto operation failed. |
