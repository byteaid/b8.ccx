# Components — CSS-only

Buttons, button group, badge, breadcrumb, card, list group, pagination, placeholders, progress, spinners, close button. No JS required (button *toggle state* is the one optional JS feature, noted below).

## Buttons

```html
<button type="button" class="btn btn-primary">Primary</button>
<button type="button" class="btn btn-outline-secondary btn-sm">Small outline</button>
<a class="btn btn-link" role="button" href="#">Link styled</a>
```

- Variants: `.btn-{primary|secondary|success|danger|warning|info|light|dark|link}` and `.btn-outline-{key}` (outlines lack a visible default border on some backgrounds — check contrast).
- Sizes: `.btn-lg`, `.btn-sm`. Block-level layouts: wrap in `.d-grid .gap-2` (there is **no** `.btn-block` in v5).
- Disable: `disabled` attribute on `<button>`; on `<a>` use `.disabled` + `aria-disabled="true"` + `tabindex="-1"`.
- Toggle state (needs JS plugin): `data-bs-toggle="button"` (+ `.active` and `aria-pressed="true"` for pre-toggled). Checkbox/radio-as-buttons: `.btn-check` input + `.btn` label ([content-forms](content-forms.md)).
- Per-instance theming via CSS vars: `--bs-btn-bg`, `--bs-btn-color`, `--bs-btn-hover-bg`, `--bs-btn-active-bg`, `--bs-btn-border-color`, … set on a custom class.

## Button group / toolbar

```html
<div class="btn-group" role="group" aria-label="Actions">
  <button type="button" class="btn btn-outline-primary">Left</button>
  <button type="button" class="btn btn-outline-primary">Right</button>
</div>
```

- Sizes on the group: `.btn-group-lg/sm`. Vertical: `.btn-group-vertical`.
- Toolbar: `.btn-toolbar role="toolbar"` wrapping several groups (space with `.me-2`).
- Nesting a `.btn-group` inside another hosts dropdowns in groups.

## Badge

```html
<h4>Inbox <span class="badge text-bg-secondary">4</span></h4>
<button type="button" class="btn btn-primary position-relative">
  Alerts
  <span class="position-absolute top-0 start-100 translate-middle badge rounded-pill text-bg-danger">
    99+ <span class="visually-hidden">unread messages</span>
  </span>
</button>
```

- Always pair `.badge` with a `text-bg-{key}` (or explicit bg+text). `.rounded-pill` for pills. Badges scale with the parent's font size.

## Breadcrumb

```html
<nav aria-label="breadcrumb">
  <ol class="breadcrumb">
    <li class="breadcrumb-item"><a href="#">Home</a></li>
    <li class="breadcrumb-item active" aria-current="page">Library</li>
  </ol>
</nav>
```

- Divider is CSS: change with `--bs-breadcrumb-divider: '>'` on the `.breadcrumb` (or `$breadcrumb-divider` in Sass; embedded SVG via `url()`, none via `''`).

## Card

```html
<div class="card" style="width: 18rem;">
  <img src="…" class="card-img-top" alt="…">
  <div class="card-body">
    <h5 class="card-title">Title</h5>
    <h6 class="card-subtitle mb-2 text-body-secondary">Subtitle</h6>
    <p class="card-text">Content.</p>
    <a href="#" class="card-link">Link</a>
  </div>
</div>
```

- Structure parts: `.card-header`, `.card-footer`, `.card-body`, `.card-img-top/bottom`, `.card-img-overlay` (text over image), `.list-group-flush` inside cards.
- Cards have no width/margin — size with grid columns or width utilities; `h-100` equalizes heights inside row-cols grids.
- Nav in header: `.card-header-tabs` / `.card-header-pills` on a `.nav`.
- Color: `text-bg-{key}` on the card, or `border-{key}` + `.text-{key}` on parts.
- Layouts: `.card-group` (attached, equal height) or grid + `row-cols` (preferred; the v4 `card-deck` is gone).
- Whole-card click: `.stretched-link` on the title link.

## List group

```html
<ul class="list-group">
  <li class="list-group-item active" aria-current="true">Active</li>
  <li class="list-group-item">Second</li>
  <li class="list-group-item disabled" aria-disabled="true">Disabled</li>
</ul>
```

- Actionable items: use `<div class="list-group">` with `<a>`/`<button>` children carrying `.list-group-item .list-group-item-action`.
- Modifiers: `.list-group-flush` (edge-to-edge, no outer borders) · `.list-group-numbered` (`<ol>`) · `.list-group-horizontal{-bp}` · variants `.list-group-item-{key}` · badges inside via `.d-flex .justify-content-between .align-items-center`.
- Checkboxes/radios inside items: `.form-check-input.me-1` + `.form-check-label` as sibling.
- Tab-like behavior (JS): list-group items with `data-bs-toggle="list"` drive a `.tab-content` — same contract as tabs ([components-navigation](components-navigation.md)).

## Pagination

```html
<nav aria-label="Page navigation">
  <ul class="pagination">
    <li class="page-item disabled"><a class="page-link">Previous</a></li>
    <li class="page-item active" aria-current="page"><a class="page-link" href="#">1</a></li>
    <li class="page-item"><a class="page-link" href="#">2</a></li>
    <li class="page-item"><a class="page-link" href="#">Next</a></li>
  </ul>
</nav>
```

- Sizes: `.pagination-lg/sm`. Align with flex utils on the `.pagination` (`justify-content-center/end`). Icon arrows need `aria-hidden` spans + accessible text.

## Placeholders (loading skeletons)

```html
<p class="placeholder-glow"><span class="placeholder col-6"></span></p>
<a class="btn btn-primary disabled placeholder col-4" aria-disabled="true"></a>
```

- `.placeholder` + width via `col-*`/`w-*`; animation on the wrapper: `.placeholder-glow` or `.placeholder-wave`; sizes `.placeholder-lg/sm/xs`; color with `.bg-{key}`.

## Progress

v5.3 markup — wrapper owns the ARIA, inner bar owns the width:

```html
<div class="progress" role="progressbar" aria-label="Upload" aria-valuenow="75" aria-valuemin="0" aria-valuemax="100">
  <div class="progress-bar" style="width: 75%">75%</div>
</div>
```

- Color: `.bg-{key}` (or `text-bg-{key}` to keep label contrast) on the `.progress-bar`. Height: inline `style="height: 1px"` on `.progress`.
- Striped/animated: `.progress-bar-striped` (+ `.progress-bar-animated`) on the bar.
- Multiple bars: wrap several `.progress` in a `.progress-stacked`, each with its width on the **`.progress`** element.

## Spinners

```html
<div class="spinner-border text-primary" role="status">
  <span class="visually-hidden">Loading...</span>
</div>
<span class="spinner-grow spinner-grow-sm" aria-hidden="true"></span>
```

- Two flavors: `.spinner-border`, `.spinner-grow`; small: `-sm` suffix; color via `.text-{key}`. In buttons: spinner + `role="status"` text inside the `.btn`.

## Close button

```html
<button type="button" class="btn-close" aria-label="Close"></button>
```

- Disabled state supported. On dark surfaces wrap/scope with `data-bs-theme="dark"` (the old `.btn-close-white` is deprecated in 5.3).
- Inside dismissible components pair with `data-bs-dismiss="alert|modal|offcanvas|toast"`.
