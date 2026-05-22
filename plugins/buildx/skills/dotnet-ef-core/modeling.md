# Modeling — converters, JSON, owned types, inheritance

> Prerequisite: [skill.md](skill.md). Configuration goes in `OnModelCreating(ModelBuilder modelBuilder)` or in `IEntityTypeConfiguration<T>` classes registered with `modelBuilder.ApplyConfigurationsFromAssembly(...)`.

## Value converters

A `ValueConverter<TModel, TStore>` translates between the entity property type and the column type. EF needs a `ValueComparer<T>` whenever the converted type is not naturally comparable by reference equality (collections, mutable structs, custom records with mutable members).

### Enum as string

```csharp
modelBuilder.Entity<Invoice>()
    .Property(x => x.Status)
    .HasConversion<string>()
    .HasMaxLength(32);
```

EF infers the converter from `<string>`. Stored as readable text instead of an int — survives enum-value reordering.

### `DateOnly` / `TimeOnly`

Native on SQL Server (EF 8+) and PostgreSQL. SQLite needs an explicit `ValueConverter<DateOnly, string>`.

### Custom encrypted scalar

```csharp
var converter = new ValueConverter<string, string>(
    plain  => Encryptor.Encrypt(plain),
    cipher => Encryptor.Decrypt(cipher));

var comparer = new ValueComparer<string>(
    (a, b) => a == b,
    v => v == null ? 0 : v.GetHashCode(),
    v => v);

modelBuilder.Entity<User>()
    .Property(x => x.SocialSecurityNumber)
    .HasConversion(converter)
    .Metadata.SetValueComparer(comparer);
```

### Collection of value objects with custom storage

```csharp
var listConverter = new ValueConverter<List<string>, string>(
    list => string.Join(',', list),
    csv  => csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

var listComparer = new ValueComparer<List<string>>(
    (a, b) => a!.SequenceEqual(b!),
    v => v.Aggregate(0, (acc, item) => HashCode.Combine(acc, item.GetHashCode())),
    v => v.ToList());

modelBuilder.Entity<Tag>()
    .Property(x => x.Aliases)
    .HasConversion(listConverter)
    .Metadata.SetValueComparer(listComparer);
```

**The comparer is mandatory** when the property is mutable (lists, dictionaries, mutable structs). Without it `ChangeTracker` compares by reference and silently misses mutations — `SaveChangesAsync` returns 0 with no error.

## JSON columns (EF 8+, native)

JSON columns store an owned subtree as `nvarchar(max)` containing JSON. Queryable with `EF.Functions` JSON helpers. Preferable to a custom converter when the shape is owned by the parent.

### Single owned-one as JSON

```csharp
modelBuilder.Entity<Order>()
    .OwnsOne(x => x.ShippingAddress, nav =>
    {
        nav.ToJson();
        nav.Property(a => a.Street).HasMaxLength(200);
    });
```

### Owned collection as JSON

```csharp
modelBuilder.Entity<Order>()
    .OwnsMany(x => x.Tags, nav => nav.ToJson());
```

JSON-column rules:

- Querying inside the JSON column uses `EF.Functions.JsonContains(...)` or member access (`x.ShippingAddress.City == "Madrid"`) — translated to `JSON_VALUE` in SQL Server.
- The owned type cannot have a primary key separate from its parent.
- Index on a JSON path requires a computed column + index — not an EF concept; do it in a migration with raw SQL.

## Owned types (table or column)

Owned-one **without** `ToJson()` lands as columns on the parent table (prefix `ShippingAddress_Street`, etc.). Owned-many without `ToJson()` lands as a separate table with a shadow FK.

| Variant | Storage | Use when |
|---|---|---|
| `OwnsOne` (no JSON) | Inline columns on parent | Always-loaded, frequently-queried by individual fields. |
| `OwnsOne` + `ToJson()` | Single JSON column | Subtree is opaque to most queries; reduces column count. |
| `OwnsMany` (no JSON) | Side table with shadow FK | Small-cardinality collection, queried separately. |
| `OwnsMany` + `ToJson()` | JSON array in parent column | Subtree is fetched whole; cardinality bounded. |

Owned types are **not** entities — they have no `DbSet`, are not queryable independently, and share the parent's lifecycle.

## Inheritance — TPH / TPT / TPC

### TPH (Table-Per-Hierarchy) — DEFAULT

Single table, `Discriminator` column. Fast (single table, no joins), wastes nullable columns for type-specific properties.

```csharp
modelBuilder.Entity<Notification>()
    .HasDiscriminator<string>("kind")
    .HasValue<EmailNotification>("email")
    .HasValue<SmsNotification>("sms")
    .HasValue<PushNotification>("push");
```

### TPT (Table-Per-Type)

One table per class in the hierarchy, joined on PK. Use only when subclasses differ heavily and most queries hit the base.

```csharp
modelBuilder.Entity<Notification>().UseTptMappingStrategy();
```

### TPC (Table-Per-Concrete) — EF 7+

One table per concrete subclass, no base table. Best perf when queries never span the base. Requires explicit key generation strategy (HiLo or sequence) to avoid PK collisions across subclass tables.

```csharp
modelBuilder.Entity<Animal>().UseTpcMappingStrategy();
modelBuilder.HasSequence<long>("AnimalIds").StartsAt(1).IncrementsBy(1);
modelBuilder.Entity<Animal>().Property(x => x.Id).HasDefaultValueSql("NEXT VALUE FOR AnimalIds");
```

### Decision table

| Situation | Strategy |
|---|---|
| Subclasses share most columns; queries frequent over the base | **TPH** |
| Subclasses differ heavily; queries occasionally over the base | **TPT** |
| Subclasses differ heavily; queries NEVER over the base | **TPC** |
| Discriminator column unacceptable to DBA / data warehouse | TPT or TPC |
| Need polymorphic FK from another table | TPH only (single table) |

## Shadow properties

Properties not on the CLR type, only in the model. Used for FKs and audit columns when the entity should not expose them.

```csharp
modelBuilder.Entity<Invoice>()
    .Property<DateTimeOffset>("LastImportedAt");

// Read/write at runtime via EF.Property<T>:
var ts = db.Entry(invoice).Property<DateTimeOffset>("LastImportedAt").CurrentValue;
```

Avoid for domain-meaningful state — shadow properties are invisible to consumers and tests.

## Configuration organization

Two patterns:

```csharp
// A) inline in OnModelCreating — fine for <10 entities
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Invoice>().HasKey(x => x.Id);
    // ...
}

// B) one IEntityTypeConfiguration<T> per entity — preferred for >10
public sealed class InvoiceConfig : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        e.HasIndex(x => x.Number).IsUnique();
    }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDb).Assembly);
}
```

Pattern B keeps `OnModelCreating` from becoming a 500-line hairball and lets each entity's mapping live next to its tests.

## Naming conventions

Two libraries are used in the team:

- `EFCore.NamingConventions` — `opts.UseSnakeCaseNamingConvention()` — for snake_case tables/columns to match warehouse / BI conventions.
- Default convention (PascalCase) — for greenfield apps that won't be cross-loaded into a warehouse.

Pick **one** and apply consistently per database; mixing breaks migrations.

## Concurrency tokens

`[Timestamp]` / `IsRowVersion()` adds a `rowversion`-mapped column that EF uses for optimistic concurrency.

```csharp
modelBuilder.Entity<Invoice>().Property(x => x.RowVersion).IsRowVersion();
```

`SaveChangesAsync` throws `DbUpdateConcurrencyException` if the row was updated between read and save. Catch and reconcile per business rule.
