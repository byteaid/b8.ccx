# Distribution — Packaging and Running Scripts Elsewhere

A script lives on disk as a single `.cs` file, but consumers usually want it as something they can invoke without thinking about source. This file covers the four standard distribution shapes.

For the underlying SDK verbs see `dotnet-file-based-apps/cli-lifecycle.md`.

## Distribution shapes

| Shape | When |
|---|---|
| **Source `.cs` file** | The audience has the .NET 10 SDK and the script is small and fast enough to run via `dotnet run`. Simplest. |
| **Unix shebang executable** | Same audience; you want `./greet.cs` instead of `dotnet run greet.cs`. |
| **Global tool** (`dotnet tool install -g`) | The audience installs once and runs the tool repeatedly. The default file-based-app shape. |
| **Native AOT binary** (`dotnet publish`) | The audience does not have the .NET runtime, or you want sub-20-ms cold start. |

## Source `.cs` distribution

Drop the `.cs` in a repo, document `dotnet run --file file.cs -- <args>` in the README. Done.

`dotnet-file-based-apps/cli-lifecycle.md` covers the `dotnet run` permutations, including the `dotnet run file.cs` vs `dotnet run --file file.cs` ambiguity when a `.csproj` exists in cwd.

## Unix shebang

```csharp
#!/usr/bin/env dotnet
#:package System.CommandLine@*

using System.CommandLine;
// ...
```

Then:
```bash
chmod +x greet.cs
./greet.cs --name Ada
```

For extensionless dispatch (looks like a real binary):
```bash
cp greet.cs ~/.local/bin/greet
chmod +x ~/.local/bin/greet
greet --name Ada
```

LF line endings, no BOM. See `dotnet-file-based-apps/directives.md` § `#!`.

## Global tool

`PackAsTool=true` is the file-based-app default. Pack the script:

```bash
dotnet pack greet.cs
```

This produces a `.nupkg` under the temp output path (or `bin/` if you set `OutputPath`). The package's tool ID defaults to the script basename — override with:

```csharp
#:property PackAsTool=true
#:property ToolCommandName=greet
#:property PackageId=Acme.Greet
#:property Version=1.0.0
```

Install it locally for testing:

```bash
dotnet tool install -g --add-source ./output Acme.Greet
greet --name Ada
```

Update / uninstall:

```bash
dotnet tool update -g Acme.Greet
dotnet tool uninstall -g Acme.Greet
```

Publish to NuGet.org or a private feed:

```bash
dotnet nuget push ./output/Acme.Greet.1.0.0.nupkg --source <feed> --api-key <key>
```

Consumers then:

```bash
dotnet tool install -g Acme.Greet
```

## One-shot tool execution — `dnx` and `dotnet tool exec`

Both invoke a tool without installing it. Useful in CI and ad-hoc one-liners:

```bash
dnx Acme.Greet -- --name Ada
dotnet tool exec Acme.Greet -- --name Ada
```

`dnx` is the script form; `dotnet tool exec` is the verb form. They are equivalent.

## Native AOT binary

`PublishAot=true` is the file-based-app default — `dotnet publish` produces a self-contained native binary out of the box.

```bash
dotnet publish greet.cs -r linux-x64
dotnet publish greet.cs -r win-x64
dotnet publish greet.cs -r osx-arm64
```

Output path: `artifacts/` next to the `.cs`, or `--output <DIR>`. The binary is self-contained — copy it to a machine without .NET installed and run.

For implications (no `Reflection.Emit`, trimming required, distro-build constraints), see `dotnet-file-based-apps/native-aot.md`.

When to opt out: the script depends on a package that uses reflection-emit or `Assembly.LoadFile`. Add `#:property PublishAot=false` and consumers will need the .NET runtime — but the package compatibility surface widens.

## Container images

Console apps can build container images via `dotnet publish /t:PublishContainer` without setting `EnableSdkContainerSupport`. Works for file-based apps:

```bash
dotnet publish greet.cs /t:PublishContainer
```

The image is pushed to the local Docker daemon by default. Override the registry / repository / tag through standard MSBuild properties (`ContainerRegistry`, `ContainerRepository`, `ContainerImageTag`).

## Tab completion for installed tools

Tab completion does not register itself. After installing a tool that owns a command, the user runs once:

```bash
dotnet-suggest register --command-path "$(which greet)"
```

(See `dotnet-system-commandline/help-and-completion.md` for the full wiring including the per-shell shim script.)

Document the one-time `dotnet-suggest register` step in your README — the pre-beta5 `RegisterWithDotnetSuggest` extension that did this on every startup is gone.

## Reproducible builds

For predictable output paths in CI:

```csharp
#:property OutputPath=./output
#:property Version=$([MSBuild]::ValueOrDefault('$(BUILD_VERSION)', '0.0.0-dev'))
```

`global.json` at the repo root pins the SDK version so the same `.cs` produces the same binary across machines.

## Picking a shape

| Audience | Shape |
|---|---|
| Devs in this repo, with the SDK installed | Source `.cs` + `dotnet run`. |
| Devs anywhere, with the SDK installed | Source `.cs` + shebang, or global tool. |
| Devs in CI | Global tool installed in the build container, **or** `dnx` for one-shot. |
| Consumers without .NET | Native AOT binary or container image. |
| Mass distribution (NuGet feed) | Global tool. |

## Cross-references

- [scaffold.md](scaffold.md) — what the source file looks like before packaging.
- [cli-design.md](cli-design.md) — exit codes and output discipline that flow into pipelines.
- `dotnet-file-based-apps/cli-lifecycle.md` — `dotnet pack` / `dotnet publish` / `dnx` / `dotnet tool exec` in full.
- `dotnet-file-based-apps/native-aot.md` — when to opt out of AOT for distribution.
- `dotnet-system-commandline/help-and-completion.md` — `dotnet-suggest register` for tab completion.
- Live: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools
- Live: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install
