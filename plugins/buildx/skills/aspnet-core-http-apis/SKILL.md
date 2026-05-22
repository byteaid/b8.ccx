---
name: aspnet-core-http-apis
description: ASP.NET Core 10 HTTP API reference (controller-based). Covers `[ApiController]` (attribute routing, auto 400 + ValidationProblemDetails, binding-source inference, multipart, ProblemDetails), `ControllerBase`, action return shapes (`ActionResult<T>`, `HttpResults`/`Results<T1,T2>`, `TypedResults`), API conventions, content negotiation (STJ/XML/Newtonsoft, `[Produces]`/`[Consumes]`/`[FormatFilter]`, custom `TextOutputFormatter`), RFC 9457 ProblemDetails (`AddProblemDetails`, `IProblemDetailsService`, `ProblemDetailsFactory`, `ClientErrorMapping`, `IExceptionHandler`), built-in `Microsoft.AspNetCore.OpenApi` (transformers, multi-doc, build-time gen, plus Scalar/NSwag/Swashbuckle), `Asp.Versioning.*`, HATEOAS via `LinkGenerator`, Native AOT caveats.
when_to_use: |
  - Trigger keywords: AddControllers, ApiController, HttpGet, FromBody, ProducesResponseType, DefaultApiConventions, ApiBehaviorOptions, ClientErrorMapping, ActionResult<T>, TypedResults, ProblemDetails, IExceptionHandler, AddOpenApi, IOpenApiDocumentTransformer, AddApiVersioning, MapToApiVersion, UrlSegmentApiVersionReader, AddXmlSerializerFormatters, FormatFilter, Scalar, Swashbuckle, webapiaot.
  - Task shapes: scaffold a controller-based Web API; pick action return type; document responses; wire ProblemDetails customization; generate OpenAPI; add bearer security via transformer; switch to build-time OpenAPI; version with `Asp.Versioning.*` and split per version; author a custom output formatter; AOT-publish.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/Controllers/**/*.cs", "**/Program.cs", "**/*.csproj", "**/appsettings*.json"]
---

# ASP.NET Core HTTP APIs — Reference (Controllers)

Reference for building HTTP APIs on ASP.NET Core 10 using **controller-based** MVC. Minimal APIs are forbidden by team policy (`dotnet-conventions` § forbidden-patterns/no-minimal-apis); this skill therefore covers controllers in depth and only mentions minimal-API surface area when it changes how to read a public-docs sample.

## Mental model

- An API endpoint = `RouteAttribute` + verb attribute + a public method on a class deriving from `ControllerBase`. `[ApiController]` opts into the API conventions.
- Response shapes flow through `ObjectResult` and the registered `IOutputFormatter`s. Content negotiation chooses based on the request's `Accept` header.
- `IResult` / `Results<T1,T2,…>` (`HttpResults`) are an alternate result family that bypasses formatters and content negotiation; the `Content-Type` is decided by the result implementation. They participate in OpenAPI metadata via `IEndpointMetadataProvider`.
- ProblemDetails (RFC 9457) is the canonical error envelope. `AddProblemDetails()` wires it for unhandled exceptions, status code pages, and the auto-400 filter.
- OpenAPI generation is first-class via `Microsoft.AspNetCore.OpenApi` (`AddOpenApi` + `MapOpenApi`); document/operation/schema transformers shape the output. Swagger UI is opt-in.

## Non-negotiable rules

1. **No minimal APIs.** Every endpoint is a controller action. See `dotnet-conventions` for the rationale.
2. **`[ApiController]` on every API controller** (or `[assembly: ApiController]`). Attribute routing required; conventional routing is rejected.
3. **Derive from `ControllerBase`** for APIs (NOT `Controller` — that's MVC views).
4. **Document every action's responses** with `[ProducesResponseType<T>(code)]` / `[ProducesResponseType(code)]`, OR apply a convention (`[ApiConventionType(typeof(DefaultApiConventions))]`).
5. **`ActionResult<T>` is the default return type** for actions with a single success body. Use `IActionResult` only when there is no canonical `T`. Use `Results<T1,T2,…>` only when sharing a handler with code that lives outside MVC.
6. **`AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`** is the standard error trifecta. Customize via `CustomizeProblemDetails` or `IProblemDetailsWriter`, never by hand-rolling JSON.
7. **OpenAPI document = `Microsoft.AspNetCore.OpenApi`** (built-in). Swashbuckle / NSwag only when a feature truly needs them and the team has approved the third-party dep — otherwise prefer Scalar (UI) on top of the built-in document.
8. **`IFormFile` / `IFormFileCollection` actions need `[Consumes("multipart/form-data")]`** unless `[ApiController]` infers it (the default).

## Controllers — surface

Setup:

```csharp
builder.Services.AddControllers();         // API only
// builder.Services.AddControllersWithViews(); // MVC + views — see aspnet-core-mvc
// builder.Services.AddRazorPages();           // see aspnet-core-razor-pages

var app = builder.Build();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

### `[ApiController]` behaviors

Apply at class, base class, or assembly (`[assembly: ApiController]`). Five behaviors:

1. **Attribute routing required.**
2. **Automatic HTTP 400** — model-state errors emit `ValidationProblemDetails` via `ModelStateInvalidFilter`. Disable: `ApiBehaviorOptions.SuppressModelStateInvalidFilter = true`. Custom factory via `ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ...)`.
3. **Binding-source inference**: `[FromServices]` for complex types in DI; `[FromBody]` for complex types not in DI (excluding special types like `IFormCollection`, `CancellationToken`); `[FromForm]` for `IFormFile`/`IFormFileCollection`; `[FromRoute]` when name matches a route token in any template; `[FromQuery]` for everything else. NOT inferred for simple types (`string`, `int`) → `[FromBody]`; apply explicitly. At most one body parameter; otherwise runtime exception. Disable single source: `ApiBehaviorOptions.DisableImplicitFromServicesParameters = true`. Disable globally: `SuppressInferBindingSourcesForParameters = true`. Avoid `[FromRoute]` for values that may contain `%2f` (`/`) — not unescaped; use `[FromQuery]`.
4. **Multipart/form-data inference** for `IFormFile`/`IFormFileCollection`. Toggle with `SuppressConsumesConstraintForFormFileParameters`.
5. **ProblemDetails for client error status codes.** Toggle with `SuppressMapClientErrors`. Customize via `ApiBehaviorOptions.ClientErrorMapping[code].{Link,Title,Type}`.

`[Consumes("…")]` selects an action by `Content-Type` (returns 415 otherwise) — useful when two actions share a route but expect JSON vs form-encoded.

### Attribute routing + `ControllerBase` helpers

```csharp
[ApiController]
[Route("api/[controller]")]
public class PetsController : ControllerBase
{
    [HttpGet]                                  public ActionResult<IEnumerable<Pet>> GetAll() => /* ... */;
    [HttpGet("{id:int}", Name = "GetPetById")] public ActionResult<Pet> GetById(int id) => /* ... */;
    [HttpPost]                                 public ActionResult<Pet> Create(Pet p) => CreatedAtRoute("GetPetById", new { id = p.Id }, p);
    [HttpPut("{id:int}")]                      public IActionResult Update(int id, Pet p) => /* ... */;
    [HttpDelete("{id:int}")]                   public IActionResult Delete(int id) => NoContent();
    [AcceptVerbs("GET", "POST")]               public IActionResult Either() => Ok();
}
```

Route tokens at startup: `[controller]` → `Pets`, `[action]` → method name, `[area]` → area name.

`ControllerBase` helpers — 2xx: `Ok`, `Ok(value)`, `Created(uri, value)`, `CreatedAtAction`, `CreatedAtRoute`, `Accepted`, `AcceptedAtAction`, `AcceptedAtRoute`, `NoContent`. 3xx: `Redirect`, `RedirectPermanent`, `RedirectToAction`, `RedirectToRoute`, `LocalRedirect`. 4xx: `BadRequest`, `BadRequest(modelState)`, `Unauthorized`, `Forbid`, `NotFound`, `Conflict`, `UnprocessableEntity`, `ValidationProblem`. 5xx: `Problem`, `StatusCode(code, value?)`. Files: `File`, `PhysicalFile`, `VirtualFile`. Validation: `TryUpdateModelAsync`, `TryValidateModel`. Properties: `User`, `Request`, `Response`, `HttpContext`, `ModelState`, `Url` (`IUrlHelper`), `RouteData`, `TempData`.

### Action return types

Four shapes; each documented with `[ProducesResponseType]` for OpenAPI:

```csharp
// 1. Specific type — when the action can only succeed
[HttpGet] public Task<List<Product>> Get() => _ctx.Products.ToListAsync();

// 2. IActionResult — when multiple status codes
[HttpGet("{id}")]
[ProducesResponseType<Product>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public IActionResult GetById(int id) => _ctx.Products.Find(id) is { } p ? Ok(p) : NotFound();

// 3. ActionResult<T> — implicit cast from T → ObjectResult and from ActionResult → ActionResult<T>
[HttpGet("{id}")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public ActionResult<Product> GetById2(int id) => _ctx.Products.Find(id) is { } p ? p : NotFound();

// 4. HttpResults — IResult / Results<T1,T2,…>
[HttpGet("{id}")]
public Results<NotFound, Ok<Product>> GetById3(int id)
    => _ctx.Products.Find(id) is { } p ? TypedResults.Ok(p) : TypedResults.NotFound();
```

Notes:
- `IEnumerable<T>` is buffered before write; declare `IAsyncEnumerable<T>` to stream with `System.Text.Json`. `Newtonsoft.Json` and XML formatters always buffer.
- `ActionResult<T>` does NOT support implicit casts from interfaces — return concrete collections.
- `HttpResults` (`IResult`) bypass content negotiation and configured formatters; the `Content-Type` is decided by the result implementation. Useful when sharing handlers with non-MVC code.

### API conventions

Substitute for `[ProducesResponseType]` decoration on every action. Three apply scopes — specific action via `[ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Put))]`; controller via `[ApiConventionType(typeof(DefaultApiConventions))]`; assembly via `[assembly: ApiConventionType(typeof(DefaultApiConventions))]`. `DefaultApiConventions.Put` applies `[ProducesDefaultResponseType]` + `[ProducesResponseType(204|404|400)]`.

Custom convention with name matching:

```csharp
public static class MyAppConventions
{
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ApiConventionNameMatch(ApiConventionNameMatchBehavior.Prefix)]
    public static void Find([ApiConventionNameMatch(ApiConventionNameMatchBehavior.Suffix)] int id) { }
}
```

`Prefix` matches `Find`, `FindPet`, `FindById`. Suffix on a parameter matches `id`, `petId`. `ApiConventionTypeMatch` constrains parameter type; `params[]` matches "any remaining". API analyzers warn when an action returns an undocumented status code.

## Formatting & content negotiation

Default formatters: `application/json` (System.Text.Json), `text/json`, `text/plain` (`StringOutputFormatter`), `application/octet-stream`. Negotiation runs through `ObjectResult` and helper methods that wrap it.

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = null;          // PascalCase
        o.JsonSerializerOptions.Converters.Add(new MyConverter());
    })
    .AddXmlSerializerFormatters();    // adds XmlSerializer-based formatter
    // .AddXmlDataContractSerializerFormatters();
```

Newtonsoft.Json (package `Microsoft.AspNetCore.Mvc.NewtonsoftJson`) — required for `JsonPatch`, `Newtonsoft.Json` attributes, `IJsonHelper`, `TempData` with NJ. Team rule: prefer System.Text.Json + a hand-written patch endpoint over pulling in NewtonsoftJson.

Controller-level filters: `[Produces("application/json")]` (restrict response Content-Type), `[Consumes("application/json")]` (restrict request Content-Type), `[FormatFilter]` + `{format?}` route token (enable `.json` / `.xml` URL suffixes).

`MvcOptions` switches: `RespectBrowserAcceptHeader = true` (honor browser `Accept`; default `false` since browsers usually get the first formatter), `ReturnHttpNotAcceptable = true` (return 406 instead of falling back when no formatter satisfies `Accept`).

URL-format mapping example: `[HttpGet("{id:long}.{format?}")]` plus `[FormatFilter]` on the controller — `/api/todoitems/5.json` selects JSON, `.xml` selects XML if registered.

Custom formatter — derive from `TextInputFormatter` / `TextOutputFormatter` (text) or `InputFormatter` / `OutputFormatter` (binary). DI via `OutputFormatterWriteContext.HttpContext.RequestServices`, NOT constructor injection (formatters are singletons created early). Register via `o.OutputFormatters.Insert(0, new VcardOutputFormatter())`. Override `CanWriteResult` instead of `CanWriteType` when the polymorphic runtime type matters (declared `Person`, runtime `Student`). Skeleton:

```csharp
public class VcardOutputFormatter : TextOutputFormatter
{
    public VcardOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/vcard"));
        SupportedEncodings.Add(Encoding.UTF8);
    }
    protected override bool CanWriteType(Type? t)
        => typeof(Contact).IsAssignableFrom(t) || typeof(IEnumerable<Contact>).IsAssignableFrom(t);
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext ctx, Encoding enc)
    {
        var sb = new StringBuilder();
        if (ctx.Object is IEnumerable<Contact> many) foreach (var c in many) Format(sb, c);
        else Format(sb, (Contact)ctx.Object!);
        await ctx.HttpContext.Response.WriteAsync(sb.ToString(), enc);
    }
    static void Format(StringBuilder b, Contact c) { /* ... */ }
}
```

## ProblemDetails (RFC 7807 / RFC 9457)

Wiring:

```csharp
builder.Services.AddProblemDetails();              // registers IProblemDetailsService
builder.Services.AddControllers();

var app = builder.Build();
app.UseExceptionHandler();                         // unhandled → ProblemDetails
app.UseStatusCodePages();                          // empty 4xx/5xx → ProblemDetails
```

Three middlewares emit ProblemDetails when registered AND `Accept` header is supported (default writer accepts `application/json`, `application/problem+json`, `*/*`, `application/*`):
- `ExceptionHandlerMiddleware` (when no custom handler).
- `StatusCodePagesMiddleware` (by default).
- `DeveloperExceptionPageMiddleware` (when `Accept` does not include `text/html`).

Customize globally:

```csharp
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    ctx.ProblemDetails.Extensions["instance"] = ctx.HttpContext.Request.Path;
});
```

`IProblemDetailsService.WriteAsync(ProblemDetailsContext)` / `TryWriteAsync(ctx)` — the second returns `false` if no `IProblemDetailsWriter` matches the `Accept` header; use it for fallback:

```csharp
app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var pds = ctx.RequestServices.GetService<IProblemDetailsService>();
    if (pds is null || !await pds.TryWriteAsync(new() { HttpContext = ctx }))
        await ctx.Response.WriteAsync("Fallback: An error occurred.");
}));
```

Return ProblemDetails directly from a controller: `return Problem("boom", statusCode: 500, title: "Crash");` / `return ValidationProblem(ModelState);`. The `ProblemDetailsFactory` (controllers) is the central factory for `ProblemDetails` and `ValidationProblemDetails`; replace with `services.AddTransient<ProblemDetailsFactory, MyFactory>()`.

`ApiBehaviorOptions.ClientErrorMapping[code].Link` lets you override the `type`/`title`/`link` of the auto-generated client error responses.

`IExceptionHandler` (.NET 8+) chain — multiple handlers run in registration order until one returns `true`:

```csharp
internal sealed class NotFoundExceptionHandler(IProblemDetailsService pds) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not KeyNotFoundException) return false;
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return await pds.TryWriteAsync(new()
        {
            HttpContext = ctx,
            ProblemDetails = new() { Status = 404, Title = "Resource not found", Detail = ex.Message }
        });
    }
}
builder.Services.AddExceptionHandler<NotFoundExceptionHandler>();
builder.Services.AddProblemDetails();
app.UseExceptionHandler();
```

Controller-only exception filter pattern (legacy; prefer `IExceptionHandler` for new code):

```csharp
public class HttpResponseException(int sc, object? v = null) : Exception
{ public int StatusCode { get; } = sc; public object? Value { get; } = v; }

public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
{
    public int Order => int.MaxValue - 10;
    public void OnActionExecuting(ActionExecutingContext _) { }
    public void OnActionExecuted(ActionExecutedContext ctx)
    {
        if (ctx.Exception is HttpResponseException ex)
        { ctx.Result = new ObjectResult(ex.Value) { StatusCode = ex.StatusCode }; ctx.ExceptionHandled = true; }
    }
}
builder.Services.AddControllers(o => o.Filters.Add<HttpResponseExceptionFilter>());
```

Don't decorate the error-handler action with HTTP-method attributes — they would prevent matching (`UseExceptionHandler("/error")` re-executes through that path).

## OpenAPI generation

### Built-in `Microsoft.AspNetCore.OpenApi`

Features: OpenAPI **3.1** by default (configurable to 3.0), JSON Schema **draft 2020-12**, runtime + build-time generation, document/operation/schema **transformers**, multiple documents per app, Native AOT compatible.

```csharp
builder.Services.AddOpenApi();                        // doc name "v1"
// builder.Services.AddOpenApi("internal", o => /* options */);

if (app.Environment.IsDevelopment()) app.MapOpenApi();   // /openapi/{documentName}.json
// app.MapOpenApi("/openapi/{documentName}.yaml");        // YAML (runtime only)
```

`OpenApiOptions`: `OpenApiVersion` (`OpenApi3_0` | `OpenApi3_1`); `DocumentName` (set via `AddOpenApi(name)`); `ShouldInclude(ApiDescription)` — default `GroupName == DocumentName || GroupName == null`; `CreateSchemaReferenceId(JsonTypeInfo)` — return `null` to inline; `AddDocumentTransformer` / `AddOperationTransformer` / `AddSchemaTransformer`.

Multi-doc — `app.MapGet(...).WithGroupName("public")` matches a doc whose `ShouldInclude` returns true for that group.

Cache + secure the doc endpoint:

```csharp
app.MapOpenApi().CacheOutput().RequireAuthorization("ApiTesterPolicy");
```

### Transformers (.NET 10)

Three kinds; execution: schema → operation → document; per-document; in registration order; document transformers see all prior changes. Register via `o.AddDocumentTransformer<T>()` / `AddOperationTransformer<T>()` / `AddSchemaTransformer<T>()` (DI-activated) or lambda overloads.

Document transformer — add Bearer security globally:

```csharp
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider sp) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext ctx, CancellationToken ct)
    {
        var schemes = await sp.GetAllSchemesAsync();
        if (!schemes.Any(s => s.Name == "Bearer")) return;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            { Type = SecuritySchemeType.Http, Scheme = "bearer", In = ParameterLocation.Header, BearerFormat = "Json Web Token" }
        };
        foreach (var op in document.Paths.Values.SelectMany(p => p.Operations))
        {
            op.Value.Security ??= [];
            op.Value.Security.Add(new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        }
    }
}
```

Operation transformer — skip `[AllowAnonymous]` endpoints by checking `ctx.Description.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any()` and adding `OpenApiSecurityRequirement` otherwise.

Schema transformer — `if (ctx.JsonTypeInfo.Type == typeof(decimal)) schema.Format = "decimal"` inside `AddSchemaTransformer((schema, ctx, ct) => ...)`.

`GetOrCreateSchemaAsync` (.NET 10) — generate a schema for a CLR type from inside any transformer and add it as a reusable component (e.g., attach a shared `ProblemDetails` schema to `4XX` responses across the document).

### Annotations on controllers

Three strategies, in priority order: `[ProducesResponseType<T>(code)]` / `[ProducesDefaultResponseType]`; conventions (above); `IResult`/`Results<…>` return types (auto-contribute via `IEndpointMetadataProvider`).

`[Produces("application/json")]` / `[Consumes("application/xml")]` on action or controller. `[ApiExplorerSettings(IgnoreApi = true)]` excludes from OpenAPI. `[ApiExplorerSettings(GroupName = "v1")]` selects which OpenAPI document includes the action (multi-doc).

### Build-time generation

Add `Microsoft.Extensions.ApiDescription.Server` package — generates the OpenAPI doc on `dotnet build` (`obj/{ProjectName}.json` by default, suffixed `_{DocumentName}` when name ≠ `v1`).

```xml
<PropertyGroup>
  <OpenApiDocumentsDirectory>.</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name my-api --document-name v2 --openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>
</PropertyGroup>
```

Build-time runs the app entry point under a mock server. Suppress side-effects: `if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider") builder.AddServiceDefaults();`. Logs from build-time generator are swallowed by Terminal Logger at default verbosity — use `dotnet build -tlp:v=d` or `--tl:off`.

### UI choices

Built-in package ships JSON/YAML only. Pick one UI:

- **Scalar** — `dotnet add package Scalar.AspNetCore` + `app.MapScalarApiReference();`. Recommended.
- **Swagger UI** — `Swashbuckle.AspNetCore.SwaggerUI` + `app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));`.
- Swashbuckle and NSwag remain available but require third-party deps; gate via `dotnet-conventions`.

## API versioning (`Asp.Versioning.*`)

Packages: `Asp.Versioning.Mvc` (controllers), `Asp.Versioning.Mvc.ApiExplorer` (versioned ApiExplorer for OpenAPI). (`Asp.Versioning.Http` is the minimal-API package — not used here.)

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion              = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions              = true;        // adds api-supported-versions / api-deprecated-versions response headers
    o.ApiVersionReader               = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),                      // /api/v{version:apiVersion}/...
        new QueryStringApiVersionReader("api-version"),        // ?api-version=2.0
        new HeaderApiVersionReader("X-Api-Version"),           // X-Api-Version: 2.0
        new MediaTypeApiVersionReader("v"));                   // Accept: application/json;v=2
}).AddApiExplorer(o =>
{
    o.GroupNameFormat            = "'v'VVV";          // v1, v1.1, v2
    o.SubstituteApiVersionInUrl  = true;
});
```

```csharp
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[ApiVersion("0.9", Deprecated = true)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet, MapToApiVersion("1.0")] public IActionResult GetV1() => Ok();
    [HttpGet, MapToApiVersion("2.0")] public IActionResult GetV2() => Ok();
}
```

Multi-doc OpenAPI per version:

```csharp
builder.Services.AddOpenApi("v1", o => o.ShouldInclude = api => api.GroupName == "v1");
builder.Services.AddOpenApi("v2", o => o.ShouldInclude = api => api.GroupName == "v2");
```

Inject `IApiVersionDescriptionProvider` to enumerate versions at runtime.

## HATEOAS

Not first-class. Add a `links` property to your DTO (or wrap with an envelope) and populate via `LinkGenerator.GetUriByName(httpContext, "EndpointName", values)`. Name routes with `[HttpGet(Name="…")]` or `[Route(Name="…")]`; use `Url.Link("Name", new { id })` (controllers) or inject `LinkGenerator`. For media-type-specific HATEOAS (HAL, JSON:API, Siren) write a custom `IOutputFormatter` — no built-in `application/hal+json` formatter.

```csharp
public record OrderDto(int Id, decimal Total, IDictionary<string, string> Links);

[HttpGet("{id:int}", Name = "GetOrder")]
public ActionResult<OrderDto> Get(int id, [FromServices] LinkGenerator lg)
{
    var self  = lg.GetUriByName(HttpContext, "GetOrder", new { id })!;
    var items = lg.GetUriByName(HttpContext, "GetOrderItems", new { id })!;
    return new OrderDto(id, 99.95m, new Dictionary<string, string> { ["self"] = self, ["items"] = items });
}
```

## Native AOT

Works:
- `WebApplication.CreateSlimBuilder` / `CreateEmptyBuilder`.
- `dotnet new webapiaot` template.
- `Microsoft.AspNetCore.OpenApi` (built-in) — supports trimming + AOT.
- `System.Text.Json` source generators (`JsonSerializerContext` + `[JsonSerializable]`).

Needs care:
- Reflection-based JSON: configure a `JsonSerializerContext`:
  ```csharp
  builder.Services.ConfigureHttpJsonOptions(o =>
      o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

  [JsonSerializable(typeof(Todo))]
  [JsonSerializable(typeof(List<Todo>))]
  internal partial class AppJsonContext : JsonSerializerContext { }
  ```
- MVC controllers: partial AOT — works for endpoints generated by RDG; some MVC features (model binding extensibility, application parts, `IModelValidator`) may produce trimmer warnings.
- Swashbuckle, Newtonsoft.Json, XML formatters: not AOT-compatible.

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <StripSymbols>true</StripSymbols>
  <IlcOptimizationPreference>Size</IlcOptimizationPreference>
</PropertyGroup>
```

## Quick decision matrix

| Question | Answer |
|---|---|
| Controllers vs minimal APIs | **Controllers always** (team policy). |
| Single success body | `ActionResult<T>` |
| Multiple status codes | `ActionResult<T>` if there's a canonical T; `IActionResult` otherwise; `Results<T1,T2>` to share with non-MVC handlers |
| Document a 404 | `[ProducesResponseType(StatusCodes.Status404NotFound)]` or convention |
| Universal error envelope | `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()` |
| Per-exception type response | `IExceptionHandler` (chain) |
| Disable the auto-400 from `[ApiController]` | `ApiBehaviorOptions.SuppressModelStateInvalidFilter = true` |
| Customize the auto-400 body | `ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ...)` |
| Apply Bearer security to all OpenAPI ops | document transformer |
| Skip `[AllowAnonymous]` from security in OpenAPI | operation transformer |
| Build-time OpenAPI for CI artifacts | `Microsoft.Extensions.ApiDescription.Server` package |
| URL versioning | `UrlSegmentApiVersionReader` + `[Route("api/v{version:apiVersion}/[controller]")]` |
| Multi-version OpenAPI | one `AddOpenApi("vN")` per version + `ShouldInclude = api => api.GroupName == "vN"` |
| Bind a multi-source filter object | `[AsParameters]` on a record (supported on controllers as well via inferred sources) |

## Cross-references

- Public docs (Web APIs): https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0
- Action return types: https://learn.microsoft.com/en-us/aspnet/core/web-api/action-return-types?view=aspnetcore-10.0
- ProblemDetails / errors: https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors?view=aspnetcore-10.0
- Format response data: https://learn.microsoft.com/en-us/aspnet/core/web-api/advanced/formatting?view=aspnetcore-10.0
- Conventions: https://learn.microsoft.com/en-us/aspnet/core/web-api/advanced/conventions?view=aspnetcore-10.0
- Custom formatters: https://learn.microsoft.com/en-us/aspnet/core/web-api/advanced/custom-formatters?view=aspnetcore-10.0
- OpenAPI generation: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- Customize OpenAPI: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0
- Asp.Versioning project: https://github.com/dotnet/aspnet-api-versioning
- Related: `aspnet-core-fundamentals` — middleware order, DI, options, error-handling primitives, routing, antiforgery.
- Related: `aspnet-core-mvc` — MVC views, view components, Tag Helpers, deep model binding & validation surface.
- Related: `aspnet-core-razor-pages` — `@page` / `PageModel`.
- Related: `aspnet-core-grpc` — gRPC services.
- Related: `aspnet-core-signalr` — real-time / WebSockets.
- Related: `aspnet-core-security` — authentication, authorization, JWT, OIDC.
- Related: `aspnet-core-servers-and-hosting` — Kestrel, IIS, Docker, App Service.
- Related: `aspnet-core-performance` — caching, rate-limit tuning, ObjectPool.
- Related: `dotnet-conventions` § forbidden-patterns — bans minimal APIs, third-party libs.
- Related: `dotnet-asynchronous-programming` — `async`/`await` shape, `IAsyncEnumerable<T>` for streaming JSON.
