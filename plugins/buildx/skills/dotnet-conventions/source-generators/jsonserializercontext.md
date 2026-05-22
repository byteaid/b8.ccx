# `JsonSerializerContext` — source-generated JSON

## Rule

Declare a `[JsonSerializable(typeof(T))]` `JsonSerializerContext` for every concrete DTO root that is serialized by ASP.NET Core, `HttpClient` JSON helpers, or background workers. Wire the context into JSON options. Never rely on the reflection-based `JsonSerializer.Serialize<T>(obj)` defaults in production code paths.

## Rationale

- **AOT-compatible** — Native AOT publish requires source-generated metadata; reflection-based serialization is rejected at trim time.
- **Faster startup** — no first-call reflection cache build; the metadata is already generated.
- **Smaller** — generated metadata is per-type, not the full reflection graph.
- **Trim-safe** — DTOs cannot be silently trimmed away because the context references them.
- **Build-time errors** — typos and missing types surface during build, not at first serialization.

## Canonical shape

`Program.cs` of the host:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, OrderJsonContext.Default));

builder.Services.Configure<JsonOptions>(o =>
    o.JsonSerializerOptions.TypeInfoResolverChain.Insert(0, OrderJsonContext.Default));
```

Context definition:

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(IReadOnlyList<OrderDto>))]
[JsonSerializable(typeof(OrderCreateRequest))]
[JsonSerializable(typeof(ProblemDetails))]
public sealed partial class OrderJsonContext : JsonSerializerContext;
```

Manual serialization (when needed):

```csharp
var json = JsonSerializer.Serialize(order, OrderJsonContext.Default.OrderDto);
var dto  = JsonSerializer.Deserialize(json, OrderJsonContext.Default.OrderDto);
```

## Conventions

- One context per **bounded module** (`OrderJsonContext`, `PaymentJsonContext`). Not a single `AppJsonContext` mega-class.
- The context lives in the project that owns the DTOs.
- Register every context with `TypeInfoResolverChain.Insert(0, ctx.Default)` so it takes priority over the default resolver.
- `[JsonSourceGenerationOptions]` carries the casing policy — never set `PropertyNamingPolicy` in two places.

## When reflection-based JSON is acceptable

- One-off diagnostic logging where AOT does not apply.
- Quick-and-dirty `dotnet`-script tools where the trim/AOT story is intentionally bypassed.

In a host that is published for production, always use the generated context.

## Enforcement

- **Code review:** flag `JsonSerializer.Serialize(obj)` / `Deserialize<T>` calls in host code that have no `JsonSerializerContext`.
- **AOT publish (`PublishAot=true`)** is the definitive enforcement — reflection-based serialization fails at publish time.
