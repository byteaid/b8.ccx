# Code Analysis & Style Enforcement

The static-analysis surfaces shipped with the .NET 10 SDK toolchain — Roslyn analyzers (`CA*` / `IDE*`), nullable reference types, trimming (`IL2xxx`), Native AOT (`IL3xxx`), banned-symbols, public-API tracker — plus the MSBuild knobs and `.editorconfig` keys that wire them into the build. Aligned with the team's zero-warnings rule (`dotnet build` exits clean — see [build-quality/zero-warnings-rule.md](build-quality/zero-warnings-rule.md)) and no-suppression policy.

## Surfaces at a glance

| Surface | Prefix | Default in .NET 10 SDK | Build-time? | Toggle |
|---|---|---|---|---|
| Code quality (Roslyn) | `CA1xxx`–`CA5xxx` | Enabled (`Default` mode) for `net5.0+` | Yes | `EnableNETAnalyzers`, `AnalysisMode`, `AnalysisLevel` |
| Code style (Roslyn) | `IDE0xxx`/`IDE1xxx`/`IDE2xxx`/`IDE3xxx` | IDE-only by default | Only if `EnforceCodeStyleInBuild=true` | `EnforceCodeStyleInBuild`, per-rule severity |
| Compiler warnings | `CS####` | Always on | Yes | `NoWarn`, `WarningsAsErrors`, `TreatWarningsAsErrors` |
| Nullable flow analysis | `CS86xx` / `CS87xx` | On in .NET 6+ templates | Yes | `<Nullable>`, `#nullable` directive |
| Platform compatibility | `CA1416`, `CA1418`, `CA1422` | Enabled `net5.0+` | Yes | `[SupportedOSPlatform]`, `[UnsupportedOSPlatform]` |
| Trimming (ILLink) | `IL2xxx` | Off; on with `PublishTrimmed`/`IsTrimmable`/`EnableTrimAnalyzer` | Yes | `PublishTrimmed`, `IsTrimmable`, `EnableTrimAnalyzer` |
| Native AOT | `IL3xxx` | Off; on with `PublishAot`/`IsAotCompatible`/`EnableAotAnalyzer` | Yes | `PublishAot`, `IsAotCompatible`, `EnableAotAnalyzer` |
| Single-file | `IL3000`–`IL3002` | Off; on with `PublishSingleFile`/`EnableSingleFileAnalyzer` | Yes | `EnableSingleFileAnalyzer` |
| Public API tracker | `RS0016`/`RS0017`/`RS0022`/`RS0024`–`RS0027`/`RS0036`–`RS0041` | NuGet opt-in | Yes | `Microsoft.CodeAnalysis.PublicApiAnalyzers` |
| Banned symbols | `RS0030` | NuGet opt-in + `BannedSymbols.txt` | Yes | `Microsoft.CodeAnalysis.BannedApiAnalyzers` |

## Canonical strict-project snippet

Apply at the solution root (`Directory.Build.props`) so every project inherits.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <AnalysisLevel>latest-Recommended</AnalysisLevel>
    <AnalysisModeSecurity>All</AnalysisModeSecurity>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
    <WarningsAsErrors>$(WarningsAsErrors);CS8600;CS8601;CS8602;CS8603;CS8604;CS8618</WarningsAsErrors>

    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

`<NoWarn>` should stay empty in this team's projects — the [no-warning-suppression rule](forbidden-patterns/no-warning-suppression.md) bans wholesale silencing.

## `AnalysisMode` semantics

| Mode | Behavior |
|---|---|
| `None` | All CA rules off. Opt-in per ID. |
| `Default` | SDK's curated default-on set (~30 rules) is `warning`/`error`. Rest are `suggestion`/`silent`. |
| `Minimum` | Adds rules where build enforcement is strongly recommended. |
| `Recommended` | Superset of `Minimum`. **Team default.** |
| `All` | Enables all rules as `warning`. Excludes legacy/deprecated CA1014, CA1017, CA1021, CA1045, CA1060, CA1505, CA1506, CA1509. |

Per-category overrides: `<AnalysisModeSecurity>All</AnalysisModeSecurity>`, `<AnalysisModeReliability>All</AnalysisModeReliability>`, etc. Categories: `Design`, `Documentation`, `Globalization`, `Interoperability`, `Maintainability`, `Naming`, `Performance`, `SingleFile`, `Reliability`, `Security`, `Style`, `Usage`.

## `AnalysisLevel` pinning

`<AnalysisLevel>latest-Recommended</AnalysisLevel>` follows the SDK. To pin: `<AnalysisLevel>10.0</AnalysisLevel>`. Compound form: `<version>-<mode>` (`latest-All`, `10-Minimum`).

NuGet override (escape hatch when you need a newer analyzer pack on an older SDK):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.*">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
<PropertyGroup>
  <_SkipUpgradeNetAnalyzersNuGetWarning>true</_SkipUpgradeNetAnalyzersNuGetWarning>
</PropertyGroup>
```

When the NuGet is referenced, **omit** `<EnableNETAnalyzers>` (otherwise build emits a warning).

## `.editorconfig` severity grammar

| Form | Scope |
|---|---|
| `dotnet_diagnostic.<ID>.severity = <sev>` | Single rule (highest precedence) |
| `dotnet_analyzer_diagnostic.category-<NAME>.severity = <sev>` | All **default-enabled** rules in category |
| `dotnet_analyzer_diagnostic.severity = <sev>` | All **default-enabled** rules |

Severity values: `error` | `warning` | `suggestion` | `silent` | `none` | `default`.

Inline severity on style options (.NET 9+):

```ini
[*.{cs,vb}]
dotnet_style_require_accessibility_modifiers = always:warning
csharp_style_namespace_declarations = file_scoped:error
```

Generated-code marker (per-glob):

```ini
[*.{Designer,g,generated}.cs]
generated_code = true

[**/obj/**]
generated_code = true
```

A file is auto-marked generated when its name ends in `.designer.cs` / `.generated.cs` / `.g.cs` / `.g.i.cs`, starts with `TemporaryGeneratedFile_`, or contains `<auto-generated>` in the leading comment block.

## Naming-rule schema (IDE1006)

Three coupled blocks: symbol group + naming style + rule binding.

```ini
[*.{cs,vb}]
dotnet_naming_symbols.private_static_fields.applicable_kinds = field
dotnet_naming_symbols.private_static_fields.applicable_accessibilities = private
dotnet_naming_symbols.private_static_fields.required_modifiers = static

dotnet_naming_style.s_underscore_camel.required_prefix = s_
dotnet_naming_style.s_underscore_camel.capitalization = camel_case

dotnet_naming_rule.private_static_fields_should_have_s_prefix.symbols = private_static_fields
dotnet_naming_rule.private_static_fields_should_have_s_prefix.style = s_underscore_camel
dotnet_naming_rule.private_static_fields_should_have_s_prefix.severity = warning
```

Authoring keys: `applicable_kinds`, `applicable_accessibilities`, `required_modifiers`, `capitalization`, `required_prefix`, `required_suffix`, `word_separator`.

## Nullable reference types

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>     <!-- enable | warnings | annotations | disable -->
</PropertyGroup>
```

| Value | Annotations | Warnings | Notes |
|---|---|---|---|
| `disable` | off | off | nullable-oblivious |
| `enable` | on | on | **Team default.** |
| `warnings` | off | on | members not-null at method opening |
| `annotations` | on | off | annotations only, no flow analysis |

`#nullable enable|disable|restore [warnings|annotations]` overrides per region. Generated files are auto-disabled.

Annotation attributes worth knowing (`System.Diagnostics.CodeAnalysis`):

| Attribute | Phase | Use |
|---|---|---|
| `[AllowNull]` | precondition | Non-nullable parameter/property may receive `null`. |
| `[DisallowNull]` | precondition | Nullable parameter/property must not be `null`. |
| `[MaybeNull]` / `[NotNull]` | postcondition | Non-nullable return may be null / nullable return guaranteed not. |
| `[MaybeNullWhen(bool)]` / `[NotNullWhen(bool)]` | conditional | Out param state tied to method return value. |
| `[NotNullIfNotNull(nameof(p))]` | conditional | Return is not-null iff `p` is. |
| `[MemberNotNull(nameof(_field), …)]` | helper | Listed members not-null after this method returns. |
| `[DoesNotReturn]` / `[DoesNotReturnIf(bool)]` | unreachable | Method always throws / unreachable on condition. |

Common nullable diagnostics worth promoting to errors: `CS8600`, `CS8601`, `CS8602`, `CS8603`, `CS8604`, `CS8618`, `CS8625`. The strict snippet above already does this.

## Suppression mechanics — when each is appropriate

Team policy: suppression is **forbidden** by default. The list below is descriptive (so you can recognise existing suppressions during clean-as-you-touch); use only when the rule itself is wrong for the codebase, with architecture sign-off.

| Mechanism | Scope | When |
|---|---|---|
| `dotnet_diagnostic.CA1822.severity = none` (.editorconfig) | Project / glob | Whole-rule disable. Survives in source control. |
| `[SuppressMessage(...)]` attribute | Member / type / namespace | Single justified site. `Justification` field is mandatory. |
| `GlobalSuppressions.cs` | Assembly | Auto-generated by IDE "Suppress in Suppression File". |
| `#pragma warning disable/restore` | Statement / lines | Smallest possible window; restore at the closing brace. |
| `<NoWarn>$(NoWarn);CA1822</NoWarn>` (csproj) | Project | Equivalent to severity=`none`; prefer `.editorconfig` for visibility. |
| `<WarningsNotAsErrors>` | Project | Keep specific IDs as warnings under global `-warnaserror`. |
| `[UnconditionalSuppressMessage(...)]` | Member / type | **Only correct way to suppress `IL2xxx`/`IL3xxx`** — persisted in IL, respected by ILLink/AOT. Plain `[SuppressMessage]` is silently dropped at trim. |

Stale-suppression detection: leave `dotnet_diagnostic.IDE0079.severity = warning` in `.editorconfig` to flag suppressions whose underlying problem has been fixed.

## Trim & Native AOT analyzers

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>          <!-- enables trim analyzer too -->
  <PublishAot>true</PublishAot>                  <!-- implies trim + AOT analyzers -->
  <IsTrimmable>true</IsTrimmable>                <!-- library marker -->
  <IsAotCompatible>true</IsAotCompatible>        <!-- library marker; sets IsTrimmable + all four enable-flags -->
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
  <VerifyReferenceTrimCompatibility>true</VerifyReferenceTrimCompatibility>
  <VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
</PropertyGroup>
```

`<IsAotCompatible>true</IsAotCompatible>` is the canonical library-level switch — it stamps `[AssemblyMetadata("IsTrimmable", "True")]` and sets all four enable-flags.

Frequent diagnostics:

| ID | Trigger |
|---|---|
| `IL2026` | Calling `[RequiresUnreferencedCode]` member |
| `IL2057`–`IL2059` | Unrecognized `Type.GetType` patterns |
| `IL2067`–`IL2092` | Mismatched `[DynamicallyAccessedMembers]` |
| `IL2125` | Referenced assembly missing `IsTrimmable` |
| `IL3050` | Calling `[RequiresDynamicCode]` API |
| `IL3056` | `[RequiresDynamicCode]` on attribute member |
| `IL3000`–`IL3002` | `Assembly.Location` / `CodeBase` in single-file |

Annotations:

```csharp
using System.Diagnostics.CodeAnalysis;

[RequiresUnreferencedCode("Uses reflection to load handlers.")]
[RequiresDynamicCode("Builds a runtime expression tree.")]
public static T Build<T>() => default!;

public static void Call(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type t)
    => t.GetMethods();

[DynamicDependency("Helper", typeof(MyHelpers))]
public void RunHelper() => typeof(MyHelpers).GetMethod("Helper")!.Invoke(null, null);
```

`DynamicallyAccessedMemberTypes` is `[Flags]`: `PublicConstructors`, `NonPublicConstructors`, `PublicMethods`, `NonPublicMethods`, `PublicFields`, `NonPublicFields`, `PublicProperties`, `NonPublicProperties`, `PublicEvents`, `NonPublicEvents`, `PublicNestedTypes`, `NonPublicNestedTypes`, `Interfaces`, `All`.

## Banned symbols (deprecated APIs)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="*">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <AdditionalFiles Include="BannedSymbols.txt" />
</ItemGroup>
```

`BannedSymbols.txt` (one DocID per line, optional `;` + reason):

```
T:System.DateTime; Use System.DateTimeOffset (or TimeProvider via the team rule).
M:System.Console.WriteLine(System.String); No console output in libraries.
P:System.Environment.UserName; PII — use IUserContext.
N:System.Web; Use System.Net.Http.
```

The team's first-party bans (AutoMapper, Mapster, MediatR, Brighter — see [forbidden-patterns/no-automapper-no-mediatr.md](forbidden-patterns/no-automapper-no-mediatr.md)) can be hard-enforced this way at the symbol level once the packages are removed from the graph.

## Public-API tracker (libraries only)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="*">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <AdditionalFiles Include="PublicAPI.Shipped.txt" />
  <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
</ItemGroup>
```

`Shipped` is the stable surface; `Unshipped` is the delta since last release. Removed APIs go into the shipped file prefixed with `*REMOVED*`. Multi-target: `PublicAPI.Shipped.<TFM>.txt`.

## Source-generator-emitted diagnostics

Roslyn source generators (`SYSLIB####`) follow the same severity/suppression machinery. Disable specific generators when needed:

```xml
<EnableConfigurationBindingGenerator>false</EnableConfigurationBindingGenerator>
<EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>
```

Team-mandated generators (`LoggerMessage`, `JsonSerializerContext`) are owned by [source-generators/index.md](source-generators/index.md).

## CI / `dotnet format`

```bash
dotnet build -c Release /p:TreatWarningsAsErrors=true /p:EnforceCodeStyleInBuild=true
dotnet format --verify-no-changes --severity warn
dotnet test -c Release --no-build
```

Subcommands:

| Command | Scope |
|---|---|
| `dotnet format` | All — formatting + style + analyzers (with code fixes) |
| `dotnet format whitespace` | Whitespace only |
| `dotnet format style` | `IDE*` rules |
| `dotnet format analyzers` | `CA*` rules with fixers |

Verbose modes for CI: `--verify-no-changes` (no writes — exit non-zero on diff), `--severity warn|error|info`, `--diagnostics IDE0005 IDE0040`.

SARIF output for code scanning:

```xml
<PropertyGroup>
  <ErrorLog>$(IntermediateOutputPath)$(MSBuildProjectName).sarif,version=2.1</ErrorLog>
</PropertyGroup>
```

Upload `.sarif` files to GitHub Advanced Security / Azure DevOps for navigable analyzer results.

## Decision matrix — which knob to use

| Goal | Use |
|---|---|
| Turn on CA rules globally | `<AnalysisMode>Recommended</AnalysisMode>` |
| Turn on CA rules for one category only | `<AnalysisModeSecurity>All</AnalysisModeSecurity>` |
| Pin ruleset across SDK upgrades | `<AnalysisLevel>10.0</AnalysisLevel>` |
| Add IDE rules to CI build | `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` + per-rule severity |
| Promote a specific warning to error | `<WarningsAsErrors>$(WarningsAsErrors);CA2007</WarningsAsErrors>` |
| Treat all warnings as errors except CA | `TreatWarningsAsErrors=true` + `CodeAnalysisTreatWarningsAsErrors=false` |
| Disable a rule project-wide (rare; needs sign-off) | `dotnet_diagnostic.CA1822.severity = none` |
| Suppress a single instance (rare; needs sign-off) | `[SuppressMessage]` with `Justification` |
| Mark an assembly trim/AOT-safe | `<IsAotCompatible>true</IsAotCompatible>` |
| Suppress an `IL2xxx`/`IL3xxx` properly | `[UnconditionalSuppressMessage]` (NOT `[SuppressMessage]`) |
| Ban a deprecated API at symbol level | `BannedApiAnalyzers` + `BannedSymbols.txt` |
| Track public-API changes | `PublicApiAnalyzers` + `PublicAPI.{Shipped,Unshipped}.txt` |
| Force null-safety as errors | `<Nullable>enable</Nullable>` + `<WarningsAsErrors>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618</WarningsAsErrors>` |

## Cross-references

- Public docs (overview): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
- Public docs (quality rules): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/
- Public docs (style rules): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/
- Public docs (configuration options): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options
- Public docs (suppress warnings): https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/suppress-warnings
- Public docs (nullable references): https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references
- Public docs (trimming): https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming
- Public docs (Native AOT): https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- Public docs (platform-compat analyzer): https://learn.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer
- Internal: [build-quality/zero-warnings-rule.md](build-quality/zero-warnings-rule.md) — the team's clean-build rule.
- Internal: [build-quality/clean-as-you-touch.md](build-quality/clean-as-you-touch.md) — eradicate forbidden patterns inside files you edit.
- Internal: [forbidden-patterns/no-warning-suppression.md](forbidden-patterns/no-warning-suppression.md) — why suppression is banned by default.
