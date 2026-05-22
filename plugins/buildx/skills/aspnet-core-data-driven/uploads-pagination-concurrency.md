# Uploads, Sort/Filter/Page, Concurrency UI, Dapper Companion

`IFormFile` uploads bound to EF, sort + filter + `PaginatedList<T>`, optimistic concurrency, Dapper sharing the EF connection / transaction.

## File uploads with `IFormFile`

EF stores either bytes (small file) or a path/URL (large file). Bytes example:

```csharp
public class FileUploadStudent
{
    public int ID { get; set; }
    public string LastName { get; set; } = "";
    public byte[]? Photo { get; set; }     // mapped to varbinary(max) / BLOB
}
```

```csharp
[BindProperty] public Student Student { get; set; } = new();
[BindProperty] public IFormFile? PhotoUpload { get; set; }

public async Task<IActionResult> OnPostAsync()
{
    if (!ModelState.IsValid) return Page();

    if (PhotoUpload is { Length: > 0 })
    {
        using var ms = new MemoryStream();
        await PhotoUpload.CopyToAsync(ms);
        Student.Photo = ms.ToArray();
    }

    _context.Students.Add(Student);
    await _context.SaveChangesAsync();
    return RedirectToPage("./Index");
}
```

The form must use `enctype="multipart/form-data"`:

```cshtml
<form method="post" enctype="multipart/form-data">
    <input asp-for="Student.LastName" />
    <input asp-for="PhotoUpload" type="file" accept="image/*" />
    <button type="submit">Save</button>
</form>
```

Limits: configure `FormOptions.MultipartBodyLengthLimit` and `RequestSizeLimitAttribute` for large uploads. Stream large files to disk/blob with `MultipartReader` instead of `IFormFile` to avoid full buffering.

## Sort, filter, page

### Sort

```csharp
public string NameSort { get; set; } = "";
public string DateSort { get; set; } = "";

public async Task OnGetAsync(string sortOrder)
{
    NameSort = string.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
    DateSort = sortOrder == "Date" ? "date_desc" : "Date";

    IQueryable<Student> q = _context.Students;

    q = sortOrder switch
    {
        "name_desc" => q.OrderByDescending(s => s.LastName),
        "Date"      => q.OrderBy(s => s.EnrollmentDate),
        "date_desc" => q.OrderByDescending(s => s.EnrollmentDate),
        _           => q.OrderBy(s => s.LastName),
    };

    Students = await q.AsNoTracking().ToListAsync();
}
```

### Filter

```csharp
if (!string.IsNullOrEmpty(searchString))
{
    q = q.Where(s =>
        s.LastName.Contains(searchString) ||
        s.FirstMidName.Contains(searchString));
}
```

`Contains` translates to `LIKE %s%` server-side. Case sensitivity follows DB collation: SQL Server insensitive default; **SQLite sensitive default** — explicit `ToUpper` makes it insensitive at the cost of breaking index usage.

Form posts via GET to keep filter bookmarkable:

```cshtml
<form asp-page="./Index" method="get">
    <input type="text" name="SearchString" value="@Model.CurrentFilter" />
    <input type="submit" value="Search" />
</form>
```

### Pagination — `PaginatedList<T>`

```csharp
public class PaginatedList<T> : List<T>
{
    public int PageIndex  { get; }
    public int TotalPages { get; }

    public PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
    {
        PageIndex  = pageIndex;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        AddRange(items);
    }

    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage     => PageIndex < TotalPages;

    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source, int pageIndex, int pageSize)
    {
        var count = await source.CountAsync();
        var items = await source.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PaginatedList<T>(items, count, pageIndex, pageSize);
    }
}
```

Wired into `OnGetAsync`: reset `pageIndex = 1` when `searchString` is supplied; otherwise `searchString = currentFilter`. Page size from configuration: `Configuration.GetValue("PageSize", 4)`. Pagination links forward `currentFilter` and `sortOrder` so state survives navigation.

### Grouping (LINQ -> `GROUP BY`)

```csharp
IQueryable<EnrollmentDateGroup> data =
    from s in _context.Students
    group s by s.EnrollmentDate into g
    select new EnrollmentDateGroup
    {
        EnrollmentDate = g.Key,
        StudentCount   = g.Count()
    };
Students = await data.AsNoTracking().ToListAsync();
```

## Concurrency UI flow

For token strategies, fluent API, and rowversion semantics, load `dotnet-ef-core`. The web-side flow:

```csharp
public async Task<IActionResult> OnPostAsync(int id)
{
    var departmentToUpdate = await _context.Departments
        .Include(d => d.Administrator)
        .FirstOrDefaultAsync(m => m.DepartmentID == id);

    if (departmentToUpdate is null)
    {
        var deletedDepartment = new Department();
        await TryUpdateModelAsync(deletedDepartment, "Department");
        ModelState.AddModelError(string.Empty,
            "Unable to save. The department was deleted by another user.");
        return Page();
    }

    // Treat the value posted in the hidden field as the *original*.
    _context.Entry(departmentToUpdate)
            .Property(d => d.ConcurrencyToken)
            .OriginalValue = Department.ConcurrencyToken;

    if (await TryUpdateModelAsync<Department>(
            departmentToUpdate, "Department",
            s => s.Name, s => s.StartDate, s => s.Budget, s => s.InstructorID))
    {
        try
        {
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry         = ex.Entries.Single();
            var clientValues  = (Department)entry.Entity;
            var databaseEntry = entry.GetDatabaseValues();

            if (databaseEntry is null)
            {
                ModelState.AddModelError(string.Empty,
                    "Unable to save. The department was deleted by another user.");
                return Page();
            }

            var dbValues = (Department)databaseEntry.ToObject();
            await SetDbErrorMessage(dbValues, clientValues);

            // Refresh token so the next POST starts from the latest value.
            Department.ConcurrencyToken = dbValues.ConcurrencyToken;
            ModelState.Remove($"{nameof(Department)}.{nameof(Department.ConcurrencyToken)}");
        }
    }
    return Page();
}
```

Hidden field in the Edit view:

```cshtml
<form method="post">
    <input type="hidden" asp-for="Department.DepartmentID" />
    <input type="hidden" asp-for="Department.ConcurrencyToken" />
    <!-- editable fields -->
</form>
```

Three resolution strategies (the UI usually picks one):

1. **Client wins** — overwrite. Set `entry.OriginalValues.SetValues(databaseValues)` then `SaveChangesAsync` again.
2. **Store wins** — discard user's edits. Reload entity values from DB.
3. **Property merge** — present diffs to the user. Most common in web apps.

## Dapper as companion

Use Dapper when you want hand-tuned SQL with no change tracking. EF Core and Dapper coexist in the same app — share the same connection / transaction.

```csharp
public class StudentReadModel(IConfiguration cfg)
{
    private readonly string _conn = cfg.GetConnectionString("SchoolContext")!;

    public async Task<IReadOnlyList<StudentRow>> RecentAsync(int top, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (@top) ID, LastName, FirstName, EnrollmentDate
            FROM   Student
            ORDER  BY EnrollmentDate DESC
        """;
        await using var c = new SqlConnection(_conn);
        var rows = await c.QueryAsync<StudentRow>(
            new CommandDefinition(sql, new { top }, cancellationToken: ct));
        return rows.AsList();
    }
}
public record StudentRow(int ID, string LastName, string FirstName, DateTime EnrollmentDate);
```

Sharing a transaction with EF:

```csharp
await using var tx = await _context.Database.BeginTransactionAsync();
var conn = _context.Database.GetDbConnection();
await conn.ExecuteAsync("UPDATE Student SET LastName = @n WHERE ID = @id",
                        new { n = "Smith", id = 1 }, transaction: tx.GetDbTransaction());
await _context.SaveChangesAsync();
await tx.CommitAsync();
```

When to choose Dapper:
- Reporting / dashboards (large flat queries with custom shape).
- Bulk inserts via `SqlBulkCopy` / `COPY` (Postgres).
- Stored-proc-heavy domains.
- Critical hot paths where per-query 1-2x overhead from EF translation matters.
