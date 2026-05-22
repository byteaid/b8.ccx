# `IDisposable` / `IAsyncDisposable` Patterns

## Synchronous (subclass-friendly)

```csharp
public class Resource : IDisposable
{
    private bool _disposed;
    private IntPtr _native;
    private Stream? _owned;

    public Resource(string path)
    {
        _native = NativeMethods.Open(path);
        _owned  = new FileStream(path, FileMode.Open);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) { _owned?.Dispose(); _owned = null; }
        if (_native != IntPtr.Zero) { NativeMethods.Close(_native); _native = IntPtr.Zero; }
        _disposed = true;
    }

    // Finalizer ONLY when this type directly owns unmanaged resources.
    ~Resource() => Dispose(disposing: false);
}
```

Rules:

- Public parameterless `Dispose()` is the API; protected virtual `Dispose(bool)` does the work.
- Idempotent and safe under repeated calls, post-construction-failure, and from any thread.
- Sealed types: skip the virtual; just write `Dispose()` and (optionally) a finalizer.
- Subclass overrides chain to `base.Dispose(disposing)` last.

## Asynchronous

```csharp
public class AsyncResource : IAsyncDisposable, IDisposable
{
    private Stream? _stream;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_stream is not null) { await _stream.DisposeAsync().ConfigureAwait(false); _stream = null; }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) _stream?.Dispose();
        _stream = null;
    }
}
```

`await using var r = new AsyncResource();` is the consumer side. Always `ConfigureAwait(false)` inside `DisposeAsyncCore`. Implement both `IDisposable` and `IAsyncDisposable` when sync teardown is reasonable; otherwise just `IAsyncDisposable`.

## `SafeHandle` — preferred for unmanaged handles

```csharp
internal sealed class MyHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MyHandle() : base(ownsHandle: true) { }
    protected override bool ReleaseHandle() => NativeMethods.Close(handle) == 0;
}
```

Why `SafeHandle`: ref counting (`DangerousAddRef` / `DangerousRelease`); critical finalizer (runs after regular finalizers); plays well with `out`/`ref` marshalling and `GC.KeepAlive`; composes with `IDisposable`.
