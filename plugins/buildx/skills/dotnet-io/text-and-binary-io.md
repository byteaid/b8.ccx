# Text I/O, Binary I/O, Async, Buffered & Composed Streams

## Reading / writing text

```csharp
string body = File.ReadAllText("a.txt");                    // UTF-8 + BOM detection
string[] lines = File.ReadAllLines("a.txt");
IEnumerable<string> e = File.ReadLines("a.txt");            // lazy
await File.WriteAllTextAsync("a.txt", "hi", Encoding.UTF8);
await File.WriteAllLinesAsync("a.txt", new[] { "a", "b" });
await File.AppendAllTextAsync("a.log", $"{DateTimeOffset.UtcNow:O} ok\n");
```

`File.ReadAllText`/`ReadAllLines`/`ReadLines` open with **UTF-8 with BOM detection** by default. Recognized BOMs: UTF-8 EF BB BF, UTF-16 LE/BE, UTF-32 LE/BE.

`File.WriteAllText("path", text)` (no encoding arg) writes UTF-8 **without BOM**. Pass `new UTF8Encoding(true)` to emit BOM.

### `StreamReader` / `StreamWriter`

```csharp
using var r = new StreamReader("a.txt", Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                               bufferSize: 4096, leaveOpen: false);
string? line;
while ((line = await r.ReadLineAsync()) != null) Process(line);

await foreach (string l in r.ReadLinesAsync()) Process(l);   // .NET 7+

using var w = new StreamWriter("a.txt", append: false, new UTF8Encoding(false))
{
    AutoFlush = false,
    NewLine   = "\n",   // override Environment.NewLine
};
await w.WriteLineAsync("hello");
```

### Encoding pitfalls

- **Default is UTF-8** since .NET Core (was system ANSI on .NET Framework). Porting code may now succeed where it previously corrupted, or vice versa.
- `Encoding.Default` returns UTF-8 in .NET Core+. To get the system code page on Windows .NET 5+, register `CodePagesEncodingProvider`:
  ```csharp
  Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
  var ansi = Encoding.GetEncoding(1252);
  ```
- Default `Encoding.UTF8` instance emits BOM. `new UTF8Encoding(false)` is BOM-less.
- `StreamReader` detects BOM only with `detectEncodingFromByteOrderMarks: true` (default for the path-taking ctor).
- Mixing `\r\n` vs `\n`: `StreamWriter.NewLine` controls writes; `StreamReader.ReadLine` accepts both.

## Reading / writing bytes

```csharp
byte[] all = File.ReadAllBytes("a.bin");
await File.WriteAllBytesAsync("b.bin", all);

using var fs = new FileStream("a.bin", FileMode.Open, FileAccess.Read,
                              FileShare.Read, 0, FileOptions.Asynchronous | FileOptions.SequentialScan);
byte[] buf = ArrayPool<byte>.Shared.Rent(81920);
try
{
    int n;
    while ((n = await fs.ReadAsync(buf.AsMemory())) != 0)
        Process(buf.AsSpan(0, n));
}
finally { ArrayPool<byte>.Shared.Return(buf); }
```

`BinaryReader`/`BinaryWriter` is little-endian on .NET Core with `Read7BitEncodedInt`-prefixed UTF-8 strings. For new on-the-wire formats prefer `BinaryPrimitives` (`System.Buffers.Binary`) for explicit endianness.

## Async file I/O

- `*Async` methods are real async **only when** the `FileStream` was opened with `FileOptions.Asynchronous`. `FileStream.IsAsync` exposes the flag.
- All `*Async` methods accept a `CancellationToken`. On Windows, cancellation of an in-flight overlapped I/O is best-effort (`CancelIoEx`).
- Avoid `Task.Run(() => fs.Read(...))` — use `ReadAsync` directly.
- `Flush(true)` issues `FlushFileBuffers` (Windows) / `fsync` (Unix); `FlushAsync` does **not** by itself.
- Use `await using` for async-friendly disposal.

```csharp
async Task CopyAsync(string src, string dst, CancellationToken ct)
{
    var srcOpts = new FileStreamOptions {
        Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan, BufferSize = 0
    };
    var dstOpts = new FileStreamOptions {
        Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None,
        Options = FileOptions.Asynchronous, BufferSize = 0,
        PreallocationSize = new FileInfo(src).Length,
    };
    await using var s = new FileStream(src, srcOpts);
    await using var d = new FileStream(dst, dstOpts);
    await s.CopyToAsync(d, 81_920, ct);
}
```

## `BufferedStream` & composing streams

`BufferedStream` (default 4096 bytes) is for streams **without** internal buffering (`NetworkStream`, raw `PipeStream`). `FileStream`, `MemoryStream`, and compression streams already buffer — wrapping rarely helps, may hurt.

`FileStream` `BufferSize: 0` disables its internal buffer — useful for span-sized aligned reads or when you'll wrap with another buffer.

Streams are composable via decorators:
- **Read pipeline:** `BaseStream → DecompressStream → DecryptStream → StreamReader.ReadToEnd()`
- **Write pipeline:** `StreamWriter → EncryptStream → CompressStream → BaseStream`

```csharp
await using var file   = File.OpenRead("data.gz.enc");
await using var gunzip = new GZipStream(file, CompressionMode.Decompress);
using var aes          = Aes.Create(); aes.Key = key; aes.IV = iv;
await using var dec    = new CryptoStream(gunzip, aes.CreateDecryptor(), CryptoStreamMode.Read);
using var rdr          = new StreamReader(dec, Encoding.UTF8);
string contents = await rdr.ReadToEndAsync();
```

Disposing the outermost stream cascades. Each decorator owns the underlying by default; pass `leaveOpen: true` to override.
