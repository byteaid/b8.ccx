# Forbidden — `.proto` files outside the dedicated gRPC project

## What it looks like

```
src/Acme.Foo.WebAPI/
├── Protos/
│   └── orders.proto              # banned — .proto in a host project
└── Services/
    └── OrdersGrpcService.cs

src/Acme.Foo.gRPC.Server/
├── Greeter.proto                 # banned — also in the server project
└── GreeterService.cs

src/Acme.Foo.Web.Client/
└── Protos/
    └── greeter.proto             # banned — .proto in a client project
```

## Why it's banned

1. **`.proto` files are contracts shared between server and client.** Living in either side's project couples them.
2. **Build configuration differs by side.** Server: `GrpcServices="Server"`. Client: `GrpcServices="Client"`. When the `.proto` lives in one side, the other side has to either re-include or duplicate.
3. **Single source of truth.** Versioning, breaking-change detection, and SDK generation all hinge on the `.proto` having one canonical home.
4. **Cross-language clients** (a future Python/Node service consuming the same `.proto`) need a project they can reference without dragging C# server code along.

## What to do instead

One dedicated project per product: `{Company}.{Product}.gRPC`. Contains only `.proto` files and the `<Protobuf>` MSBuild items. Both server and client projects reference the shared project.

```
src/
├── Acme.Foo.gRPC/                              # shared proto project
│   ├── Acme.Foo.gRPC.csproj
│   └── Protos/
│       ├── orders.proto
│       └── greeter.proto
├── Acme.Foo.gRPC.Server/                        # service implementations
│   ├── Acme.Foo.gRPC.Server.csproj             # <ProjectReference> Acme.Foo.gRPC
│   ├── OrdersService.cs
│   └── GreeterService.cs
└── Acme.Foo.Web.Client/                         # Blazor WASM client
    └── Acme.Foo.Web.Client.csproj              # <ProjectReference> Acme.Foo.gRPC
```

Project shapes:

```xml
<!-- Acme.Foo.gRPC.csproj — shared proto project -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Protobuf Include="Protos\*.proto" GrpcServices="Both" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="..." />
    <PackageReference Include="Grpc.Tools" Version="..." PrivateAssets="all" />
    <PackageReference Include="Google.Protobuf" Version="..." />
  </ItemGroup>
</Project>
```

The server overrides `GrpcServices="Server"` and the client overrides `GrpcServices="Client"` only when they need a different generation target — by default, `Both` from the shared project covers everyone.

## Enforcement

- **On sight, inside a host or client project you're editing:** move the `.proto` to `Acme.Foo.gRPC` and switch the project to a `<ProjectReference>`.
- **Quick scan:**

  ```bash
  find src/ -name "*.proto" -not -path "*/Acme.Foo.gRPC/*"
  ```

  must return zero matches.
- **Architecture review:** new gRPC contracts go into the dedicated project from day one.

## See also

- [../project-layout/hexagonal-layers.md](../project-layout/hexagonal-layers.md)
