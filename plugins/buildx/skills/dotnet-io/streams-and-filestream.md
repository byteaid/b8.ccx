# `Stream`, `FileStream`, `RandomAccess`

## `Stream`

Capability triad: `CanRead`, `CanWrite`, `CanSeek`. Plus `CanTimeout`, `Length`, `Position`, `ReadTimeout`, `WriteTimeout`.

| Sync | Async |
|---|---|
| `Read(byte[], int, int)` / `Read(Span<byte>)` | `ReadAsync(byte[], int, int[, CT])` / `ReadAsync(Memory<byte>[, CT])` |
| `ReadByte()` | — |
| `ReadExactly(Span<byte>)` (.NET 7+) | `ReadExactlyAsync(Memory<byte>[, CT])` |
| `ReadAtLeast(Span<byte>, min[, throwOnEnd])` (.NET 7+) | `ReadAtLeastAsync(Memory<byte>, min[, throwOnEnd][, CT])` |
| `Write(byte[], int, int)` / `Write(ROS<byte>)` | `WriteAsync(byte[], int, int[, CT])` / `WriteAsync(ROM<byte>[, CT])` |
| `WriteByte(byte)` | — |
| `Seek(long, SeekOrigin)` / `SetLength(long)` | — |
| `Flush([bool flushToDisk])` | `FlushAsync([CT])` |
| `CopyTo(Stream[, bufferSize])` | `CopyToAsync(...)` |
| `Close()` / `Dispose()` | `DisposeAsync()` |

Built-in concrete streams: `FileStream` (preferred for sequential streaming), `MemoryStream` (resizable; `GetBuffer()` exposes raw mutable array, `ToArray()` copies), `BufferedStream` (decorator), `UnmanagedMemoryStream`, `NetworkStream`, `PipeStream`, `CryptoStream`, `GZipStream` / `DeflateStream` / `BrotliStream` / `ZLibStream`, `Stream.Null` (sink), `Stream.Synchronized(Stream)` (coarse-grained lock; not a perf substitute).

## `FileStream`

```csharp
var opts = new FileStreamOptions
{
    Mode      = FileMode.OpenOrCreate,
    Access    = FileAccess.ReadWrite,
    Share     = FileShare.Read,
    Options   = FileOptions.Asynchronous | FileOptions.SequentialScan,
    BufferSize = 4096,                  // 0 disables internal buffering
    PreallocationSize = 1L << 30,       // hint to filesystem (NTFS, XFS, ...)
    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,   // .NET 7+
};
using var fs = new FileStream(@"big.dat", opts);
```

| `FileMode` | Behavior |
|---|---|
| `CreateNew` | Throws if exists. |
| `Create` | Overwrites or creates. |
| `Open` | Throws if missing. |
| `OpenOrCreate` | Opens or creates. |
| `Truncate` | Opens then truncates to 0. |
| `Append` | Opens or creates; **only** with `FileAccess.Write`; `Seek` to before EOF throws. |

`FileAccess`: `Read` / `Write` / `ReadWrite`. `FileShare`: `None` / `Read` / `Write` / `ReadWrite` / `Delete` / `Inheritable` (combine with `|`).

### `FileOptions`

| Flag | Effect |
|---|---|
| `Asynchronous` | Opens with `FILE_FLAG_OVERLAPPED` on Windows; required for true non-blocking I/O. **Without it, `*Async` falls back to `Task.Run`-wrapped sync.** |
| `SequentialScan` | Hints OS read-ahead (Windows). |
| `RandomAccess` | Disables read-ahead (Windows). |
| `WriteThrough` | Bypass OS write-back cache. |
| `DeleteOnClose` | Removes file on dispose. |
| `Encrypted` | EFS (Windows). |

`FileStream.Lock`/`Unlock` are Windows-only advisory range locks; throw `PlatformNotSupportedException` on Unix. Prefer `FileShare` for cross-platform code.

## `RandomAccess`

Static class operating on `SafeFileHandle`. **Thread-safe** for offset-based reads/writes (the OS APIs `pread`/`pwrite` and overlapped I/O take an explicit offset). **Only regular disk files** (no pipes/unseekable).

| Method | Purpose |
|---|---|
| `GetLength(SafeFileHandle)` / `SetLength(SafeFileHandle, long)` | File length. |
| `Read(SafeFileHandle, Span<byte>, long offset)` | Positional read; may be short. |
| `Read(SafeFileHandle, IReadOnlyList<Memory<byte>>, long offset)` | Scatter read. |
| `ReadAsync(SafeFileHandle, Memory<byte>, long, CT)` / scatter variant | |
| `Write(SafeFileHandle, ROS<byte>, long)` / gather variant | |
| `WriteAsync(...)` | |
| `FlushToDisk(SafeFileHandle)` (.NET 9+) | fsync. |

```csharp
using SafeFileHandle h = File.OpenHandle("huge.bin", FileMode.Open, FileAccess.Read,
    FileShare.Read, FileOptions.Asynchronous | FileOptions.RandomAccess);

long length = RandomAccess.GetLength(h);
const int chunk = 1 << 20;                  // 1 MiB
int chunks = (int)((length + chunk - 1) / chunk);

var tasks = new Task[chunks];
var hash  = new byte[chunks][];
for (int i = 0; i < chunks; i++)
{
    int idx = i;
    tasks[i] = Task.Run(async () =>
    {
        long off = (long)idx * chunk;
        int  len = (int)Math.Min(chunk, length - off);
        var buf = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            int read = await RandomAccess.ReadAsync(h, buf.AsMemory(0, len), off);
            hash[idx] = SHA256.HashData(buf.AsSpan(0, read));
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    });
}
await Task.WhenAll(tasks);
```
