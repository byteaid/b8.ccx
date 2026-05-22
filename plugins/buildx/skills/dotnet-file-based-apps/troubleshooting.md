# Troubleshooting — Symptoms and Causes

Quick reference. Each row is a symptom; the cause column points at the rule or sub-file with the fix.

## Build / restore

| Symptom | Cause | Fix |
|---|---|---|
| `error NU1604: Project dependency does not contain an inclusive lower bound.` after a `#:package Id` (no version). | Central Package Management not configured. | Add `Directory.Packages.props`, **or** pin a version (`@1.2.3`), **or** use `@*`. See [directives.md](directives.md) § `#:package`. |
| `dotnet run file.cs` runs the project's `Program.cs`, not `file.cs`. | A `.csproj` exists in cwd; the positional argument is treated as a program arg to the project. | Use `dotnet run --file file.cs`, **or** `cd` out of the project cone. See [cli-lifecycle.md](cli-lifecycle.md). |
| `IL2*` / `IL3*` warnings on `dotnet publish`. | Native AOT is on (the file-based default) and the code or a dependency is not AOT-clean. | Either annotate / refactor the offending code, or set `#:property PublishAot=false`. See [native-aot.md](native-aot.md). |
| Build cache returns stale outputs after editing `Directory.Build.props`. | Implicit-file edits don't always invalidate the cache reliably. | `dotnet clean file-based-apps` (whole cache) or `dotnet clean file.cs && dotnet build file.cs`. See [layout-and-build.md](layout-and-build.md). |
| Two parallel `dotnet run file.cs` invocations clobber each other's output. | Concurrent runs contend on cache outputs. | Pre-build once (`dotnet build file.cs`) and run with `--no-build`. See [layout-and-build.md](layout-and-build.md). |
| `#:` lines emit warnings during build. | The file is being compiled as part of a `.csproj` cone (project-based), not as a file-based app. | Move the `.cs` out of the project cone, or convert it to a real project with `dotnet project convert`. See [cli-lifecycle.md](cli-lifecycle.md). |

## Runtime / dispatch

| Symptom | Cause | Fix |
|---|---|---|
| Shebang doesn't dispatch — running `./script.cs` produces "no such file" or invokes the wrong interpreter. | Wrong line endings, BOM, or missing `chmod +x`. | Save as **LF**, **no BOM**, then `chmod +x script.cs`. See [directives.md](directives.md) § `#!`. |
| `dotnet run -` (stdin) ignores environment variables / `applicationUrl`. | stdin pipe mode skips launch-profile lookup. | Move the snippet to a file and use `dotnet run app.cs --launch-profile <name>`. See [configuration.md](configuration.md). |
| `<App>.run.json` is being ignored; `Properties/launchSettings.json` settings apply instead. | Both files exist; the traditional path wins and the CLI logs a warning. | Either remove `Properties/launchSettings.json`, or accept the precedence and edit the traditional file. See [configuration.md](configuration.md). |
| User secrets set with `dotnet user-secrets set --file file.cs` no longer surface after the file is moved. | The user-secrets ID is hashed from the **full path** of the `.cs`. | Re-run `dotnet user-secrets set --file file.cs` at the new path, or symlink. See [configuration.md](configuration.md). |
| `AppContext.BaseDirectory` points at the temp build cache, not the source `.cs`. | Expected. `BaseDirectory` is the build output. | Use `AppContext.GetData("AppFilePath")` to get the source path. See [configuration.md](configuration.md). |

## Layout / structure

| Symptom | Cause | Fix |
|---|---|---|
| A `.cs` script silently inherits MSBuild properties from a far-away `Directory.Build.props`. | The implicit-file walk traverses every parent directory. | Drop a local `Directory.Build.props` (even just `<Project/>`) inside the scripts dir to isolate. See [layout-and-build.md](layout-and-build.md). |
| Scripts inside a `.csproj` cone behave nothing like documented file-based defaults. | The project's implicit files override file-based defaults. | Move scripts to a peer directory. See [layout-and-build.md](layout-and-build.md). |
| Trying to share helper code by adding sibling `.cs` files — they don't get compiled with the script. | There is no "multiple loose `.cs` files in one file-based app" mode. | Extract helpers into a class-library `.csproj` and reference via `#:project`, or convert the script to a project. See [layout-and-build.md](layout-and-build.md). |

## Conversion

| Symptom | Cause | Fix |
|---|---|---|
| `dotnet project convert` fails with "malformed directive". | A `#:` line has invalid syntax (typo, missing `=`, etc.). | Fix the directive, or pass `--force` to convert anyway and clean up afterwards. |
| The converted `.csproj` has a different `Sdk` than expected when there were multiple `#:sdk` lines. | The **first** `#:sdk` becomes the project `Sdk` attribute; subsequent ones become `<Sdk>` elements. | Reorder the `#:sdk` lines so the most specific SDK is first. See [directives.md](directives.md) § `#:sdk`. |

## Aspire-specific note

A file-based AppHost using `#:sdk Aspire.AppHost.Sdk@x.y.z` works for execution but currently has reduced testability vs a real `.csproj` AppHost (the integration-test fixtures `Aspire.Hosting.Testing` expects a real project). Choose `.csproj` for AppHost when you need integration tests. See `dotnet-aspire` for the testing fixtures.

## Cross-references

- [directives.md](directives.md) — directive syntax.
- [cli-lifecycle.md](cli-lifecycle.md) — verbs and their flags.
- [layout-and-build.md](layout-and-build.md) — implicit files and cache.
- [native-aot.md](native-aot.md) — AOT failure diagnosis.
- [configuration.md](configuration.md) — secrets, profiles, runtime path access.
