# Streaming, `Utf8JsonReader` / `Utf8JsonWriter`, Newtonsoft Migration

Async streaming patterns, NDJSON, low-level UTF-8 reader/writer, and the Newtonsoft.Json → STJ migration table. Load when streaming large payloads, hand-rolling JSON over `Utf8JsonReader/Writer`, or porting Newtonsoft code.

## Streaming

```csharp
await using var stream = File.Create("data.json");
await JsonSerializer.SerializeAsync(stream, value, AppJsonContext.Default.Forecast, ct);

await using var read = File.OpenRead("data.json");
var v = await JsonSerializer.DeserializeAsync(read, AppJsonContext.Default.Forecast, ct);

// Element-at-a-time over a root-level JSON array.
await foreach (Order? o in JsonSerializer.DeserializeAsyncEnumerable<Order>(stream, options, ct))
    if (o is not null) await Process(o);

// NDJSON / JSON Lines (.NET 9+).
await foreach (var ev in JsonSerializer.DeserializeAsyncEnumerable<Event>(
    stream, topLevelValues: true, options, ct))
    Process(ev);
```

ASP.NET Core minimal API auto-streams an `IAsyncEnumerable<T>` returned from a handler; client-side consume with `client.GetFromJsonAsAsyncEnumerable<T>(...)`.

### NDJSON streaming recipe

```csharp
await foreach (var ev in JsonSerializer.DeserializeAsyncEnumerable<Event>(
    networkStream, topLevelValues: true, AppJsonContext.Default.Event, ct))
    await sink.WriteAsync(ev, ct);
```

## `Utf8JsonReader` / `Utf8JsonWriter`

```csharp
var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
{
    Indented = true,
    IndentCharacter = ' ',          // .NET 9+
    IndentSize = 2,                 // .NET 9+
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n"                  // .NET 9+
});

writer.WriteStartObject();
writer.WriteString("name", "Ada");
writer.WriteNumber("age", 36);
writer.WriteStartArray("tags");
writer.WriteStringValue("alpha");
writer.WriteEndArray();
writer.WriteRawValue("""{ "verbatim": true }""");      // splice raw JSON
writer.WriteEndObject();
writer.Flush();
byte[] utf8 = buffer.WrittenSpan.ToArray();

ReadOnlySpan<byte> json = "..."u8;
var reader = new Utf8JsonReader(json, new JsonReaderOptions
{
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip,
    MaxDepth = 64
});
while (reader.Read())
{
    switch (reader.TokenType)
    {
        case JsonTokenType.PropertyName: var name = reader.GetString(); break;
        case JsonTokenType.String:       var v = reader.ValueSpan; break;
        case JsonTokenType.Number:       if (reader.TryGetInt64(out var l)) { } break;
    }
}
```

`Utf8JsonReader` operates over `ReadOnlySpan<byte>` or `ReadOnlySequence<byte>` (multi-segment). Methods include `GetString`, `GetBytesFromBase64`, `GetBoolean`, `GetByte`, `GetInt32/64/UInt64`, `GetSingle/Double/Decimal`, `GetGuid`, `GetDateTime`/`GetDateTimeOffset`, `TryGet*` siblings, `GetComment`, `ValueSpan`, `ValueSequence`, `ValueTextEquals(...)`, `Skip` / `TrySkip`.

`Utf8JsonReader` is a `ref struct` — cannot be stored in heap fields, captured in lambdas, or used in iterators.

## Patch operations on a mutable DOM

```csharp
JsonNode root = JsonNode.Parse(json)!;
root["user"]!["age"] = (int)root["user"]!["age"]! + 1;
root["timestamps"] ??= new JsonArray();
((JsonArray)root["timestamps"]!).Add(DateTimeOffset.UtcNow);
string updated = root.ToJsonString();
```

`JsonDocument` is `IDisposable`; wrap in `using` (it's pooled).

## Custom `DateTime` as Unix epoch

```csharp
public sealed class UnixSecondsConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        => DateTimeOffset.FromUnixTimeSeconds(r.GetInt64());
    public override void Write(Utf8JsonWriter w, DateTimeOffset v, JsonSerializerOptions o)
        => w.WriteNumberValue(v.ToUnixTimeSeconds());
}
```

## Migration: Newtonsoft.Json → STJ

| Newtonsoft.Json | STJ |
|---|---|
| `[JsonProperty("x")]` | `[JsonPropertyName("x")]` |
| `[JsonProperty(Required = Required.Always)]` | `[JsonRequired]` or C# `required` |
| `[JsonIgnore(NullValueHandling.Ignore)]` | `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` |
| `[JsonObject(...)]` | **No type-level equivalent.** Configure via global options or per-property. |
| `[OnSerializing/Serialized/Deserializing/Deserialized]` | Implement `IJsonOnSerializing/Serialized/Deserializing/Deserialized` |
| `ContractResolver = CamelCasePropertyNamesContractResolver` | `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` |
| `NullValueHandling.Ignore` | `DefaultIgnoreCondition = WhenWritingNull` |
| `DefaultValueHandling.Ignore` | `DefaultIgnoreCondition = WhenWritingDefault` |
| `Formatting.Indented` | `WriteIndented = true` |
| `MissingMemberHandling.Error` | `UnmappedMemberHandling = Disallow` |
| `PreserveReferencesHandling.All` | `ReferenceHandler = ReferenceHandler.Preserve` |
| `ReferenceLoopHandling.Ignore` | `ReferenceHandler = ReferenceHandler.IgnoreCycles` (replaces with `null`) |
| `TypeNameHandling` | **No equivalent.** Use `[JsonDerivedType]`. |
| `DateFormatString`, `DateTimeZoneHandling` | **No equivalent.** ISO 8601 only — write a custom converter. |
| `Comments` | `ReadCommentHandling = Skip` |
| `ObjectCreationHandling.Reuse` | `PreferredObjectCreationHandling = Populate` |
| `JsonConvert.PopulateObject(json, target)` | **No equivalent.** Custom converter or property-level `Populate`. |
| `TraceWriter` | **No equivalent.** STJ has no logging hooks. |
| `TypeNameAssemblyFormatHandling` | **Not supported by design** (security). |
| `JObject` / `JArray` / `JToken` / `JValue` | `JsonObject` / `JsonArray` / `JsonNode` / `JsonValue` (mutable) or `JsonElement` (read-only). |
| `JsonTextReader` / `JsonTextWriter` | `Utf8JsonReader` (ref struct, UTF-8) / `Utf8JsonWriter`. |

Key behavior gaps to verify after migration:
- STJ does not honor `[Serializable]`, `[DataContract]`, `[DataMember]`. Use STJ attributes.
- STJ ignores fields by default (Newtonsoft serializes them).
- STJ is case-sensitive by default; web defaults flip this.
- STJ throws on quoted numbers without `NumberHandling.AllowReadingFromString`.
