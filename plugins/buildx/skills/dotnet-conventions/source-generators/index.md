# .NET conventions — Source generators

Source generators ship with the BCL or are first-party Microsoft libraries. **No third-party generators** are accepted as a cross-cutting standard. For mapping, use hand-written `IXxxMapper` services — see [../forbidden-patterns/no-automapper-no-mediatr.md](../forbidden-patterns/no-automapper-no-mediatr.md) and `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers.

The two generators below are built into the BCL — AOT-friendly, faster startup, build-time safety.

## Final topics

| Purpose | Generator | Replaces | File |
|---|---|---|---|
| Logging | `LoggerMessage` attribute (built-in) | Interpolated strings in `logger.Log*` | [loggermessage.md](loggermessage.md) |
| JSON (de)serialization | `JsonSerializerContext` (built-in) | Runtime reflection-based `JsonSerializer` | [jsonserializercontext.md](jsonserializercontext.md) |

## See also

- [../forbidden-patterns/no-automapper-no-mediatr.md](../forbidden-patterns/no-automapper-no-mediatr.md)
- `dotnet-hexagonal-architecture` § core-and-infrastructure § Mappers
