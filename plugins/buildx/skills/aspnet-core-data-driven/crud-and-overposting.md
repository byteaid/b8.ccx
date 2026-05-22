# CRUD Patterns and Overposting Mitigation

Razor Pages CRUD, MVC controller CRUD, anti-overposting (`TryUpdateModelAsync` allow-list / `[Bind]` / view-model + `PropertyValues.SetValues`).

## CRUD patterns (Razor Pages)

### Read — `Details` page

```csharp
public async Task<IActionResult> OnGetAsync(int? id)
{
    if (id is null) return NotFound();

    Student = await _context.Students
        .Include(s => s.Enrollments)
        .ThenInclude(e => e.Course)
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.ID == id);

    return Student is null ? NotFound() : Page();
}
```

### Create — anti-overposting

**Option A** — `TryUpdateModelAsync` allow-list:

```csharp
public async Task<IActionResult> OnPostAsync()
{
    var emptyStudent = new Student();
    if (await TryUpdateModelAsync<Student>(
            emptyStudent, "student",
            s => s.FirstMidName, s => s.LastName, s => s.EnrollmentDate))
    {
        _context.Students.Add(emptyStudent);
        await _context.SaveChangesAsync();
        return RedirectToPage("./Index");
    }
    return Page();
}
```

`TryUpdateModelAsync` ignores any form field outside the explicit allow-list (e.g. a hacker-supplied `Secret` column).

**Option B** — view-model + `PropertyValues.SetValues` (preferred for non-trivial apps):

```csharp
public class StudentVM
{
    public int ID { get; set; }
    public string LastName { get; set; } = "";
    public string FirstMidName { get; set; } = "";
    public DateTime EnrollmentDate { get; set; }
}

[BindProperty] public StudentVM StudentVM { get; set; } = new();

public async Task<IActionResult> OnPostAsync()
{
    if (!ModelState.IsValid) return Page();
    var entry = _context.Add(new Student());
    entry.CurrentValues.SetValues(StudentVM);   // property-name match
    await _context.SaveChangesAsync();
    return RedirectToPage("./Index");
}
```

The view-model contains only UI fields; the domain entity may have audit columns / secrets the form cannot touch.

### Edit — fetch-then-update

Each request gets a new `DbContext`, so the entity starts detached. Fetch, mutate via allow-list, save:

```csharp
public async Task<IActionResult> OnPostAsync(int id)
{
    var studentToUpdate = await _context.Students.FindAsync(id);
    if (studentToUpdate is null) return NotFound();

    if (await TryUpdateModelAsync<Student>(
            studentToUpdate, "student",
            s => s.FirstMidName, s => s.LastName, s => s.EnrollmentDate))
    {
        await _context.SaveChangesAsync();
        return RedirectToPage("./Index");
    }
    return Page();
}
```

EF computes column-level `UPDATE` for changed properties only.

### Delete — with DB-error UI feedback

```csharp
public async Task<IActionResult> OnGetAsync(int? id, bool? saveChangesError = false)
{
    if (id is null) return NotFound();
    Student = await _context.Students.AsNoTracking().FirstOrDefaultAsync(m => m.ID == id);
    if (Student is null) return NotFound();
    if (saveChangesError.GetValueOrDefault())
        ErrorMessage = $"Delete {id} failed. Try again.";
    return Page();
}

public async Task<IActionResult> OnPostAsync(int? id)
{
    if (id is null) return NotFound();
    var student = await _context.Students.FindAsync(id);
    if (student is null) return NotFound();

    try
    {
        _context.Students.Remove(student);
        await _context.SaveChangesAsync();
        return RedirectToPage("./Index");
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Delete failed for student {Id}", id);
        return RedirectToAction("./Delete", new { id, saveChangesError = true });
    }
}
```

## CRUD patterns (MVC controllers)

```csharp
public class StudentsController(SchoolContext context) : Controller
{
    public async Task<IActionResult> Index()
        => View(await context.Students.AsNoTracking().ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("EnrollmentDate,FirstMidName,LastName")] Student student)
    {
        if (!ModelState.IsValid) return View(student);
        try
        {
            context.Add(student);
            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Unable to save changes.");
            return View(student);
        }
    }

    [HttpPost, ActionName("Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(int? id)
    {
        if (id is null) return NotFound();
        var studentToUpdate = await context.Students.FirstOrDefaultAsync(s => s.ID == id);
        if (studentToUpdate is null) return NotFound();

        if (await TryUpdateModelAsync<Student>(studentToUpdate, "",
                s => s.FirstMidName, s => s.LastName, s => s.EnrollmentDate))
        {
            try { await context.SaveChangesAsync(); return RedirectToAction(nameof(Index)); }
            catch (DbUpdateException) { ModelState.AddModelError("", "Unable to save changes."); }
        }
        return View(studentToUpdate);
    }
}
```

MVC-specific items:
- `[ValidateAntiForgeryToken]` on POSTs (auto-applied by `[AutoValidateAntiforgeryToken]` filter on Razor Pages).
- `[Bind("Prop1,Prop2,...")]` is the MVC counterpart to `TryUpdateModelAsync` allow-listing.
- `[HttpPost, ActionName("Edit")]` on `EditPost` to disambiguate from `[HttpGet] Edit`.

For deeper MVC-specific topics (filters, conventions, areas) load `aspnet-core-mvc`. For Razor Pages specifics (handlers, route binding, partial pages, page filters) load `aspnet-core-razor-pages`.
