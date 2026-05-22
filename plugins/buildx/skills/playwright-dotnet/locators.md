# Locators

Choosing a locator, official priority, chaining, `data-testid` config.

## Official priority order

Use the highest entry that works. Drop down only when the previous level is non-viable.

| # | Locator | When |
|---|---|---|
| 1 | `GetByRole` | **Default.** Anything with an ARIA role (button, link, heading, textbox, checkbox, radio, combobox, alert, dialog, navigation, row, cell). What a screen reader sees. |
| 2 | `GetByLabel` | Inputs with associated `<label>`. More precise than role for forms with multiple textboxes. |
| 3 | `GetByPlaceholder` | Inputs with placeholder and no label. |
| 4 | `GetByText` | Visible text on headings, paragraphs, unassociated labels. |
| 5 | `GetByAltText` | Images with `alt`. |
| 6 | `GetByTitle` | Elements with `title`. |
| 7 | `GetByTestId` | Last resort when nothing semantic works. Requires `data-testid`. |
| 8 | `Locator("css")` | Escape hatch. Fragile — depends on DOM structure. |
| 9 | `Locator("xpath")` | Almost never. Verbose and fragile. |

## `GetByRole`

```csharp
await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
await Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
await Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Level = 1 })).ToBeVisibleAsync();
await Page.GetByRole(AriaRole.Textbox,  new() { Name = "Email" }).FillAsync("a@b.com");
await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Accept terms" }).CheckAsync();
await Page.GetByRole(AriaRole.Radio,    new() { Name = "Express shipping" }).CheckAsync();
await Page.GetByRole(AriaRole.Combobox, new() { Name = "Country" }).SelectOptionAsync("MX");

await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Error");
await Expect(Page.GetByRole(AriaRole.Navigation)).ToBeVisibleAsync();
await Expect(Page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();

var rows = Page.GetByRole(AriaRole.Row);
await Expect(rows).ToHaveCountAsync(5);
```

### Common options

| Option | Effect |
|---|---|
| `Name = "..."` | Accessible name (label, aria-label, aria-labelledby, text) |
| `Exact = true` | Exact name match (default: substring) |
| `Checked = true` | Only checked checkboxes/radios |
| `Disabled = true` | Only disabled |
| `Expanded = true` | Only expanded (accordions, dropdowns) |
| `IncludeHidden = true` | Include hidden |
| `Level = 2` | For headings — `<h2>` |
| `Pressed = true` | Toggle buttons |
| `Selected = true` | Options in a select |

## `GetByLabel`

For inputs with associated `<label>` (via `for`/`id` or wrapping):

```csharp
await Page.GetByLabel("Email").FillAsync("user@example.com");
await Page.GetByLabel("Password").FillAsync("secret");
await Page.GetByLabel("Remember me").CheckAsync();
await Page.GetByLabel("Date of birth", new() { Exact = true }).FillAsync("1990-01-15");
```

## `GetByPlaceholder`

```csharp
await Page.GetByPlaceholder("Search...").FillAsync("playwright");
```

## `GetByText`

```csharp
await Expect(Page.GetByText("Welcome back")).ToBeVisibleAsync();
await Page.GetByText("View all orders").ClickAsync();
await Expect(Page.GetByText(new Regex("Total: \\$\\d+"))).ToBeVisibleAsync();
await Page.GetByText("Submit", new() { Exact = true }).ClickAsync();
```

Gotcha: matches ANY element containing the text — combine with `Filter` or use `GetByRole` if multiple matches.

## `GetByAltText` / `GetByTitle`

```csharp
await Page.GetByAltText("Company logo").ClickAsync();
await Page.GetByTitle("Close dialog").ClickAsync();
```

## `GetByTestId`

```html
<div data-testid="order-summary">...</div>
```

```csharp
await Expect(Page.GetByTestId("order-summary")).ToContainTextAsync("$42.00");
```

Configure the attribute name (e.g., `data-test`):

```csharp
Playwright.Selectors.SetTestIdAttribute("data-test");
```

Or in `.runsettings`:

```xml
<Playwright>
  <TestIdAttribute>data-test</TestIdAttribute>
</Playwright>
```

## `Locator(css)` — escape hatch

```csharp
Page.Locator("div.card >> text=Orders");
Page.Locator("table tbody tr").Nth(2);
Page.Locator("input[name='email']");
```

### Playwright CSS extensions

| Extension | Effect |
|---|---|
| `:visible` | Visible only |
| `:text("...")` | Contains text |
| `:text-is("...")` | Exact text |
| `:text-matches("regex")` | Regex |
| `:has(selector)` | Has descendant matching |
| `:has-text("...")` | Has descendant with text |
| `nth=0` / `nth=-1` | By index (negative from end) |
| `:left-of(sel)` | Layout — to the left of |
| `:right-of(sel)` | Layout — to the right |
| `:above(sel)` | Layout — above |
| `:below(sel)` | Layout — below |
| `:near(sel)` | Layout — near (50px default) |

## Chaining and filtering

```csharp
// Chain inside a container
var card = Page.GetByTestId("order-card");
await card.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

// Filter by text
var rows = Page.GetByRole(AriaRole.Row);
var pendingRow = rows.Filter(new() { HasText = "Pending" });
await pendingRow.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

// Filter by descendant
var card2 = Page.Locator(".card").Filter(new() { Has = Page.GetByText("$42.00") });
await card2.GetByRole(AriaRole.Link, new() { Name = "Details" }).ClickAsync();

// Index
await Page.GetByRole(AriaRole.ListItem).Nth(2).ClickAsync();
await Page.GetByRole(AriaRole.Button, new() { Name = "Add" }).First.ClickAsync();
await Page.GetByRole(AriaRole.Row).Last.GetByRole(AriaRole.Cell).First.InnerTextAsync();
```

## `FrameLocator` — iframes

```csharp
var frame = Page.FrameLocator("iframe#payment");
await frame.GetByRole(AriaRole.Textbox, new() { Name = "Card number" }).FillAsync("4242...");

// Nested
var inner = Page.FrameLocator("iframe.outer").FrameLocator("iframe.inner");
```

## Shadow DOM

Playwright **pierces `open` shadow DOM automatically**. `GetByRole`, `GetByText`, etc. search inside the shadow tree. `closed` shadow DOM is inaccessible (browser limitation).

## `data-testid` in Blazor

```razor
<div data-testid="order-summary">
    <span data-testid="order-total">@order.Total.ToString("C")</span>
</div>

<button data-testid="place-order" @onclick="PlaceOrder">Place Order</button>
```

Convention: kebab-case values (`order-summary`, not `OrderSummary`). Prefer `GetByRole` when a semantic role exists.

## Common errors

| Error | Cause | Fix |
|---|---|---|
| `strict mode violation: locator resolved to N elements` | Locator too broad | `Filter`, scope to parent, `First`/`Nth(n)` |
| `GetByRole` doesn't find a button | `<div onclick>` has no role | Use `<button>`, `role="button"`, or `GetByText` |
| `GetByLabel` doesn't find input | `<label>` not associated | Add `for`/`id`, or wrap input |
| `GetByText` matches multiple | Substring match too generous | `Exact = true`, or drop down to `GetByRole` + `Name` |
| Fragile CSS `div > ul > li:nth-child(3)` | Position-dependent | Use `GetByRole(AriaRole.ListItem).Filter(...)` |

## Sources

- https://playwright.dev/dotnet/docs/locators
- https://playwright.dev/dotnet/docs/other-locators
- https://playwright.dev/dotnet/docs/best-practices#use-locators
