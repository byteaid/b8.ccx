---
name: dotnet-serialization
description: Serialization reference for .NET 10 / C# 14. Primary focus is `System.Text.Json` (in-box, AOT-friendly): `JsonSerializer` API + `JsonSerializerOptions`/Web defaults, property customization (attributes, naming policies, `[JsonExtensionData]`), source generation via `JsonSerializerContext` (AOT, ASP.NET Core wiring), custom `JsonConverter<T>`/factory + registration precedence, polymorphism (`[JsonPolymorphic]`/`[JsonDerivedType]`, `$type`), required/init properties, `JavaScriptEncoder`, reference handling, unmapped-member handling, streaming (`DeserializeAsyncEnumerable`, NDJSON), low-level `Utf8JsonReader`/`Writer`, Newtonsoft→STJ migration. Also `XmlSerializer`, `DataContractSerializer`, `System.Formats.Cbor`, MessagePack-CSharp. `BinaryFormatter` removed (.NET 9+, `SYSLIB0011`).
when_to_use: |
  - Trigger keywords: JsonSerializer, JsonSerializerOptions, JsonSerializerContext, JsonConverter, JsonPolymorphic, JsonDerivedType, JsonExtensionData, JsonNode, Utf8JsonReader, ReferenceHandler, JsonNamingPolicy, UnmappedMemberHandling, DeserializeAsyncEnumerable, NDJSON, XmlSerializer, BinaryFormatter, SYSLIB0011, MessagePack, CBOR, Newtonsoft migration.
  - Task shapes: pick a JSON option set; cache `JsonSerializerOptions`; add a custom converter; configure polymorphism with discriminator; enable AOT via source-gen; stream a large array; consume NDJSON; migrate from Newtonsoft.Json; mutate a JSON DOM; pick MessagePack vs JSON; wire `JsonSerializerContext` into ASP.NET Core.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.csproj"]
---

# .NET Serialization — Reference

Reference for converting CLR object graphs to/from JSON, XML, and binary formats on .NET 10. Default is `System.Text.Json` (STJ) with source generation; only deviate with reason.

## Mental model

| Need | Use |
|---|---|
| New code, JSON, modern stack | `System.Text.Json` + source generation |
| Legacy XML / WCF data contracts | `DataContractSerializer` |
| Free-form XML (custom names, attributes, schema) | `XmlSerializer` |
| Compact binary on the wire | **MessagePack** (or CBOR for standardized binary) |
| Persisted-object / arbitrary-graph binary | **No built-in option** — design a domain DTO + JSON or MessagePack |
| Anything that imports `BinaryFormatter` | **Stop.** Removed in .NET 9+; throws `NotSupportedException` (SYSLIB0011). |

| Serializer | Namespace | AOT | Streaming | Status |
|---|---|---|---|---|
| `JsonSerializer` (source-gen) | `System.Text.Json` | yes | yes | Recommended default |
| `JsonSerializer` (reflection) | `System.Text.Json` | partial (warnings) | yes | Default unless disabled |
| `JsonNode` / `JsonDocument` / `JsonElement` | `System.Text.Json[.Nodes]` | yes | partial | Mutable / read-only DOMs |
| `Utf8JsonReader` / `Writer` | `System.Text.Json` | yes | yes | Low-level UTF-8 |
| `XmlSerializer` | `System.Xml.Serialization` | shim only | via stream/writer | Legacy |
| `DataContractSerializer` | `System.Runtime.Serialization` | reflection-based | yes | Legacy / WCF |
| `BinaryFormatter` | — | n/a | n/a | **Removed.** Do not use. |
| MessagePack-CSharp | NuGet `MessagePack` | yes (source-gen) | yes | High-throughput binary |
| `System.Formats.Cbor` | in-box | yes | reader/writer | Low-level CBOR |

## Non-negotiable rules

1. **Reuse a single `JsonSerializerOptions`.** First call locks the options and builds a metadata cache. Allocating per call is the #1 STJ perf bug. Cache statically (or DI-singleton).
2. **Source generation over reflection.** Faster cold start, ~30% lower allocations, AOT-safe, trim-friendly.
3. **AOT projects** must set `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>` (implicit when `PublishTrimmed=true`).
4. **`JsonStringEnumConverter<TEnum>` (generic) is AOT-safe.** The non-generic `JsonStringEnumConverter` is not.
5. **STJ does not honor `[Serializable]`, `[DataContract]`, `[DataMember]`.** Use STJ attributes.
6. **`JsonDocument` is `IDisposable`.** Wrap in `using` (it's pooled).
7. **`Utf8JsonReader` is a `ref struct`.** Cannot be stored in heap fields, captured in lambdas, or used in iterators.
8. **Polymorphism is opt-in.** Use `[JsonDerivedType]` + `[JsonPolymorphic]`; without a discriminator, polymorphism is serialize-only and round-trips as the base type.
9. **`BinaryFormatter` is removed.** SYSLIB0011 — migrate to JSON / MessagePack / CBOR / a hand-rolled `BinaryReader`/`Writer` schema.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `System.Text.Json` core (API surface, `JsonSerializerOptions`, attributes, naming, encoding, polymorphism, converters, required/init, built-in types, perf cheatsheet) | [system-text-json.md](system-text-json.md) | Picking JSON options, customizing properties, configuring polymorphism, writing custom converters. |
| Source generation (`JsonSerializerContext`, modes, AOT wiring, ASP.NET Core integration, combining contexts) | [source-generators.md](source-generators.md) | Enabling AOT, eliminating reflection for trim, wiring `JsonSerializerContext`. |
| Streaming, `Utf8JsonReader/Writer`, NDJSON, JSON DOM patches, Newtonsoft migration table | [streaming-and-utf8.md](streaming-and-utf8.md) | Streaming large payloads, hand-rolling UTF-8 JSON, porting Newtonsoft code. |
| XML — `XmlSerializer`, `DataContractSerializer` | [xml.md](xml.md) | Generating/parsing XML, WCF data contracts. |
| Binary formats — MessagePack, CBOR, Protobuf, `BinaryFormatter` removal | [binary-formats.md](binary-formats.md) | Picking a wire format, integrating MessagePack, low-level CBOR, migrating off `BinaryFormatter`. |

## Quick recipe — camel-case + indented + ignore null

```csharp
private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter<MyEnum>() }
};
```

## Quick recipe — polymorphic API DTOs

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EmailNotification), "email")]
[JsonDerivedType(typeof(SmsNotification), "sms")]
[JsonDerivedType(typeof(PushNotification), "push")]
public abstract class Notification { public string? RecipientId { get; set; } }
```

## Cross-references

- Public docs (STJ overview): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview
- Public docs (Source generation): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
- Public docs (Source generation modes): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes
- Public docs (Custom converters): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to
- Public docs (Polymorphism): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism
- Public docs (Required properties): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties
- Public docs (Reference handling): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/preserve-references
- Public docs (Missing members): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members
- Public docs (Custom contracts): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/custom-contracts
- Public docs (Supported types): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/supported-types
- Public docs (DOM use): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom
- Public docs (`Utf8JsonReader`): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader
- Public docs (`Utf8JsonWriter`): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter
- Public docs (Migrate from Newtonsoft): https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft
- Public docs (XML examples): https://learn.microsoft.com/en-us/dotnet/standard/serialization/examples-of-xml-serialization
- Public docs (`DataContractSerializer`): https://learn.microsoft.com/en-us/dotnet/api/system.runtime.serialization.datacontractserializer
- Public docs (`System.Formats.Cbor`): https://learn.microsoft.com/en-us/dotnet/api/system.formats.cbor
- Public repo (MessagePack-CSharp): https://github.com/MessagePack-CSharp/MessagePack-CSharp
- Related skill: `dotnet-formatting` — non-JSON `ToString`/`Parse` and culture handling.
- Related skill: `dotnet-io` — stream/buffer plumbing under the serializer.
- Related skill: `dotnet-networking` — `System.Net.Http.Json` extensions and HTTP wiring.
