# Authentication, Authorization, and CORS

JWT (incl. `?access_token=` for WebSockets), cookie, Windows, mTLS, `[Authorize]`, `HubInvocationContext`, CORS.

## Authentication & authorization

User available via `Hub.Context.User` (`ClaimsPrincipal`). Multiple connections may share one user.

### JWT bearer

JS / TS:

```typescript
new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", { accessTokenFactory: () => this.loginToken })
    .build();
```

.NET:

```csharp
.WithUrl("https://example.com/chathub", o =>
{
    o.AccessTokenProvider = () => Task.FromResult(_token);
});
```

Server JWT setup — fish the token out of the WebSocket query string:

```csharp
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    o.Authority = "https://your-issuer";
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) &&
                ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
```

ASP.NET Core 10: known API endpoints no longer redirect to login pages with cookie auth — they return **401/403**.

### Cookie / Windows / mTLS

Browser cookie flows automatically inherit the cookie (no extra client config). Windows auth: `AddNegotiate()` + `IUserIdProvider` (because no `NameIdentifier` claim). .NET client opts in via `o.UseDefaultCredentials = true`. Edge supports Windows auth + WebSockets; Chrome and Safari do not — they fall back to long polling.

### `[Authorize]` and custom requirements

```csharp
[Authorize]
public class ChatHub : Hub
{
    public Task Send(string m) => /* ... */;
    [Authorize("Administrators")] public void BanUser(string u) { /* ... */ }
}

app.MapHub<ChatHub>("/chat").RequireAuthorization("Administrators");
```

Custom resource-based handler — receive `HubInvocationContext` (exposes `HubMethodName`, `HubMethodArguments`, `Hub`, `ServiceProvider`, `HubMethod` MethodInfo):

```csharp
public class DomainRestrictedRequirement
    : AuthorizationHandler<DomainRestrictedRequirement, HubInvocationContext>,
      IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, DomainRestrictedRequirement req,
        HubInvocationContext resource)
    {
        // Check resource.HubMethodName, ctx.User claims, etc.
        return Task.CompletedTask;
    }
}
```

For deep auth — JWT issuance, OIDC, Identity, Data Protection — load `aspnet-core-security`.

## CORS

Specific origin (no `*`), `GET` and `POST`, **`AllowCredentials()`**, `app.UseCors()` BEFORE `MapHub`:

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("https://example.com")
    .AllowAnyHeader()
    .WithMethods("GET", "POST")
    .AllowCredentials()));

app.UseCors();
app.MapHub<ChatHub>("/chatHub");
```

Per-endpoint: `app.MapHub<ChatHub>("/chatHub").RequireCors("SignalRPolicy");` or `[EnableCors("SignalRPolicy")]` on the hub class.

**CORS does NOT cover WebSockets.** Validate `Origin` header in custom middleware before `MapHub`/`UseAuthentication`. `Origin` is client-controlled — don't use it for AuthN.
