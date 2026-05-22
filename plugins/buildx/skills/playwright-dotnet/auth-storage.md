# Authentication and StorageState

Log in once, reuse across tests, multi-role (admin vs user).

## `StorageState`

Serializes a context's cookies + localStorage to JSON. Other contexts/tests import that file to start "already logged in" without repeating the login.

## Step 1: Login and save state

```csharp
[TestClass]
public static class AuthSetup
{
    public static string UserStatePath  { get; private set; } = "";
    public static string AdminStatePath { get; private set; } = "";

    [AssemblyInitialize]
    public static async Task GlobalSetup(TestContext context)
    {
        var playwright = await Playwright.CreateAsync();
        var browser    = await playwright.Chromium.LaunchAsync();

        // User
        UserStatePath = Path.Combine(
            context.TestRunResultsDirectory ?? ".", "user-state.json");
        var userCtx = await browser.NewContextAsync(new()
        {
            BaseURL = "https://localhost:5001",
            IgnoreHTTPSErrors = true,
        });
        var userPage = await userCtx.NewPageAsync();
        await userPage.GotoAsync("/login");
        await userPage.GetByLabel("Email").FillAsync("user@example.com");
        await userPage.GetByLabel("Password").FillAsync("P@ssw0rd!");
        await userPage.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await userPage.WaitForURLAsync("**/dashboard");
        await userCtx.StorageStateAsync(new() { Path = UserStatePath });
        await userCtx.CloseAsync();

        // Admin
        AdminStatePath = Path.Combine(
            context.TestRunResultsDirectory ?? ".", "admin-state.json");
        var adminCtx = await browser.NewContextAsync(new()
        {
            BaseURL = "https://localhost:5001",
            IgnoreHTTPSErrors = true,
        });
        var adminPage = await adminCtx.NewPageAsync();
        await adminPage.GotoAsync("/login");
        await adminPage.GetByLabel("Email").FillAsync("admin@example.com");
        await adminPage.GetByLabel("Password").FillAsync("Adm1n!");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await adminPage.WaitForURLAsync("**/admin/dashboard");
        await adminCtx.StorageStateAsync(new() { Path = AdminStatePath });
        await adminCtx.CloseAsync();

        await browser.CloseAsync();
        playwright.Dispose();
    }
}
```

## Step 2: Reuse via `ContextOptions`

```csharp
[TestClass]
public class UserDashboardTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL           = "https://localhost:5001",
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

[TestClass]
public class AdminTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL           = "https://localhost:5001",
        IgnoreHTTPSErrors = true,
        StorageStatePath  = AuthSetup.AdminStatePath,
    };

    [TestMethod]
    public async Task Admin_CanSeeAllUsers()
    {
        await Page.GotoAsync("/admin/users");
        await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(10);
    }
}
```

## Multi-role in a single test

When a single test needs both admin and user (admin approves → user sees approved):

```csharp
[TestClass]
public class ApprovalFlowTests : BrowserTest
{
    [TestMethod]
    public async Task Admin_Approves_User_SeesApproved()
    {
        var adminCtx = await Browser.NewContextAsync(new()
        {
            BaseURL = "https://localhost:5001", IgnoreHTTPSErrors = true,
            StorageStatePath = AuthSetup.AdminStatePath,
        });
        var adminPage = await adminCtx.NewPageAsync();

        var userCtx = await Browser.NewContextAsync(new()
        {
            BaseURL = "https://localhost:5001", IgnoreHTTPSErrors = true,
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

## Aspire integration

Login runs against the Aspire `web` host; `BaseURL` comes from `app.GetEndpoint("web")`:

```csharp
[AssemblyInitialize]
public static async Task GlobalSetup(TestContext context)
{
    // ... start Aspire — see dotnet-aspire/playwright-testing.md ...
    var baseUrl = AspireTestHost.App.GetEndpoint("web").ToString();

    var ctx = await browser.NewContextAsync(new()
    {
        BaseURL = baseUrl, IgnoreHTTPSErrors = true,
    });
    // ... login ...
    await ctx.StorageStateAsync(new() { Path = userStatePath });
}
```

See `dotnet-aspire` § playwright-testing.

## Common errors

| Symptom | Cause | Fix |
|---|---|---|
| `state.json` expires mid-suite | Short-lived JWT (15 min) | Use refresh token or session cookie without expiry |
| `state.json` doesn't exist | `[AssemblyInitialize]` failed silently | Verify login passed with `WaitForURLAsync` |
| Cookies not sent | Cross-domain — `StorageState` only saves cookies for the context's domain | Login from the correct domain (SSO needs that domain first) |
| `BaseURL` mismatch | Cookies are domain-bound | Match recorded `BaseURL` to test `BaseURL` |

## Sources

- https://playwright.dev/dotnet/docs/auth
- https://playwright.dev/dotnet/docs/api/class-browsercontext#browser-context-storage-state
