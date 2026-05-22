# .NET conventions — C# style

Targets latest stable **.NET 10 / C# 14**. Primary constructors, collection expressions, raw string literals, required properties, extension members, `field` keyword.

## Final topics

| Rule | File |
|---|---|
| `sealed` by default (all classes; unseal only when inheritance is the intent) | [sealed-by-default.md](sealed-by-default.md) |
| `readonly record struct` for value objects | [readonly-record-struct.md](readonly-record-struct.md) |
| `IAsyncEnumerable<T>` for streaming results | [iasyncenumerable-streaming.md](iasyncenumerable-streaming.md) |
| `ValueTask` where allocation matters | [valuetask.md](valuetask.md) |
| `TimeProvider` for date/time (never `DateTime.UtcNow` directly) | [time-provider.md](time-provider.md) |
| `Guid.CreateVersion7()` for new IDs (sortable, time-ordered) | [guid-createversion7.md](guid-createversion7.md) |
| Application return values use the hexagonal `Result` base + `ErrorCode` enum — see `dotnet-hexagonal-architecture` § interface-and-bases | (cross-skill) |
| English-only code, comments, identifiers, commit messages | [english-only.md](english-only.md) |
| `dotnet` CLI for all project/solution/package management (never edit `.csproj`/`.sln` manually) | [dotnet-cli-only.md](dotnet-cli-only.md) |
| Async naming, never `.Result` / `.Wait()` | [async-hygiene.md](async-hygiene.md) |

## See also

- [../forbidden-patterns/no-datetime-utcnow.md](../forbidden-patterns/no-datetime-utcnow.md)
- [../forbidden-patterns/no-non-v7-guids.md](../forbidden-patterns/no-non-v7-guids.md)
- [../source-generators/index.md](../source-generators/index.md)
