# Compression, Zip, Tar, Globbing, Low-Level Enumeration

## Compression

| Stream | Format | Use |
|---|---|---|
| `DeflateStream` | RFC 1951 raw deflate | Inside other formats. |
| `GZipStream` | RFC 1952 (deflate + gzip header/trailer) | `.gz` files; widely interchangeable. |
| `ZLibStream` (.NET 6+) | RFC 1950 (deflate + zlib header) | Wire formats requiring zlib (e.g., PNG `IDAT`). |
| `BrotliStream` | RFC 7932 | HTTP `br`; smaller than gzip at comparable speed. |

All four: `Stream` derivatives but **not seekable**, no `Length`/`Position`. Constructors `(Stream, CompressionMode)` for decompress; `(Stream, CompressionLevel[, leaveOpen])` for compress. `CompressionLevel`: `Optimal` / `Fastest` / `NoCompression` / `SmallestSize` (.NET 6+). Disposing the compress stream finalizes the trailer — required for valid output.

```csharp
await using (var src = File.OpenRead("a.txt"))
await using (var dst = File.Create("a.txt.gz"))
await using (var gz  = new GZipStream(dst, CompressionLevel.SmallestSize))
    await src.CopyToAsync(gz);

// In-memory single-shot Brotli (no stream allocation)
ReadOnlySpan<byte> input = ...;
Span<byte> output = stackalloc byte[BrotliEncoder.GetMaxCompressedLength(input.Length)];
BrotliEncoder.TryCompress(input, output, out int written, quality: 4, window: 22);
```

## Zip (`ZipFile`, `ZipArchive`, `ZipArchiveEntry`)

```csharp
ZipFile.CreateFromDirectory(@".\src", @".\out.zip", CompressionLevel.Optimal, includeBaseDirectory: false);
ZipFile.ExtractToDirectory(@".\out.zip", @".\dst", overwriteFiles: true);

using (var fs  = new FileStream("pkg.zip", FileMode.Open, FileAccess.ReadWrite))
using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
{
    var entry = zip.CreateEntry("Readme.txt", CompressionLevel.Optimal);
    using var w = new StreamWriter(entry.Open());
    w.WriteLine("Generated " + DateTimeOffset.UtcNow);
}

using (var zip = ZipFile.OpenRead("pkg.zip"))
    foreach (var e in zip.Entries)
        Console.WriteLine($"{e.FullName} {e.Length}/{e.CompressedLength}");
```

`ZipArchiveMode`: `Read` (sequential, low memory), `Create` (write-only sequential), `Update` (random access; loads central dir; needs seekable stream).

**Path traversal (Zip Slip)** — `ZipArchiveEntry.ExtractToFile` is **not** safe by itself when paths contain `..\`. Always validate:

```csharp
string baseDir = Path.GetFullPath(extractRoot);
if (!baseDir.EndsWith(Path.DirectorySeparatorChar)) baseDir += Path.DirectorySeparatorChar;
string dest = Path.GetFullPath(Path.Combine(baseDir, entry.FullName));
if (!dest.StartsWith(baseDir, StringComparison.Ordinal))
    throw new IOException("Zip entry outside extraction root: " + entry.FullName);
```

`ZipFile.ExtractToDirectory` performs this validation internally since .NET Core 2.0.

## Tar (.NET 7+, `System.Formats.Tar`)

| API | Purpose |
|---|---|
| `TarFile.CreateFromDirectory(srcDir, destPath_or_Stream, includeBaseDirectory)` + `Async` | Create POSIX-default tar. |
| `TarFile.ExtractToDirectory(srcPath_or_Stream, destDir, overwriteFiles)` + `Async` | Path-traversal-safe extraction. |
| `TarReader(Stream[, leaveOpen])` | Stream-by-stream entry iteration. |
| `TarWriter(Stream, TarEntryFormat[, leaveOpen])` | Custom entry composition. |
| `TarEntry` (`PaxTarEntry`, `GnuTarEntry`, `UstarTarEntry`, `V7TarEntry`) | Entry model. |
| `TarEntryFormat` | `V7` / `Ustar` / `Pax` (default) / `Gnu`. |

```csharp
await using (var fs = File.Create("out.tar.gz"))
await using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
    await TarFile.CreateFromDirectoryAsync(@".\payload", gz, includeBaseDirectory: false);

await using var reader = new TarReader(File.OpenRead("a.tar"));
while (await reader.GetNextEntryAsync() is { } entry)
    if (entry.EntryType == TarEntryType.RegularFile)
        await entry.ExtractToFileAsync(Path.Combine("out", entry.Name), overwrite: true);
```

## Globbing — `Microsoft.Extensions.FileSystemGlobbing`

NuGet: `Microsoft.Extensions.FileSystemGlobbing`. `Matcher` supports `**`, `*`, `?`, character classes, `!` negation.

```csharp
var matcher = new Matcher();
matcher.AddIncludePatterns(new[] { "**/*.cs", "**/*.csproj" });
matcher.AddExcludePatterns(new[] { "**/bin/**", "**/obj/**" });

var dir = new DirectoryInfoWrapper(new DirectoryInfo(root));
PatternMatchingResult res = matcher.Execute(dir);
foreach (FilePatternMatch m in res.Files) Console.WriteLine(m.Path);
```

## Low-level enumeration — `System.IO.Enumeration`

`FileSystemEnumerator<T>` is the no-allocation engine that powers `Directory.Enumerate*`. Subclass to get callback hooks.

| Override | Signature |
|---|---|
| `ShouldIncludeEntry(ref FileSystemEntry)` | Filter results; entry is a ref struct (no allocation). |
| `ShouldRecurseIntoEntry(ref FileSystemEntry)` | Gate recursion. |
| `TransformEntry(ref FileSystemEntry) → T` | Project entry. |
| `OnDirectoryFinished(ROS<char>)` | Per-directory cleanup. |
| `ContinueOnError(int errorCode)` | Suppress an OS error. |

`FileSystemEntry` (ref struct): `FileName` (`ROS<char>`), `Directory`, `RootDirectory`, `Attributes`, `Length`, `CreationTimeUtc`, `LastAccessTimeUtc`, `LastWriteTimeUtc`, `IsDirectory`, `IsHidden`, `ToFullPath()`, `ToSpecifiedFullPath()`.

`FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase)` and `MatchesWin32Expression(...)` are the same matchers used internally.

```csharp
public static IEnumerable<string> FindRecentDlls(string root) =>
    new FileSystemEnumerable<string>(
        root,
        (ref FileSystemEntry e) => e.ToFullPath(),
        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
    {
        ShouldIncludePredicate = (ref FileSystemEntry e) =>
            !e.IsDirectory
            && FileSystemName.MatchesSimpleExpression("*.dll", e.FileName)
            && (DateTime.UtcNow - e.LastWriteTimeUtc).TotalHours < 24,

        ShouldRecursePredicate = (ref FileSystemEntry e) =>
            !e.FileName.SequenceEqual("node_modules") &&
            !e.FileName.SequenceEqual("bin") &&
            !e.FileName.SequenceEqual("obj"),
    };
```
