# Content & Forms

Reboot, typography, images, tables; every form control; form layout; validation.

## Reboot (what the reset gives you)

- Native font stack, root `font-size` left to the browser (16px typical), `rem` units, `line-height: 1.5`, `background-color` on `<body>` via `--bs-body-bg`.
- Margin resets: elements only carry `margin-bottom` (no `margin-top`) so vertical rhythm composes downward.
- `box-sizing: border-box` global; links underlined by default (`$link-decoration`); `<hr>` restyled via `border-top` + opacity; hidden attribute enforced.
- Forms: fieldsets/legends reset; buttons/inputs inherit font; `[role="button"]` gets `cursor: pointer`.

## Typography

| Need | Markup |
|---|---|
| Headings | `<h1>`…`<h6>`; classes `.h1`–`.h6` for heading style on any tag |
| Hero headings | `.display-1` … `.display-6` (larger, lighter) |
| Lead paragraph | `.lead` |
| Secondary heading text | `<small class="text-body-secondary">` inside the heading |
| Inline | `<mark>`/`.mark`, `<small>`/`.small`, `<s>`, `<u>`, `<strong>`, `<em>` |
| Abbreviation | `<abbr title="...">`, `.initialism` for slightly smaller all-caps |
| Blockquote | `.blockquote` + `<figcaption class="blockquote-footer">` inside a `<figure>` |
| Lists | `.list-unstyled` (no bullets/padding, immediate children only) · `.list-inline` + `.list-inline-item` |
| Description lists | grid classes on `<dl>` rows: `<dl class="row"><dt class="col-sm-3">…<dd class="col-sm-9">…` |
| Responsive font size | RFS engine scales `font-size` Sass-side; `.fs-{1..6}` utilities are static |

## Images

- `.img-fluid` = `max-width: 100%; height: auto` (responsive default for any content image).
- `.img-thumbnail` = 1px rounded border box. Round with `.rounded`, `.rounded-circle`.
- Figures: `<figure class="figure"><img class="figure-img img-fluid rounded"><figcaption class="figure-caption">`.
- Alignment: `.float-start/.float-end`, or center block images with `.d-block .mx-auto`, or wrap in `.text-center`.

## Tables

Opt-in styling — plain `<table>` is untouched; add `.table`:

```html
<table class="table table-striped table-hover align-middle">
  <thead><tr><th scope="col">#</th><th scope="col">Name</th></tr></thead>
  <tbody class="table-group-divider">
    <tr><th scope="row">1</th><td>Mark</td></tr>
  </tbody>
</table>
```

- Variants: `.table-{primary|secondary|success|danger|warning|info|light|dark}` on table, `<tr>`, or cell. (Variant colors are not color-mode adaptive until v6.)
- Modifiers: `.table-striped` · `.table-striped-columns` · `.table-hover` · `.table-active` (row/cell) · `.table-bordered` (+ `border-primary` etc.) · `.table-borderless` · `.table-sm`.
- `.table-group-divider` = heavier separator between `thead`/`tbody`/`tfoot` groups. `.caption-top` moves the caption above.
- Vertical alignment: `.align-middle` on table/row/cell.
- Responsive scroll: wrap in `.table-responsive` (always) or `.table-responsive-{sm|md|lg|xl|xxl}` (scrolls **below** that breakpoint). Clips overflowing content like dropdowns — keep menus out of responsive tables.
- Nested tables don't inherit parent styles; style them explicitly.

## Form controls

Every control pairs a class with an accessible label. Standard field block:

```html
<div class="mb-3">
  <label for="email" class="form-label">Email address</label>
  <input type="email" class="form-control" id="email" aria-describedby="emailHelp">
  <div id="emailHelp" class="form-text">We'll never share your email.</div>
</div>
```

| Control | Class / markup |
|---|---|
| Text/textarea/date/file/etc. | `.form-control` (use the correct `type=`); file inputs are plain `.form-control` |
| Readonly as plain text | `.form-control-plaintext` + `readonly` |
| Color picker | `.form-control .form-control-color` |
| Sizing | `.form-control-lg` / `.form-control-sm` (labels: `.col-form-label-{lg,sm}` in horizontal layouts) |
| Select | `<select class="form-select">`; sizes `.form-select-lg/sm`; `multiple`/`size` supported |
| Checkbox / radio | `.form-check` wrapper > `.form-check-input` + `.form-check-label`; `id`/`for` pairing mandatory |
| Switch | `.form-check.form-switch` + `role="switch"` on the input |
| Inline / reverse | `.form-check-inline` · `.form-check-reverse` |
| Indeterminate checkbox | JS only: `input.indeterminate = true` |
| Toggle buttons | `.btn-check` input + `.btn` label (checkbox/radio styled as buttons) |
| Range | `.form-range` (+ `min`/`max`/`step`) |
| Disabled | `disabled` attribute; whole groups via `<fieldset disabled>` |

### Input group

```html
<div class="input-group mb-3">
  <span class="input-group-text">@</span>
  <input type="text" class="form-control" placeholder="Username" aria-label="Username">
</div>
```

Prepends/appends text, buttons, dropdowns, selects around controls. Sizes: `.input-group-lg/sm` on the wrapper. Multiple inputs/addons chain flatly inside the group. Validation feedback inside a group needs `.has-validation` on the group so rounding stays correct.

### Floating labels

```html
<div class="form-floating mb-3">
  <input type="email" class="form-control" id="fEmail" placeholder="name@example.com">
  <label for="fEmail">Email address</label>
</div>
```

Order is fixed: input **first**, label after; a `placeholder` attribute is required (it's visually unused but drives `:placeholder-shown`). Works with `.form-control`, `.form-select`, textareas.

## Form layout

- Vertical rhythm: wrap each field in `.mb-3` (or use `.row .g-3` + `.col-*` wrappers for grids of fields).
- **Grid forms**: standard grid inside the form — `.row` + `.col-md-6` etc.

```html
<form class="row g-3">
  <div class="col-md-6"><label class="form-label" for="a">First</label><input id="a" class="form-control"></div>
  <div class="col-md-6"><label class="form-label" for="b">Last</label><input id="b" class="form-control"></div>
  <div class="col-12"><button class="btn btn-primary">Submit</button></div>
</form>
```

- **Horizontal form**: `.row` per field, label gets `.col-sm-2 .col-form-label`, control wrapped in `.col-sm-10`.
- **Inline/auto layout**: `row-cols-auto`/`col-auto` + `align-items-center`; use `.visually-hidden` labels when hiding them visually.

## Validation

Client-side custom-styles pattern (the canonical one):

```html
<form class="needs-validation" novalidate>
  <div class="mb-3">
    <label for="name" class="form-label">Name</label>
    <input type="text" class="form-control" id="name" required>
    <div class="valid-feedback">Looks good!</div>
    <div class="invalid-feedback">Please provide a name.</div>
  </div>
  <button class="btn btn-primary" type="submit">Submit</button>
</form>

<script>
  document.querySelectorAll('.needs-validation').forEach(form => {
    form.addEventListener('submit', event => {
      if (!form.checkValidity()) { event.preventDefault(); event.stopPropagation() }
      form.classList.add('was-validated')
    }, false)
  })
</script>
```

Mechanics:

- Styles ride HTML5 `:valid`/`:invalid` pseudo-classes on `input`/`select`/`textarea`, **scoped under `.was-validated`** — nothing shows until that class is added (typically on first submit attempt). Remove `.was-validated` to reset (e.g. after an AJAX submit).
- `novalidate` suppresses the browser's native bubbles while keeping the constraint-validation API (`checkValidity()`, `setCustomValidity()`).
- **Server-side**: skip `.was-validated`; render `.is-invalid` / `.is-valid` directly on each control (this is what ASP.NET Core tag helpers should emit). `.invalid-feedback` shows when its sibling control is invalid.
- Feedback `<div>` must come **after** the control it describes (CSS sibling selector). Select background icons only appear on `.form-select`.
- Tooltip-style feedback: `.invalid-tooltip` / `.valid-tooltip` instead of `-feedback`, with `position-relative` on the parent.
- Supported on `.form-control`, `.form-select`, `.form-check-input`; input groups need `.has-validation`.
- Known gap: custom client-side feedback is not exposed to assistive tech yet — for strict a11y use server-side rendering or browser-default validation.
