# Migrations, design-time factory, seeding

> Prerequisite: [skill.md](skill.md) § DbContext registration.

## The Aspire problem

`dotnet ef migrations add X` instantiates the `DbContext` to read its model. Without Aspire running there is no `ConnectionStrings__<name>` env var, so EF fails with `Unable to create a DbContext of type 'BillingDb'`. The fix is an `IDesignTimeDbContextFactory<T>` shipped inside the `[Company].[Product].{TechName}` adapter project (e.g. `Acme.Billing.SqlServer`). The factory's connection string is only used to let EF generate SQL — `migrations add` never opens the connection.

## Design-time factory (canonical)

```csharp
// src/Acme.Billing.SqlServer/BillingDbDesignTimeFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class BillingDbDesignTimeFactory : IDesignTimeDbContextFactory<BillingDb>
{
    public BillingDb CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("BILLINGDB_DESIGN_CS")
                 ?? "Server=localhost,1433;Database=billingdb_design;User=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False";

        var opts = new DbContextOptionsBuilder<BillingDb>()
            .UseSqlServer(cs, sql => sql.MigrationsAssembly(typeof(BillingDb).Assembly.FullName))
            .Options;

        return new BillingDb(opts);
    }
}
```

Rules:

- The CS name (`billingdb`) MUST match the AppHost's `AddDatabase("billingdb")`. The design-time fallback is just enough to compile the model — it is not a runtime CS.
- For `database update` against a real Aspire fixture, override with `--connection "Server=localhost,<port>;Database=billingdb;..."` (port from the Aspire dashboard).
- The factory class must be `public` and parameterless-constructible. EF Tools discovers it by reflection.

## Canonical commands

```bash
# Add migration
dotnet ef migrations add AddInvoiceTable \
  --project src/Acme.Billing.SqlServer \
  --startup-project src/Acme.Billing.WebAPI

# Apply against a real Aspire CS (manual)
dotnet ef database update \
  --project src/Acme.Billing.SqlServer \
  --startup-project src/Acme.Billing.WebAPI \
  --connection "Server=localhost,<port>;Database=billingdb;User=sa;Password=...;TrustServerCertificate=True"

# Generate SQL script for review / DBA
dotnet ef migrations script <FromMigration> <ToMigration> \
  --project src/Acme.Billing.SqlServer \
  --startup-project src/Acme.Billing.WebAPI \
  --idempotent \
  -o migrations.sql

# Self-contained migration bundle (deploy artifact)
dotnet ef migrations bundle \
  --project src/Acme.Billing.SqlServer \
  --startup-project src/Acme.Billing.WebAPI \
  --self-contained -r linux-x64 -o ./artifacts/billing-migrator
```

`--idempotent` script wraps each migration in a `IF NOT EXISTS` guard against `__EFMigrationsHistory` — safe to re-apply. Bundles are single-file executables that read the CS from a `--connection` argument or the `ConnectionStrings__<name>` env var.

## When to bundle vs script

| Mode | When |
|---|---|
| **Bundle** (`migrations bundle`) | Production deploy step; the runtime image does not have the .NET SDK; CI/CD pipeline. Reproducible binary, no dependency on `dotnet ef`. |
| **Idempotent SQL script** (`migrations script --idempotent`) | DBA review required; environment runs strict change-control; rollback playbooks need raw SQL. |
| **`Database.MigrateAsync` on boot** | Local dev / integration tests only. **Never in prod** — racy across replicas, no rollback, no DBA review. |

## Migrate on boot (dev only)

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BillingDb>();
    await db.Database.MigrateAsync();
}
```

In prod, the migration bundle runs as a separate Aspire `AddExecutable` resource with `WaitForCompletion(migrator)` blocking the API — see `dotnet-aspire` for that wiring.

## Idempotent reference seeding

Two valid shapes. Pick **one** for a given table — never both.

### A) `migrationBuilder.InsertData` inside the migration

Preferred for static reference data that ships with the schema (currencies, tax categories, system roles). The data lives in the migration; rolling back the migration removes it.

```csharp
public partial class SeedTaxCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "tax_categories",
            columns: new[] { "code", "rate", "name" },
            values: new object[,]
            {
                { "STD", 0.21m, "Standard" },
                { "RED", 0.10m, "Reduced" },
                { "SUP", 0.04m, "Super-reduced" },
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(table: "tax_categories", keyColumn: "code",
            keyValues: new object[] { "STD", "RED", "SUP" });
    }
}
```

`InsertData` is idempotent across re-applies because the migrations history table prevents re-running. It composes with `--idempotent` script generation.

### B) Hand-written upsert seeder

Use when reference data needs to be reconciled (rates change, names get corrected) without authoring a migration each time. The seeder runs once at startup after `MigrateAsync` and upserts by natural key.

```csharp
public static class BillingDbSeeder
{
    public static async Task SeedAsync(BillingDb db, CancellationToken ct = default)
    {
        await UpsertTaxCategoriesAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertTaxCategoriesAsync(BillingDb db, CancellationToken ct)
    {
        var wanted = new[]
        {
            new TaxCategory { Code = "STD", Rate = 0.21m, Name = "Standard" },
            new TaxCategory { Code = "RED", Rate = 0.10m, Name = "Reduced" },
            new TaxCategory { Code = "SUP", Rate = 0.04m, Name = "Super-reduced" },
        };
        foreach (var tc in wanted)
        {
            var existing = await db.TaxCategories.FirstOrDefaultAsync(x => x.Code == tc.Code, ct);
            if (existing is null)
            {
                db.TaxCategories.Add(tc);
            }
            else if (existing.Rate != tc.Rate || existing.Name != tc.Name)
            {
                existing.Rate = tc.Rate;
                existing.Name = tc.Name;
            }
        }
    }
}
```

**Team rule:** the seeder runs the same way in dev and prod. Never `if (IsDevelopment()) SeedAsync(...)`. Dev-only fixtures live in a dedicated seeding type / data file inside the test project's `Seeding/` folder, never gated on `IsDevelopment()` in the production seeder.

`HasData` (the model-builder seeding API) is **not used** — it generates migration inserts with hard-coded keys, breaks under merge, and forces a migration for every data change.

## Test-time seeding

Test-time fixtures are not the same problem as reference seeding. See `dotnet-aspire` § test-seeding for the canonical strategy (per-test class, fresh fixture, tear-down).

## Model-snapshot conflicts in branches

Two devs both run `migrations add X` on parallel branches → both edit `BillingDbContextModelSnapshot.cs` → merge conflict on the snapshot.

| Situation | Resolution |
|---|---|
| Only one branch's migration is needed | Take theirs; `dotnet ef migrations remove` on yours; rebase; `migrations add` again. |
| Both migrations are needed and conflict-free at the schema level | Keep both migrations on the timeline (rename if names collide); regenerate the snapshot by deleting `BillingDbContextModelSnapshot.cs` and running `dotnet ef migrations add Reconcile --no-build` after both migrations are merged. |
| Both migrations touch the same table/column | Manual reconciliation: pick one, `migrations remove` the other, fold its changes into a new migration. |

`dotnet ef migrations has-pending-model-changes` (EF 8+) detects an out-of-date snapshot in CI before the merge lands.

## Configuration knobs in `OnConfiguring` (NOT used)

`OnConfiguring` is reserved for the design-time fallback. The runtime CS is injected via `DbContextOptionsBuilder` from `Program.cs`. Mixing both produces double-configuration warnings and is hard to debug.
