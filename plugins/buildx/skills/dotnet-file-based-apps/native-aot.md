# Native AOT — Default for File-Based Apps

`PublishAot=true` is the file-based-app default. `dotnet publish file.cs` produces a self-contained native binary unless you opt out per file:

```csharp
#:property PublishAot=false
```

This file documents the implications inherited from Native AOT and when to opt out.

## Implications when AOT is on

- **No reflection-emit.** No `System.Reflection.Emit`, no dynamic IL generation. Libraries that build expression trees at runtime via `Emit` will not work; libraries that use cached compiled expressions over closed-shape types may work depending on trimming.
- **No `Assembly.LoadFile`.** Plug-in mechanisms that load assemblies at runtime do not work.
- **No C++/CLI.** Mixed-mode assemblies are not supported.
- **No built-in COM (Windows).** COM interop is unavailable.
- **Trimming is required.** AOT implies trimming. Code with trimmer warnings may behave differently in AOT than in JIT — heed `IL2*` warnings during publish.
- **Single-file output.** AOT publishes as a single executable.
- **Required runtime libraries ship with the app.** Larger on-disk size vs framework-dependent.
- **`System.Linq.Expressions` runs interpreted.** Slower than JIT-compiled expressions.
- **Generic instantiations are pre-generated.** Increases on-disk size; inhibits some patterns that rely on JIT-time specialization.
- **Diagnostics support has limits.** Some tracing/profiling features are reduced.
- **Some ASP.NET Core features are unsupported.** Verify per feature against the live AOT compatibility matrix.

## When to opt out

Set `#:property PublishAot=false` when:

- A package you depend on uses reflection-emit (e.g. some serializers, mocking frameworks, ORMs in non-AOT modes).
- You need plug-in loading via `Assembly.LoadFile`.
- The script targets a runtime that does not have AOT support.
- Startup time is not a concern, and JIT throughput on the hot path matters more.
- You are iterating during development and just want the fastest build (JIT publishes much faster than AOT).

```csharp
#:property PublishAot=false
```

You can also opt out only for `Release` builds:

```csharp
#:property PublishAot=$(Configuration) == 'Debug' ? 'false' : 'true'
```

(The exact MSBuild expression syntax depends on what you want — see [`directives.md`](directives.md) § `#:property` for `[MSBuild]::ValueOrDefault` and similar property functions.)

## Supported targets (.NET 9+, applies to .NET 10)

| OS / arch | Notes |
|---|---|
| Windows x64 / Arm64 / x86 | Fully supported. |
| Linux x64 / Arm64 / Arm | Fully supported. **Linux AOT binary built on distro version N runs on N+; it is not backward-compatible to older glibc** — build on the oldest supported distro. |
| macOS x64 / Arm64 | Fully supported. |
| iOS / iOSSimulator | Supported. |
| tvOS / tvOSSimulator | Supported. |
| MacCatalyst | Supported. |
| Android | **Experimental.** No Java interop. |

## Startup-time benchmark

Internal benchmarks (from the System.CommandLine 2.0 migration guide, representative of the broader AOT story):

| Mode | Cold-start time |
|---|---|
| Native AOT | ~17 ms |
| JIT (framework-dependent) | ~76 ms |

For a tiny CLI binary the difference is real; for a long-running process it is amortized away.

## Deployment size

AOT binaries ship the runtime and only the trimmed libraries the app actually references. Size is dominated by:

- The set of types/methods kept by the trimmer.
- Whether globalization-invariant mode is enabled (saves substantial size if true).
- Whether `System.Linq.Expressions` is used (its interpreter pulls in non-trivial code).

For very small CLIs the resulting binary is typically a few megabytes; for ASP.NET Core minimal-APIs apps, tens of megabytes.

## Diagnosing AOT publish failures

Symptoms:
- `IL2*` trimmer warnings on publish.
- `IL3*` AOT analyzer warnings.
- A package writes "RequiresDynamicCodeAttribute" warnings.

Procedure:
1. Re-run publish with `dotnet publish file.cs -v normal` (or `detailed`).
2. Read the warning categories: `IL2104` (assembly produced trimmer warnings), `IL3050` (RequiresDynamicCode), `IL2026` (RequiresUnreferencedCode).
3. If the offending code is yours: add `[DynamicallyAccessedMembers]` annotations or restructure to avoid reflection.
4. If the offending code is in a third-party package and the package does not advertise AOT support: opt out (`#:property PublishAot=false`).
5. Test the binary on a clean machine (no .NET runtime installed) to confirm self-containment.

## Cross-references

- [directives.md](directives.md) — `#:property` syntax.
- [cli-lifecycle.md](cli-lifecycle.md) — `dotnet publish` defaults.
- Live: https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
