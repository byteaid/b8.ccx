# Authentication and StorageState

Log in once, reuse across tests and across runs, multi-role (admin vs user).

## `StorageState`

Serializes a context's cookies + localStorage to JSON. Other contexts/tests import that file to start "already logged in" without repeating the login.

## Where the state files live

- **Path:** `TestResults/.auth/{role}-state.json`. Sibling of the per-run folders, shared across runs because login is expensive.
- **Resolver:** `TestArtifacts.AuthDir(TestContext)` from `dotnet-testing` § Test artefacts. Computes `Path.GetDirectoryName(TestContext.TestRunDirectory)` + `/.auth/` and creates the directory if missing.
- **Lifecycle:** files survive across `dotnet test` invocations. Delete the folder to force a re-login (or check `File.Exists(path)` before generating).
- **`.gitignore`:** add `TestResults/` (or at minimum `TestResults/.auth/`) — these files contain valid session cookies.

## Step 1: Login and save state (assembly-wide)

`[AssemblyInitialize]` is the canonical home for auth setup. It is one of the **two** legitimate uses of `[AssemblyInitialize]` (the other is pre-computing reference data) — it does NOT mount the production AppHost the tests will exercise. Each test class still mounts its own AppHost per `dotnet-testing` § mstest-integration.

Two viable strategies depending on what the auth endpoint needs:

### Strategy A — short-lived auxiliary AppHost (the team default)

Boot an AppHost just long enough to log in, save the state, dispose. The per-class AppHosts of the actual tests come later and reuse the saved state.

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Acme.Inventory.Test;

[TestClass]
public static class AuthSetup
{
    public static string UserStatePath  { get; private set; } = "";
    public static string AdminStatePath { get; private set; } = "";

    [AssemblyInitialize]
    public static async Task GlobalSetup(TestContext context)
    {
        var authDir = TestArtifacts.AuthDir(context);
        UserStatePath  = Path.Combine(authDir, "user-state.json");
        AdminStatePath = Path.Combine(authDir, "admin-state.json");

        // Skip if both files already exist and are recent enough.
        if (File.Exists(UserStatePath) && File.Exists(AdminStatePath) &&
            IsFresh(UserStatePath) && IsFresh(AdminStatePath))
            return;

        // Spin up an auxiliary AppHost just for login.
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Acme_Inventory_AppHost>([]);
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("web")
            .WaitAsync(TimeSpan.FromMinutes(3));
        var baseUrl = app.GetEndpoint("web").ToString();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        await LoginAndSave(browser, baseUrl, "user@example.com",  "P@ssw0rd!", "/dashboard",       UserStatePath);
        await LoginAndSave(browser, baseUrl, "admin@example.com", "Adm1n!",    "/admin/dashboard", AdminStatePath);
    }

    private static async Task LoginAndSave(
        IBrowser browser, string baseUrl, string email, string password,
        string postLoginUrlPattern, string statePath)
    {
        var ctx = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl,
            IgnoreHTTPSErrors = true,
        });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.WaitForURLAsync($"**{postLoginUrlPattern}");
        await ctx.StorageStateAsync(new() { Path = statePath });
        await ctx.CloseAsync();
    }

    private static bool IsFresh(string path) =>
        File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-12);
}
```

Notes:

- The auxiliary AppHost is fully disposed before the first test class mounts its own. There is no shared `_app`, no `AppHostFixtureBase`, no cross-class fixture — only a JSON file left on disk.
- Cookies are domain-bound, not port-bound — saving against `https://localhost:<port-A>` works when a later AppHost serves on `https://localhost:<port-B>`. The cookie file's port-less domain match is what the browser checks.
- The `IsFresh` gate avoids paying the boot cost on every `dotnet test` run during local development. Tune the threshold to your session lifetime (15 min for short JWTs, 12 h for long-lived sessions).

### Strategy B — login against an out-of-band endpoint

When the auth provider is external (Okta, Entra ID, a long-lived dev tenant) and does not require the AppHost, skip the auxiliary AppHost entirely:

```csharp
[AssemblyInitialize]
public static async Task GlobalSetup(TestContext context)
{
    var authDir = TestArtifacts.AuthDir(context);
    UserStatePath  = Path.Combine(authDir, "user-state.json");
    AdminStatePath = Path.Combine(authDir, "admin-state.json");

    if (File.Exists(UserStatePath) && File.Exists(AdminStatePath)) return;

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync();
    // Login flow against https://dev-tenant.example.com — no AppHost needed.
    // ...
}
```

## Step 2: Reuse via `ContextOptions`

Each per-class test mounts its own AppHost (per `dotnet-testing` § mstest-integration) and reuses the saved state by overriding `ContextOptions`:

```csharp
[TestClass]
public sealed class UserDashboard_Tests : PageTest
{
    private static DistributedApplication _app = null!;
    private static string _baseUrl = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Acme_Inventory_AppHost>([]);
        appHost.AddFileLogging(Path.Combine(TestArtifacts.RunDir(context), "logs"));
        _app = await appHost.BuildAsync();
        await _app.StartAsync();
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("web")
            .WaitAsync(TimeSpan.FromMinutes(3));
        _baseUrl = _app.GetEndpoint("web").ToString();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL           = _baseUrl,
        IgnoreHTTPSErrors = true,
        StorageStatePath  = AuthSetup.UserStatePath,
    };

    [TestMethod]
    public async Task Dashboard_ShowsUserOrders()
    {
        await Page.GotoAsync("/dashboard");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My Orders" }))
            .ToBeVisibleAsync();
    }
}
```

## Multi-role in a single test

When a single test needs both admin and user (admin approves → user sees approved):

```csharp
[TestClass]
public sealed class ApprovalFlow_Tests : BrowserTest
{
    private static DistributedApplication _app = null!;
    private static string _baseUrl = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context) { /* per-class mount; sets _baseUrl */ }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task Admin_Approves_User_SeesApproved()
    {
        var adminCtx = await Browser.NewContextAsync(new()
        {
            BaseURL = _baseUrl, IgnoreHTTPSErrors = true,
            StorageStatePath = AuthSetup.AdminStatePath,
        });
        var adminPage = await adminCtx.NewPageAsync();

        var userCtx = await Browser.NewContextAsync(new()
        {
            BaseURL = _baseUrl, IgnoreHTTPSErrors = true,
            StorageStatePath = AuthSetup.UserStatePath,
        });
        var userPage = await userCtx.NewPageAsync();

        await adminPage.GotoAsync("/admin/requests/1");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Expect(adminPage.GetByText("Approved")).ToBeVisibleAsync();

        await userPage.GotoAsync("/my-requests/1");
        await Expect(userPage.GetByText("Approved")).ToBeVisibleAsync();

        await adminCtx.CloseAsync();
        await userCtx.CloseAsync();
    }
}
```

## Cookie auth vs Bearer token

### Cookie auth (Blazor + BFF — the team default)

`StorageState` automatically includes cookies. `StorageStatePath` is all you need — cookies sent on every request. See `dotnet-blazor-auth` for the full BFF pattern.

### Bearer token in header

```csharp
var context = await Browser.NewContextAsync(new()
{
    ExtraHTTPHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer eyJhbGci..."
    }
});
```

For HTTP-only API testing (no browser):

```csharp
var apiContext = await Playwright.APIRequest.NewContextAsync(new()
{
    BaseURL = "https://localhost:5001",
    ExtraHTTPHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer eyJhbGci..."
    }
});

var response = await apiContext.GetAsync("/api/orders");
await Expect(response).ToBeOKAsync();
```

## Common errors

| Symptom | Cause | Fix |
|---|---|---|
| `state.json` expires mid-suite | Short-lived JWT (15 min) | Tighten `IsFresh` threshold, or use refresh token / long-lived session cookie |
| `state.json` doesn't exist | `[AssemblyInitialize]` failed silently | Verify login passed with `WaitForURLAsync`; check the auxiliary AppHost logs |
| Cookies not sent | Cross-domain — `StorageState` only saves cookies for the context's domain | Login from the correct domain (SSO needs that domain first) |
| `BaseURL` mismatch | Cookies are domain-bound | Match recorded domain to test `BaseURL` (`localhost` works across ports) |
| Auth files committed to git | Missing `.gitignore` entry | Add `TestResults/` (or at least `TestResults/.auth/`) |

## Sources

- https://playwright.dev/dotnet/docs/auth
- https://playwright.dev/dotnet/docs/api/class-browsercontext#browser-context-storage-state
