# Blazor Globalization & Localization

`@bind` formatting, `BlazorWebAssemblyLoadAllGlobalizationData`, ICU subsetting, invariant globalization, time zones, runtime culture switching for WASM and Blazor Server / Web App. Load when localizing a Blazor app or shrinking WASM globalization payload.

## `@bind` formatting & input types

- `@bind` formats values using `CultureInfo.CurrentCulture` by default.
- Override with `@bind:culture="myCulture"`.
- `<input type="date">` / `<input type="number">` are forced to `InvariantCulture` by the browser. Don't override.

## `BlazorWebAssemblyLoadAllGlobalizationData`

Default = trim to app's culture(s). For runtime-switchable WASM, set:

```xml
<PropertyGroup>
  <BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>
</PropertyGroup>
```

## `<BlazorIcuDataFileName>` (WASM)

WASM bundles a reduced ICU dataset. Pin one with MSBuild:

| File | Locales |
|---|---|
| `icudt.dat` | Full data |
| `icudt_EFIGS.dat` | `en-*`, `fr-FR`, `es-ES`, `it-IT`, `de-DE` |
| `icudt_CJK.dat` | `en-*`, `ja`, `ko`, `zh-*` |
| `icudt_no_CJK.dat` | All locales except `ja`, `ko`, `zh-*` |

```xml
<PropertyGroup>
  <BlazorIcuDataFileName>icudt_no_CJK.dat</BlazorIcuDataFileName>
</PropertyGroup>
```

Custom subsets (.NET 8+) are also supported.

## Invariant globalization (WASM)

Strips ICU entirely; everything formats `en-US`-ish invariant. Cuts download size, speeds startup. Three equivalent toggles:

```xml
<!-- 1. Project file -->
<PropertyGroup><InvariantGlobalization>true</InvariantGlobalization></PropertyGroup>
```

```json
// 2. runtimeconfig.json
{ "runtimeOptions": { "configProperties": { "System.Globalization.Invariant": true } } }
```

```text
# 3. Environment variable
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
```

## Time zones (WASM)

```xml
<PropertyGroup>
  <InvariantTimezone>true</InvariantTimezone>
</PropertyGroup>
```

`<BlazorEnableTimeZoneSupport>false</BlazorEnableTimeZoneSupport>` overrides `InvariantTimezone`; prefer `InvariantTimezone` alone.

## Setting WASM culture statically

JS start option (BCP-47 string required):

```html
<script>
    Blazor.start({ applicationCulture: 'en-US' });                                // Standalone WASM
    Blazor.start({ webAssembly: { applicationCulture: 'en-US' } });               // Blazor Web App
</script>
```

C# alternative in WASM `Program.cs`:

```csharp
CultureInfo.DefaultThreadCurrentCulture   = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
```

**.NET 10 change**: standalone Blazor WASM apps now load globalization data based on `DefaultThreadCurrentUICulture` (in .NET 9 only `DefaultThreadCurrentCulture` drove the loaded data).

## Setting WASM culture dynamically (user preference)

Persist in browser local storage; load before the host runs.

```html
<script>
    window.blazorCulture = {
        get: () => window.localStorage['BlazorCulture'],
        set: (value) => window.localStorage['BlazorCulture'] = value
    };
</script>
```

```csharp
// Program.cs (WASM)
builder.Services.AddLocalization();
var host = builder.Build();

var js     = host.Services.GetRequiredService<IJSRuntime>();
var stored = await js.InvokeAsync<string>("blazorCulture.get");
var culture = CultureInfo.GetCultureInfo(stored ?? "en-US");
if (stored is null) await js.InvokeVoidAsync("blazorCulture.set", "en-US");

CultureInfo.DefaultThreadCurrentCulture   = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await host.RunAsync();
```

```razor
@* CultureSelector.razor — WASM *@
<select @bind="selectedCulture" @bind:after="ApplySelectedCultureAsync">
    @foreach (var c in supportedCultures) { <option value="@c">@c.DisplayName</option> }
</select>

@code {
    private CultureInfo[] supportedCultures = [ new("en-US"), new("es-CR") ];
    private CultureInfo? selectedCulture;
    protected override void OnInitialized() => selectedCulture = CultureInfo.CurrentCulture;

    private async Task ApplySelectedCultureAsync()
    {
        if (CultureInfo.CurrentCulture != selectedCulture)
        {
            await JS.InvokeVoidAsync("blazorCulture.set", selectedCulture!.Name);
            Navigation.NavigateTo(Navigation.Uri, forceLoad: true);   // forceLoad REQUIRED
        }
    }
}
```

`forceLoad: true` is required so the runtime restarts and re-applies the culture to all `CultureInfo` lookups.

## Setting Blazor Server / Web App culture dynamically

URL-based schemes break SignalR — use a localization cookie.

```csharp
// Program.cs
var supportedCultures = new[] { "en-US", "es-CR" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
```

```razor
@* Components/App.razor — write the cookie during initial SSR render *@
@code {
    [CascadingParameter] private HttpContext? HttpContext { get; set; }
    protected override void OnInitialized() =>
        HttpContext?.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture)));
}
```

```csharp
// Controllers/CultureController.cs
[Route("[controller]/[action]")]
public class CultureController : Controller
{
    public IActionResult Set(string culture, string redirectUri)
    {
        if (culture is not null)
        {
            HttpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(
                    new RequestCulture(culture, culture)));
        }
        return LocalRedirect(redirectUri);   // open-redirect-safe
    }
}
```

## Auto-render Blazor Web App (CSR + SSR mixed)

Set `BlazorWebAssemblyLoadAllGlobalizationData=true` in the `.Client` project; configure both:
- Local-storage `blazorCulture.get/set` JS (CSR path).
- Cookie write via `App.razor` and a `CultureController` (SSR path).

Use a `Dictionary<string, string>` for display names — Blazor WASM globalization doesn't include localized culture display names; without the dictionary every culture renders only as its BCP-47 code on the client.

## Blazor `IStringLocalizer<T>` example

```
Localization/SharedResource.cs
Localization/SharedResource.resx          (default)
Localization/SharedResource.es.resx       (Spanish)
```

```razor
@page "/culture-example-2"
@inject IStringLocalizer<CultureExample2> Loc
<p>@Loc["Greeting"]</p>
```

## Blazor support matrix (recap)

| Localizer | Blazor Server | Blazor WASM | Blazor Web App |
|---|---|---|---|
| `IStringLocalizer` / `IStringLocalizer<T>` | Yes | Yes | Yes |
| `IHtmlLocalizer` | **No** | **No** | **No** |
| `IViewLocalizer` | **No** | **No** | **No** |
