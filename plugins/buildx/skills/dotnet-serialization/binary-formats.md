# Binary Formats — MessagePack, CBOR, Protobuf, BinaryFormatter

Binary serialization options. Load when picking a wire format, integrating MessagePack-CSharp, writing low-level CBOR, or migrating away from `BinaryFormatter`.

## `BinaryFormatter` — REMOVED

Calling `BinaryFormatter.Serialize`/`Deserialize` throws `NotSupportedException` (SYSLIB0011). Migrate to JSON / MessagePack / CBOR / a hand-rolled `BinaryReader`+`BinaryWriter` schema.

## MessagePack-CSharp

NuGet `MessagePack`. Compatible with .NET 10. Source-gen resolver required for Native AOT.

```csharp
[MessagePackObject]
public class Person
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int Age { get; set; }
}

byte[] bytes = MessagePackSerializer.Serialize(new Person { Name = "Ada", Age = 36 });
var p = MessagePackSerializer.Deserialize<Person>(bytes);

// Contractless mode — uses property names like JSON.
var opts = MessagePackSerializerOptions.Standard
    .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

// LZ4-compressed
var lz4 = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4BlockArray);
```

Trade-offs vs JSON: 3–10× faster, 2–5× smaller payloads; **not human-readable**; `[Key(int)]` is positional — renumbering breaks compatibility (use `[Key(string)]` to ease evolution at a perf cost).

## CBOR (`System.Formats.Cbor`)

In-box low-level reader/writer (no high-level object serializer). Useful for IoT, COSE, WebAuthn.

```csharp
var w = new CborWriter();
w.WriteStartMap(2);
w.WriteTextString("name"); w.WriteTextString("Ada");
w.WriteTextString("age");  w.WriteInt32(36);
w.WriteEndMap();
byte[] bytes = w.Encode();
```

## Protocol Buffers

`Google.Protobuf` (canonical `.proto`-driven) or `protobuf-net` (attribute-driven on POCOs). Both work on .NET 10. Out of scope for in-box BCL.
