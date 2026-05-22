# `System.Text.Json` — Core API and Options

Core `JsonSerializer` API surface, namespace map, defaults, and `JsonSerializerOptions`. Load when picking JSON option sets, configuring naming/encoding, comparing to Newtonsoft defaults, or wiring property-level customization.

## Namespaces

| Namespace | Purpose |
|---|---|
| `System.Text.Json` | `JsonSerializer`, `JsonSerializerOptions`, `JsonDocument`, `JsonElement`, `Utf8JsonReader`, `Utf8JsonWriter`, `JsonException`. |
| `System.Text.Json.Serialization` | Attributes, `JsonConverter<T>`, `JsonConverterFactory`, `ReferenceHandler`, `JsonSerializerContext`, `JsonStringEnumConverter<TEnum>`. |
| `System.Text.Json.Serialization.Metadata` | Contract model: `JsonTypeInfo`, `JsonPropertyInfo`, `DefaultJsonTypeInfoResolver`, `JsonPolymorphismOptions`. |
| `System.Text.Json.Nodes` | Mutable DOM: `JsonNode`, `JsonObject`, `JsonArray`, `JsonValue`. |
| `System.Net.Http.Json` | `HttpClient` extensions (`GetFromJsonAsync`, `PostAsJsonAsync`, `GetFromJsonAsAsyncEnumerable`). |
| `System.Text.Json.Schema` (.NET 9+) | `JsonSchemaExporter`. |

## `JsonSerializer` API surface

| Group | Methods |
|---|---|
| Sync serialize | `Serialize<TValue>(value, ...)`, `Serialize(object?, Type, ...)`, `Serialize(Utf8JsonWriter, ...)`, `Serialize(Stream, ...)`. |
| UTF-8 serialize | `SerializeToUtf8Bytes(...)` (5–10% faster — skips UTF-16 round-trip). |
| DOM serialize | `SerializeToDocument` / `SerializeToElement` / `SerializeToNode`. |
| Async serialize | `SerializeAsync(Stream\|PipeWriter, ...)`. |
| Sync deserialize | `Deserialize<T>(string \| ReadOnlySpan<char> \| ReadOnlySpan<byte> \| Stream \| Utf8JsonReader \| JsonDocument \| JsonElement \| JsonNode, ...)`. |
| Async deserialize | `DeserializeAsync(Stream\|PipeReader, ...)`. |
| Async streaming | `DeserializeAsyncEnumerable<T>(Stream\|PipeReader, ..., topLevelValues: false)`. |

```csharp
string json = JsonSerializer.Serialize(value);
byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value);
await JsonSerializer.SerializeAsync(stream, value, options, ct);

var v = JsonSerializer.Deserialize<Foo>(json);
var v2 = await JsonSerializer.DeserializeAsync<Foo>(stream, options, ct);

await foreach (Foo? item in JsonSerializer.DeserializeAsyncEnumerable<Foo>(stream, options, ct))
    Process(item);
```

## Defaults vs Newtonsoft.Json

| Behavior | STJ default | Newtonsoft default |
|---|---|---|
| Property-name match | Case-**sensitive** | Case-insensitive |
| Field serialization | **Ignored** | Serialized |
| Comments / trailing commas / single quotes / unquoted names | Throw | Allowed |
| `null` → non-nullable value type | Throw | Default-initialized |
| Quoted numbers | Throw (unless `NumberHandling`) | Allowed |
| Char escaping | Strict | Permissive |
| Polymorphism | Opt-in via `[JsonDerivedType]` | Automatic with `TypeNameHandling` |
| `object` deserialization | `JsonElement` | Inferred CLR type |
| Max depth | 64 | 64 |

ASP.NET Core overrides three defaults via `JsonSerializerDefaults.Web`.

## `JsonSerializerOptions`

### Construction

```csharp
JsonSerializerOptions defaults = JsonSerializerOptions.Default;       // immutable singleton
JsonSerializerOptions web      = JsonSerializerOptions.Web;           // .NET 9+ singleton
var webOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)   // any version
{
    WriteIndented = true
};
var copy = new JsonSerializerOptions(existingOptions);                // does NOT copy metadata cache
```

### Key properties

| Property | Default | Notes |
|---|---|---|
| `AllowOutOfOrderMetadataProperties` | `false` | Allow `$type`/`$id`/`$ref` not at start of object. May over-buffer on streaming. |
| `AllowTrailingCommas` | `false` | |
| `Converters` | empty | First matching `CanConvert` wins. |
| `DefaultBufferSize` | 16384 | Async stream buffer. |
| `DefaultIgnoreCondition` | `Never` | `Never` / `Always` / `WhenWritingDefault` / `WhenWritingNull`. |
| `DictionaryKeyPolicy` | `null` | Applied **only on serialization** of dictionary keys. |
| `Encoder` | strict default | See § Character encoding. |
| `IgnoreReadOnlyFields` / `IgnoreReadOnlyProperties` | `false` | |
| `IncludeFields` | `false` | Public fields (and `[JsonInclude]` non-public). |
| `IndentCharacter` / `IndentSize` / `NewLine` | `' '` / `2` / `Environment.NewLine` | .NET 9+. |
| `MaxDepth` | 64 | |
| `NumberHandling` | `Strict` | `AllowReadingFromString`, `WriteAsString`, `AllowNamedFloatingPointLiterals` (bit flags). |
| `PreferredObjectCreationHandling` | `Replace` | `Populate` reuses existing instances. |
| `PropertyNameCaseInsensitive` | `false` (web: `true`) | |
| `PropertyNamingPolicy` | `null` (web: `CamelCase`) | |
| `ReadCommentHandling` | `Disallow` | `Skip` / `Allow` (`Allow` only with `Utf8JsonReader`). |
| `ReferenceHandler` | `null` (throw) | `Preserve` / `IgnoreCycles` / custom. |
| `RespectNullableAnnotations` | `false` | .NET 9+. Treat non-null NRT as required-non-null. |
| `RespectRequiredConstructorParameters` | `false` | .NET 9+. |
| `TypeInfoResolver` / `TypeInfoResolverChain` | reflection (when enabled) | Source-gen contexts plug in here. |
| `UnknownTypeHandling` | `JsonElement` | Or `JsonNode`. |
| `UnmappedMemberHandling` | `Skip` | `Disallow` throws on extras. |
| `WriteIndented` | `false` | |

### Web defaults (`JsonSerializerDefaults.Web`)

| Property | Web value |
|---|---|
| `PropertyNameCaseInsensitive` | `true` |
| `PropertyNamingPolicy` | `JsonNamingPolicy.CamelCase` |
| `NumberHandling` | `AllowReadingFromString` |

ASP.NET Core also defaults `MaxDepth` to **32** via the framework `JsonOptions`.

## Property customization

### Attributes

| Attribute | Effect |
|---|---|
| `[JsonPropertyName("foo")]` | Override JSON name; both directions; **does not** affect ctor parameter matching. |
| `[JsonIgnore]` | Skip; `Condition = WhenWritingNull \| WhenWritingDefault \| Always \| Never`. |
| `[JsonInclude]` | Include non-public accessors and explicitly-included fields. |
| `[JsonRequired]` | Throw on deserialize if absent; equivalent of C# `required`. |
| `[JsonPropertyOrder(int)]` | Lower writes first; default `0`; ties → declaration order. |
| `[JsonExtensionData]` | `Dictionary<string, object>` / `<string, JsonElement>` catches unmapped properties. |
| `[JsonConverter(typeof(C))]` | Per-property, per-type, or per-enum. |
| `[JsonConstructor]` | Pick parameterized ctor for deserialize. |
| `[JsonNumberHandling]` | Per-type override. |
| `[JsonObjectCreationHandling(Populate)]` | Reuse pre-initialized property (.NET 8+). |
| `[JsonUnmappedMemberHandling(...)]` | Per-type policy (.NET 8+). |
| `[JsonStringEnumMemberName("Partly cloudy")]` | Custom string for enum member (.NET 9+). |
| `[JsonDerivedType(typeof(T), discriminator?)]` / `[JsonPolymorphic(...)]` | Polymorphism. |

### Naming policies

| Policy | `TempCelsius` → |
|---|---|
| `CamelCase` | `tempCelsius` |
| `KebabCaseLower` (.NET 8+) | `temp-celsius` |
| `KebabCaseUpper` (.NET 8+) | `TEMP-CELSIUS` |
| `SnakeCaseLower` (.NET 8+) | `temp_celsius` |
| `SnakeCaseUpper` (.NET 8+) | `TEMP_CELSIUS` |

`DictionaryKeyPolicy` applies **only on write**; on read, dictionary keys come straight from JSON.

```csharp
public sealed class UpperCasePolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) => name.ToUpperInvariant();
}
```

## Required & init properties, constructors

Three ways to mark required:

```csharp
public class Person
{
    public required string Name { get; set; }    // C# 11 required modifier
    [JsonRequired] public string Email { get; init; }
    public int Age { get; set; }
}
```

Both map to `JsonPropertyInfo.IsRequired = true`. Use `[JsonRequired]` when source-gen mode (compile-time ordering with `required` is awkward), non-C# language, or when the requirement should be JSON-only.

### Non-optional ctor parameters (.NET 9+)

```csharp
record Person(string Name, int? Age = null);
var opts = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
JsonSerializer.Deserialize<Person>("""{"Age":42}""", opts); // throws — Name absent
```

Global feature switch:

```xml
<RuntimeHostConfigurationOption
    Include="System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault"
    Value="true" />
```

### `[JsonConstructor]` and records

- Ctor parameter names match JSON properties **case-insensitively** regardless of `PropertyNameCaseInsensitive`.
- `[JsonPropertyName]` does **not** rename ctor parameters.
- Use `[JsonInclude]` to surface non-public setters/getters.

### `RespectNullableAnnotations` (.NET 9+)

When `true`, non-nullable reference type properties are treated as **required-non-null**: a JSON `null` triggers `JsonException`. Off by default for back-compat.

## Built-in types

- Primitives + numerics: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `Half`, `float`, `double`, `decimal`, `string`, `char`, `Guid`, `Uri`, `Version`, `Int128`/`UInt128` (.NET 8+).
- `DateTime`/`DateTimeOffset`/`DateOnly`/`TimeOnly` — ISO 8601 round-trip preserving `Kind`.
- `TimeSpan` — `c` round-trip format.
- `Guid` — always `D` format on read.
- Enums — numeric by default. Use `JsonStringEnumConverter<TEnum>` for AOT-safe string conversion. `[Flags]` enums serialize as comma-separated string. `[JsonStringEnumMemberName(...)]` (.NET 9+) for custom names.
- Collections: arrays, `List<T>`, `IEnumerable<T>` and read-only/list/collection variants, `Stack<T>`, `Queue<T>`, `LinkedList<T>`, `HashSet<T>`, `SortedSet<T>`, `Dictionary<TKey,TValue>`, `ImmutableArray<T>`, `ConcurrentBag/Queue/Stack/Dictionary`, `ObservableCollection<T>`. Dictionary keys may be: `string`, all numerics, `bool`, `Guid`, `Enum`, `DateTime`/`DateTimeOffset`, `Uri`, `Version`.

`NumberHandling = AllowNamedFloatingPointLiterals` accepts `"NaN"` / `"Infinity"` / `"-Infinity"`.

Not built-in (workaround required): `DataTable`, `DataSet`, `ExpandoObject` (use `JsonNode`), `TimeZoneInfo`, `BigInteger`, `DBNull`, `System.Type` (security), `ValueTuple`.

```csharp
public sealed class BigIntegerConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        return BigInteger.Parse(doc.RootElement.GetRawText(), CultureInfo.InvariantCulture);
    }
    public override void Write(Utf8JsonWriter w, BigInteger v, JsonSerializerOptions o)
        => w.WriteRawValue(v.ToString(CultureInfo.InvariantCulture));
}
```

## Character encoding & encoder safety

`JavaScriptEncoder.Default` allows only `BasicLatin`; everything else escapes to `\uXXXX`. Defense-in-depth against XSS / charset confusion.

```csharp
// Allow specific Unicode ranges
new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic)
};

// Allow all (relaxed) — does not escape <, >, &, '. UTF-8 only, trusted JSON parsers only.
new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

// Allow specific characters
var settings = new TextEncoderSettings();
settings.AllowRange(UnicodeRanges.BasicLatin);
settings.AllowCharacters('ж', 'а');
var opts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(settings) };
```

JSON encoder always escapes `\`, `"`, control chars regardless of allow list.

## Reference handling & cycles

Default: throws `JsonException` on cycle.

```csharp
// Newtonsoft-compatible $id / $ref / $values metadata
new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };

// Replace back-reference with JSON null. Lossy but clean for any parser.
new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };
```

`ReferenceHandler.Preserve` cannot preserve value types, immutable types, or arrays — they ignore `$id` on read; `$ref` on those throws. Uses `ReferenceEqualityComparer.Instance` to dedupe.

For cross-call persistence, write a custom `ReferenceResolver` + `ReferenceHandler`. Reset between top-level calls to avoid unbounded growth.

## Missing / unmapped members

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class Poco { public int Id { get; set; } }
JsonSerializer.Deserialize<Poco>("""{"Id":42, "X":1}"""); // throws

// Or capture into an extension data bucket:
public class Poco
{
    public int Id { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extras { get; set; }
}
```

Three configuration sites: type attribute, `JsonSerializerOptions.UnmappedMemberHandling`, `JsonTypeInfo.UnmappedMemberHandling`.

## Custom converters

| Pattern | Base | When |
|---|---|---|
| Basic | `JsonConverter<T>` | Single concrete type or closed generic. |
| Factory | `JsonConverterFactory` | `Enum`, open generics (`Dictionary<,>`, `List<>`). |

### Basic converter

```csharp
public sealed class DateOnlyMmddyyyyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateOnly.ParseExact(reader.GetString()!, "MM/dd/yyyy", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
}
```

Reader rules in `Read`:
- Reader is positioned on the **start token** of the value.
- Must exit on the **matching end token**. Read past the end → `JsonException("read too much or not enough")`.
- For object scopes loop `while (reader.Read())` and bail on `EndObject`.

### Factory converter

```csharp
public sealed class DictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && t.GetGenericArguments()[0].IsEnum;

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options)
    {
        Type[] args = t.GetGenericArguments();
        return (JsonConverter)Activator.CreateInstance(
            typeof(Inner<,>).MakeGenericType(args), options)!;
    }

    private sealed class Inner<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>>
        where TKey : struct, Enum
    {
        private readonly JsonConverter<TValue> _value;
        public Inner(JsonSerializerOptions options) =>
            _value = (JsonConverter<TValue>)options.GetConverter(typeof(TValue));

        public override Dictionary<TKey, TValue> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType != JsonTokenType.StartObject) throw new JsonException();
            var d = new Dictionary<TKey, TValue>();
            while (r.Read())
            {
                if (r.TokenType == JsonTokenType.EndObject) return d;
                var name = r.GetString()!;
                if (!Enum.TryParse(name, ignoreCase: true, out TKey key))
                    throw new JsonException($"Bad enum '{name}' for {typeof(TKey)}.");
                r.Read();
                d[key] = _value.Read(ref r, typeof(TValue), o)!;
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter w, Dictionary<TKey, TValue> d, JsonSerializerOptions o)
        {
            w.WriteStartObject();
            foreach (var (k, v) in d)
            {
                var name = k.ToString();
                w.WritePropertyName(o.PropertyNamingPolicy?.ConvertName(name) ?? name);
                _value.Write(w, v, o);
            }
            w.WriteEndObject();
        }
    }
}
```

### Registration precedence

Highest → lowest:
1. `[JsonConverter]` on the **property**.
2. `JsonSerializerOptions.Converters` (first matching `CanConvert`).
3. `[JsonConverter]` on the **type/enum/struct**.
4. Built-in.

(Different from Newtonsoft.Json, where the type-attribute beats the `Converters` list.)

### Errors

- Throw `JsonException` for malformed JSON. Path / line / byte position auto-filled.
- Throw `NotSupportedException` to signal a non-serializable type; path is appended.
- Other exceptions don't gain JSON-path info.

For value-type members, cache `options.GetConverter(typeof(T))` once in the converter ctor and call its `.Read`/`.Write` directly to avoid the boxing path through `JsonSerializer`.

## Polymorphism

```csharp
[JsonPolymorphic(
    TypeDiscriminatorPropertyName = "$type",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
    IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(WeatherBase), "base")]
[JsonDerivedType(typeof(WeatherWithCity), "withCity")]
[JsonDerivedType(typeof(WeatherWithSeries), 1)]      // int discriminator also OK
public class WeatherBase
{
    public DateTimeOffset Date { get; set; }
    public int TempC { get; set; }
}
```

Rules:
- `$type` (or custom) **must be the first property** by default. `AllowOutOfOrderMetadataProperties = true` relaxes that — caution: streaming buffers the whole object.
- Without a discriminator, polymorphism is **serialize-only**.
- Serialize must be invoked with the **base** as the static type (or via `JsonTypeInfo<TBase>`).
- `int` and `string` discriminators may coexist; mixing is discouraged.
- **Derived types do not inherit** their base's polymorphism — configure each level independently.
- Only types using the default object/collection/dictionary converters can use type discriminators.
- Source generation: **metadata mode supports polymorphism; fast-path does not.**

| `JsonUnknownDerivedTypeHandling` | Behavior |
|---|---|
| `FailSerialization` (default) | Throws `NotSupportedException`. |
| `FallBackToBaseType` | Writes using the declared base type's contract. |
| `FallBackToNearestAncestor` | Walks up looking for the nearest declared `JsonDerivedType`; ambiguous diamond throws. |

### Contract-model variant (no attributes)

```csharp
public class PolyResolver : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var info = base.GetTypeInfo(type, options);
        if (info.Type == typeof(BasePoint))
        {
            info.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$point-type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(ThreeDPoint), "3d"),
                    new JsonDerivedType(typeof(FourDPoint), "4d"),
                }
            };
        }
        return info;
    }
}
```

## Performance cheat sheet

| Technique | Gain |
|---|---|
| Reuse a single `JsonSerializerOptions` | Avoids re-warming the metadata cache (10–100× cold). |
| Source generation (`JsonSerializerContext`) | ~30% allocations, ~20% throughput, AOT-safe. |
| `SerializeToUtf8Bytes` instead of `Serialize` (string) | 5–10% throughput, no UTF-16 detour. |
| `DeserializeAsyncEnumerable` for large arrays | O(1) memory vs O(n). |
| Pass `JsonTypeInfo<T>` directly | Skips `Type` lookup. |
| `JsonSourceGenerationMode.Serialization` (fast path) | ~50% on writes. |
| `WriteRawValue` for known-good JSON snippets | Skip re-validation. |
| `Encoder = UnsafeRelaxedJsonEscaping` | Skip per-char escape checks (trusted UTF-8 consumers only). |
| Raise `DefaultBufferSize` for big payloads | Fewer async pumps. |
