---
name: aspnet-core-razor-pages
description: ASP.NET Core 10 Razor Pages reference. Covers bootstrap (`AddRazorPages`/`MapRazorPages`, `/Pages` URL-to-file mapping), two-file `PageModel` vs single-file `@functions`, page handlers (`On[Async]<Verb>[Name]`, named handlers + `@page "{handler?}"`, HEAD-to-GET), `@page` directive, `RazorPagesOptions` + conventions, URL gen across areas, `[BindProperty]` + `SupportsGet`, antiforgery (auto on POST/PUT/DELETE/PATCH, `[IgnoreAntiforgeryToken]`, `IAntiforgery.GetAndStoreTokens`), page filters (`IPageFilter`/`IAsyncPageFilter` — MVC action filters DO NOT run), conventions API (`AddPageRoute`, `AddFolderRouteModelConvention`, `PageRouteTransformerConvention`), authorization conventions (`AuthorizePage`/`AuthorizeFolder`/`AllowAnonymousToPage`), areas + `_ViewImports` scoping, CSS isolation, `WebApplicationFactory<T>` testing with AngleSharp.
when_to_use: |
  - Trigger keywords: AddRazorPages, MapRazorPages, PageModel, @page, OnGet, OnPostAsync, [BindProperty], SupportsGet, RedirectToPage, IPageFilter, IPageRouteModelConvention, AddPageRoute, PageRouteTransformerConvention, AuthorizePage, AuthorizeFolder, AllowAnonymousToPage, IAntiforgery.GetAndStoreTokens, asp-page, asp-page-handler.
  - Task shapes: scaffold `Pages/{Folder}/{Name}.cshtml` + `.cshtml.cs`; design route templates via `@page`; wire named handlers via `asp-page-handler`; convert a controller filter into a page filter; require authorization on a folder; generate URLs across areas; debug an antiforgery POST in tests.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Pages/**/*.cshtml", "**/Pages/**/*.cshtml.cs", "**/Areas/**/Pages/**/*.cshtml", "**/Areas/**/Pages/**/*.cshtml.cs", "**/Program.cs", "**/*.csproj"]
---

# ASP.NET Core Razor Pages — Reference

Reference for ASP.NET Core 10 Razor Pages. Razor Pages share a great deal with MVC (Razor syntax, Tag Helpers, model binding, validation, view components, partials). This skill covers what is **unique to Razor Pages**; defer to `aspnet-core-mvc` for shared topics.

## Mental model

- A **Razor Page** is a self-routed `.cshtml` file with `@page` as the first directive. The optional `.cshtml.cs` code-behind is a `PageModel` subclass; the page implicitly has `@model` of that type.
- Each request matches one **page handler**: `On[Async]<HttpVerb>[HandlerName][Async]`. The handler name is the text between `On<Verb>` and (optional) `Async`.
- **No controller, no action.** Filters that act on actions (`IActionFilter`) are ignored by Razor Pages — use `IPageFilter` / `IAsyncPageFilter` instead.
- Routing is page-relative by default — `RedirectToPage("./Index")`, `("../Index")`, `("Index")`, `("/Index")` mean different things; the leading character matters.
- `[BindProperty]` does NOT bind on GET by default (security). Opt in with `SupportsGet = true`.

## Non-negotiable rules

1. **`@page` is the first directive on the page.** Without it the file is a partial / view, not a Razor Page.
2. **Don't use MVC `IActionFilter` on Razor Pages — it is ignored.** Use `IPageFilter` / `IAsyncPageFilter`. (`IAuthorizationFilter`, `IResourceFilter`, `IExceptionFilter`, `IResultFilter` DO work.)
3. **Page handlers are sync OR async — implement only one form.** The `Async` suffix is convention only.
4. **`[BindProperty]` skips GET** by default; opt in with `SupportsGet = true`.
5. **`/Pages/_ViewImports.cshtml` does NOT cross into `/Areas/<Area>/Pages/`.** Each area's pages root needs its own `_ViewImports.cshtml`.
6. **`AllowAnonymous*` wins over `Authorize*`** when both apply to the same path. `AuthorizeFolder + AllowAnonymousToPage` works; `AllowAnonymousToFolder + AuthorizePage` does NOT.
7. **`MapRazorPages()` after `UseRouting()` and `UseAuthorization()`.** Wire `UseAuthentication()` before `UseAuthorization()`.
8. **Named handler URL** = `?handler=Foo` by default. Add `{handler?}` to `@page` to put it in the URL path instead.

## Project bootstrap

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();
```

`AddRazorPages()` returns an `IMvcBuilder`-compatible builder; chain `.AddMvcOptions(...)`, `.AddJsonOptions(...)`, `.AddRazorPagesOptions(...)`, `.AddXmlSerializerFormatters()`, `.AddViewOptions(...)`. `MapStaticAssets().WithStaticAssets()` is the .NET 10 optimized static-asset pipeline (replaces `UseStaticFiles()` in templates; both still work).

Default project layout:

```
RazorPagesMovie/
  Pages/
    Shared/_Layout.cshtml, _ValidationScriptsPartial.cshtml
    _ViewImports.cshtml, _ViewStart.cshtml
    Error.cshtml(.cs), Index.cshtml(.cs), Privacy.cshtml(.cs)
  wwwroot/
  Program.cs, *.csproj (SDK="Microsoft.NET.Sdk.Web")
```

URL → file mapping: `/Pages/Index.cshtml` → `/`, `/Index`. `/Pages/Store/Contact.cshtml` → `/Store/Contact`. `/Pages/Store/Index.cshtml` → `/Store`, `/Store/Index`.

## PageModel pattern

Two-file convention (preferred):

```cshtml
@* Pages/Index.cshtml *@
@page
@model IndexModel
<h2>@Model.Message</h2>
```

```csharp
public class IndexModel : PageModel
{
    public string Message { get; private set; } = "PageModel in C#";
    public void OnGet() => Message += $" Server time is {DateTime.Now}";
}
```

`PageModel` provides: `HttpContext`, `Request`, `Response`, `RouteData`, `User`, `ModelState`, `TempData`, `ViewData`, `PageContext`, `Url`, `MetadataProvider`, plus helpers `Page()`, `Partial(...)`, `RedirectToPage(...)` family, `NotFound()`, `BadRequest()`, `Unauthorized()`, `Forbid()`, `Challenge()`, `SignIn()` / `SignOut()`, `Content()`, `File()` / `PhysicalFile()`, `LocalRedirect(...)`, `StatusCode(...)`, `TryUpdateModelAsync(...)`, `TryValidateModel(...)`.

Single-file form (uncommon — prefer two-file):

```cshtml
@page
@functions {
    public string Message { get; set; } = "Hello";
    public void OnGet() => Message += " from OnGet";
}
<h2>@Message</h2>
```

## Page handlers

Naming: `On[Async]<HttpVerb>[HandlerName][Async]`. Handler returning `void` / `Task` renders the page implicitly (= `return Page();`). Examples:

| Method | Verb | Handler |
|---|---|---|
| `OnGet` / `OnGetAsync` | GET | `""` (default) |
| `OnPost` / `OnPostAsync` | POST | `""` |
| `OnPostJoinList` / `OnPostJoinListAsync` | POST | `JoinList` |
| `OnHead` (falls back to `OnGet`) | HEAD | `""` |
| `OnPutAsync`, `OnDeleteAsync` | PUT/DELETE | `""` |

Return type helpers: `Page()` (`PageResult`), `RedirectToPage(name, route?)` (`RedirectToPageResult`), `RedirectToPagePermanent(...)` (301), `RedirectToPagePreserveMethod(...)` (307), `Redirect(url)` / `LocalRedirect(url)` (302), `NotFound`, `BadRequest`, `Unauthorized`, `Forbid`, `Challenge`, `Content(text, ct)`, `File(...)`, `Partial("_Name", model)`, `StatusCode(int)`, `SignIn(...)` / `SignOut(...)`.

Common shape:

```csharp
public class CreateModel(CustomerDbContext context) : PageModel
{
    public IActionResult OnGet() => Page();

    [BindProperty] public Customer? Customer { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        if (Customer is not null) context.Customer.Add(Customer);
        await context.SaveChangesAsync();
        return RedirectToPage("./Index");
    }
}
```

Named handlers + form button selection:

```cshtml
<form method="POST">
    <div><label>Name: <input asp-for="Customer.Name" /></label></div>
    <input type="submit" asp-page-handler="JoinList"   value="Join" />
    <input type="submit" asp-page-handler="JoinListUC" value="JOIN UC" />
</form>
```

```csharp
public async Task<IActionResult> OnPostJoinListAsync() { /* ... */ return RedirectToPage("/Index"); }
public async Task<IActionResult> OnPostJoinListUCAsync() { Customer.Name = Customer.Name?.ToUpperInvariant(); return await OnPostJoinListAsync(); }
```

Default URLs: `/Customers/CreateFATH?handler=JoinList`. Add `@page "{handler?}"` to put the handler in the URL path: `/Customers/CreateFATH/JoinList`.

HEAD fallback: missing `OnHead` → `OnGet` runs. Provide a custom HEAD when you need to write headers without producing a body.

## Routing

`@page` must be the first Razor directive. Forms (for a page at `/Pages/Customers/Edit.cshtml`):

| `@page` | Route |
|---|---|
| `@page` | `/Customers/Edit` |
| `@page "{id:int}"` | `/Customers/Edit/{id:int}` |
| `@page "{id:int?}"` | `/Customers/Edit/{id:int?}` |
| `@page "{handler?}"` | `/Customers/Edit/{handler?}` |
| `@page "item"` | `/Customers/Edit/item` (literal segment append) |
| `@page "/Some/Other/Path"` or `@page "~/Some/Other/Path"` | absolute override |

Route constraints same as `aspnet-core-fundamentals` § routing. Combine with `:` (e.g., `@page "{id:int:min(1)?}"`).

Programmatic config:

```csharp
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/MyPages";                       // change root from /Pages
    options.Conventions.AuthorizeFolder("/MyPages/Admin");
});
```

Helpers: `.WithRazorPagesAtContentRoot()` (no `Pages/` folder); `.WithRazorPagesRoot("/path")`.

Page-name URL generation — for a page at `Pages/Customers/Create.cshtml`:

| Call | Resolves to |
|---|---|
| `RedirectToPage("/Index")` | `/Pages/Index` (root-relative — leading `/`) |
| `RedirectToPage("./Index")` | `/Pages/Customers/Index` (current folder) |
| `RedirectToPage("../Index")` | `/Pages/Index` (parent) |
| `RedirectToPage("Index")` | `/Pages/Customers/Index` (sibling) |

Cross-area: `RedirectToPage("/Index", new { area = "Services" })`. URL string: `Url.Page("./Index", routeValues)`. Reserved route parameter names: `action`, `area`, `controller`, `handler`, `page`, `pagehandler`.

## Razor syntax (Pages-relevant)

Implicit `@expr`, explicit `@(expr)`. Implicit cannot use C# generics — wrap in explicit form. C# `string` → HTML-encoded; `IHtmlContent` → raw; `@Html.Raw(...)` bypasses encoding (XSS risk — never with unsanitized input). Escape literal `@` with `@@`. Single-line `@:Name: @people[i].Name` and explicit `<text>` block for non-HTML markup. `@if` / `@switch` / `@for` / `@foreach` / `@while` / `@do` / `@try` / `@lock`. Razor comments `@* ... *@` are server-stripped.

Razor omits attributes whose value is `null` or `false` (does NOT apply to `data-*` attributes — those are kept verbatim):

```cshtml
<input type="checkbox" checked="@true"/>   <!-- checked="checked" -->
<input type="checkbox" checked="@false"/>  <!-- attribute omitted -->
<div data-id="@null" data-active="@false"></div>   <!-- preserved -->
```

Directives in `.cshtml`: `@page`, `@model T`, `@namespace X.Y`, `@using ...`, `@inject T Name`, `@inherits TypeName`, `@attribute [Attr]`, `@implements IFoo`, `@functions { ... }`, `@section Name { ... }`, `@addTagHelper *, AssemblyName`, `@removeTagHelper *, AssemblyName`, `@tagHelperPrefix th:`. Razor-component-only directives (NOT used in Razor Pages): `@code`, `@layout`, `@preservewhitespace`, `@rendermode`, `@typeparam`, `@bind`, `@key`, `@ref`, `@attributes`, `@formname`, `@on{EVENT}`.

`_ViewImports.cshtml` combined behavior: `@addTagHelper` / `@removeTagHelper` (all run, in order); `@tagHelperPrefix` / `@inject` / `@model` / `@inherits` / `@namespace` (closest to view wins); `@using` (all included; duplicates ignored). Functions and section definitions NOT allowed.

## Layout, sections, view files

`_Layout.cshtml` — `RenderBody()` exactly once; `RenderSection("Name", required: bool)` / `RenderSectionAsync(...)`. `IgnoreBody()` / `IgnoreSection("Name")` skip rendering and suppress "must render" enforcement. Sections defined in a page are visible **only** to the immediate layout. `_ViewStart.cshtml` runs before every full page (not partials, not layouts). Defaults to `@{ Layout = "_Layout"; }`.

ViewData / ViewBag / `[ViewData]` work the same as MVC — see `aspnet-core-mvc` § views. `TempData` survives one redirect; backed by cookie (default) or session.

Tag Helpers, partials, view components — same primitives as MVC; refer to `aspnet-core-mvc` § tag-helpers, § partials, § view-components. Razor-Pages-specific: the `<form method="post">` Tag Helper auto-injects `__RequestVerificationToken`; `<a asp-page="...">` and `<a asp-area="..." asp-page="...">` for cross-page / cross-area links; `<input type="submit" asp-page-handler="HandlerName" />` to target a named handler. View component search path adds `/Pages/Components/<Name>/Default.cshtml` (in addition to MVC paths).

## Model binding (Razor-Pages-specific)

Default source order for handler parameters and `[BindProperty]` properties: form fields → route data → query string → uploaded files. Body (`[FromBody]`) only when annotated; never default for Razor Pages.

```csharp
[BindProperty]                      public Customer? Customer { get; set; }
[BindProperty(SupportsGet = true)]  public string? SearchString { get; set; }   // opt-in for GET
[BindProperty(Name = "ai_user", SupportsGet = true)] public string? Cookie { get; set; }
```

Class-wide: `[BindProperties]`. Source attributes (`[FromQuery]` / `[FromRoute]` / `[FromForm]` / `[FromBody]` / `[FromHeader]` / `[FromServices]`), property-level binding rules (`[BindRequired]`, `[BindNever]`, `[Bind("A,B,C")]`, `[Bind(Prefix = "X")]`), records, collections/dictionaries, special parameters (`CancellationToken`, `IFormCollection`, `IFormFile`/`IFormFileCollection`), `IParsable<T>` custom binding, custom binders (`IModelBinder` / `IModelBinderProvider`), custom value providers, globalization rules, manual `TryUpdateModelAsync` — all identical to MVC; see `aspnet-core-mvc` § model-binding.

`[FromBody]` rules: body stream is read once → only one parameter may be `[FromBody]`; nested `[FromXxx]` on properties of a `[FromBody]`-bound type are ignored.

Validation — DataAnnotations + `IValidatableObject` + custom `ValidationAttribute` + client-side `IClientModelValidator` + `AttributeAdapterBase<T>` + `IValidationAttributeAdapterProvider` + `[ValidateNever]` + manual `ModelState.AddModelError` + `TryValidateModel` — all identical to MVC; see `aspnet-core-mvc` § validation. Razor-Pages-specific scripts: `@section Scripts { <partial name="_ValidationScriptsPartial" /> }` brings in `jquery.validate.js` + `jquery.validate.unobtrusive.js`. Disable client validation: `.AddViewOptions(o => o.HtmlHelperOptions.ClientValidationEnabled = false)`.

## Page filters

| Interface | Methods |
|---|---|
| `IPageFilter` | `OnPageHandlerSelected`, `OnPageHandlerExecuting`, `OnPageHandlerExecuted` |
| `IAsyncPageFilter` | `OnPageHandlerSelectionAsync`, `OnPageHandlerExecutionAsync` |

Lifecycle per request: `OnPageHandlerSelected[Async]` (handler chosen, BEFORE binding) → model binding → `OnPageHandlerExecuting` (or first half of async) → handler body → `OnPageHandlerExecuted` (or post-`await next()`).

Implement either sync OR async (not both — async wins if both present). **Cannot be applied to a single page handler method** — only page-level / global. For per-handler logic, use a `ResultFilterAttribute`-derived attribute on the page model.

```csharp
public class SampleAsyncPageFilter(IConfiguration config) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext ctx)
    {
        // log user-agent, etc.
        return Task.CompletedTask;
    }
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext ctx, PageHandlerExecutionDelegate next)
    {
        // pre
        await next.Invoke();
        // post
    }
}
builder.Services.AddRazorPages()
    .AddMvcOptions(o => o.Filters.Add(new SampleAsyncPageFilter(builder.Configuration)));
```

Folder-scoped via convention:

```csharp
builder.Services.AddRazorPages(options =>
    options.Conventions.AddFolderApplicationModelConvention("/Movies",
        model => model.Filters.Add(new SampleAsyncPageFilter(builder.Configuration))));
```

Override on a single PageModel — override `OnPageHandlerSelectionAsync` / `OnPageHandlerExecutionAsync` directly (`PageModel` implements `IAsyncPageFilter`).

Attribute-based via `ResultFilterAttribute`:

```csharp
public class AddHeaderAttribute(string name, string value) : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext c)
        => c.HttpContext.Response.Headers.Append(name, new[] { value });
}

[AddHeader("Author", "Rick")]
public class TestModel : PageModel { public void OnGet() { } }
```

**Available MVC filter types in Razor Pages:** Authorization, Resource, Exception, Result, plus Page filter. **MVC Action filters are IGNORED** (handlers replace actions). Use `ServiceFilterAttribute` / `TypeFilterAttribute` for DI-aware filters.

## Conventions API

`PageConventionCollection` exposed via `RazorPagesOptions.Conventions`. Convention interfaces:

| Interface | Stage | Use |
|---|---|---|
| `IPageRouteModelConvention` | Route model | Add/replace route templates. |
| `IPageApplicationModelConvention` | App model | Add filters / handler-level metadata. |
| `IPageHandlerModelConvention` | Handler model | Modify handler-level data (parameter binding, etc.). |

`AttributeRouteModel.Order`: `-1` (before defaults), `0` / `null` (default; specificity-based), `1+` (explicit). Avoid setting `Order` when possible.

```csharp
builder.Services.AddRazorPages(options =>
{
    options.Conventions.Add(new GlobalTemplatePageRouteModelConvention());
    options.Conventions.AddFolderRouteModelConvention("/OtherPages",  m => { /* mutate */ });
    options.Conventions.AddPageRouteModelConvention("/About",         m => { /* mutate */ });
    options.Conventions.AddPageRoute("/Contact", "TheContactPage/{text?}");           // alias route
    options.Conventions.AddFolderApplicationModelConvention("/OtherPages", m => { });
    options.Conventions.AddPageApplicationModelConvention("/About",        m => { });
    options.Conventions.ConfigureFilter(model => /* per-page filter selection */);
    options.Conventions.ConfigureFilter(new SomeFilterFactoryOrFilter());

    options.Conventions.Add(
        new PageRouteTransformerConvention(new SlugifyParameterTransformer()));
});
```

`PageRouteTransformerConvention` applies an `IOutboundParameterTransformer` to *automatically generated* route segments only — it does NOT touch `@page`-declared segments or `AddPageRoute` routes. `/Pages/SubscriptionManagement/ViewAll.cshtml` → `/subscription-management/view-all`.

`AddPageRoute` declares an alias that the page reaches via both routes; the page must still handle the additional segments via `@page "{text?}"`.

## Authorization

Attribute-based:

```csharp
[Authorize] public class SecureModel : PageModel { public IActionResult OnGet() => Page(); }
[Authorize(Roles = "Admin")]            public class AdminModel : PageModel { }
[Authorize(Policy = "AtLeast21")]       public class ContactModel : PageModel { }
```

`@attribute [Authorize]` directly in `.cshtml` is also valid.

Convention-based — paths are **View Engine paths** (Razor Pages root-relative WITHOUT `.cshtml` extension, forward-slash only). For areas, the path is relative to the area's pages root (e.g., `Areas/Identity/Pages/Manage/Accounts.cshtml` → `/Manage/Accounts`):

```csharp
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizePage("/Contact");
    options.Conventions.AuthorizePage("/Contact", "AtLeast21");          // with policy
    options.Conventions.AuthorizeFolder("/Private");
    options.Conventions.AuthorizeFolder("/Private", "AtLeast21");

    options.Conventions.AuthorizeAreaPage("Identity", "/Manage/Accounts");
    options.Conventions.AuthorizeAreaFolder("Identity", "/Manage", "AtLeast21");

    options.Conventions.AllowAnonymousToPage("/Private/PublicPage");
    options.Conventions.AllowAnonymousToFolder("/Private/PublicPages");
});
```

Combining rules:
- `AuthorizeFolder("/Private").AllowAnonymousToPage("/Private/Public")` — works.
- `AllowAnonymousToFolder("/Public").AuthorizePage("/Public/Private")` — DOES NOT work. When both apply, `AllowAnonymousFilter` wins.

Pipeline: `app.UseAuthentication()` BEFORE `app.UseAuthorization()`; `MapRazorPages()` after `UseAuthorization()`.

## Antiforgery (CSRF)

Razor Pages auto-validates the antiforgery token on POST/PUT/DELETE/PATCH. The `<form>` Tag Helper auto-injects `<input name="__RequestVerificationToken" type="hidden" value="..." />`. For pure HTML `<form>` (no Tag Helper), inject manually with `@Html.AntiForgeryToken()`.

Opt out a single page: decorate with `[IgnoreAntiforgeryToken]`. AJAX/JSON/SPA — call `IAntiforgery.GetAndStoreTokens(HttpContext)` (returns token + cookie pair), then send the header `RequestVerificationToken` with each unsafe request. Configure via `services.AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; })`.

## Areas

```
Areas/
  Identity/
    Pages/
      _ViewImports.cshtml
      Manage/Index.cshtml(.cs), Accounts.cshtml(.cs)
  Products/
    Pages/
      _ViewImports.cshtml
      About.cshtml(.cs), Index.cshtml(.cs)
```

`MapRazorPages()` discovers areas automatically (no extra wiring). Root `/Pages/_ViewImports.cshtml` is **NOT** imported into area pages — provide one per area `Pages/` folder (or in app root). Place `_ViewStart.cshtml` in app root for one shared layout app-wide. Page name in conventions is relative to the area's pages root.

Link generation uses `asp-area`:

```cshtml
<a asp-area="Products" asp-page="/About">Products About</a>
<a asp-area="" asp-page="/About">root /About</a>            @* explicit "" exits area *@
<a href='@Url.Page("/Manage/About", new { area = "Services" })'>Services Manage About</a>
```

Ambient area gotcha: when `asp-area` is omitted, the current request's area is the default — links may resolve incorrectly across areas. Always set `asp-area` explicitly for cross-area links.

Change the default area folder name — set `RazorViewEngineOptions.AreaViewLocationFormats` (placeholders `{0}` view, `{1}` controller, `{2}` area).

## CSS isolation + collocated JS

`Pages/Index.cshtml.css` next to `Pages/Index.cshtml` emits per-page CSS with attribute selectors (`h1[b-3xxtam6d07] { color: red; }`) and the rendered HTML element gets the matching scope attribute (`<h1 b-3xxtam6d07>`). The bundle is referenced once in the layout: `<link rel="stylesheet" href="WebApp.styles.css" />`.

Collocated JS: `Pages/Index.cshtml.js` next to `Pages/Index.cshtml`, then `@section Scripts { <script src="~/Pages/Index.cshtml.js"></script> }` (or generic in layout: `<script asp-src-include="@(ViewContext.View.Path).js"></script>`). On publish the framework moves these scripts to the web root automatically; URLs don't change.

## Scaffolding

```bash
dotnet tool install --global dotnet-aspnet-codegenerator
dotnet add package Microsoft.VisualStudio.Web.CodeGeneration.Design
dotnet aspnet-codegenerator razorpage -m Movie -dc RazorPagesMovieContext \
    -udl -outDir Pages/Movies --referenceScriptLibraries
```

Flags (subset): `-m <model>`, `-dc <DbContext>`, `-outDir <path>`, `-udl` (default layout), `--referenceScriptLibraries` (`_ValidationScriptsPartial`), `--useSqlite`, `--noTypeInfo`. Generates `Index`/`Create`/`Edit`/`Delete`/`Details` pages plus DbContext registration.

## Testing (Razor-Pages-specific notes)

PageModel unit tests — when code reads `User`, `Url`, `TempData`, set up a fake `PageContext`:

```csharp
var http = new DefaultHttpContext();
var modelState = new ModelStateDictionary();
var actionContext = new ActionContext(http, new RouteData(), new PageActionDescriptor(), modelState);
var pageContext = new PageContext(actionContext) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState) };
var page = new IndexModel(mockDb.Object)
{
    PageContext = pageContext,
    TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()),
    Url = new UrlHelper(actionContext)
};
page.ModelState.AddModelError("Message.Text", "Required");
var result = await page.OnPostAddMessageAsync();
Assert.IsType<PageResult>(result);
```

Integration tests — `WebApplicationFactory<Program>` (mark `partial class Program {}` after top-level statements). Antiforgery-aware POST helper: `HtmlHelpers.GetDocumentAsync(...)` + `HttpClient.SendAsync(IHtmlFormElement, IHtmlButtonElement, ...)` extract the token from rendered HTML and replay it (canonical sample uses **AngleSharp**). `WebApplicationFactoryClientOptions.AllowAutoRedirect = false` to assert redirects manually. Mock auth handler — register an `AuthenticationHandler<AuthenticationSchemeOptions>` that returns a successful `AuthenticationTicket` and set `client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme")`.

Detailed integration-test infrastructure (single `Company.Product.Test`, areas-as-folders, in-class `[ClassInitialize]` server) → `dotnet-testing`.

## Quick decision matrix

| Question | Answer |
|---|---|
| New page-oriented UI | Razor Pages (this skill) |
| New JSON API | Controllers (`aspnet-core-http-apis`) |
| Multiple submit buttons targeting different server logic | Named handlers + `asp-page-handler` |
| Authorize one folder of pages | `options.Conventions.AuthorizeFolder("/Private")` |
| Bind a property on GET (e.g., search box) | `[BindProperty(SupportsGet = true)]` |
| Render a partial that needs to fetch data | View Component, not partial |
| Slugify auto-generated routes | `PageRouteTransformerConvention(new SlugifyParameterTransformer())` |
| Add a custom HTTP header to every page response | `IPageApplicationModelConvention` adding a `ResultFilterAttribute` to every model |
| Disable antiforgery on one page | `[IgnoreAntiforgeryToken]` on the PageModel |
| URL alias for a page | `options.Conventions.AddPageRoute("/Contact", "TheContactPage/{text?}")` |
| Per-handler logic that needs to inspect parameters | `ResultFilterAttribute` (page-level) — page filters can't be per-handler |

## Cross-references

- Public docs (Razor Pages): https://learn.microsoft.com/en-us/aspnet/core/razor-pages/?view=aspnetcore-10.0
- Razor SDK: https://learn.microsoft.com/en-us/aspnet/core/razor-pages/sdk?view=aspnetcore-10.0
- Conventions: https://learn.microsoft.com/en-us/aspnet/core/razor-pages/razor-pages-conventions?view=aspnetcore-10.0
- Filters: https://learn.microsoft.com/en-us/aspnet/core/razor-pages/filter?view=aspnetcore-10.0
- Authorization conventions: https://learn.microsoft.com/en-us/aspnet/core/razor-pages/security/authorization/conventions?view=aspnetcore-10.0
- Razor syntax: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor?view=aspnetcore-10.0
- Tag Helpers intro: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/tag-helpers/intro?view=aspnetcore-10.0
- Model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0
- Validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0
- Related: `aspnet-core-mvc` — Razor syntax details, Tag Helpers, partials, view components, DEEP model binding + validation surface.
- Related: `aspnet-core-fundamentals` — middleware, DI, options, routing primitives, error handling.
- Related: `aspnet-core-http-apis` — when an endpoint needs to return JSON.
- Related: `aspnet-core-blazor` — `.razor` components (`@code`, `@layout`, `@rendermode`, `@bind`); Tag Helpers do NOT apply.
- Related: `aspnet-core-security` — cookie / OIDC / Identity UI; antiforgery beyond the Tag Helper.
- Related: `dotnet-testing` — single integration-test project layout, areas-as-folders, `WebApplicationFactory<T>` setup.
- Related: `dotnet-conventions` — banned third-party libs (e.g., FluentValidation gated).
