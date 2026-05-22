# Culture Providers — Built-in & Custom

Built-in `IRequestCultureProvider` types, custom delegates, subclassing, route-based localization, runtime culture switching for non-Blazor apps. Load when picking or composing culture-resolution strategies.

## Default order (most → least specific)

| # | Provider | Source |
|---|---|---|
| 0 | `QueryStringRequestCultureProvider` | `?culture=...&ui-culture=...` |
| 1 | `CookieRequestCultureProvider` | Cookie `.AspNetCore.Culture` |
| 2 | `AcceptLanguageHeaderRequestCultureProvider` | `Accept-Language` HTTP header |

If none match → `DefaultRequestCulture`.

## `QueryStringRequestCultureProvider`

Default keys `culture`, `ui-culture` (override via `QueryStringKey` / `UIQueryStringKey`). If only one is supplied, the same value is used for both. Useful for debugging / E2E rigs.

## `CookieRequestCultureProvider`

Default cookie name: `.AspNetCore.Culture` (`CookieRequestCultureProvider.DefaultCookieName`). Cookie format: `c=%LANGCODE%|uic=%LANGCODE%`. **Never hand-format**. Build with `MakeCookieValue(RequestCulture)`.

```csharp
[HttpPost]
public IActionResult SetLanguage(string culture, string returnUrl)
{
    Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });

    return LocalRedirect(returnUrl);
}
```

## `AcceptLanguageHeaderRequestCultureProvider`

Reads browser-set `Accept-Language`. Browser-controlled — usually combined with a cookie/query for user preference. **Not supported on Blazor WASM client side** — do it on the server (BFF) or via local-storage.

## `RouteDataRequestCultureProvider` (route-based)

Reads route values such as `{culture}` / `{ui-culture}`. Ships in `Microsoft.AspNetCore.Localization.Routing`. Must run **after** routing.

```csharp
options.RequestCultureProviders.Insert(0, new RouteDataRequestCultureProvider
{
    RouteDataStringKey   = "culture",
    UIRouteDataStringKey = "culture",
    Options              = options
});
```

URLs like `/{culture}/{controller}/{action}` (e.g. `/es-MX/products`) drive the resolved culture.

## `CustomRequestCultureProvider` (delegate)

```csharp
options.AddInitialRequestCultureProvider(new CustomRequestCultureProvider(async ctx =>
{
    var segments = ctx.Request.Path.Value!
        .Split('/', StringSplitOptions.RemoveEmptyEntries);
    var culture = segments.Length > 1 && segments[0].Length == 2 ? segments[0] : "en";
    return await Task.FromResult(new ProviderCultureResult(culture));
}));
```

`AddInitialRequestCultureProvider` inserts at index 0 — wins over defaults.

## Subclassing `RequestCultureProvider` (DI access)

```csharp
public class AppSettingsRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var configuration = httpContext.RequestServices.GetService<IConfigurationRoot>();
        var culture = configuration?["culture"];
        if (culture is null) return Task.FromResult<ProviderCultureResult?>(null);
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture, culture));
    }
}
```

## Reordering / replacing

```csharp
options.RequestCultureProviders.Clear();          // drop all defaults
options.RequestCultureProviders.Insert(0, qs);    // your own order
```

## Runtime culture switching

### In-process

`CultureInfo.CurrentCulture` / `CurrentUICulture` are thread-static; the middleware sets them per request. Process-wide defaults:

```csharp
CultureInfo.DefaultThreadCurrentCulture   = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
```

### Cookie pattern (browser, server-rendered)

Round-trip: form post → controller writes cookie → 302 back → next request hits `CookieRequestCultureProvider`. Use `LocalRedirect` to prevent open-redirect.

### Route-based localization (`/{culture}/...`)

```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders.Insert(0, new RouteDataRequestCultureProvider
    { RouteDataStringKey = "culture", UIRouteDataStringKey = "culture", Options = options });
});

app.UseRouting();
app.UseRequestLocalization();

app.MapControllerRoute(
    name: "default",
    pattern: "{culture=en-US}/{controller=Home}/{action=Index}/{id?}");
```

Constraint to limit accepted cultures (recommended; otherwise any path segment becomes "culture"):

```csharp
pattern: "{culture:regex(^[a-z]{{2}}(-[A-Z]{{2}})?$)=en-US}/{controller=Home}/{action=Index}/{id?}"
```
