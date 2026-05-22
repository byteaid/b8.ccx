# Aspire Consumer and Blazor Data-Binding

`AddSqlServerDbContext` consumer wiring, Aspire migrations strategies, design-time factory; Blazor render-mode-aware `IDbContextFactory<T>`, `EditForm`.

## .NET Aspire — consumer side

For the AppHost (`AddSqlServer`, `WithDataVolume`, `AddDatabase`, `WithReference`, `WaitFor`) load `dotnet-aspire`. Consumer-side wiring:

### EF Core flavor (`Aspire.Microsoft.EntityFrameworkCore.SqlServer`)

```csharp
builder.AddSqlServerDbContext<SchoolContext>("contoso", configureSettings: s =>
{
    s.DisableHealthChecks = false;     // health probe registered automatically
    s.DisableTracing      = false;
    s.DisableMetrics      = false;
    s.DisableRetry        = false;     // execution strategy on by default
    s.CommandTimeout      = 30;
});
```

Configuration knobs in `appsettings.json` (auto-bound):

```json
{
  "Aspire": {
    "Microsoft": {
      "EntityFrameworkCore": {
        "SqlServer": {
          "ConnectionString": "Server=...",
          "DisableHealthChecks": false,
          "DisableTracing": false,
          "DisableMetrics": false,
          "DisableRetry": false,
          "CommandTimeout": 30
        }
      }
    }
  }
}
```

### Raw ADO.NET (`Aspire.Microsoft.Data.SqlClient`)

```csharp
builder.AddSqlServerClient("contoso", configureSettings: s =>
{
    s.DisableHealthChecks = false;
});

public class ReportService(SqlConnection connection) { ... }
```

### Postgres alternative

```csharp
builder.AddNpgsqlDbContext<SchoolContext>("school");
```

### Migrations under Aspire

- Run from a **dedicated migration worker project** (`IHostedService`) that is not scaled out, OR
- Apply via `dotnet ef migrations script --idempotent` during deploy, OR
- Use `dotnet ef migrations bundle` and run as a one-shot init container in the orchestrator.

### Design-time factory for `dotnet ef` under Aspire

When the AppHost owns the connection string, `dotnet ef` cannot start the DI graph. Provide an `IDesignTimeDbContextFactory<T>` that reads the env var or a known-default fallback:

```csharp
public sealed class SchoolContextFactory : IDesignTimeDbContextFactory<SchoolContext>
{
    public SchoolContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__SchoolContext")
                ?? "Server=(localdb)\\mssqllocaldb;Database=SchoolDesignTime;Trusted_Connection=True";
        var opts = new DbContextOptionsBuilder<SchoolContext>().UseSqlServer(conn).Options;
        return new SchoolContext(opts);
    }
}
```

## Blazor consumers

For Blazor render modes, lifecycle, `OwningComponentBase` for component-scoped DbContext, and `@bind*` semantics, load `aspnet-core-blazor`. Sketch:

### Server (interactive Server) — `IDbContextFactory<T>`

```razor
@page "/students"
@inject IDbContextFactory<SchoolContext> Factory

<ul>
    @foreach (var s in students)
    {
        <li>@s.LastName, @s.FirstMidName</li>
    }
</ul>

@code {
    private List<Student> students = new();

    protected override async Task OnInitializedAsync()
    {
        await using var ctx = await Factory.CreateDbContextAsync();
        students = await ctx.Students.AsNoTracking().ToListAsync();
    }
}
```

### WASM — never `DbContext` in the browser

The WASM client calls a typed `HttpClient` that hits the BFF host; the host owns the `DbContext`.

```csharp
// Server endpoint (BFF)
app.MapGet("/api/students", async (SchoolContext ctx) =>
    await ctx.Students.AsNoTracking().ToListAsync());

// WASM client
public class StudentsClient(HttpClient http)
{
    public Task<List<Student>?> ListAsync() =>
        http.GetFromJsonAsync<List<Student>>("api/students");
}
```

For the BFF wiring (cookie + OIDC + `MapForwarder`) load `aspnet-core-security` § BFF.

### `EditForm` over an EF entity (or view-model)

```razor
<EditForm Model="@student" OnValidSubmit="OnValid" FormName="Edit">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <InputText @bind-Value="student.LastName" />
    <ValidationMessage For="() => student.LastName" />

    <InputDate @bind-Value="student.EnrollmentDate" />
    <button type="submit">Save</button>
</EditForm>

@code {
    Student student = new();
    Task OnValid() => /* call API */ Task.CompletedTask;
}
```

Built-in inputs: `InputText`, `InputTextArea`, `InputNumber<T>`, `InputDate<T>`, `InputCheckbox`, `InputSelect<T>`, `InputRadioGroup<T>`, `InputFile`. Validation pulled from EF entities or view-model `[Required]` / `[StringLength]` annotations.
