# Zero-warnings rule

## Rule

Before any handback, `dotnet build` on the affected project(s) must exit with **zero errors AND zero warnings**. Warnings are treated as errors. The team configures `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` per project. Suppression of any kind is forbidden — fix the root cause.

## Rationale

- Warnings flag real defects: nullable flow bugs, async/await misuse, deprecated API calls, allocation hot paths. Ignoring them is technical debt that compounds.
- "Zero warnings" is the only steady state. Once a single warning is allowed to slip, the next one slips too, and the codebase drifts to "lots of warnings, who knows which are new".
- A zero-warning build is a **fast, deterministic gate** — pass/fail with no discretionary judgment. Reviewers, CI, and humans all agree on the answer.

## Canonical project setup

Every `.csproj` carries:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

Or — preferred — set the four compiler-level properties once in `Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

## CLI gate

```bash
# The handback gate
dotnet build {Company}.{Product}.slnx --nologo -warnaserror

# Verify exit code is zero AND output contains "0 Warning(s)" / "0 Error(s)"
```

The build must pass before any agent reports completion. If it doesn't, the agent fixes the cause; if the cause is out of scope, the agent reports and stops — never suppresses to "make the build pass".

## What "fix the root cause" means in practice

| Warning class | Fix |
|---|---|
| Nullable (`CS86xx`) | Add the null check, narrow the type, or change the signature. |
| Async (`CS1998`, `CS4014`) | Make the method actually async, or stop awaiting / fire-and-forget appropriately. |
| Allocation (`CA1848`, `CA1859`) | Convert to `LoggerMessage`, return a more specific type, etc. |
| Obsolete (`CS0618`) | Migrate to the replacement API. |
| Style (`IDE00xx`) | Apply the suggested edit; bring the file into convention. |

If the fix cascades beyond your task scope, surface as a TODO and report.

## Enforcement

- **Banned mechanisms:** `#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`, `dotnet_diagnostic.*.severity = none` in `.editorconfig`. See [../forbidden-patterns/no-warning-suppression.md](../forbidden-patterns/no-warning-suppression.md).
- **Handback contract:** every report includes the line `dotnet build: 0 errors, 0 warnings` (or the equivalent CI gate output).
- **CI:** the build-warnaserror job is the merge gate; PRs that fail it cannot land.

## See also

- [clean-as-you-touch.md](clean-as-you-touch.md) — what to fix while you're already in the file.
- [handback-format.md](handback-format.md) — the standard report format.
