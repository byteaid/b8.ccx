# Layout, Implicit Files, Build Cache, Multi-File Composition

File-based apps walk parent directories the same way project-based builds do. Anything you'd expect MSBuild to honor is honored — sometimes silently, sometimes harmfully.

## Implicit build files (honored)

| File | Effect |
|---|---|
| `Directory.Build.props` | MSBuild properties applied to the synthesized project. |
| `Directory.Build.targets` | Custom MSBuild targets execute during build. |
| `Directory.Packages.props` | Central Package Management. **Required** to use `#:package Id` without a version. |
| `nuget.config` | NuGet sources / settings during restore. |
| `global.json` | Pins the SDK version used for the file-based app. |

The walk goes up the directory tree from the `.cs` file. The closest file at each level wins — but every level's file is merged.

## Folder layout — recommendations

Two hard rules:

1. **Don't nest a `.cs` script inside a `.csproj` cone.** The project's implicit files override the file-based defaults.
2. **Be wary of ambient `Directory.Build.props`.** Drop a local one inside the scripts dir to isolate.

### Bad — script inside a project cone

```
MyProject/
  MyProject.csproj
  Program.cs
  scripts/
    utility.cs        # inside the project cone — bad
```

### Good — peer directory

```
MyProject/
  MyProject.csproj
  Program.cs
scripts/
  utility.cs          # peer dir — good
```

### Bad — hostile ambient props

```
repo/
  Directory.Build.props   # affects everything below
  app1.cs
  app2.cs
```

### Good — isolated scripts

```
repo/
  Directory.Build.props
  projects/MyProject.csproj
  scripts/
    Directory.Build.props   # isolated config for scripts
    app1.cs
    app2.cs
```

The local `Directory.Build.props` in `scripts/` can simply have an empty `<Project/>` to override the parent, or override only the properties that matter (e.g. `<PublishAot>false</PublishAot>` for the whole scripts directory).

## Build cache

Cache key derives from:

- Source file content.
- Directive configuration.
- SDK version.
- Existence/content of implicit build files.

### Quirks

- **Edits to implicit build files may not invalidate the cache reliably.** Symptom: you add `<NoWarn>` to `Directory.Build.props`, rebuild, and the warning still fires. Fix: `dotnet clean file-based-apps` to nuke the cache.
- **Moving files to different directories does not invalidate.** A `.cs` keyed by its content + directives looks the same after a move; the parent props ladder may have changed but the cache won't notice.
- **Concurrent runs of the same file-based app contend on output files.** Pre-build then run with `--no-build`:

```bash
dotnet build file.cs
dotnet run   file.cs --no-build &
dotnet run   file.cs --no-build &
```

### Force-clean workarounds

```bash
dotnet clean file-based-apps              # nuke whole cache
dotnet clean file.cs && dotnet build file.cs
```

## Default cache output paths

Default output: `<temp>/dotnet/runfile/<appname>-<appfilesha>/bin/<configuration>/`. The `<temp>` prefix follows the OS convention (`%TEMP%` on Windows, `$TMPDIR` / `/tmp` on Unix).

For predictable, repo-local output (CI, debugging, archiving), override with:

```csharp
#:property OutputPath=./output
```

or per-invocation `--output ./output` on `dotnet build` / `dotnet publish`.

## Runtime path access

App file and directory paths are exposed at runtime via `System.AppContext.GetData(...)` (named keys; consult the .NET 10 SDK release notes for the canonical key list). Use this when a script needs to locate sibling assets relative to its own `.cs`.

```csharp
var appFilePath = (string?)AppContext.GetData("AppFilePath");
```

`AppContext.BaseDirectory` still works but points at the build output, not the source `.cs` — `GetData` is the only way to resolve back to the source location.

## Multi-file composition

There is **no** "multiple loose `.cs` files in one file-based app" mode. The unit is a single `.cs`. To grow:

1. **Extract shared code into a class library `.csproj`**, reference via `#:project`:

   ```
   tools/
     Shared/
       Shared.csproj
       Helpers.cs
     greet.cs           // #:project ../Shared/Shared.csproj
     report.cs          // #:project ../Shared/Shared.csproj
   ```

2. **Or convert to a real project** with `dotnet project convert` (see [cli-lifecycle.md](cli-lifecycle.md)).

The trade-off: introducing a class library means shared code now needs to be packaged, versioned, or co-located alongside every script that consumes it. For two or three small scripts this is overkill — duplicate the helper. For five or more, extract.

## Picking a TFM

By default, file-based apps build against `net10.0`. Override per file:

```csharp
#:property TargetFramework=net9.0
```

`global.json` at any parent level pins the SDK version. The TFM (what gets compiled against) is independent of the SDK version (the toolchain). A .NET 10 SDK can target `net9.0`, `net8.0`, etc., subject to the usual support matrix.

## Cross-references

- [directives.md](directives.md) — `#:property OutputPath=...`, `#:property PublishAot=false`.
- [cli-lifecycle.md](cli-lifecycle.md) — `dotnet build / clean / publish` flags.
- [troubleshooting.md](troubleshooting.md) — symptom-to-cause table.
- Live: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
