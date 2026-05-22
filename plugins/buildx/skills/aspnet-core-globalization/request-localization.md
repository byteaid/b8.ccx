# `RequestLocalization` Middleware

`UseRequestLocalization` configuration, options, pipeline ordering, per-request behavior. Load when wiring localization middleware, debugging culture-resolution order, or inspecting which provider matched.

```csharp
var supportedCultures = new[] { "en-US", "fr", "es-CR" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);
```

Convenience overload: `app.UseRequestLocalization("en-US");`.

## Pipeline ordering (rule 3)

| Rule | Why |
|---|---|
| Before MVC / endpoints / Razor Pages / Razor components | Otherwise the request runs with the default thread culture. |
| **After** `UseRouting` if `RouteDataRequestCultureProvider` is used | Route values aren't populated until routing has run. |
| For Blazor: immediately before `MapRazorComponents` (or `MapBlazorHub`) | Same. |

## `RequestLocalizationOptions` highlights

| Member | Purpose |
|---|---|
| `DefaultRequestCulture` | Fallback when no provider matches. |
| `SupportedCultures` / `SupportedUICultures` | Restrict what providers can resolve. |
| `RequestCultureProviders` | Ordered list. First match wins. |
| `FallBackToParentCultures` (default `true`) | If `fr-CA` not supported but `fr` is, use `fr`. |
| `FallBackToParentUICultures` (default `true`) | Same for UI cultures. |
| `ApplyCurrentCultureToResponseHeaders` (default `false`) | Auto-emit `Content-Language: <CurrentUICulture>`. |
| `CultureInfoUseUserOverride` | Whether to use Windows non-default `DateTimeFormat`/`NumberFormat`. **No effect on Linux.** |
| `AddInitialRequestCultureProvider(...)` | Insert at index 0 — runs first. |

## What the middleware does per request

1. Iterate `RequestCultureProviders`; first one returning non-null `ProviderCultureResult` wins.
2. Validate against `SupportedCultures` / `SupportedUICultures` (with parent-culture fallback if enabled).
3. Set `CultureInfo.CurrentCulture` and `CurrentUICulture` on the request thread.
4. Optionally write `Content-Language` response header.
5. Stash `IRequestCultureFeature` on `HttpContext.Features`.

In .NET 3.0+, an unsupported requested culture logs at `LogLevel.Debug` (down from `Warning` in 2.x).

## Inspecting the resolved culture

```csharp
var feature = HttpContext.Features.Get<IRequestCultureFeature>();
var culture = feature?.RequestCulture.Culture;     // CultureInfo
var ui      = feature?.RequestCulture.UICulture;
var picked  = feature?.Provider?.GetType().Name;   // which provider matched
```

## Globalization & model binding

| Source | Globalized? |
|---|---|
| Form data | Yes — uses `CurrentCulture`. |
| Route values | **No** — always invariant culture. |
| Query string | **No** — always invariant culture. |

By design: URLs round-trip identically across cultures.

## Diagnostic logging

```json
{ "Logging": { "LogLevel": {
    "Microsoft.AspNetCore.Localization": "Debug",
    "Microsoft.Extensions.Localization": "Debug"
} } }
```

Logs: which provider matched, which culture resolved, whether requested culture was unsupported, `SearchedLocation` for each `LocalizedString`, "Resource not found" for missing keys.
