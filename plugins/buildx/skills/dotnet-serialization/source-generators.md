# `JsonSerializerContext` — Source Generation

Source-generated metadata for `System.Text.Json`. Load when enabling AOT, eliminating reflection for trim, wiring `JsonSerializerContext` into ASP.NET Core, or combining contexts.

## Modes

| Mode | What it generates | Limitations |
|---|---|---|
| `Metadata` | `JsonTypeInfo<T>` for the regular pipeline. | Full feature set. |
| `Serialization` (fast path) | Inline `Write(...)` with hand-rolled `Utf8JsonWriter` calls. | **Serialize only.** No polymorphism. No reference handling. Not used for async unless payload fits buffer. |
| `Default` | Both. | Default if `GenerationMode` not set. |

## Definition

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(List<WeatherForecast>))]
[JsonSerializable(typeof(MyDto), TypeInfoPropertyName = "MyDtoCustom")]
internal partial class AppJsonContext : JsonSerializerContext;
```

Rules:
- Class must be `partial` and inherit `JsonSerializerContext`.
- Must list collection types explicitly (`List<T>`, `T[]`, `Dictionary<K,V>`).
- Members typed as `object` need their **runtime types** also `[JsonSerializable]`-listed.
- Use `JsonStringEnumConverter<TEnum>` (generic) for AOT.
- `[JsonRequired]` instead of C# `required` keyword in source-gen mode.

## Calling

```csharp
// Direct via JsonTypeInfo<T> — fastest, AOT-safe.
string json = JsonSerializer.Serialize(forecast, AppJsonContext.Default.WeatherForecast);
WeatherForecast? f = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherForecast);

// Via context + Type.
string json2 = JsonSerializer.Serialize(forecast, typeof(WeatherForecast), AppJsonContext.Default);

// Via options.TypeInfoResolver (allows runtime-only converters).
var opts = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };
```

## Combining contexts

```csharp
var opts = new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(ContextA.Default, ContextB.Default)
};
opts.TypeInfoResolverChain.Add(ContextC.Default);
opts.TypeInfoResolverChain.Insert(0, ContextD.Default);
```

## Disable reflection (AOT / trimmed)

```xml
<PropertyGroup>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

Implicit when `PublishTrimmed=true`. Reflection-only call throws `InvalidOperationException`. Check programmatically:

```csharp
TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault
    ? new DefaultJsonTypeInfoResolver()
    : AppJsonContext.Default
```

The property is a link-time constant — the unused branch is trimmed.

## ASP.NET Core wiring

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>          // minimal APIs
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

builder.Services.AddControllers().AddJsonOptions(o =>   // MVC / Web API
    o.JsonSerializerOptions.TypeInfoResolverChain.Add(AppJsonContext.Default));
```

`HttpClientJsonExtensions.GetFromJsonAsync` / `PostAsJsonAsync` / `GetFromJsonAsAsyncEnumerable` all have overloads that accept a `JsonTypeInfo<T>` or `JsonSerializerContext`.

## Source-gen + AOT recipe

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Default,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(List<Order>))]
internal partial class AppJsonContext : JsonSerializerContext;
```

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```
