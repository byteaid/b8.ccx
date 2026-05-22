---
name: aspnet-core-globalization
description: ASP.NET Core 10 globalization & localization reference. Covers `AddLocalization`/`AddViewLocalization`/`AddDataAnnotationsLocalization`, localizers (`IStringLocalizer<T>`, `IHtmlLocalizer<T>`, `IViewLocalizer`, `IStringLocalizerFactory`), `.resx` lookup under `ResourcesPath`, shared resources via marker class, DataAnnotations localization, `RequestLocalization` middleware ordering, the four built-in `IRequestCultureProvider`s + custom, route-based localization, PO files via OrchardCore, Blazor globalization (`BlazorWebAssemblyLoadAllGlobalizationData`, `<BlazorIcuDataFileName>`), invariant globalization, runtime culture switching (cookie / `applicationCulture`), ICU vs NLS, model-binding rule (route+query invariant; form `CurrentCulture`).
when_to_use: |
  - Trigger keywords: AddLocalization, AddViewLocalization, AddDataAnnotationsLocalization, IStringLocalizer, IViewLocalizer, ResourcesPath, .resx, SharedResource, UseRequestLocalization, IRequestCultureProvider, CookieRequestCultureProvider, RouteDataRequestCultureProvider, BlazorWebAssemblyLoadAllGlobalizationData, BlazorIcuDataFileName, InvariantGlobalization, AddPortableObjectLocalization, ICU, FallBackToParentCultures.
  - Task shapes: scaffold a localized MVC/Razor Pages/Blazor app; pick a culture-selection strategy; add a shared resource; localize DataAnnotations errors; debug "literal key returned"; switch culture at runtime on Blazor Server or WASM; pin or trim ICU on WASM; force invariant globalization; resolve a route-based `{culture}` after `UseRouting`.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.cshtml", "**/*.razor", "**/Program.cs", "**/*.resx", "**/*.po", "**/*.csproj", "**/appsettings*.json"]
---

# ASP.NET Core Globalization & Localization — Reference

Reference for globalizing/localizing ASP.NET Core 10 apps (MVC, Razor Pages, Minimal APIs, Blazor Server, Blazor WebAssembly, Blazor Web App). Pin the rules; defer the long catalogues to the Microsoft docs cited at the bottom.

## Mental model

- **Globalization (G11N)** = make the app work in different languages/regions. **Localization (L10N)** = adapt to a specific language/region. **Internationalization (I18N)** = both.
- Two `CultureInfo`s drive everything per request:
  - **`CurrentCulture`** — date/time/number/currency formatting, casing, sort, comparison.
  - **`CurrentUICulture`** — `ResourceManager` lookup of translated strings.
- BCP-47 codes (`<lang>-<REGION>`, e.g. `es-MX`) are the universal currency. Required by `applicationCulture` (Blazor) and `UseRequestLocalization`. **Neutral** culture = language only (`fr`); **specific** culture = language + region (`fr-CA`); fallback walks up the parent chain.
- The middleware/provider model: `UseRequestLocalization` runs an ordered list of `IRequestCultureProvider`s. First match wins; the result is validated against `SupportedCultures` / `SupportedUICultures`, then assigned to `CurrentCulture` / `CurrentUICulture` for the request.
- Three workstreams to localize: (1) make content localizable (extract strings); (2) provide localized resources (`.resx` / `.po` / DB / JSON); (3) implement a culture-selection strategy per request.

## Non-negotiable rules

1. **`.resx` Build Action MUST be `Embedded Resource`.** Otherwise `ResourceManagerStringLocalizer` cannot locate them. Default in MSBuild for `.resx`, but VS templates sometimes ship something else.
2. **Never create a `<TypeName>.resx` (no culture in name) for `IStringLocalizer<T>`.** VS auto-generates `<TypeName>.Designer.cs` which collides with the dummy/marker resource class and breaks `IStringLocalizer<T>`. Always create `<TypeName>.<culture>.resx`.
3. **Pipeline order**: `UseRequestLocalization` MUST run **before** any middleware that reads the culture (MVC, endpoints, Razor Pages, Razor components). For `RouteDataRequestCultureProvider`, MUST run **after** `UseRouting()` (otherwise route values aren't populated). For Blazor, place `UseRequestLocalization` immediately before `MapRazorComponents` (or `MapBlazorHub` on classic Blazor Server).
4. **Class library resources need `[assembly: ResourceLocation(...)]`** in `AssemblyInfo.cs`. Without it, a referenced library's `.resx` is invisible to the consumer.
5. **Project filenames with characters that aren't valid .NET identifiers** (e.g. `my-project-name.csproj` → assembly `my-project-name`, root namespace `my_project_name`) require `[assembly: RootNamespace(...)]` — `RootNamespace` is build-time and the runtime can't see it.
6. **Don't hand-format the localization cookie.** `c='en-UK'|uic='en-US'` is invalid (quotes). Always use `CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(...))`.
7. **Route values and query strings parse as invariant culture; form fields parse as `CurrentCulture`.** By design — URLs must round-trip identically across cultures.
8. **Blazor: `IHtmlLocalizer` and `IViewLocalizer` are MVC-only.** Not supported on Blazor Server, WASM, or Web App. Use `IStringLocalizer<T>` for components.
9. **Blazor Server / Web App culture switching MUST use cookies** — URL/query schemes break SignalR/WebSocket round-trips.
10. **`<input type="date">` and `<input type="number">` are forced to `InvariantCulture`** by the browser. Don't override with `@bind:culture`.
11. **Blazor WASM defaults to trimming globalization data to the app's culture(s)**. To allow runtime culture switching to any culture, set `<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>`. Increases download size.
12. **`IStringLocalizer` returns the key when no resource matches.** This is by design — the default language can stay as inline literals; no default `.resx` is needed.
13. **Linux containers without `libicu` cannot do non-invariant globalization** — install `libicu` (`apt install libicu-dev` / `apk add icu-libs`) or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. The Microsoft `aspnet:10.0` and `aspnet:10.0-alpine` images ship with it.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| `UseRequestLocalization` middleware — options, pipeline ordering, per-request behavior, model-binding rule, diagnostic logging | [request-localization.md](request-localization.md) | Wiring localization middleware; debugging which provider matched. |
| Built-in & custom `IRequestCultureProvider`s, route-based localization, in-process culture switching | [culture-providers.md](culture-providers.md) | Picking or composing culture-resolution strategies; route-based `{culture}` URLs. |
| `.resx` naming + `ResourcesPath`, shared resources via marker class, DataAnnotations localization, OrchardCore PO, custom backends, all localizer abstractions | [resource-files.md](resource-files.md) | Authoring resources; localizing validation messages; replacing the resource backend. |
| Blazor — `@bind` formatting, ICU subsetting, invariant globalization, `applicationCulture`, runtime switching for WASM and Server / Web App | [blazor-localization.md](blazor-localization.md) | Localizing a Blazor app; shrinking WASM globalization payload. |
| ICU vs NLS, Linux container caveats, observable behavior differences, troubleshooting playbook | [formatting-and-comparison.md](formatting-and-comparison.md) | Debugging missing translations, Linux startup failures, sort-order drift after migration. |

## Quick decision matrix

| Scenario | Recommended provider(s) |
|---|---|
| Public marketing site, language switcher in footer | Cookie + AcceptLanguage (default order) |
| API for SPA / mobile | AcceptLanguage only (clear other providers) or custom from JWT claim |
| URL-based marketing site (`/es-MX/...`) | Route + Cookie (route first) |
| Tenant with per-user DB-stored preference | Custom `RequestCultureProvider` reading from claim/DB |
| Debug / E2E rig | QueryString (default order) |
| Blazor Server / Web App | Cookie (URL-based breaks WebSocket) |
| Blazor WASM | C# `DefaultThreadCurrentCulture` from local storage |

## Cross-references

- Public docs (Globalization & localization overview): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization?view=aspnetcore-10.0
- Public docs (Strategies for selecting culture): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/select-language-culture?view=aspnetcore-10.0
- Public docs (Make content localizable): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/make-content-localizable?view=aspnetcore-10.0
- Public docs (Provide localized resources): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/provide-resources?view=aspnetcore-10.0
- Public docs (PO localization with OrchardCore): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/portable-object-localization?view=aspnetcore-10.0
- Public docs (Localization extensibility): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization-extensibility?view=aspnetcore-10.0
- Public docs (Troubleshoot localization): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/troubleshoot-aspnet-core-localization?view=aspnetcore-10.0
- Public docs (Blazor globalization & localization): https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0
- Public docs (Globalization behavior of model binding): https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0#globalization-behavior-of-model-binding-route-data-and-query-strings
- Public docs (.NET Globalization & ICU): https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu
- Public docs (Runtime config for globalization): https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization
- Related skill: `aspnet-core-blazor` — render modes, `@bind` semantics, lifecycle (this skill only owns the globalization-specific pieces).
- Related skill: `aspnet-core-mvc` / `aspnet-core-razor-pages` — non-localization MVC / Razor Pages topics.
- Related skill: `aspnet-core-fundamentals` — middleware ordering for the rest of the pipeline.
- Related skill: `aspnet-core-security` — `LocalRedirect` for open-redirect-safe culture-switch endpoints.
