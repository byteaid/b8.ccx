# Authorization

`[Authorize]`, role/policy/resource-based authz, custom requirements + handlers, `IAuthorizationPolicyProvider`.

## Attributes & endpoints

| Attribute | Effect |
|---|---|
| `[Authorize]` | Require authenticated user |
| `[Authorize(Roles = "Admin,Manager")]` | OR among listed roles |
| `[Authorize(Roles="Admin"), Authorize(Roles="Power")]` | AND across attributes |
| `[Authorize(Policy = "RequireAdult")]` | Named policy |
| `[Authorize(AuthenticationSchemes = "Bearer,Cookies")]` | Limit to scheme(s) |
| `[AllowAnonymous]` | Bypass all `[Authorize]` |

```csharp
app.MapGet("/secure", () => "ok").RequireAuthorization();
app.MapGet("/admin",  () => "ok").RequireAuthorization("RequireAdminRole");
app.MapGet("/inline", () => "ok").RequireAuthorization(p => p.RequireRole("Admin"));
```

Imperative: `var ok = await _authz.AuthorizeAsync(user, doc, "EditDocumentPolicy"); if (!ok.Succeeded) return Forbid();`.

## Razor / Blazor `AuthorizeView`

```razor
<AuthorizeView Roles="Admin,SuperUser">
    <Authorized>Hello @context.User.Identity?.Name</Authorized>
    <NotAuthorized>Sign in.</NotAuthorized>
    <Authorizing>Loading...</Authorizing>
</AuthorizeView>

<AuthorizeView Policy="EditDocument" Resource="@doc">
    <button @onclick="Save">Save</button>
</AuthorizeView>
```

Blazor WASM: `builder.Services.AddAuthorizationCore();`. For the Blazor surface load `aspnet-core-blazor`.

## Roles via policy

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ElevatedRights",    p => p.RequireRole("Admin", "SuperUser"))               // OR
    .AddPolicy("AdminAndSuperUser", p => { p.RequireRole("Admin"); p.RequireRole("SuperUser"); }); // AND
```

Windows Authentication groups appear as claims; `User.IsInRole(@"DOMAIN\GroupName")`. Project SIDs into stable role claims via `IClaimsTransformation`.

## Built-in policy builder methods

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("HasEmployeeId",       p => p.RequireClaim("EmployeeId"))
    .AddPolicy("EmployeeIdInList",    p => p.RequireClaim("EmployeeId", "1", "2", "5"))
    .AddPolicy("UsernameJoe",         p => p.RequireUserName("joe"))
    .AddPolicy("Assertion",           p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("api:read"))))
    .AddPolicy("BearerOnly",          p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
```

## Custom requirement + handler

```csharp
public sealed class MinimumAgeRequirement(int age) : IAuthorizationRequirement
{
    public int MinimumAge { get; } = age;
}

public sealed class MinimumAgeHandler : AuthorizationHandler<MinimumAgeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, MinimumAgeRequirement req)
    {
        var dob = ctx.User.FindFirst(c => c.Type == ClaimTypes.DateOfBirth);
        if (dob is null) return Task.CompletedTask;
        var birth = Convert.ToDateTime(dob.Value);
        var age = DateTime.Today.Year - birth.Year;
        if (birth > DateTime.Today.AddYears(-age)) age--;
        if (age >= req.MinimumAge) ctx.Succeed(req);
        return Task.CompletedTask;
    }
}

builder.Services.AddSingleton<IAuthorizationHandler, MinimumAgeHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AtLeast21", p => p.Requirements.Add(new MinimumAgeRequirement(21)));
```

Handler semantics:
- `ctx.Succeed(req)` -> mark requirement satisfied; other handlers still execute (side-effects).
- `ctx.Fail()` -> guarantees failure even if other handlers succeed.
- Authorization handlers run **even if authentication failed** (so anonymous-allowed policies work).
- Order is undefined.
- `AuthorizationOptions.InvokeHandlersAfterFailure` (default `true`) — set false to short-circuit on `Fail()`.

A class implementing both `IAuthorizationRequirement` and `IAuthorizationHandler` is invoked by the built-in `PassThroughAuthorizationHandler` (no DI registration needed).

## Resource-based authorization

```csharp
public sealed class DocumentAuthHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Document>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, OperationAuthorizationRequirement op, Document doc)
    {
        if (op.Name == "Edit" && doc.OwnerId == ctx.User.FindFirstValue(ClaimTypes.NameIdentifier))
            ctx.Succeed(op);
        return Task.CompletedTask;
    }
}
public static class DocumentOps
{
    public static OperationAuthorizationRequirement Edit   = new() { Name = "Edit"   };
    public static OperationAuthorizationRequirement Delete = new() { Name = "Delete" };
}

var ok = await _authz.AuthorizeAsync(User, doc, DocumentOps.Edit);
if (!ok.Succeeded) return Forbid();
```

For endpoint routing, the `Resource` on `AuthorizationHandlerContext` is `HttpContext`.

## Default & fallback policies

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```

`SetFallbackPolicy` is the Microsoft-recommended way to **secure by default**.

## Custom `IAuthorizationPolicyProvider` (parameterized policies)

ASP.NET Core uses **only one** registered `IAuthorizationPolicyProvider` — chain to `DefaultAuthorizationPolicyProvider` for non-dynamic policies. Pattern: `[MinimumAge(21)]` attribute encodes the age in the policy name (`MinAge:21`); the provider parses it and builds a policy on demand.
