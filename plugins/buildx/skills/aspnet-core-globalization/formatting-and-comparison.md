# ICU vs NLS, Linux Specifics, Troubleshooting

Runtime backend selection (ICU / NLS / Invariant), Linux container caveats, observable behavior differences, troubleshooting playbook. Load when debugging missing translations, container startup failures, or sort-order drift after migration.

## ICU vs NLS — runtime backend

| Backend | Where used by default | How to force |
|---|---|---|
| **ICU** | All platforms .NET 5+: Linux, macOS, **Windows 10 May 2019 or later** | `System.Globalization.UseNls=false` (default); ensure `libicu` is present |
| **NLS** (Windows-only) | Older Windows hosts; opt-in fallback | `System.Globalization.UseNls=true` in `runtimeconfig.json`, or `DOTNET_SYSTEM_GLOBALIZATION_USENLS=1` |
| **Invariant** | Containers without ICU; fast/small | `System.Globalization.Invariant=true`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, or `<InvariantGlobalization>true</InvariantGlobalization>` |

## Linux specifics

| Image / setup | Outcome |
|---|---|
| `mcr.microsoft.com/dotnet/aspnet:10.0` | Includes `libicu`. Full ICU works. |
| `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` | Includes `icu-libs` (Alpine package). |
| Custom slim Debian/Ubuntu image without `libicu` | `CultureNotFoundException` / formatting fails unless `Invariant=true`. |
| `RequestLocalizationOptions.CultureInfoUseUserOverride` | **No effect on Linux** — maps to per-user Windows regional overrides. |

Minimum Linux fix: `apt-get install -y libicu-dev` (Debian/Ubuntu) or `apk add --no-cache icu-libs` (Alpine), or accept invariant mode.

## Observable differences (ICU vs NLS)

- ICU has wider locale coverage (>800 cultures vs NLS).
- Sort order for some scripts differs (e.g., German phonebook vs dictionary). **Apps that hash/index/store collated strings must pin a backend** to avoid drift after migration.
- ICU returns slightly different `CultureInfo.DisplayName` strings.

## Troubleshooting playbook

| Symptom | Cause | Fix |
|---|---|---|
| App returns the literal key for every culture | `.resx` not matched / `Build Action` not `Embedded Resource` / `ResourcesPath` mismatch | Confirm filename naming; set `Build Action = Embedded Resource`; verify `ResourcesPath`. |
| Class library's resources never found | Missing `[assembly: ResourceLocation(...)]` (and possibly `RootNamespace`) | Add both attributes. |
| Localization works for default culture but never switches | `UseRequestLocalization` registered after MVC/endpoints | Move it before. |
| `RouteDataRequestCultureProvider` resolves null | `UseRequestLocalization` runs before `UseRouting` | Order: `UseRouting()` → `UseRequestLocalization()` → endpoints. |
| `CookieRequestCultureProvider` ignores the cookie | Cookie hand-formatted with quotes | Use `MakeCookieValue`. |
| `CustomRequestCultureProvider` bypassed | Registered after defaults | Use `AddInitialRequestCultureProvider(...)` or `Insert(0, ...)`. |
| Project name has hyphens, runtime fails to find resources | Root namespace ≠ assembly name | `[assembly: RootNamespace(...)]`. |
| `Resources/Welcome.fr.resx` exists but `IStringLocalizer<Welcome>` returns the key | Sibling `Welcome.resx` generated `Welcome.Designer.cs`, hijacking the type | Delete `Welcome.resx` and `Welcome.Designer.cs`. |
| Validation messages not translated | `AddDataAnnotationsLocalization()` not called, or `.resx` placed under wrong type-name path | Call it; verify naming. |
| Decimal commas in form fields rejected by jQuery validation | jQuery validation uses invariant by default | jQuery globalize patch — see dotnet/AspNetCore.Docs issue #4076. |
| Blazor WASM never loads non-default culture | Trimmer stripped non-`en-US` ICU | `<BlazorWebAssemblyLoadAllGlobalizationData>true</...>` or pin a wider `<BlazorIcuDataFileName>`. |
| `CultureNotFoundException` on Linux container at startup | `libicu` missing and Invariant not set | Install `libicu` or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. |
