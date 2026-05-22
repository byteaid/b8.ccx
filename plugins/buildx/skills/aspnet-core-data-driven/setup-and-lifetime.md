# Project Setup, DbContext Lifetime, Bootstrap Seeding, Scaffolding

NuGet packages, `appsettings.json`, `AddDbContext`/`Pool`/`Factory`, `EnsureCreated` vs migrations, Razor Pages / MVC scaffold commands, `dotnet ef dbcontext scaffold`.

## Project setup

NuGet packages on the web tier:

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` / `.Sqlite` / `Npgsql.EntityFrameworkCore.PostgreSQL` | Provider |
| `Microsoft.EntityFrameworkCore.Design` | Design-time (migrations, scaffold-dbcontext) |
| `Microsoft.EntityFrameworkCore.Tools` | PMC commands |
| `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore` | Dev-page filter for DB errors |
| `Microsoft.VisualStudio.Web.CodeGeneration.Design` | Enables `dotnet aspnet-codegenerator` |
| `Aspire.Microsoft.EntityFrameworkCore.SqlServer` | Aspire client integration (EF Core flavor) |
| `Aspire.Microsoft.Data.SqlClient` | Aspire client integration (raw ADO.NET) |
| `Dapper` | Optional micro-ORM companion |

Tools:

```bash
dotnet tool install --global dotnet-ef
dotnet tool install --global dotnet-aspnet-codegenerator
```

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SchoolContext":      "Server=(localdb)\\mssqllocaldb;Database=ContosoUniversity1;Trusted_Connection=True;MultipleActiveResultSets=true",
    "SchoolContextSQLite":"Data Source=CU.db"
  }
}
```

## DbContext lifetime

| API | Lifetime | When to use |
|---|---|---|
| `AddDbContext<T>` | Scoped (per-request) | **Razor Pages / MVC default.** Safe with controllers/pages. |
| `AddDbContextPool<T>` | Pooled scoped | High throughput. Constructor must take only `DbContextOptions<T>`; **no captured mutable scoped state**; `OnConfiguring` not allowed to capture scoped state. |
| `AddDbContextFactory<T>` | Singleton factory `IDbContextFactory<T>` | **Blazor Server**, background services, parallel work. Caller does `await using var ctx = await Factory.CreateDbContextAsync();`. |
| `AddPooledDbContextFactory<T>` | Pooled factory | Blazor Server at scale. |

```csharp
// Razor Pages / MVC
builder.Services.AddDbContext<SchoolContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("SchoolContext")));

// Blazor Server
builder.Services.AddDbContextFactory<SchoolContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("SchoolContext")));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();   // dev only
builder.Services.AddRazorPages();                              // or AddControllersWithViews()

var app = builder.Build();
if (app.Environment.IsDevelopment())
    app.UseMigrationsEndPoint();                               // auto-migrate banner page
```

For DbContext config (`UseSqlServer`, retry strategies, command timeout, model conventions) load `dotnet-ef-core`.

## Bootstrap-time DB creation / seeding

Dev-only `EnsureCreated` (cannot coexist with migrations):

```csharp
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<SchoolContext>();
    ctx.Database.EnsureCreated();
    DbInitializer.Initialize(ctx);
}
```

Production seeding pattern uses an idempotent guard:

```csharp
public static class DbInitializer
{
    public static void Initialize(SchoolContext context)
    {
        if (context.Students.Any()) return;     // already seeded
        // AddRange + SaveChanges in topological order
    }
}
```

For prod migrations, use **idempotent SQL script** (`dotnet ef migrations script --idempotent`), **migration bundle** (`dotnet ef migrations bundle --self-contained -o efbundle.exe`), or a dedicated migration worker (`IHostedService`) that is not scaled out. `Database.Migrate()` at startup in scaled-out farms is a race.

## Scaffolding

Razor Pages CRUD:

```bash
dotnet aspnet-codegenerator razorpage \
  -m Student \
  -dc ContosoUniversity.Data.SchoolContext \
  -udl \
  -outDir Pages/Students \
  --referenceScriptLibraries \
  -dbProvider sqlserver
```

Generates 5 page pairs (`Index`, `Create`, `Details`, `Edit`, `Delete` — `*.cshtml` + `*.cshtml.cs`), wires DI, uses `_ValidationScriptsPartial`.

MVC controller + views:

```bash
dotnet aspnet-codegenerator controller \
  -name StudentsController \
  -m Student \
  -dc ContosoUniversity.Data.SchoolContext \
  --useDefaultLayout \
  --relativeFolderPath Controllers
```

Reverse-engineer DbContext (database-first):

```bash
dotnet ef dbcontext scaffold "Server=(localdb)\\mssqllocaldb;Database=Existing;Trusted_Connection=True" \
  Microsoft.EntityFrameworkCore.SqlServer \
  --output-dir Models \
  --context-dir Data \
  --context AppDbContext \
  --use-database-names \
  --no-onconfiguring \
  --table dbo.Student --table dbo.Enrollment
```
