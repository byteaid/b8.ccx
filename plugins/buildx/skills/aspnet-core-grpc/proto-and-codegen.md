# Proto, Codegen, and Protobuf Type System

`.proto` syntax, `<Protobuf>` MSBuild item, scalar / wrapper / well-known types, decimals, collections, oneof, Any, ByteString.

## `.proto` and codegen

```protobuf
syntax = "proto3";
option csharp_namespace = "GrpcGreeter";
package greet;

service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}
message HelloRequest { string name = 1; }
message HelloReply   { string message = 1; }
```

`<Protobuf>` MSBuild item attributes: `GrpcServices` (`Both` / `Server` / `Client` / `None`), `Access` (`Public` / `Internal`), `ProtoCompile`, `CompileOutputs`, `ProtoRoot`, `AdditionalImportDirs`, `OutputDir`, `Link`. Generated `.cs` lives in `obj/`, regenerated each build.

Each unary RPC produces TWO client methods: `SayHello` (blocking) and `SayHelloAsync`. Use the async one.

## Protobuf type system

| proto | C# |
|---|---|
| `double` / `float` | `double` / `float` |
| `int32` / `sint32` / `sfixed32` | `int` |
| `int64` / `sint64` / `sfixed64` | `long` |
| `uint32` / `fixed32` / `uint64` / `fixed64` | `uint` / `ulong` |
| `bool` | `bool` |
| `string` | `string` (default empty, **never null**) |
| `bytes` | `Google.Protobuf.ByteString` (default empty, **never null**) |

Field name `first_name` -> property `FirstName`.

### Nullable / wrapper types

`google/protobuf/wrappers.proto`: `BoolValue`, `Int32Value`, `Int64Value`, `UInt32Value`, `UInt64Value`, `FloatValue`, `DoubleValue`, `StringValue`, `BytesValue`. Map to `bool?`, `int?`, etc.

### Date / time (well-known types)

```protobuf
import "google/protobuf/duration.proto";
import "google/protobuf/timestamp.proto";
message Meeting {
  google.protobuf.Timestamp start = 1;
  google.protobuf.Duration  duration = 2;
}
```

```csharp
m.Start    = Timestamp.FromDateTimeOffset(meetingTime);   // UTC
m.Duration = Duration.FromTimeSpan(meetingLength);
DateTimeOffset t = m.Start.ToDateTimeOffset();
TimeSpan? d      = m.Duration?.ToTimeSpan();
```

### Decimal (DIY — no native support)

```protobuf
message DecimalValue { int64 units = 1; sfixed32 nanos = 2; }
```

Companion partial class with implicit conversions to/from `decimal`. Range +-9_223_372_036_854_775_807.999_999_999, precision 9 decimals.

### Collections, oneof, Any

- `repeated T` -> `RepeatedField<T> : IList<T>` (no setter; use `.Add(...)`).
- `map<K,V>` -> `MapField<K,V> : IDictionary<K,V>`.
- `oneof` -> `switch (response.ResultCase)` on the generated enum.
- `Any.Pack(person)`, `if (any.Is(Person.Descriptor)) any.Unpack<Person>()`. `Value` / `Struct` for dynamic JSON-shaped data.

### `bytes` — high-throughput

- Avoid `ByteString.CopyFrom(byte[])` (extra allocation/copy).
- `ByteString.UnsafeWrap(ReadOnlyMemory<byte>)` — zero-copy (`Google.Protobuf` >= 3.15).
- Read with `byteString.Span` / `byteString.Memory`. `MemoryMarshal.TryGetArray` to avoid copy when an array is needed.
- **Keep payloads under 85_000 bytes** to avoid the LOH; chunk large blobs via streaming.

## Code-first (`protobuf-net.Grpc`)

Community project; not first-party. Good for all-.NET systems; bad for polyglot.

```csharp
[DataContract] public class HelloRequest { [DataMember(Order = 1)] public string Name { get; set; } }
[DataContract] public class HelloReply   { [DataMember(Order = 1)] public string Message { get; set; } }

[ServiceContract]
public interface IGreeterService
{
    [OperationContract]
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);
}

builder.Services.AddCodeFirstGrpc();
app.MapGrpcService<GreeterService>();

// Client
using ProtoBuf.Grpc.Client;
var client = channel.CreateGrpcService<IGreeterService>();
```

Code-first and proto-first services coexist in the same app and share `AddGrpc` configuration.
