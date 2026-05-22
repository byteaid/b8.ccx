# Error Handling, Atomic Writes, Memory-Mapped Files, Isolated Storage

## Error handling

Hierarchy:

```
SystemException
└── IOException
    ├── DirectoryNotFoundException
    ├── DriveNotFoundException
    ├── EndOfStreamException
    ├── FileLoadException
    ├── FileNotFoundException
    ├── PathTooLongException        // .NET Framework only at runtime
    └── PipeException
UnauthorizedAccessException : SystemException
OperationCanceledException  : SystemException
```

`IOException.HResult`'s low 16 bits = Win32 error code on Windows. Common values:

| Constant | Code | Meaning |
|---|---|---|
| `ERROR_FILE_NOT_FOUND` | 2 | usually surfaces as `FileNotFoundException` |
| `ERROR_PATH_NOT_FOUND` | 3 | `DirectoryNotFoundException` |
| `ERROR_ACCESS_DENIED` | 5 | `UnauthorizedAccessException` |
| `ERROR_SHARING_VIOLATION` | 32 | locked by another process |
| `ERROR_LOCK_VIOLATION` | 33 | |
| `ERROR_FILE_EXISTS` | 80 | `FileMode.CreateNew` collision |
| `ERROR_DISK_FULL` | 112 | |
| `ERROR_DIRECTORY_NOT_EMPTY` | 145 | |

```csharp
try { /* ... */ }
catch (FileNotFoundException)        { /* missing */ }
catch (DirectoryNotFoundException)   { /* missing dir */ }
catch (UnauthorizedAccessException)  { /* ACL/readonly */ }
catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32) { /* sharing violation */ }
catch (IOException ex) when ((ex.HResult & 0xFFFF) == 80) { /* exists */ }
catch (IOException ex)               { /* other I/O */ }
```

## Sharing-violation retry

```csharp
static async Task<FileStream> OpenWithRetryAsync(
    string path, FileStreamOptions opts,
    int maxAttempts = 5, TimeSpan? initial = null, CancellationToken ct = default)
{
    var delay = initial ?? TimeSpan.FromMilliseconds(50);
    for (int attempt = 1; ; attempt++)
    {
        try { return new FileStream(path, opts); }
        catch (IOException ex) when (attempt < maxAttempts && IsTransient(ex))
        {
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromTicks(delay.Ticks * 2);
        }
    }
    static bool IsTransient(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code is 32 or 33;
    }
}
```

## Atomic writes

```csharp
static async Task AtomicWriteAsync(string path, byte[] payload, CancellationToken ct)
{
    string dir = Path.GetDirectoryName(path)!;
    string tmp = Path.Combine(dir, Path.GetRandomFileName());
    var opts = new FileStreamOptions {
        Mode = FileMode.CreateNew, Access = FileAccess.Write,
        Share = FileShare.None, Options = FileOptions.Asynchronous,
    };
    await using (var fs = new FileStream(tmp, opts))
    {
        await fs.WriteAsync(payload, ct);
        await fs.FlushAsync(ct);
        fs.Flush(flushToDisk: true);            // fsync
    }
    File.Move(tmp, path, overwrite: true);      // atomic on same filesystem
}
```

`File.Replace(src, dst, backup)` is a Windows-style three-way replace that preserves dst's ACL and attributes — useful for in-place updates without losing metadata.

## Memory-mapped files

Use cases: very large files (avoid copying via `FileStream`), shared memory across processes, random access via pointers/`Span`.

```csharp
using var mmf = MemoryMappedFile.CreateFromFile(@"big.dat", FileMode.Open);
using var view = mmf.CreateViewAccessor(offset: 0, size: 1L << 20, MemoryMappedFileAccess.Read);
int magic = view.ReadInt32(0);
view.ReadArray(4, buffer, 0, buffer.Length);

// IPC: shared memory between processes via named map
using var shared = MemoryMappedFile.CreateOrOpen("MyApp.SharedRegion", capacity: 64 * 1024);
using var w = shared.CreateViewStream();
new BinaryWriter(w).Write(42);
```

`MemoryMappedFileAccess`: `ReadWrite` (default) / `Read` / `Write` / `CopyOnWrite` / `ReadExecute` / `ReadWriteExecute`. `MemoryMappedFileOptions`: `None` / `DelayAllocatePages` (Windows; reserve without committing physical memory).

Views are paged in on demand. A `Span<byte>` over the view's `SafeMemoryMappedViewHandle.AcquirePointer` is the fastest random-access path. Always `Flush()` views and `Dispose` the `MemoryMappedFile` to release the system mapping object.

## Isolated storage

User/app-scoped virtual filesystem. Legacy CAS origin, still useful for desktop user-settings.

Scopes (`IsolatedStorageScope`): `User` / `Roaming` / `Machine` / `Assembly` / `Domain` / `Application`. Combine with `|`.

```csharp
using var store = IsolatedStorageFile.GetUserStoreForAssembly();
store.CreateDirectory("settings");
using (var fs = new IsolatedStorageFileStream("settings/config.json", FileMode.Create, store))
using (var w  = new StreamWriter(fs))
    w.Write(json);
```

Backing locations (Windows 7+): `User` → `%LOCALAPPDATA%\IsolatedStorage\…`; `User|Roaming` → `%APPDATA%\IsolatedStorage\…`; `Machine` → `%PROGRAMDATA%\IsolatedStorage\…`.

**Do not use `Machine` scope in multi-user threat environments** — another local user can plant files; consuming them creates EoP/DoS/info-disclosure exposure (hard-link tricks). Use `%PROGRAMFILES%`/`HKLM` (admin-only-writable) for trusted machine-wide config.

`IsolatedStorageFileStream` derives from `FileStream`, so all stream APIs apply.
