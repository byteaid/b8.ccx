# Components, Lifecycle, Forms, Routing

Component authoring, `[Parameter]`, lifecycle methods, data binding, event handling, `EditForm` + antiforgery, routing + `Router.NotFoundPage`, cascading values, `Virtualize<TItem>`. Load for component-authoring work or routing/form bugs.

## Components

File layout: `.razor` extension, **PascalCase filename**, **kebab-case route** (`@page "/product-detail"`). Namespace = root namespace + folder path. Directive ordering: `@page` → `@rendermode` → `@using` (System → Microsoft → 3rd → app, alphabetical) → other directives alphabetically.

Code-behind partial class is preferred for non-trivial components. `_Imports.razor` `@using` does **not** apply to the `.cs` file — add namespaces directly.

### Parameters

```csharp
[Parameter] public string Title { get; set; } = "Default";
[Parameter, EditorRequired] public string? Id { get; set; }     // design-time only
[Parameter] public RenderFragment? ChildContent { get; set; }
[Parameter(CaptureUnmatchedValues = true)]
public IDictionary<string, object>? AdditionalAttributes { get; set; }
```

`EventCallback` / `EventCallback<T>` are structs that wrap delegate + receiver. Calling `InvokeAsync` automatically calls `StateHasChanged` on the receiver — prefer over raw `Action`/`Func`.

Two-way binding contract: parameter `Foo` + `EventCallback<T> FooChanged` enables `<Child @bind-Foo="..." />`. Add `Expression<Func<T?>>? FooExpression` to plug into `EditContext` validation (the `InputBase<T>` contract).

### Generic components & templates

```razor
@typeparam TItem
@typeparam TKey where TKey : notnull
<List TItem="Product" Items="products">
    <ItemTemplate Context="p"><strong>@p.Name</strong></ItemTemplate>
</List>
```

`@ref` is populated **after first render** — invoke from `OnAfterRender(Async)`. Loop variable closure: copy locally for non-`foreach` loops (`for (int c=...) { var ct = c; ... }`).

`@((MarkupString)html)` injects raw HTML — only with **trusted** content. Otherwise XSS.

## Lifecycle

Order on first render:

1. `SetParametersAsync(ParameterView)` — entry point; default impl assigns `[Parameter]`/`[CascadingParameter]` then calls `OnInitialized{Async}` and `OnParametersSet{Async}`.
2. `OnInitialized()` then `OnInitializedAsync()` — once per instance.
3. `OnParametersSet()` then `OnParametersSetAsync()` — every parameter delivery.
4. Render → DOM diff applied.
5. `OnAfterRender(bool firstRender)` then `OnAfterRenderAsync(bool firstRender)` — only on the side that owns the DOM. **Never during prerender.**

If `OnInitializedAsync` returns an incomplete `Task`, `ComponentBase` renders once immediately with whatever state is set, then again when the Task completes. That's the source of "flicker" without `[PersistentState]`.

Call `StateHasChanged()` to enqueue a re-render. From outside the dispatcher (timer, background service, hub callback): `await InvokeAsync(StateHasChanged);` — otherwise `InvalidOperationException: The current thread is not associated with the Dispatcher`.

`ShouldRender` only suppresses *this* component's `BuildRenderTree`; `OnAfterRender` still fires.

Disposal: implement `IDisposable` and/or `IAsyncDisposable`. On Server, wrap JS interop disposal in `try/catch (JSDisconnectedException)` — the circuit may be gone. Cancel CTSs on dispose to abort lifecycle work the user navigated away from.

`SetParametersAsync` override — only when intercepting parameters (custom validation):

```csharp
public override Task SetParametersAsync(ParameterView parameters)
{
    parameters.SetParameterProperties(this);
    return base.SetParametersAsync(ParameterView.Empty);
}
```

## Data binding

```razor
<input @bind="name" />                                 @* event: onchange (default) *@
<input @bind="name" @bind:event="oninput" />           @* live update *@
<input @bind="dob" @bind:format="yyyy-MM-dd" />        @* output format *@
<input @bind="searchText" @bind:event="oninput" @bind:after="OnSearchAsync" />
<input @bind:get="filter" @bind:set="OnFilterSet" />   @* explicit getter/setter, no backing prop *@
```

`@bind:after` runs after the implicit `StateHasChanged`. **Cannot** combine with `@bind:get`/`@bind:set`. `@bind` swallows parse errors silently for primitives — use `EditForm` + `InputNumber<T>` to surface errors via `EditContext` validation. Empty input → `null` for nullable types, default value for non-nullable.

## Event handling

Async handlers must return `Task`/`ValueTask`, never `async void`. Built-in events live in `Microsoft.AspNetCore.Components.Web` (`MouseEventArgs`, `KeyboardEventArgs`, `ChangeEventArgs`, `PointerEventArgs`, `TouchEventArgs`, `WheelEventArgs`, etc.).

`@onclick:preventDefault` and `@onclick:stopPropagation` accept boolean expressions:

```razor
<a href="..." @onclick="Handle" @onclick:preventDefault @onclick:stopPropagation>Link</a>
<input @onkeydown:preventDefault="ShouldPrevent" />
```

Custom events: register in JS with `Blazor.registerCustomEventType('custompaste', { ... })`, define an `EventArgs` subclass, decorate `[EventHandler("oncustompaste", typeof(...), enableStopPropagation: true, enablePreventDefault: true)]`.

Element focus: capture `ElementReference` via `@ref`, call `await elementRef.FocusAsync();`.

## Forms (`EditForm`)

```razor
<EditForm Model="Model" OnValidSubmit="Submit" FormName="Starship2">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <InputText @bind-Value="Model!.Id" />
    <ValidationMessage For="@(() => Model!.Id)" />
    <button type="submit">Submit</button>
</EditForm>

@code {
    [SupplyParameterFromForm] private Starship? Model { get; set; }
    protected override void OnInitialized() => Model ??= new();
    private void Submit() { /* ... */ }
}
```

Submission callbacks (mutually exclusive): `OnValidSubmit`, `OnInvalidSubmit`, `OnSubmit` (you call `EditContext.Validate()` yourself).

Built-in inputs (all derive from `InputBase<TValue>`): `InputText`, `InputTextArea`, `InputNumber<T>`, `InputDate<T>` (`DateTime`/`DateTimeOffset`/`DateOnly`/`TimeOnly`), `InputCheckbox`, `InputSelect<T>`, `InputRadio<T>` / `InputRadioGroup<T>`, `InputFile` (exposes `IBrowserFile`, `OpenReadStream(maxAllowedSize)`).

Custom validator: subscribe to `EditContext.OnValidationRequested` / `OnFieldChanged`, store messages in a `ValidationMessageStore`, call `EditContext.NotifyValidationStateChanged()`.

Antiforgery: `AddRazorComponents()` registers the services, `app.UseAntiforgery()` is mandatory in the pipeline. `EditForm` injects `<AntiforgeryToken />` and `[RequireAntiforgeryToken]` automatically; for plain `<form>` add `<AntiforgeryToken />` manually.

`<EditForm Enhance ...>` (or `<form data-enhance ...>`) posts via fetch + DOM diff — no full page reload. Only valid against Blazor endpoints. `data-permanent` preserves DOM across enhanced updates. `data-enhance` is **not** inherited from ancestors.

Overposting: for static-SSR forms with DB-backed models, expose a DTO/view-model with only user-settable fields and map to the persistence model server-side.

Static-SSR forms validate **server-side after submit only** — no client-side validation without a circuit / WASM.

## Routing

```razor
@page "/user/{Id:int}"
@page "/user/{Id:int}/{Option:bool?}"
@page "/route/{text?}"
@page "/documents/{*path}"        @* catch-all *@
```

Constraints: `bool`, `datetime`, `decimal`, `double`, `float`, `guid`, `int`, `long`, `nonfile` (parsed with **invariant culture**). Catch-all is `string` and decodes `/` and `%xx`.

Route params bind to `[Parameter]` props (case-insensitive). When navigating between optional-param URLs without unmounting, default values applied in `OnInitialized` won't reset — set defaults in `OnParametersSet{Async}` instead.

`NavigationManager` essentials:

```csharp
Nav.NavigateTo("/counter", new NavigationOptions { ReplaceHistoryEntry = true });
Nav.Refresh(forceReload: true);
Nav.LocationChanged += OnLocationChanged;
var reg = Nav.RegisterLocationChangingHandler(async ctx =>
{
    if (HasUnsavedChanges() && !await ConfirmAsync()) ctx.PreventNavigation();
});
Nav.NotFound();    // .NET 8+, paired with Router.NotFoundPage in .NET 10
```

`<NavLink href="counter" Match="NavLinkMatch.All" ActiveClass="active">`.

### Not-Found page (.NET 10 first-class)

```razor
@page "/not-found"
@layout MainLayout
<h3>Not Found</h3>
```

```razor
<Router AppAssembly="@typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
</Router>
```

Legacy `<NotFound>...</NotFound>` parameter is back-compat only and does **not** display in .NET 8/9 Web Apps — use `NotFoundPage`.

### Static vs interactive routing

- **Static routing**: server endpoint routing → component picked per HTTP request. Used during static SSR and during the prerender pass of an interactive page.
- **Interactive routing**: when `Routes` itself has an interactive render mode, the router runs in the circuit/WASM runtime. Subsequent navigations don't hit HTTP and don't prerender — `OnInitializedAsync` runs **once** in this regime. `[PersistentState]` is loaded only on initial page load by default; opt in via `AllowUpdates = true`.

Routing across assemblies: `app.MapRazorComponents<App>().AddAdditionalAssemblies(typeof(Client._Imports).Assembly)` on the server **and** `<Router AdditionalAssemblies="...">` on the client side.

Query strings: `[SupplyParameterFromQuery]`, `Nav.GetUriWithQueryParameter("page", 2)`, `Nav.GetUriWithQueryParameters(IDictionary)`.

## Cascading values & parameters

Local `<CascadingValue Value="..." Name="..." IsFixed="true">`. `IsFixed` skips per-render change detection — cheaper for values that never mutate.

Root-level (.NET 8+):

```csharp
builder.Services.AddCascadingValue(sp => new ThemeInfo { ButtonClass = "btn-primary" });
builder.Services.AddCascadingValue("AlphaGroup", sp => new Dalek { Units = 456 });
builder.Services.AddCascadingValue(sp =>
    new CascadingValueSource<Dalek>(new Dalek(), isFixed: false));
```

`CascadingValueSource<T>.NotifyChangedAsync()` triggers re-render of subscribers. Notifications only delivered to interactive render modes (Static SSR sees a snapshot).

**Cascading values do NOT cross render-mode boundaries.** For shared state across modes, use root-level cascading services + `[PersistentState]`.

## `Virtualize<TItem>`

```razor
<div style="height:500px; overflow-y:scroll" tabindex="-1">
    <Virtualize Items="rows" Context="row" ItemSize="100">
        <FlightSummary @key="row.Id" Details="@row.Summary" />
    </Virtualize>
</div>
```

Server-paged source via `ItemsProvider` (mutually exclusive with `Items`):

```csharp
private async ValueTask<ItemsProviderResult<Employee>> LoadEmployees(ItemsProviderRequest req)
{
    var num = Math.Min(req.Count, totalEmployees - req.StartIndex);
    var rows = await Service.GetAsync(req.StartIndex, num, req.CancellationToken);
    return new ItemsProviderResult<Employee>(rows, totalEmployees);
}
```

Slots: `<ItemContent>`, `<Placeholder>`, `<EmptyContent>`. Defaults: `ItemSize=50` (px), `OverscanCount=3`.

Layout requirements:
- Identical row height (or use `ItemSize` as a good estimate).
- Scroll container: `display: block`, `flex` with `flex-direction: column`, or `table-row-group`.
- `tabindex="-1"` (or 0) on the scroll container for keyboard scrolling in Chromium.
- Don't style spacer elements (no borders, no `content` pseudo-elements).

Inside `<tbody>` use `SpacerElement="tr"`. `RefreshDataAsync()` re-fetches the visible window.
