---
name: aspnet-core-blazor
description: ASP.NET Core Blazor reference for .NET 10. Covers the Blazor Web App unified model, render modes (Static SSR / InteractiveServer / InteractiveWebAssembly / InteractiveAuto) + propagation, components + lifecycle, data binding, forms with antiforgery, routing + `Router.NotFoundPage`, DI in WASM vs Server + `OwningComponentBase`, `Virtualize<TItem>`, state via `[PersistentState]` / `PersistentComponentState`, JS interop (`IJSRuntime`, modules, `JSImport`/`JSExport`), streaming SSR, AOT/trimming/lazy-load, OTel meters, and Blazor auth surface (`AuthenticationStateProvider`, `AuthorizeView`).
when_to_use: |
  - Trigger keywords: Blazor, Blazor Web App, .razor, @rendermode, InteractiveServer, InteractiveWebAssembly, InteractiveAuto, Static SSR, prerender, ComponentBase, EditForm, NavigationManager, Router, NotFoundPage, Virtualize, PersistentState, PersistentComponentState, IJSRuntime, JSImport, JSExport, AuthorizeView, AuthenticationStateProvider, CascadingValue, OwningComponentBase, circuit, streaming rendering, AOT, lazy assemblies.
  - Task shapes: scaffold a Blazor Web App; pick a render mode for a page/component; convert a static page to interactive; wire `Virtualize` for a long list; persist prerender state across the interactivity handoff; author a `.razor.js` module and dispose it; register cascading values; debug double-execution / prerender flicker; secure a Blazor route; wire OTel meters.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.razor", "**/*.razor.cs", "**/*.razor.js", "**/Program.cs", "**/_Imports.razor"]
---

# ASP.NET Core Blazor — Reference

Reference for authoring and reviewing Blazor code on .NET 10. Pin the rules; defer the long catalogues to the Microsoft docs cited at the bottom.

## Mental model

- UI = **Razor components** (`.razor`), C# classes deriving from `ComponentBase : IComponent`. Components compile to .NET assemblies; the renderer builds a render tree, diffs it, and applies DOM mutations.
- Three deployment shapes: **Blazor Web App** (unified, .NET 8+, the canonical .NET 10 choice), **standalone Blazor WebAssembly**, **Blazor Hybrid** (MAUI/WPF/WinForms — out of scope here).
- In a Blazor Web App, "hosting model" is the wrong frame: each component picks a **render mode** (Static / InteractiveServer / InteractiveWebAssembly / InteractiveAuto). The *Routes* and *HeadOutlet* roots can also set a global mode.
- Interactive Server = component lives on the server; UI events + DOM diffs travel over a per-tab **SignalR circuit**. Interactive WebAssembly = .NET runtime + assemblies download to the browser, JIT/AOT in the WASM sandbox. InteractiveAuto = Server first → WASM after the bundle is cached.
- Async ≠ parallel; circuits are single-threaded per dispatcher. See `dotnet-asynchronous-programming` for `async`/`await` semantics; this skill assumes they are known.

## Non-negotiable rules

1. **Every interactive Blazor Web App needs `app.UseAntiforgery()`** in the pipeline (after `UseAuthentication`/`UseAuthorization`, before `MapRazorComponents<App>`). `AddRazorComponents()` registers the services; the middleware is on you.
2. **Every `EditForm` (and every plain `<form @onsubmit>`) needs a unique `FormName`** — without it the framework cannot identify which form posted.
3. **Static SSR has no Blazor `[Authorize]` / `AuthorizeRouteView` enforcement.** Authorize at the ASP.NET Core middleware layer; treat Blazor `[Authorize]` as a UI hint. The API is the only enforcement point.
4. **Parameters crossing a Static → Interactive boundary must be JSON-serializable.** `RenderFragment` / `ChildContent` cannot cross. Wrap the interactive child in another component (the templated `Routes` ↔ `Router` pattern).
5. **Cannot switch to a *different* interactive mode in a child** (e.g. `InteractiveWebAssembly` child of `InteractiveServer` parent → runtime error). Inherit, or set the same mode again.
6. **Making a root component interactive is unsupported.** `App.razor` is always Static; set the mode on `Routes` / `HeadOutlet`.
7. **Don't call `IJSRuntime` from `OnInitialized{Async}`.** During prerender there is no DOM. Place JS interop in `OnAfterRenderAsync(firstRender: true)`.
8. **Component parameters must be auto-properties.** Framework reflects on the setter; never put logic there. `async void` is not a valid parameter type.
9. **`@key` every dynamic loop child** (`Row @key="item.Id"`). Without it, Blazor reuses subtrees by position and you see ghost state on reorders/removals.
10. **`OwningComponentBase` for component-scoped scoped services.** Default scoped services live for the entire circuit on Server (one user-tab); for `DbContext` and similar, scope per component lifetime.
11. **`PersistentComponentState` payload sent to WASM/Auto is exposed to the browser.** Never persist secrets through it.

## Sub-index

| Topic | File | Load when |
|---|---|---|
| Render modes (Static / InteractiveServer / WASM / Auto), `Program.cs` wiring, propagation rules, streaming SSR | [render-modes.md](render-modes.md) | Scaffolding an app; picking, switching, or debugging a render mode. |
| Components, lifecycle, data binding, event handling, forms (`EditForm`), routing + `Router.NotFoundPage`, cascading values, `Virtualize<TItem>` | [components-and-routing.md](components-and-routing.md) | Authoring components, wiring forms, routing, virtualizing long lists. |
| JavaScript interop — `IJSRuntime`, module isolation, `JSImport`/`JSExport`, `IJSObjectReference` | [js-interop.md](js-interop.md) | Authoring `.razor.js` modules, calling browser APIs, marshalling structured data. |
| State management (`[PersistentState]`, `PersistentComponentState`, state containers), DI lifetimes + `OwningComponentBase`, Blazor auth surface | [state-and-auth.md](state-and-auth.md) | Persisting state across prerender, scoping `DbContext`, wiring `AuthorizeView` / `AuthenticationStateProvider`. |
| Performance, AOT, IL trimming, lazy assemblies, observability meters, GC | [aot-and-trimming.md](aot-and-trimming.md) | Shrinking WASM payload, enabling AOT, wiring OTel meters / activity sources. |

## Quick decision matrix

| Question | Answer |
|---|---|
| New page that doesn't need interactivity | Static SSR (no `@rendermode`) |
| Stateful UI, server-only logic, low latency to user | InteractiveServer |
| Offline-capable / static-host / CPU-heavy in-browser | InteractiveWebAssembly |
| Want fast first paint + offload after warmup | InteractiveAuto |
| Need to flush HTML during slow init | `@attribute [StreamRendering]` |
| Long list to render | `Virtualize<TItem>` with `ItemsProvider` for paged data |
| Need to keep state across prerender → interactive | `[PersistentState]` (or `RegisterPersistentService` for services) |
| Need to call browser API once | `OnAfterRenderAsync(firstRender: true)` + `IJSObjectReference` module |
| Inside a loop | `@key` every child, copy loop var locally for `for`/`while` |
| Component owns a `DbContext` | Inherit `OwningComponentBase<AppDbContext>` |
| Form post | `<EditForm Model FormName>` + `<DataAnnotationsValidator />` + `app.UseAntiforgery()` |
| Custom Not-Found page | `Router.NotFoundPage` + `NavigationManager.NotFound()` |

## Cross-references

- Public docs (Blazor overview): https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-10.0
- Public docs (Render modes): https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0
- Public docs (Lifecycle): https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle?view=aspnetcore-10.0
- Public docs (Forms): https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/?view=aspnetcore-10.0
- Public docs (Routing): https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0
- Public docs (DI): https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0
- Public docs (`Virtualize<TItem>`): https://learn.microsoft.com/en-us/aspnet/core/blazor/components/virtualization?view=aspnetcore-10.0
- Public docs (Persistent state): https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence?view=aspnetcore-10.0
- Public docs (JS interop): https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0
- Public docs (Performance): https://learn.microsoft.com/en-us/aspnet/core/blazor/performance?view=aspnetcore-10.0
- Public docs (Security overview): https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0
- Related skill: `aspnet-core-security` — cookie/JWT/OIDC/Identity, antiforgery deep dive, BFF.
- Related skill: `aspnet-core-signalr` — hub authoring, scale-out, MessagePack (the Blazor circuit *uses* SignalR but you don't author the hub).
- Related skill: `aspnet-core-data-driven` — EF Core consumption from Blazor pages.
- Related skill: `dotnet-asynchronous-programming` — `async`/`await`, `CancellationToken`, dispatcher rules.
- Related skill: `dotnet-testing` § E2E — Playwright setup for Blazor pages (load `playwright-dotnet` for the browser-driver detail).
