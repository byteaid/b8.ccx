---
name: dotnet-io
description: File and stream I/O reference for .NET 10. Covers `Path` cross-platform, `File`/`Directory`/`*Info` (helpers, `EnumerationOptions`, defensive walks, symlinks), `FileStream` + `FileStreamOptions` (modes, sharing, `FileOptions.Asynchronous`/`SequentialScan`/`RandomAccess`), `RandomAccess` positional IO via `SafeFileHandle`, `Stream` (`ReadExactly`/`ReadAtLeast`, span overloads), text I/O (`StreamReader`/`Writer`, BOM, UTF-8), binary (`BinaryPrimitives`), async + `fsync`, `BufferedStream`, compression (`GZip`/`Brotli`/`ZLib`, `ZipArchive` + Zip-Slip, `System.Formats.Tar`), `FileSystemWatcher`, `Matcher` globbing, `FileSystemEnumerable<T>`, error model (`IOException` + HRESULT, atomic writes), memory-mapped files, isolated storage, and `System.IO.Pipelines` (`PipeReader`/`Writer`, `AdvanceTo(consumed, examined)`, stream interop).
when_to_use: |
  - Trigger keywords: File.OpenHandle, FileStream, FileOptions.Asynchronous, RandomAccess, SafeFileHandle, EnumerationOptions, FileSystemEnumerable, FileSystemWatcher, Encoding.UTF8, BOM, BinaryPrimitives, BufferedStream, BrotliStream, ZipArchive, Zip Slip, TarFile, MemoryMappedFile, PipeReader, PipeWriter, AdvanceTo, atomic write, fsync, FileSystemGlobbing.Matcher.
  - Task shapes: open a file with the right options; parallel positional reads on a huge file; enumerate a tree resiliently; build an atomic write; debounce `FileSystemWatcher`; compress/archive/mitigate Zip Slip; design a `Pipe`-based protocol parser; choose UTF-8 with/without BOM.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs"]
---

# .NET File and Stream I/O — Reference

Reference for the `System.IO` surface and adjacent namespaces on .NET 10. Bytes only; bytes-to-objects belongs to `dotnet-serialization`.

## Mental model

| Need | Primitive |
|---|---|
| Read whole text file | `File.ReadAllTextAsync` |
| Stream lines, low memory | `File.ReadLinesAsync` (.NET 7+) or `StreamReader` |
| Read whole binary file | `File.ReadAllBytesAsync` |
| Sequential big binary | `FileStream(FileOptions.Asynchronous \| SequentialScan)` + `CopyToAsync` |
| Random access, single thread | `FileStream(FileOptions.RandomAccess)` |
| Parallel positional reads | `RandomAccess.ReadAsync` over `File.OpenHandle` |
| Random access huge file | `MemoryMappedFile.CreateFromFile` + view |
| In-process IPC shared memory | `MemoryMappedFile.CreateOrOpen` (named) |
| Compress on the wire / at rest | `BrotliStream` (preferred); `GZipStream` for `.gz` |
| Archive a folder | `ZipFile.CreateFromDirectory` or `TarFile.CreateFromDirectoryAsync` |
| Walk huge tree, low alloc | `FileSystemEnumerable<T>` / subclass `FileSystemEnumerator<T>` |
| Glob pattern matching | `Microsoft.Extensions.FileSystemGlobbing.Matcher` |
| Watch for changes | `FileSystemWatcher` (debounce!) |
| Streaming protocol parser | `System.IO.Pipelines` |
| Crypto on a stream | `CryptoStream` decorator |

## Non-negotiable rules

1. **`FileOptions.Asynchronous` is mandatory** for true async I/O. Without it, `ReadAsync`/`WriteAsync` fall back to `Task.Run`-wrapped sync I/O — same blocked thread.
2. **`RandomAccess` for parallel positional I/O.** `new FileStream(...).Position = off; ReadAsync(...)` is **not** safe across concurrent calls (mutates `Position`).
3. **`Stream.Read` may return short** even before EOF (e.g., on `NetworkStream`). Loop, or use `ReadExactly` (.NET 7+) which throws `EndOfStreamException` on short read.
4. **Validate every zip/tar entry** for path traversal (Zip Slip) before extracting. `ZipFile.ExtractToDirectory` and `TarFile.ExtractToDirectory*` do this internally; `ZipArchiveEntry.ExtractToFile` does not.
5. **Atomic writes:** write to a temp file in the **same directory**, `Flush(true)` (`fsync`), then `File.Move(tmp, path, overwrite: true)`.
6. **Default text encoding is UTF-8 in .NET Core+** (was system ANSI on .NET Framework). Pin an explicit encoding for untrusted input.
7. **`BinaryFormatter` is gone** (.NET 9+). For binary on the wire use `BinaryPrimitives` (`System.Buffers.Binary`) for explicit endianness, or a contract serializer.
8. **Use `await using`** for streams in async paths so `DisposeAsync` actually runs (compression streams flush trailers in dispose).
9. **`FileSystemWatcher` events are coalesced, lossy under load, and duplicated** by editors that save via temp+rename. Debounce in user code.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `Path` (Windows + Unix forms), `File`/`Directory`/`*Info`, `EnumerationOptions`, `FileSystemWatcher` | [paths-and-directories.md](paths-and-directories.md) | Manipulating paths; reading metadata; enumerating trees; reacting to file changes. |
| `Stream` capability triad, `FileStream` + `FileStreamOptions` + `FileOptions`, `RandomAccess` positional I/O | [streams-and-filestream.md](streams-and-filestream.md) | Choosing the right `FileStream` flags; doing parallel positional reads/writes. |
| Text I/O (UTF-8, BOM, `StreamReader`/`Writer`), binary I/O, async + `fsync`, `BufferedStream`, composing streams | [text-and-binary-io.md](text-and-binary-io.md) | Reading/writing text/bytes; async copy; layering compression + crypto streams. |
| Compression (`GZip`/`Brotli`/`ZLib`/`Deflate`), `ZipArchive` + Zip Slip, `System.Formats.Tar`, globbing `Matcher`, low-level `FileSystemEnumerable<T>` | [compression-and-archives.md](compression-and-archives.md) | Archiving / extracting; defending against path-traversal; glob-matching trees. |
| Error model (`IOException` + HRESULT mapping), retry on sharing violations, atomic writes, `MemoryMappedFile`, isolated storage | [errors-and-special-storage.md](errors-and-special-storage.md) | Mapping `HRESULT` to recovery; transactional file replace; mmap views; legacy isolated storage. |
| `System.IO.Pipelines` (`Pipe`, `PipeReader`/`PipeWriter`, `AdvanceTo` invariants, stream interop), append/log patterns, security checklist | [pipelines.md](pipelines.md) | Building a streaming protocol parser; high-throughput append logs; auditing for I/O security. |

## Cross-references

- Public docs (I/O overview): https://learn.microsoft.com/en-us/dotnet/standard/io/
- Public docs (`Path` formats): https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
- Public docs (Async file I/O): https://learn.microsoft.com/en-us/dotnet/standard/io/asynchronous-file-i-o
- Public docs (Composing streams): https://learn.microsoft.com/en-us/dotnet/standard/io/composing-streams
- Public docs (I/O errors): https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors
- Public docs (`RandomAccess`): https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess
- Public docs (`FileStream`): https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-io-filestream
- Public docs (`MemoryMappedFile`): https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile
- Public docs (`FileSystemEnumerable<T>`): https://learn.microsoft.com/en-us/dotnet/api/system.io.enumeration.filesystemenumerable-1
- Public docs (Compression): https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-compress-and-extract-files
- Public docs (Pipelines): https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines
- Public docs (.NET 8 Unix backslash break): https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/file-path-backslash
- Public docs (Globbing `Matcher`): https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.filesystemglobbing.matcher
- Related skill: `dotnet-serialization` — JSON/XML/binary serializers running on top of streams.
- Related skill: `dotnet-networking` — `NetworkStream`, `HttpClient` request/response streams, `PipeReader`-based protocol parsers.
- Related skill: `dotnet-garbage-collection` — LOH (`byte[]` ≥ 85 KB), pooled memory, why `ArrayPool<byte>` matters.
- Related skill: `dotnet-parallel-and-threading` — `Channel<T>` for producer/consumer alternative to `Pipe`.
