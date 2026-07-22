# Layout

Breakpoints, containers, the 12-column flexbox grid, column control, gutters, CSS-Grid opt-in.

## Breakpoints

| Tier | Infix | Range |
|---|---|---|
| Extra small | *(none)* | <576px |
| Small | `sm` | ≥576px |
| Medium | `md` | ≥768px |
| Large | `lg` | ≥992px |
| Extra large | `xl` | ≥1200px |
| Extra extra large | `xxl` | ≥1400px |

- All breakpoints are `min-width` — a class with infix `md` applies at `md` **and everything above**. Un-infixed = from 0.
- Source of truth is the Sass map `$grid-breakpoints: (xs: 0, sm: 576px, md: 768px, lg: 992px, xl: 1200px, xxl: 1400px)` in `_variables.scss`.
- Sass mixins for custom CSS: `media-breakpoint-up(md)`, `media-breakpoint-down(md)` (max-width, i.e. *below* md), `media-breakpoint-only(md)`, `media-breakpoint-between(md, xl)`.
- Runtime (JS) reads: breakpoint values are exposed as CSS vars `--bs-breakpoint-{tier}` on `:root`.

## Containers

Required wrapper for the default grid. Three families:

| Class | Behavior |
|---|---|
| `.container` | `max-width` steps per breakpoint: 100% → 540 → 720 → 960 → 1140 → 1320px |
| `.container-{sm\|md\|lg\|xl\|xxl}` | 100% wide **until** that breakpoint, then adopts the fixed max-widths above |
| `.container-fluid` | `width: 100%` always |

Max-widths come from `$container-max-widths: (sm: 540px, md: 720px, lg: 960px, xl: 1140px, xxl: 1320px)`. Custom container: `@include make-container()`. Nesting containers is allowed but rarely needed.

## Grid — core pattern

```html
<div class="container">
  <div class="row">
    <div class="col-sm-8">main</div>
    <div class="col-sm-4">sidebar</div>
  </div>
</div>
```

- 12 template columns per row. `col-4` spans 4/12 = 33.3%.
- `container → row → col → content`. Content never sits directly in a `.row`; columns never sit outside a `.row` (padding/negative-margin pairing breaks otherwise).
- Class anatomy: `.col{-bp}{-n}` — `.col` (equal width), `.col-6` (fixed span, all sizes), `.col-md-6` (span from `md` up; stacks full-width below `md`), `.col-auto` (width of content), `.col-md-auto`.
- More than 12 columns in a row → the overflowing group wraps to a new line as a unit.

### Auto-layout

```html
<div class="row">
  <div class="col">equal</div>
  <div class="col-6">fixed half</div>
  <div class="col">equal</div>  <!-- the two .col split the remaining 6 -->
</div>
```

### Responsive stacking (the standard card/form layout)

```html
<div class="row">
  <div class="col-md-8">stacked on phones, 2/3 from md up</div>
  <div class="col-6 col-md-4">half on phones, 1/3 from md up</div>
</div>
```

### Row columns — uniform card grids

`.row-cols{-bp}-{1..6|auto}` on the **row** sets how many equal columns per line; children just use `.col`.

```html
<div class="row row-cols-1 row-cols-md-3 g-4">
  <div class="col"><div class="card h-100">…</div></div>
  <!-- repeat; 1 per row on phones, 3 from md up, 1.5rem gutters -->
</div>
```

### Nesting

Put a new `.row` inside any column; the nested row gets its own 12-column scale relative to its parent's width.

## Column control

- **Vertical alignment** (flex): on the row `align-items-{start|center|end}`, per column `align-self-{start|center|end}`. Responsive infixes work: `align-items-md-center`.
- **Horizontal alignment**: on the row `justify-content-{start|center|end|around|between|evenly}`.
- **Column breaks**: force a wrap with `<div class="w-100"></div>` between columns (make it responsive by pairing with display utils, e.g. `w-100 d-none d-md-block`).
- **Reordering**: `.order{-bp}-{0..5}`, plus `.order-first` (−1) and `.order-last` (6). Unordered siblings keep DOM order.
- **Offsetting**: `.offset{-bp}-{0..11}` adds left margin in column units — `.offset-md-3` pushes 3 columns. `offset-md-0` resets at a tier. Flex alternative: `.ms-auto` / `.me-auto` push columns apart.
- **Standalone column classes**: `.col-*` widths work outside a `.row` (element gets the percentage width, padding included) — pair with `.float-*` or flex when needed; prefer full grid for real layouts.

## Gutters

- Default gutter: `1.5rem` (`$grid-gutter-width`) — horizontal padding on columns, negative margin on the row.
- Classes on the **row**: `.g-{0..5}` (both axes) · `.gx-{0..5}` (horizontal) · `.gy-{0..5}` (vertical). Responsive: `.g-md-4`. Scale = spacing scale (0, .25, .5, 1, 1.5, 3 rem).
- `.g-0` = edge-to-edge columns. For a full-bleed row without a container add `.mx-0` to the row.
- Large custom gutters can overflow the container: either add matching horizontal padding to the container (`.px-4`) or wrap the row in an `.overflow-hidden` div. `gy-*` overflow at page bottom → same `.overflow-hidden` wrapper fix.
- Vertical gutters act when columns **wrap** (multi-line rows, row-cols grids).

## CSS Grid opt-in (experimental)

Compile with `$enable-grid-classes: false; $enable-cssgrid: true;` to swap the flexbox grid for CSS Grid: `.grid` container + `.g-col-{n}` / `.g-col-{bp}-{n}` children, `--bs-columns` / `--bs-gap` per-instance overrides. Don't mix both systems in one project; the flexbox grid remains the default and the safe choice.

## Layout-adjacent utilities

`d-{none|block|flex|grid|inline|inline-block|...}` with breakpoint infixes handles show/hide per tier (`d-none d-lg-block` = only ≥lg). Full display/flex reference: [utilities-helpers](utilities-helpers.md).
