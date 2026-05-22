# Render Modes & App Wiring

The four render modes (Static SSR / InteractiveServer / InteractiveWebAssembly / InteractiveAuto), application-level wiring, and propagation rules. Load when scaffolding a Blazor Web App, picking a mode, or debugging prerender flicker.

## Render modes

| Name | Static class member | Where it renders | Interactive |
|---|---|---|---|
| Static Server | (none / no `@rendermode`) | Server, response stream | no |
| Interactive Server | `RenderMode.InteractiveServer` | Server (SignalR circuit) | yes |
| Interactive WebAssembly | `RenderMode.InteractiveWebAssembly` | Browser (WASM) | yes |
| Interactive Auto | `RenderMode.InteractiveAuto` | Server first → WASM after cached | yes |

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();
app.UseAntiforgery();              // required — non-negotiable rule #1

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddInteractiveWebAssemblyRenderMode()
   .AddAdditionalAssemblies(typeof(BlazorSample.Client._Imports).Assembly);

app.Run();
```

Apply a mode per instance (`<Counter @rendermode="InteractiveWebAssembly" />`), per page (`@rendermode InteractiveWebAssembly` next to `@page`), or globally on `Routes` / `HeadOutlet`. Templates emit `@using static Microsoft.AspNetCore.Components.Web.RenderMode` so you can drop the prefix.

Disable prerender per instance: `@rendermode @(new InteractiveServerRenderMode(prerender: false))`. Same for the Wasm/Auto types.

Detect runtime location: `RendererInfo.Name` (`Static` | `Server` | `WebAssembly` | `WebView`), `RendererInfo.IsInteractive` (`false` during prerender or pure static SSR), `AssignedRenderMode`.

Mark a page for static-only inside an interactive app: `@attribute [ExcludeFromInteractiveRouting]`, then in `App.razor` choose render mode based on `HttpContext.AcceptsInteractiveRouting()`.

## Propagation rules

- **Cannot switch to a *different* interactive mode in a child** (e.g. `InteractiveWebAssembly` child of `InteractiveServer` parent → runtime error). Inherit, or set the same mode again.
- **Making a root component interactive is unsupported.** `App.razor` is always Static; set the mode on `Routes` / `HeadOutlet`.
- **Parameters crossing a Static → Interactive boundary must be JSON-serializable.** `RenderFragment` / `ChildContent` cannot cross. Wrap the interactive child in another component (the templated `Routes` ↔ `Router` pattern).
- **Cascading values do NOT cross render-mode boundaries.** For shared state across modes, use root-level cascading services + `[PersistentState]`.

## Streaming SSR

`@attribute [StreamRendering]` flushes partial HTML during async initialization (Blazor Web App, .NET 8+). Especially valuable when `OnInitializedAsync` hits a slow API.

## Quick decision matrix

| Question | Answer |
|---|---|
| New page that doesn't need interactivity | Static SSR (no `@rendermode`) |
| Stateful UI, server-only logic, low latency to user | InteractiveServer |
| Offline-capable / static-host / CPU-heavy in-browser | InteractiveWebAssembly |
| Want fast first paint + offload after warmup | InteractiveAuto |
| Need to flush HTML during slow init | `@attribute [StreamRendering]` |
