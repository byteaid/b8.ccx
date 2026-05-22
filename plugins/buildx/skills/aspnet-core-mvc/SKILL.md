---
name: aspnet-core-mvc
description: ASP.NET Core 10 MVC reference for controllers-with-views (presentation stack, NOT Web APIs). Covers controller discovery, action selectors/verbs, conventional + attribute routing + `IOutboundParameterTransformer`, link generation via `IUrlHelper`/`LinkGenerator`, areas, the filter pipeline (Authorization → Resource → ModelBinding → Action → Result), `IServiceFilter`/`ITypeFilter`/`IFilterFactory`, application-model conventions, Razor view engine (`_Layout`/`_ViewStart`/`_ViewImports`, partials, view components, CSS isolation), Tag Helpers + authoring, model-binding (`[BindProperty]`/`[BindNever]`, `IModelBinder`/`IModelBinderProvider`, value providers, `IParsable<T>`), DataAnnotations + `IValidatableObject` + `IClientModelValidator`, `WebApplicationFactory<T>` testing.
when_to_use: |
  - Trigger keywords: AddControllersWithViews, MapControllerRoute, ViewResult, ViewComponent, .cshtml, _Layout, RenderSection, ViewData, TempData, LinkGenerator, IOutboundParameterTransformer, [Area], IActionFilter, IExceptionFilter, ServiceFilter, IFilterFactory, IModelBinder, IValueProvider, BindProperty, IValidatableObject, IClientModelValidator, TagHelper, [HtmlTargetElement], partial, view component, CSS isolation.
  - Task shapes: scaffold an MVC controller with views; design a route table mixing conventional + attribute routes; partition by areas; write a custom action/exception filter via DI; choose partial vs view component; write a custom Tag Helper; write a custom model binder; add a validation attribute with client-side validation.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Controllers/**/*.cs", "**/Views/**/*.cshtml", "**/Areas/**/*.cs", "**/Areas/**/*.cshtml", "**/*.csproj"]
---

# ASP.NET Core MVC — Reference (Controllers + Views)

Reference for ASP.NET Core 10 MVC: controllers serving Razor views. For pure HTTP APIs see `aspnet-core-http-apis`.

## Mental model

- **M = Model** (POCO, no dependency on V or C). **V = Razor view** under `Views/{Controller}/`. **C = Controller** (per-request, DI-activated, picks model + view).
- `AddControllersWithViews()` registers MVC + the Razor view engine + Tag Helpers. `AddMvc()` = `AddControllersWithViews()` + `AddRazorPages()`.
- An MVC view returns HTML — no content negotiation. Web API controllers (no views) live in `aspnet-core-http-apis`.
- Filters run *inside* the MVC action invocation, not outside it (unlike middleware): Authorization → Resource → ModelBinding → Action → Result. `IExceptionFilter` wraps Action + Result only — NOT middleware/routing.
- The application model is a metadata graph built once at startup; conventions mutate it before requests start.

## Non-negotiable rules

1. **`Controller` for MVC views; `ControllerBase` for Web APIs.** `Controller` adds `View()`, `PartialView()`, `ViewData`, `ViewBag`.
2. **Don't mix conventional and attribute routing on the same action.** A `[Route]` on a controller makes every action attribute-routed.
3. **Don't synchronously render partials.** Use the `<partial>` Tag Helper or `Html.PartialAsync`. `Html.Partial` / `Html.RenderPartial` emit analyzer warnings and may deadlock.
4. **Routes that start with `/` or `~/` do NOT combine with the controller route.**
5. **Filter attributes can't take DI services in their constructor.** Use `[ServiceFilter<T>]`, `[TypeFilter<T>]`, or `IFilterFactory`.
6. **Areas must be matched first.** `app.MapControllerRoute("MyArea", "{area:exists}/{controller=Home}/{action=Index}/{id?}")` BEFORE the default route.
7. **`_ViewImports.cshtml` does NOT cross from `/Views` into `/Areas/<Area>/Views`.** Place a copy in each area, or in the application root.
8. **Custom `ValidationAttribute` doesn't run client-side automatically.** Implement `IClientModelValidator` (or use `AttributeAdapterBase<T>` + `IValidationAttributeAdapterProvider`) and add a JS adapter for `jquery-validation-unobtrusive`.

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();    // MVC + Razor + Tag Helpers
var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
```

## Controllers

A class is a controller if any: name has `Controller` suffix, inherits from such a class, or has `[Controller]`. `[NonController]` excludes. Public methods that are not `[NonAction]` are actions. `[ApiController]` opts into API conventions (auto-400 + binding inference + ProblemDetails) — see `aspnet-core-http-apis`.

Action return types:
- `IActionResult` / `Task<IActionResult>` — explicit result type, full control.
- `ActionResult<T>` — Web API; either `T` (200) or any derived `ActionResult`.
- A bare CLR object — formatted by content negotiation (Web API).
- `void` / `Task` — empty 200.

`Controller` helpers (subset): `View()` / `View(model)` / `View("name", model)`, `PartialView(...)`, `ViewComponent(...)`, `Json(value)`, `Content(text, contentType)`, `File(...)` / `PhysicalFile(...)` / `VirtualFile(...)`, plus the `ControllerBase` status-code + redirect family (`Ok`, `NotFound`, `RedirectToAction`, `RedirectToRoute`, etc. — see `aspnet-core-http-apis`).

Action selectors: `[HttpGet/Post/Put/Delete/Patch/Head/Options]`, `[AcceptVerbs("GET","POST")]`, `[ActionName("Foo")]` (rename), `[NonAction]` (public method that's not an action), `[Consumes("application/xml")]` (filter by `Content-Type`), `[Produces("application/json")]` (constrain output formatter), `[RequireHttps]`, `[FormatFilter]` (URL-suffix selection).

Lifetime — controllers are activated per request by `DefaultControllerActivator`. Constructor params resolve via DI. `AddControllersAsServices()` registers controllers themselves in DI for custom `IControllerActivator` / arbitrary lifetimes (configure `ApplicationPartManager` first).

## Routing

Conventional:

```csharp
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapDefaultControllerRoute();   // shorthand

// Multiple routes — specific first
app.MapControllerRoute("blog", "blog/{*article}", defaults: new { controller = "Blog", action = "Article" });
```

Tokens / constraints same as `aspnet-core-fundamentals` § routing. Route names have no matching effect — only used by `IUrlHelper.RouteUrl` / `LinkGenerator.GetPathByName`.

Attribute routing:

```csharp
[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    [HttpGet]                                    public IActionResult List() => Ok();
    [HttpGet("{id:int}")]                        public IActionResult Get(int id) => Ok();
    [HttpGet("by-name/{name:alpha}")]            public IActionResult ByName(string name) => Ok();
    [HttpPost("/products", Name = "Create")]     public IActionResult Create([FromBody] Product p)  // leading "/" => no combine
        => CreatedAtRoute("Create", new { id = p.Id }, p);
}
```

Token replacement at startup: `[controller]`, `[action]`, `[area]`. Use `[[` / `]]` for literal brackets. `Order` controls priority (default 0, lower runs first) — prefer route names over `Order` for disambiguation.

Reserved route parameter names: `action`, `area`, `controller`, `handler`, `page`. Reserved Razor keywords: `page`, `using`, `namespace`, `inject`, `section`, `inherits`, `model`, `addTagHelper`, `removeTagHelper`.

Slugify controller/action names via `IOutboundParameterTransformer`:

```csharp
public class SlugifyParameterTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) => value is null ? null
        : Regex.Replace(value.ToString()!, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
}
builder.Services.AddControllersWithViews(o =>
    o.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer())));
// SubscriptionManagementController.ListAll => /subscription-management/list-all
```

URL generation — inside a controller use `Url.Action("Buy", "Products", new { id = 17 })` / `Url.RouteUrl("Name", new { id })`. Outside, inject `LinkGenerator`. In Razor, prefer Tag Helpers (`<a asp-controller asp-action asp-route-id>`).

## Areas

Logical partition adding an `area` route value. Default folder layout:

```
Areas/
  Products/
    Controllers/ManageController.cs
    Views/Manage/Index.cshtml
    Views/Shared/_Layout.cshtml
```

```csharp
[Area("Products")]
public class ManageController : Controller { public IActionResult Index() => View(); }
```

Routing — area routes FIRST:

```csharp
app.MapControllerRoute("MyArea", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute("default",                "{controller=Home}/{action=Index}/{id?}");
// or per-area:
app.MapAreaControllerRoute("MyAreaProducts", "Products", "Products/{controller=Home}/{action=Index}/{id?}");
```

`{area:exists}` constrains the segment to a registered area name. View-discovery order in areas: `/Areas/<Area>/Views/<Controller>/<Action>.cshtml` → `/Areas/<Area>/Views/Shared/<Action>.cshtml` → `/Views/Shared/<Action>.cshtml` → `/Pages/Shared/<Action>.cshtml`.

`/Views/_ViewImports.cshtml` does NOT apply to area views — copy into `/Areas/<Area>/Views/_ViewImports.cshtml` or place in app root. `_ViewStart.cshtml` in app root works app-wide.

Link generation — `<a asp-area="Products">`; `asp-area=""` (explicit empty) exits an area; ambient area is sticky inside an area-tagged controller — pass `new { area = "" }` to leave.

## Filters

Stages: Authorization → Resource → ModelBinding → Action → ResultExecution. `IExceptionFilter` wraps Action + Result only (NOT middleware/routing). `IAlwaysRunResultFilter` runs even when an earlier filter short-circuited.

Convenience bases: `ActionFilterAttribute` (action + result + ordered), `ExceptionFilterAttribute`, `ResultFilterAttribute`, `ServiceFilterAttribute`, `TypeFilterAttribute`.

Sync vs async — implement only ONE form of each filter type:

```csharp
public class SampleAsyncActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext c, ActionExecutionDelegate next)
    {
        // before
        var executed = await next();   // executes action and subsequent filters
        // after — executed.Result, executed.Exception
    }
}
```

Scopes + ordering — Global → Controller → Action (before-code); reverse for after-code. `IOrderedFilter.Order` overrides scope; lower runs first.

```csharp
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add<GlobalSampleActionFilter>();
    o.Filters.Add<GlobalSampleActionFilter2>(int.MinValue);   // explicit Order
});
```

Short-circuit: auth/resource/action filter sets `context.Result`; async result filter sets `context.Cancel = true` and skips `next()`; exception filter sets `context.ExceptionHandled = true` and assigns `context.Result`.

Filter attributes can't take DI services in the constructor — use one of:

- `[ServiceFilter<T>]` — filter registered in DI; DI selects constructor.
- `[TypeFilter<T>]` — filter NOT in DI; DI fulfils ctor params; you can pass extra `Arguments`.
- `IFilterFactory` — emit a filter from a plain attribute (`IsReusable = true` only when stateless and singleton-safe).

Filter context fields worth knowing: `ActionExecutingContext.ActionArguments` (mutable), `Controller`, `Result`; `ActionExecutedContext.Result` / `Canceled` / `Exception` / `ExceptionDispatchInfo` / `ExceptionHandled`.

## Application model + conventions

Hierarchy: `ApplicationModel` → `ControllerModel` → `ActionModel` → `ParameterModel`. Each has a `Properties` dictionary that cascades into `ActionDescriptor.Properties`, accessible via `ControllerContext.ActionDescriptor.Properties`. **Writes after startup are not thread-safe.**

Two extension surfaces:
- `IApplicationModelProvider` — framework-level, for framework authors.
- Conventions — `IApplicationModelConvention` / `IControllerModelConvention` / `IActionModelConvention` / `IParameterModelConvention`. Apply globally via `MvcOptions.Conventions.Add(...)` OR as attributes on controllers/actions/parameters.

```csharp
public class MustBeInRouteParameterModelConvention : Attribute, IParameterModelConvention
{
    public void Apply(ParameterModel m)
    { m.BindingInfo ??= new(); m.BindingInfo.BindingSource = BindingSource.Path; }
}

// Namespace-as-route convention — produces /MyApp/Controllers/Foo/Bar/{id?}
public class NamespaceRoutingConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel app)
    {
        foreach (var c in app.Controllers)
        {
            if (c.Selectors.Any(s => s.AttributeRouteModel != null) || c.ControllerType.Namespace is null) continue;
            c.Selectors[0].AttributeRouteModel = new AttributeRouteModel
                { Template = c.ControllerType.Namespace.Replace('.', '/') + "/[controller]/[action]/{id?}" };
        }
    }
}
```

`ApiExplorerModel.IsVisible` controls Swagger / OpenAPI exposure — flip via convention.

## Views — Razor

Discovery order: `/Views/<Controller>/<View>.cshtml` → `/Views/Shared/<View>.cshtml` → (partials only) `/Pages/Shared/<View>.cshtml`. `return View()` uses the action method name. Customize via `IViewLocationExpander` or `RazorViewEngineOptions.ViewLocationFormats`.

Strongly-typed view: `@model WebApp.ViewModels.Address` then `@Model.Street`. Weakly-typed: `ViewData["Key"]` (strings need no cast; other types do); `ViewBag.Foo` (`dynamic` over `ViewData`); `[ViewData] public string Title { get; set; }` on the controller writes through.

`_Layout.cshtml` — typically `Views/Shared/_Layout.cshtml`. Must call `RenderBody()` exactly once. `RenderSection("Name", required: bool)` / `RenderSectionAsync(...)`. `IgnoreBody()` and `IgnoreSection("Name")` skip rendering and suppress "must render" enforcement. Sections defined in a content view are visible **only** to the immediate layout — not to partials, view components, or nested layouts.

`_ViewStart.cshtml` runs before every full view (not partials, not layouts). Hierarchical: root first, then deeper folder. Typical body: `@{ Layout = "_Layout"; }`.

`_ViewImports.cshtml` — hierarchical, additive, applied root-first then folder-by-folder. Combined behavior: `@addTagHelper` / `@removeTagHelper` (all run, in order); `@tagHelperPrefix` / `@inject` / `@model` / `@inherits` / `@namespace` (closest to view wins); `@using` (all included; duplicates ignored). Functions and section definitions NOT allowed.

CSS isolation — `Component.cshtml.css` next to a view emits `{Assembly}.styles.css`; selectors get a `b-{string}` scope id. Reference via `<link rel="stylesheet" href="~/{APP ASSEMBLY}.styles.css" />`. Per-file scope override via MSBuild item metadata `CssScope`. Disable: `<DisableScopedCssBundling>true</DisableScopedCssBundling>`. **Does NOT apply to Tag Helpers.**

## Partials

`.cshtml` without `@page` rendered inside another markup file. Does NOT run `_ViewStart`. Sections defined in a partial don't escape it.

```cshtml
<partial name="_PartialName" />
<partial name="~/Views/Folder/_PartialName.cshtml" />
<partial name="_PartialName" model="someModel" view-data="ViewData" for="@Model.ChildProperty" />

@await Html.PartialAsync("_PartialName", model)
@await Html.PartialAsync("_PartialName", model, new ViewDataDictionary(ViewData) { { "k", v } })
```

Discovery: `/Areas/<Area>/Views/<Controller>` → `/Areas/<Area>/Views/Shared` → `/Views/Shared` → `/Pages/Shared`. Caller folder wins over `Shared`.

A partial receives a **copy** of the parent's `ViewData`; mutations don't propagate. To pass extra data while keeping parent data: `new ViewDataDictionary(ViewData) { { "index", index } }`. **Use a view component, not a partial, when you need code execution / data access.**

## View components

Reusable UI widget with associated logic. Not bound by HTTP, not an endpoint, no model binding, no filters.

```csharp
public class PriorityListViewComponent(ToDoContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int maxPriority, bool isDone)
    {
        var items = await db.ToDo.Where(x => x.IsDone == isDone && x.Priority <= maxPriority).ToListAsync();
        return View(items);          // Default.cshtml
    }
}
```

A class qualifies if it inherits `ViewComponent`, has `[ViewComponent]`, or its name ends in `ViewComponent`. Must be public, non-nested, non-abstract. Default name = class name minus suffix. Opt out with `[NonViewComponent]`.

View search path: `/Views/<Controller>/Components/<ViewComponentName>/<ViewName>.cshtml` → `/Views/Shared/Components/...` → `/Pages/Shared/Components/...` → `/Areas/<Area>/Views/Shared/Components/...`. Default `<ViewName>` = `Default`.

Invocation:

```cshtml
@await Component.InvokeAsync("PriorityList", new { maxPriority = 2, isDone = false })

@addTagHelper *, MyAssembly
<vc:priority-list max-priority="2" is-done="false"></vc:priority-list>
```

Result types: `View()` / `View(model)` / `View(viewName, model)`, `Content("text")`, `new HtmlContentViewComponentResult(IHtmlContent)`. From a controller: `return ViewComponent("PriorityList", new { maxPriority, isDone });`.

## Tag Helpers

Server-side participants in HTML element rendering, written in C#. Opt-in via `@addTagHelper *, AssemblyName` (typically in `_ViewImports.cshtml`). `@removeTagHelper` undoes. `@tagHelperPrefix th:` requires a prefix on every Tag-Helper-enabled element. Opt out a single element with `!` on **both** opening and closing tags: `<!span asp-validation-for="Email"></!span>`.

C# expressions are NOT allowed inside attribute names. Conditional attribute pattern: `<input asp-for="LastName" disabled="@(Model?.LicenseId is null)" />` — when the expression evaluates to `false`, the attribute is OMITTED.

Built-in (assembly `Microsoft.AspNetCore.Mvc.TagHelpers`): Anchor (`<a asp-controller asp-action asp-route-* asp-area>`), Form (`<form asp-controller asp-action asp-antiforgery>`), Form Action (`<button formaction>`), Input (`<input asp-for>`), Label (`<label asp-for>`), Select (`<select asp-for asp-items>`), Textarea, Validation Message (`<span asp-validation-for>`), Validation Summary (`<div asp-validation-summary="All|ModelOnly|None">`), Partial, Component (`<component type render-mode>`), Cache (`<cache vary-by-* expires-after expires-on expires-sliding>`), Distributed Cache, Environment (`<environment names include exclude>`), Image / Link / Script (`asp-append-version` for cache-busting hash; `asp-fallback-*` for CDN fallback).

Authoring:

```csharp
[HtmlTargetElement("email", Attributes = "mail-to", TagStructure = TagStructure.NormalOrSelfClosing)]
public class EmailTagHelper : TagHelper
{
    [HtmlAttributeName("mail-to")] public string MailTo { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext ctx, TagHelperOutput output)
    {
        output.TagName = "a";
        output.Attributes.SetAttribute("href", $"mailto:{MailTo}");
        var content = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(content.IsEmptyOrWhiteSpace ? MailTo : content.GetContent());
        output.TagMode = TagMode.StartTagAndEndTag;
    }
}
```

`TagHelperOutput` members: `TagName`, `Attributes` (set/append/remove), `Content`, `PreContent`, `PostContent`, `PreElement`, `PostElement`, `TagMode`, `SuppressOutput()`. `TagStructure`: `Unspecified`, `NormalOrSelfClosing`, `WithoutEndTag`. `[OutputElementHint("a")]` aids IntelliSense.

Configure all instances of a built-in helper via `ITagHelperInitializer<T>`:

```csharp
public class AppendVersionTagHelperInitializer : ITagHelperInitializer<ScriptTagHelper>
{
    public void Initialize(ScriptTagHelper helper, ViewContext ctx) => helper.AppendVersion = true;
}
builder.Services.AddSingleton<ITagHelperInitializer<ScriptTagHelper>, AppendVersionTagHelperInitializer>();
```

**Tag Helpers are NOT supported in Blazor.** See `aspnet-core-blazor`.

## Model binding

Default source order per parameter: form fields (POST + form content type) → request body (only when `[ApiController]` or `[FromBody]`) → route values (simple types only) → query string (simple types only) → uploaded files (`IFormFile` / `IFormFileCollection`).

Source attributes: `[FromQuery(Name="...")]`, `[FromRoute]`, `[FromForm]`, `[FromBody]`, `[FromHeader(Name="...")]`, `[FromServices]`, `[FromKeyedServices("key")]`. If `[FromBody]` is on a complex type, source attributes on its properties are ignored.

Property-level: `[BindProperty]` / `[BindProperties]`, `[BindProperty(SupportsGet = true)]`, `[BindRequired]`, `[BindNever]`, `[Bind("A,B,C")]` / `[Bind(Prefix = "X")]` (does NOT affect input formatters), `[ModelBinder<MyBinder>]`.

Simple types convertible from string: `bool, byte, sbyte, char, DateOnly, DateTime, DateTimeOffset, decimal, double, enum, Guid, short, int, long, float, TimeOnly, TimeSpan, ushort, uint, ulong, Uri, Version`. Anything else: implement `IParsable<T>` (preferred) or `static bool TryParse(string?, out T)`.

Complex types — public parameterless constructor + writable properties (or record primary constructor). Search order: `prefix.PropertyName` then `PropertyName`. Records bind through the constructor; validation metadata comes from constructor parameters, not properties.

Collection / dictionary form patterns:

```
selectedCourses=1050&selectedCourses=2000
selectedCourses[0]=1050&selectedCourses[1]=2000           (subscripts must be sequential from 0)
selectedCourses[a]=1050&selectedCourses.index=a&...       (named indices)
selectedCourses[]=1050&selectedCourses[]=2000              (form data only)
courseMap[1050]=Chemistry&courseMap[2000]=Economics
```

No-source / conversion errors: nullable simple → `null`; non-nullable value type → `default(T)`; complex → `new T()`; array → `Array.Empty<T>()`; `byte[]` → `null`. Source exists but cannot convert → `null`/`default(T)` AND `ModelState` records an error (bad input is NOT echoed back; bind to `string` and reparse to display).

Custom `IModelBinder` + `IModelBinderProvider`:

```csharp
public class ByIdBinder<T> : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext ctx)
    {
        var value = ctx.ValueProvider.GetValue(ctx.ModelName).FirstValue;
        if (string.IsNullOrEmpty(value)) { ctx.Result = ModelBindingResult.Failed(); return; }
        var entity = await ctx.HttpContext.RequestServices.GetRequiredService<IRepo<T>>().FindAsync(int.Parse(value));
        ctx.Result = entity is null ? ModelBindingResult.Failed() : ModelBindingResult.Success(entity);
    }
}
public class ByIdBinderProvider<T> : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext ctx)
        => ctx.Metadata.ModelType == typeof(T) ? new ByIdBinder<T>() : null;
}
builder.Services.AddControllersWithViews(o => o.ModelBinderProviders.Insert(0, new ByIdBinderProvider<Customer>()));
```

Per-parameter: `[ModelBinder<MyInstructorModelBinder>]`. Custom value provider → implement `IValueProvider` + `IValueProviderFactory`, register via `o.ValueProviderFactories.Add(...)`.

Globalization: route + query strings = invariant culture (URLs are shareable); form data = culture-sensitive. Replace `QueryStringValueProviderFactory` with one that uses `CultureInfo.CurrentCulture` to opt query in.

Manual binding — `await TryUpdateModelAsync(model, "Prefix", x => x.Name, x => x.HireDate)`. Exclude types from binding/validation — `o.ModelMetadataDetailsProviders.Add(new ExcludeBindingMetadataProvider(typeof(Version)));` / `SuppressChildValidationMetadataProvider`.

## Validation

Runs after binding; populates `ModelStateDictionary`; check via `ModelState.IsValid`.

DataAnnotations: `[Required]` (non-nullable reference types in nullable contexts get implicit `[Required(AllowEmptyStrings = true)]`), `[StringLength(max, MinimumLength = n)]`, `[MaxLength]`/`[MinLength]`, `[Range]`, `[RegularExpression]`, `[EmailAddress]`/`[Phone]`/`[Url]`/`[CreditCard]`, `[Compare("OtherProperty")]`, `[DataType(DataType.Date)]` (hint only), `[Display(Name = "...")]`, `[Remote(action, controller, AdditionalFields)]`, `[ValidateNever]`. Disable implicit `[Required]` for non-nullable refs: `o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`.

Class-level — implement `IValidatableObject` (`Validate(ValidationContext)` yields `ValidationResult`s).

Custom attribute — derive `ValidationAttribute`; override `IsValid(object?, ValidationContext)`. To run client-side, also implement `IClientModelValidator` (or use `AttributeAdapterBase<T>` + `IValidationAttributeAdapterProvider`) and ship a JS adapter for `jquery-validation-unobtrusive` (`$.validator.addMethod` + `$.validator.unobtrusive.adapters.add`). For dynamic forms re-parse: `$.validator.unobtrusive.parse(form);`.

Manual / re-validation: `ModelState.AddModelError("Field", "msg")`; `ModelState.ClearValidationState("Prefix")` + `TryValidateModel(model, "Prefix")`. Top-level parameter validation works: `[BindRequired, FromQuery] int age`.

`[ApiController]` short-circuits invalid `ModelState` → 400 + `ValidationProblemDetails` (RFC 7807). Customize via `ApiBehaviorOptions.InvalidModelStateResponseFactory` — see `aspnet-core-http-apis` § ApiController.

`MvcOptions` knobs: `MaxModelValidationErrors = 50`, `MaxValidationDepth = 32`, `ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(...)`. JSON property names in error keys: `o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider())`.

`Microsoft.Extensions.Validation` (.NET 10) — `services.AddValidation()` for unified validation outside MVC. FluentValidation is third-party — `dotnet-conventions` § forbidden-patterns prefers in-house mappers / hand-written validators; gate adoption.

## DI in views

`@inject Type Name` adds a property to the view, populated from the request scope. Service must be registered. Override defaults (`Html`, `Component`, `Url`) with another `@inject`. Place repeated `@inject` directives in `_ViewImports.cshtml`. Common idiom: lookup data for dropdowns belongs in a UI-level service injected into the view, not the controller.

```cshtml
@inject IConfiguration Configuration
@inject StatisticsService Stats
<h2>@Configuration["MyRoot:MyParent:MyChildName"]</h2>
<ul><li>Total: @Stats.GetCount()</li></ul>
```

## Testing

Unit tests — Moq + xUnit. Test action behavior in isolation; do NOT test routing / binding / filters / validation in unit tests. Set up a fake `ControllerContext` when code reads `User`, `Request`, `Url`:

```csharp
var http = new DefaultHttpContext();
http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "test"));
controller.ControllerContext = new ControllerContext { HttpContext = http };
```

Asserting `ActionResult<T>` — `.Result` holds the `IActionResult` (e.g., `NotFoundObjectResult`); `.Value` holds `T`.

Integration — `Microsoft.AspNetCore.Mvc.Testing` ships `WebApplicationFactory<TEntryPoint>` (Program partial class needed). Override services via `factory.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<IFoo>(); s.AddSingleton<IFoo, FakeFoo>(); }))`. Detailed test infrastructure → `dotnet-testing`.

## Cross-references

- Public docs (overview): https://learn.microsoft.com/en-us/aspnet/core/mvc/overview?view=aspnetcore-10.0
- Controllers / actions: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/actions?view=aspnetcore-10.0
- Routing: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/routing?view=aspnetcore-10.0
- Filters: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-10.0
- Application model: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/application-model?view=aspnetcore-10.0
- Areas: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/areas?view=aspnetcore-10.0
- Views overview: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/overview?view=aspnetcore-10.0
- Layout / sections: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/layout?view=aspnetcore-10.0
- Partial views: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/partial?view=aspnetcore-10.0
- View components: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/view-components?view=aspnetcore-10.0
- Tag Helpers intro: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/tag-helpers/intro?view=aspnetcore-10.0
- Model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0
- Validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0
- Related: `aspnet-core-fundamentals` — middleware, DI, options, routing primitives, error handling.
- Related: `aspnet-core-http-apis` — `[ApiController]` semantics, ProblemDetails, OpenAPI, content negotiation.
- Related: `aspnet-core-razor-pages` — `@page`, `PageModel`, page handlers, page filters.
- Related: `aspnet-core-blazor` — Razor components (NOT MVC views; Tag Helpers don't work).
- Related: `aspnet-core-security` — authorization filters, antiforgery beyond `<form>` Tag Helper.
- Related: `dotnet-testing` — single integration-test project layout, `WebApplicationFactory` setup, areas-as-folders.
- Related: `dotnet-conventions` — banned third-party libs (FluentValidation gated).
