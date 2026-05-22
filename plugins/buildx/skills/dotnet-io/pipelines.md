# `System.IO.Pipelines`, Logs, Security

## `System.IO.Pipelines`

High-perf parsing/streaming primitive. Decouples producer (writer) from consumer (reader); manages buffer pooling, partial reads, and backpressure.

### `Pipe`

```csharp
var pipe = new Pipe(new PipeOptions(
    pool: MemoryPool<byte>.Shared,
    readerScheduler: PipeScheduler.ThreadPool,
    writerScheduler: PipeScheduler.ThreadPool,
    pauseWriterThreshold: 64 * 1024,
    resumeWriterThreshold: 32 * 1024,
    minimumSegmentSize: 4096,
    useSynchronizationContext: false));
PipeReader r = pipe.Reader;
PipeWriter w = pipe.Writer;
```

### Producer

```csharp
async Task FillAsync(Socket s, PipeWriter w, CancellationToken ct)
{
    while (true)
    {
        Memory<byte> mem = w.GetMemory(sizeHint: 512);
        int n = await s.ReceiveAsync(mem, SocketFlags.None, ct);
        if (n == 0) break;
        w.Advance(n);
        FlushResult fr = await w.FlushAsync(ct);    // honors pause/resume thresholds
        if (fr.IsCompleted) break;                  // reader done
    }
    await w.CompleteAsync();
}
```

### Consumer

```csharp
async Task DrainAsync(PipeReader r, CancellationToken ct)
{
    try
    {
        while (true)
        {
            ReadResult rr = await r.ReadAsync(ct);
            ReadOnlySequence<byte> buf = rr.Buffer;
            try
            {
                while (TryParseMessage(ref buf, out var msg))
                    await ProcessAsync(msg);

                if (rr.IsCompleted)
                {
                    if (!buf.IsEmpty) throw new InvalidDataException("truncated");
                    break;
                }
            }
            finally
            {
                r.AdvanceTo(buf.Start, buf.End);
            }
        }
    }
    finally { await r.CompleteAsync(); }
}
```

### `AdvanceTo(consumed, examined)` invariants

| Pass | Effect |
|---|---|
| `consumed = examined = buf.Start` | Buffered for next pass. |
| `consumed = buf.End`, `examined = buf.End` | All consumed; next `ReadAsync` waits for new data. |
| `consumed = buf.Start`, `examined = buf.End` | Nothing consumed but everything examined → next `ReadAsync` blocks until **more** data arrives. **Source of "hang on partial read" bugs** when only a partial message is in the buffer. |

### Cancellation

- `PipeReader.ReadAsync(CT)` honors token; cancels with `OperationCanceledException`.
- `PipeReader.CancelPendingRead()` causes pending/next `ReadAsync` to return `ReadResult { IsCanceled = true }` — non-exceptional.
- Symmetric: `PipeWriter.CancelPendingFlush()` / `FlushResult.IsCanceled`.

### Stream interop

```csharp
PipeReader r = PipeReader.Create(stream, new StreamPipeReaderOptions(
    pool: null, bufferSize: 4096, minimumReadSize: 1024, leaveOpen: true));
PipeWriter w = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true,
    minimumBufferSize: 4096));
Stream s = pipe.Reader.AsStream();   // inverse direction
```

### Pitfalls

- Using `ReadResult.Buffer` after `AdvanceTo` → undefined (pool may have reissued the memory).
- Forgetting `Complete` / `CompleteAsync` → memory leak.
- Treating `ReadResult.IsCompleted` as the exit condition on its own → may drop the final segment.
- `PipeScheduler.Inline` → easy deadlocks; use `ThreadPool` (default).
- Pipes are **not thread-safe**; one owner per side.

## Logs / append patterns

```csharp
using (StreamWriter w = File.AppendText("log.txt"))
    w.WriteLine($"{DateTimeOffset.UtcNow:O} | INFO | {message}");
```

For high-throughput logs use `FileMode.Append` + `FileShare.Read` + dedicated writer thread, or `System.IO.Pipelines` for batching. For application logging, prefer the loggers in `Microsoft.Extensions.Logging`.

## Security checklist

- Never trust input paths: combine with a base, `Path.GetFullPath`, then verify the result starts with the expected base.
- For zip/tar: validate each entry destination (Zip Slip).
- `FileShare.None` only when other readers must not coexist (e.g., atomic-write temp files).
- Pin an explicit encoding on untrusted text input — don't rely on `Encoding.Default`.
- Drop privileged file handles with `using` / `await using` ASAP; security check is at construction only.
- For machine-wide isolated storage, treat data as untrusted.
