# Resource Files — `.resx`, Shared Resources, DataAnnotations, PO, Custom Backends

`.resx` naming conventions, shared marker classes, DataAnnotations localization, OrchardCore PO files, custom `IStringLocalizer` factories. Load when authoring resources, localizing validation messages, or replacing the resource backend.

## DI registration

```csharp
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

builder.Services
    .AddControllersWithViews()                       // or AddRazorPages / AddMvc
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();
```

| Call | Adds |
|---|---|
| `AddLocalization` | `IStringLocalizer<T>`, `IStringLocalizerFactory`, `ResourceManagerStringLocalizer`. `ResourcesPath` is project-root-relative; default = root. |
| `AddViewLocalization` | `IViewLocalizer` + view-name-suffix discovery (`Index.fr.cshtml`). `LanguageViewLocationExpanderFormat`: `Suffix` (default) or `SubFolder`. |
| `AddDataAnnotationsLocalization` | Localized DataAnnotations validation messages via `IStringLocalizer`. Accepts a `DataAnnotationLocalizerProvider` delegate to redirect all attribute lookups to a single shared resource type. |

## Localizer abstractions

### `IStringLocalizer<T>` / `IStringLocalizer`

- Backed by `ResourceManagerStringLocalizer` (`System.Resources.ResourceManager` + `ResourceReader`).
- Indexer: `localizer["key", arg0, arg1, ...]`.
- Returns the **key itself** when the resource is missing.
- `LocalizedString` exposes `Name`, `Value`, `ResourceNotFound`, `SearchedLocation`.

```csharp
public class AboutController(IStringLocalizer<AboutController> localizer) : Controller
{
    [HttpGet] public string Get() => localizer["About Title"];
}
```

### `IHtmlLocalizer<T>` (MVC only)

- For resources that legitimately contain HTML.
- HTML-encodes the **arguments**, not the resource string itself.
- `Microsoft.AspNetCore.Mvc.Localization`.

```csharp
public class BookController(IHtmlLocalizer<BookController> localizer) : Controller
{
    public IActionResult Hello(string name)
    {
        ViewData["Message"] = localizer["<b>Hello</b><i> {0}</i>", name];
        return View();
    }
}
```

Guidance: localize **text**, not markup. Reserve `IHtmlLocalizer` for irreducible HTML.

### `IStringLocalizerFactory`

Lower-level factory; useful when the lookup type is decided at runtime or you want a "shared" resource.

```csharp
public TestController(IStringLocalizerFactory factory)
{
    _localizer  = factory.Create(typeof(SharedResource));
    _localizer2 = factory.Create("SharedResource", new AssemblyName(asm.FullName!).Name!);
}
```

### `IViewLocalizer` (MVC Razor only)

Implemented by `ViewLocalizer`, which **wraps an `IHtmlLocalizer`** so Razor doesn't double-encode. Resource file selected from the view's file path. **No "global shared resource" support** — additionally inject `IHtmlLocalizer<SharedResource>` when needed.

```cshtml
@inject IViewLocalizer Localizer
@inject IHtmlLocalizer<SharedResource> SharedLocalizer
<h2>@Localizer["About"]</h2>
<h1>@SharedLocalizer["Hello!"]</h1>
<p>@Localizer["<i>Hello</i> <b>{0}!</b>", User.Identity!.Name]</p>
```

## Resource files (`.resx`)

### Naming conventions

Resource base name = **full type name minus the assembly name**, plus `.<culture>.resx`.

| Type (asm: `LocalizationWebsite.Web`) | Resource file |
|---|---|
| `LocalizationWebsite.Web.Startup` | `Startup.fr.resx` |
| `LocalizationWebsite.Web.Controllers.HomeController` | `Controllers.HomeController.fr.resx` |
| `ExtraNamespace.Tools` (namespace ≠ asm name) | `ExtraNamespace.Tools.fr.resx` |

`ResourcesPath` (set on `AddLocalization`) prefixes that name. Two equivalent layouts:

| Layout | Path |
|---|---|
| Dot | `Resources/Controllers.HomeController.fr.resx` |
| Path | `Resources/Controllers/HomeController.fr.resx` |

If `ResourcesPath` is omitted, files sit at the project base directory.

### Razor view resources

Mimic the view's path; both styles work with `ResourcesPath = "Resources"`:

- `Resources/Views/Home/About.fr.resx` (path)
- `Resources/Views.Home.About.fr.resx` (dot)

Without `ResourcesPath`, the `.resx` for a view sits next to the view.

### Per-culture file creation

| File | Culture |
|---|---|
| `Welcome.fr.resx` | French (neutral) |
| `Welcome.fr-CA.resx` | French (Canada) |
| `Welcome.es-MX.resx` | Spanish (Mexico) |

### Assembly attributes for class libraries / tricky names

```csharp
using System.Reflection;
using Microsoft.Extensions.Localization;

[assembly: ResourceLocation("Resource Folder Name")]
[assembly: RootNamespace("App.Root.Namespace")]
```

`ResourceLocation` enables class-library resource discovery. `RootNamespace` is required when the project name has invalid identifier chars.

### Culture fallback

When resolving a key for `fr-CA`, `ResourceManager` walks the parent chain and returns the first hit:

1. `Welcome.fr-CA.resx`
2. `Welcome.fr.resx`
3. `Welcome.resx` — only if `NeutralResourcesLanguageAttribute` is set to `fr-CA` (or matches).

If nothing matches, `IStringLocalizer` returns the **key**.

## Shared resources

Use a **dummy/marker class** (no fields, no methods, no designer file) to anchor a shared `.resx`:

```csharp
namespace Localization;
public class SharedResource { }
```

```
Resources/SharedResource.resx
Resources/SharedResource.es.resx
Resources/SharedResource.fr.resx
```

Inject by the marker type:

```csharp
public class InfoController(
    IStringLocalizer<InfoController> localizer,
    IStringLocalizer<SharedResource> sharedLocalizer) : Controller
{
    public string TestLoc()
        => $"Shared: {sharedLocalizer["Hello!"]}  Info: {localizer["Hello!"]}";
}
```

In Razor views, inject `IHtmlLocalizer<SharedResource>` (no `IViewLocalizer` for shared).

**Do not** place an autogenerated `SharedResources.Designer.cs` next to `SharedResource.cs` — namespaces collide.

## DataAnnotations + ModelMetadata localization

### Per-class resources (default)

With `ResourcesPath = "Resources"`, messages on `Localization.ViewModels.Account.RegisterViewModel` resolve from either:

- `Resources/ViewModels.Account.RegisterViewModel.fr.resx` (dot)
- `Resources/ViewModels/Account/RegisterViewModel.fr.resx` (path)

```csharp
public class RegisterViewModel
{
    [Required(ErrorMessage = "The Email field is required.")]
    [EmailAddress(ErrorMessage = "The Email field is not a valid email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [StringLength(8, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
    public string Password { get; set; } = "";
}
```

The string passed to `ErrorMessage` becomes the **resource key** (also the fallback). `[Display(Name=...)]` is non-validation but still localized.

### One shared resource for all DataAnnotations

```csharp
builder.Services
    .AddMvc()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider =
            (type, factory) => factory.Create(typeof(SharedResource));
    });
```

All `ErrorMessage`/`Name` keys resolve against `SharedResource.<culture>.resx` regardless of model class.

### Blazor (no MVC view pipeline)

Blazor forms route through DataAnnotations validators and honor:
- `DisplayAttribute.ResourceType` / `Display.Name`
- `ValidationAttribute.ErrorMessageResourceType` / `ErrorMessageResourceName`

These produce strongly-typed lookups via the resource designer class generated by VS for the chosen `.resx`.

## Portable Object (PO) localization with OrchardCore

`OrchardCore.Localization.Core` (community NuGet) ships an `IStringLocalizer` backed by gettext-style PO files instead of `.resx`. **Pluralization** support (`msgid_plural` + `msgstr[N]`) — `.resx` has none. Plain text, no MSBuild step, friendly to Crowdin / POEditor / Weblate.

```po
msgid "Hello world!"
msgstr "Bonjour le monde!"

msgid "There is one item."
msgid_plural "There are {0} items."
msgstr[0] "Il y a un élément."
msgstr[1] "Il y a {0} éléments."

msgctxt "Views.Home.About"
msgid "Hello world!"
msgstr "Bonjour le monde!"
```

```csharp
builder.Services.AddPortableObjectLocalization();
builder.Services.Configure<RequestLocalizationOptions>(o => o
    .AddSupportedCultures("fr", "cs")
    .AddSupportedUICultures("fr", "cs"));

builder.Services.AddRazorPages().AddViewLocalization();

app.UseRequestLocalization();
app.MapRazorPages();
```

PO files default to project root (`fr.po`, `cs.po`); override via `options.ResourcesPath = "Localization";`. Translations cascade via parent culture. Custom file discovery via `ILocalizationFileLocationProvider`.

## Custom `IStringLocalizer` backends

Not bound to `.resx`. Replace the factory:

```csharp
public sealed class JsonStringLocalizerFactory(IDistributedCache cache) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) =>
        new JsonStringLocalizer(cache, resourceSource.FullName!);
    public IStringLocalizer Create(string baseName, string location) =>
        new JsonStringLocalizer(cache, $"{location}.{baseName}");
}

builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
```

`StringLocalizer<T>` is the generic adapter that routes to `IStringLocalizerFactory.Create(typeof(T))`, so custom factories transparently support `IStringLocalizer<T>`.
