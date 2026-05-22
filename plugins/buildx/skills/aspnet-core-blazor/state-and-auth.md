# State Management, DI, Auth Surface

`PersistentComponentState` / `[PersistentState]`, state-container patterns, DI lifetime nuances, `OwningComponentBase`, `AuthorizeView` / `AuthenticationStateProvider`. Load when persisting state across prerender, scoping `DbContext`, or wiring the Blazor-level auth surface.

## Dependency Injection

| Service | WASM | Server |
|---|---|---|
| `IJSRuntime` | Singleton | Scoped |
| `NavigationManager` | Singleton | Scoped |
| `HttpClient` | App-registered (Scoped) | not auto-registered |
| `AuthenticationStateProvider` | when auth added | when auth added |
| `PersistentComponentState` | yes (Web App) | yes (Web App) |

**Lifetime nuances:**

| Lifetime | WASM | Server |
|---|---|---|
| Singleton | one per app lifetime | one per process |
| Scoped | **behaves like Singleton** (no DI scopes in WASM) | one per **circuit** (per user-tab; spans many SignalR messages) |
| Transient | new per resolution; if `IDisposable`, **container holds the reference** for circuit/app lifetime → memory leak |

Treat **disposable transients as a leak**. Use `OwningComponentBase` / `OwningComponentBase<TService>` to scope a child DI scope to component lifetime — the canonical fix for `DbContext` in Blazor.

Constructor injection (.NET 9+) on a `partial class` code-behind is preferred over `@inject` for non-trivial components. Keyed services: `[Inject(Key = "primary")] public IDataAccess Data { get; set; } = default!;`.

Top-level `_Imports.razor` injection caveat: a service injected via `Components/_Imports.razor` resolves once in the always-static `App` and once again in the interactive page → **two instances**. Move such injections to `Components/Pages/_Imports.razor`.

Client-side service that fails during prerender → register a server-side equivalent on the host or disable prerendering: `@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))`.

## State management

Five primary techniques; Blazor doesn't ship a store.

1. **URL-as-state** — encode IDs/paging/filters; survives reload + reconnection.
2. **In-memory state container service** — class with property setter that fires an `event Action? OnChange`. Subscribe in `OnInitialized`, unsubscribe in `Dispose`. WASM: register as Singleton; Server: register as Scoped (per-circuit). Always re-render via `InvokeAsync(StateHasChanged)` if the mutation comes from outside the dispatcher.
3. **Cascading values** — root-level + notifying.
4. **Browser storage** — `IJSRuntime` for `localStorage`/`sessionStorage` on WASM; `Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage` (`ProtectedLocalStorage`, `ProtectedSessionStorage`) on Server. Auto-encrypted via Data Protection. **Not available during prerender** — guard with `RendererInfo.IsInteractive` or `OnAfterRenderAsync`.
5. **`PersistentComponentState`** — bridge prerender → interactive (next section).

## `[PersistentState]` (.NET 10, replaces `[SupplyParameterFromPersistentComponentState]`)

```csharp
@code {
    [PersistentState] public int? CurrentCount { get; set; }
    protected override void OnInitialized() => CurrentCount ??= Random.Shared.Next(100);
}
```

- **Public** properties only.
- Default serializer is `System.Text.Json` with default options; **not trimmer-safe by default** — preserve types via `<TrimmerRootAssembly>` or `[DynamicDependency]`.
- For multiple instances inside a loop, `@key` the parent so each child's state is keyed correctly.

Options:
- `AllowUpdates = true` — refresh on enhanced navigation (default `false` to protect in-flight forms).
- `RestoreBehavior = RestoreBehavior.SkipInitialValue` — skip restore during initial prerender.
- `RestoreBehavior = RestoreBehavior.SkipLastSnapshot` — skip restore on circuit reconnection.

Service-scoped persistence:

```csharp
public class CounterTracker { [PersistentState] public int CurrentCount { get; set; } }

builder.Services.AddScoped<CounterTracker>();
builder.Services.AddRazorComponents()
    .RegisterPersistentService<CounterTracker>(RenderMode.InteractiveAuto);
```

Only **scoped** services are supported. Custom serializer: `builder.Services.AddSingleton<PersistentComponentStateSerializer<User>, CustomUserSerializer>();`.

Imperative model — `PersistentComponentState`:

```csharp
@inject PersistentComponentState ApplicationState
@implements IDisposable

@code {
    private PersistingComponentStateSubscription sub;
    protected override void OnInitialized()
    {
        if (!ApplicationState.TryTakeFromJson<int>(nameof(currentCount), out var v))
            currentCount = Random.Shared.Next(100);
        else currentCount = v;
        sub = ApplicationState.RegisterOnPersisting(() =>
        {
            ApplicationState.PersistAsJson(nameof(currentCount), currentCount);
            return Task.CompletedTask;
        });
    }
    public void Dispose() => sub.Dispose();
}
```

`RegisterOnRestoring` (.NET 10) is the symmetrical hook for fully-imperative restoration during enhanced navigation.

**Security:** state persisted from server prerender to **InteractiveWebAssembly** or **InteractiveAuto** is **exposed in the browser**. Never put secrets there. For pure InteractiveServer, Data Protection wraps the payload (still don't put secrets).

Razor Pages / MVC host: add `<persist-component-state />` inside `</body>` of the host layout.

## Blazor surface for auth

Deep auth (cookie config, refresh tokens, custom `AuthorizationHandler`, claim mapping, `/api/auth/me`, antiforgery cookie names, signing-out flows, BFF) → `aspnet-core-security`. The Blazor-level surface:

```csharp
// Server
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(/* cookie + OIDC */)...;
builder.Services.AddAuthorization();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(o => o.SerializeAllClaims = true);

// Client
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
```

```razor
@inject AuthenticationStateProvider AuthState
@code {
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var user  = state.User;
    }
}

<AuthorizeView Roles="Admin,Superuser" Policy="Over21">
    <Authorized>Hello, @context.User.Identity?.Name</Authorized>
    <Authorizing>Checking…</Authorizing>
    <NotAuthorized>Access denied.</NotAuthorized>
</AuthorizeView>

@page "/secure"
@attribute [Authorize(Policy = "Over21")]

[CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }
```

Pipeline order:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapRazorComponents<App>()...;
```

Hard rules: Static SSR has no Blazor `[Authorize]` enforcement (use middleware); WASM never holds tokens in the BFF model (server API is the only enforcement point); for InteractiveAuto / WebAssembly, secure both the component and the API endpoint (`.RequireAuthorization()`).

## WebSocket compression security note (.NET 9+)

Compression is on by default for the SignalR-backed Interactive Server transport. Don't render data from untrusted sources (route params, query strings, JS interop output, external DBs) inside an authenticated/authorized interactive server component without considering CRIME/BREACH-style attacks. Either disable WebSocket compression for sensitive components, or sanitize/normalize the data before rendering.
