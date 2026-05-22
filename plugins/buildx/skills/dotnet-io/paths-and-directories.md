# `Path`, `File`, `Directory`, Enumeration, `FileSystemWatcher`

## `Path` — cross-platform path manipulation

| Member | Notes |
|---|---|
| `Combine(...)` / `Join(...)` / `TryJoin(...)` | `Combine` resets when an arg is rooted; `Join` never resets; `TryJoin` is allocation-free. |
| `GetFullPath(string)` / `GetFullPath(string, string basePath)` | Two-arg form (.NET Core 2.1+) avoids dependence on `Environment.CurrentDirectory` — thread-safe. |
| `GetDirectoryName` / `GetFileName` / `GetFileNameWithoutExtension` / `GetExtension` / `ChangeExtension` | All have `ROS<char>` overloads. |
| `GetRelativePath(relativeTo, path)` | OS-aware case sensitivity. |
| `IsPathRooted` / `IsPathFullyQualified` | `IsPathFullyQualified` is stricter — independent of CWD. |
| `EndsInDirectorySeparator` / `TrimEndingDirectorySeparator` | |
| `GetTempPath` / `GetTempFileName` / `GetRandomFileName` | `GetTempFileName` creates a 0-byte file; `GetRandomFileName` does not. |
| `Exists(string)` (.NET 7+) | Non-throwing existence check (file or dir). |

### Windows path forms

| Form | Example | Semantics |
|---|---|---|
| Absolute DOS | `C:\Documents\a.pdf` | Rooted at drive `C:`. |
| Drive-current-relative | `C:Projects\a.sln` | Resolves against the **per-drive current directory** (hidden env var; common bug source). |
| Root-current-drive-relative | `\Program Files\x.exe` | Root of the current drive. |
| CWD-relative | `2018\Jan.xlsx` | Relative to `Environment.CurrentDirectory`. |
| UNC | `\\server\share\dir\f.txt` | Always fully qualified. |
| DOS device, normalized | `\\.\C:\Test\f.txt` | Goes through normalization. |
| DOS device, raw | `\\?\C:\Test\f.txt` | **Skips** normalization & legacy `MAX_PATH` check. Required to address `hidden.` or `name ` (trailing space). |
| Volume-GUID device | `\\?\Volume{...}\f.txt` | |
| Legacy device | `CON`, `LPT1`, `COM1` | Pre-Win11 reinterpreted as `\\.\CON`. |

`MAX_PATH` (260) check applies only to .NET Framework. .NET Core/.NET 5+ handles long paths transparently — `\\?\` is *not* required for length, only to skip normalization. Windows file system is case-**insensitive**, case-**preserving**.

### Unix paths

- `/` is the only separator. Case-**sensitive** by default.
- **.NET 8 breaking change:** the runtime no longer translates `\` to `/`. `dir\file` is interpreted literally as a single filename containing a backslash. Use `Path.Combine` / forward slashes.

Relative paths are **dangerous in multithreaded apps** — `Environment.CurrentDirectory` is per-process. Prefer `Path.GetFullPath(path, basePath)`.

## `File`, `FileInfo`, `Directory`, `DirectoryInfo`

Many short ops on different paths → static `File`/`Directory`. Many ops on the same path → `FileInfo`/`DirectoryInfo` (instance, caches path validation).

### `File` selected members

| Group | Members |
|---|---|
| Existence/metadata | `Exists`, `GetAttributes`/`SetAttributes`, `Get/Set{Creation,LastAccess,LastWrite}Time[Utc]`, `Get/SetUnixFileMode` (.NET 7+). |
| Lifecycle | `Create`, `CreateText`, `Open(path, FileStreamOptions)` (.NET 6+), `OpenRead`/`OpenWrite`/`OpenText`, `OpenHandle(...)` (.NET 6+, returns `SafeFileHandle`), `Copy`/`Move`/`Replace`/`Delete`, `Decrypt`/`Encrypt` (Windows). |
| Whole-file text | `ReadAllText[Async]`, `ReadAllLines[Async]`, `ReadLines[Async]` (lazy), `WriteAllText[Async]`, `WriteAllLines[Async]`, `AppendAllText[Async]`, `AppendAllLines[Async]`, `AppendText`. |
| Whole-file bytes | `ReadAllBytes[Async]`, `WriteAllBytes[Async]`. |
| Symlinks (.NET 6+) | `CreateSymbolicLink`, `ResolveLinkTarget`. |

`File.WriteAllBytesAsync(string, ROM<byte>, ...)` overload added in .NET 9. `File.WriteAllText[Async]` overloads accepting `ReadOnlySpan<char>` / `ReadOnlyMemory<char>` added in .NET 9.

### `Directory` selected members

| Group | Members |
|---|---|
| Lifecycle | `CreateDirectory(path)` (idempotent), `CreateDirectory(path, UnixFileMode)` (.NET 7+), `CreateTempSubdirectory(prefix?)` (.NET 8+), `Delete[recursive]`, `Move`. |
| Listing | `GetFiles`/`GetDirectories`/`GetFileSystemEntries` (eager `string[]`); `EnumerateFiles`/`EnumerateDirectories`/`EnumerateFileSystemEntries` (lazy). |
| Symlinks (.NET 6+) | `CreateSymbolicLink`, `ResolveLinkTarget`. |

### Enumeration

```csharp
foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
    Console.WriteLine(f);

// Preferred — richer control
var opts = new EnumerationOptions
{
    RecurseSubdirectories = true,
    IgnoreInaccessible    = true,                     // swallow per-entry UnauthorizedAccessException
    AttributesToSkip      = FileAttributes.Hidden | FileAttributes.System,
    MatchType             = MatchType.Simple,         // Simple (DOS) or Win32
    MatchCasing           = MatchCasing.PlatformDefault,
    ReturnSpecialDirectories = false,
    MaxRecursionDepth     = int.MaxValue,
};
foreach (var f in Directory.EnumerateFiles(root, "*.log", opts)) { /* ... */ }
```

Wildcards: `*` matches any sequence within a single segment; `?` matches one char. **3-character extension quirk:** `*.htm` matches both `*.htm` and `*.html` (Win32 quirk). Use `MatchType = MatchType.Simple` to avoid this. Searches are case-insensitive on Windows, sensitive on Linux unless overridden by `MatchCasing`.

A single `UnauthorizedAccessException` aborts the **entire** enumeration with `SearchOption.AllDirectories` unless `IgnoreInaccessible = true`. Defensive walk: top-level dirs in nested try/catch.

## `FileSystemWatcher`

Windows uses `ReadDirectoryChangesW`; Linux uses `inotify`; macOS uses `FSEvents`/`kqueue`. Per-platform behavior differs — events can be **coalesced**, **lost** under load (`Error` event on overflow), or duplicated.

```csharp
using var w = new FileSystemWatcher(@"C:\data")
{
    Filter = "*.json",
    IncludeSubdirectories = true,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
    InternalBufferSize = 64 * 1024,            // Windows; max 64 KB on non-network paths
    EnableRaisingEvents = true,
};
w.Created += (s, e) => { };
w.Changed += (s, e) => { };
w.Deleted += (s, e) => { };
w.Renamed += (s, e) => { };
w.Error   += (s, e) => Log(e.GetException());
```

Editors save via temp+rename → expect `Created` then `Renamed`, not `Changed`. A single user-visible save commonly fires multiple events. Debounce.
