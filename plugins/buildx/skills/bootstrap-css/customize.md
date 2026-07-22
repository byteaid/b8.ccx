# Customize

Three layers, cheapest first: utility classes (markup) → CSS variables (runtime, no build) → Sass (build time). Never edit files under `node_modules/bootstrap` or dist.

## Sass

File layout: your `scss/custom.scss` imports Bootstrap from `node_modules` (or a vendored `bootstrap/scss` copy). Compile with Dart Sass + Autoprefixer. (Current Dart Sass emits deprecation warnings on Bootstrap 5.3 sources — ignorable, tracked upstream.)

### Import order (the part everyone gets wrong)

Variable **overrides go before** the imports that consume them (they're all `!default`); **map overrides go after** `variables` but before `maps`/the consumers:

```scss
// custom.scss — Option B: pick parts (preferred)
// 1. Functions first
@import "../node_modules/bootstrap/scss/functions";

// 2. Default variable overrides
$primary: #0043ce;
$enable-shadows: true;
$border-radius: .25rem;

// 3. Required stylesheets
@import "../node_modules/bootstrap/scss/variables";
@import "../node_modules/bootstrap/scss/variables-dark";

// 4. Map overrides (need the defaults defined above)
$custom-colors: ("tertiary": #7952b3);
$theme-colors: map-merge($theme-colors, $custom-colors);
// remove keys: $theme-colors: map-remove($theme-colors, "info", "light");

// 5. Remainder of required parts
@import "../node_modules/bootstrap/scss/maps";
@import "../node_modules/bootstrap/scss/mixins";
@import "../node_modules/bootstrap/scss/root";

// 6. Optional components you actually use
@import "../node_modules/bootstrap/scss/utilities";
@import "../node_modules/bootstrap/scss/reboot";
@import "../node_modules/bootstrap/scss/type";
@import "../node_modules/bootstrap/scss/containers";
@import "../node_modules/bootstrap/scss/grid";
@import "../node_modules/bootstrap/scss/buttons";
@import "../node_modules/bootstrap/scss/helpers";

// 7. Utilities API last (generates utility classes from $utilities)
@import "../node_modules/bootstrap/scss/utilities/api";

// 8. Your custom code
```

Option A (`@import ".../scss/bootstrap";` = everything) still honors variable overrides placed before it, but not map surgery between variables and maps.

- Useful knobs: `$enable-shadows`, `$enable-gradients`, `$enable-negative-margins`, `$enable-rounded`, `$enable-dark-mode`, `$prefix` (renames `--bs-`), `$spacer`, `$grid-breakpoints`, `$container-max-widths`, `$font-family-base`, `$border-radius`.
- Adding to `$theme-colors` generates **everything** for the new key: `.btn-tertiary`, `.text-bg-tertiary`, `.bg-tertiary-subtle`, alerts, list-group variants… That is the correct way to add a brand color.
- Required-key warning: several maps (`$theme-colors`, `$grid-breakpoints`…) have required keys — remove others, not those, or expect compile errors.
- Functions available after `functions` import: `tint-color()`, `shade-color()`, `shift-color()`, `color-contrast()`, `escape-svg()`, `add()/subtract()`.

## CSS variables (runtime theming)

All compiled values surface as `--bs-*` custom properties:

- **Root scope**: palette (`--bs-blue`…), theme colors + `-rgb` split channels, `-text-emphasis` / `-bg-subtle` / `-border-subtle` per theme color, body tokens (`--bs-body-bg`, `--bs-body-color`, `--bs-secondary-bg`, `--bs-tertiary-bg`, `--bs-emphasis-color`…), fonts, `--bs-border-color`, `--bs-border-radius`, focus ring, link colors, `--bs-breakpoint-*`.
- **Component scope**: each component defines its own local vars consumed by its rules — `.btn { --bs-btn-bg; --bs-btn-color; --bs-btn-hover-bg; … }`, `.modal { --bs-modal-width; … }`, `.tooltip { --bs-tooltip-bg; … }`. Override per instance/class without touching Sass:

```css
.btn-brand {
  --bs-btn-bg: #4b0082;
  --bs-btn-color: #fff;
  --bs-btn-hover-bg: #37005f;
  --bs-btn-border-color: transparent;
}
```

- The `-rgb` variables exist so utilities can compose opacity: `background-color: rgba(var(--bs-primary-rgb), var(--bs-bg-opacity))`. Follow the same pattern in custom CSS.
- Prefix is customizable via `$prefix` — grep for `--bs-` assumes the default.

## Color modes (v5.3)

- Activate: `data-bs-theme="light|dark"` — on `<html>` for the whole page, on any element/component to scope (e.g. a permanently dark navbar on a light page).
- Ship both modes: Bootstrap's dark values come from `_variables-dark.scss` and are emitted under `[data-bs-theme=dark]`. Your custom CSS must react too — use the tokens (`var(--bs-body-bg)`, `var(--bs-border-color)`, `bg-body-tertiary`, `*-subtle`, `*-emphasis`) instead of literal grays, and put mode-specific rules in the mixin:

```scss
@include color-mode(dark) {
  .brand-hero { background-image: url("hero-dark.svg"); }
}
```

- Media-query strategy instead of attribute: `$color-mode-type: media-query;` — respects OS preference automatically but kills per-component scoping and JS toggling.
- **Custom modes**: any `data-bs-theme="blue"` works — define `[data-bs-theme=blue] { --bs-body-bg: …; --bs-body-color: …; }` overriding the global tokens (and any component vars you care about).
- No built-in picker ships; the documented JS toggler pattern: read `localStorage` → fall back to `prefers-color-scheme` → set `document.documentElement.setAttribute('data-bs-theme', mode)`; listen to `matchMedia('(prefers-color-scheme: dark)')` changes when no stored choice.

## Utility API (generate/modify utility classes)

Utilities are generated from the `$utilities` Sass map, merged with yours before `utilities/api` is imported. Entry options: `property` (req), `values` (req; list or map), `class` (name override), `state` (`hover focus`…), `responsive` (bool), `print`, `rfs`, `css-var`/`local-vars`, `rtl`.

```scss
@import "../node_modules/bootstrap/scss/functions";
@import "../node_modules/bootstrap/scss/variables";
@import "../node_modules/bootstrap/scss/variables-dark";
@import "../node_modules/bootstrap/scss/maps";
@import "../node_modules/bootstrap/scss/mixins";
@import "../node_modules/bootstrap/scss/utilities";

$utilities: map-merge($utilities, (
  // add a new utility → .cursor-pointer, .cursor-grab (+ responsive variants)
  "cursor": (property: cursor, class: cursor, responsive: true,
             values: auto pointer grab),
  // modify an existing one → make .w-* responsive
  "width": map-merge(map-get($utilities, "width"), (responsive: true)),
  // remove one
  "float": null,
));

@import "../node_modules/bootstrap/scss/utilities/api";
```

- Generated class shape: `.{class}{-bp?}{-state?}-{value}`; `!important` is applied per `$enable-important-utilities`.
- Prefer this over hand-written helper classes: variants, RTL handling, and naming consistency come free.

## npm build wiring

- `npm i bootstrap@5.3.8 @popperjs/core sass`. Bundlers (Vite/webpack/Parcel): import SCSS entry (`import './scss/custom.scss'`) + JS (`import * as bootstrap from 'bootstrap'` or per-plugin `import { Modal } from 'bootstrap'` for tree-shaking via `bootstrap/js/dist/modal`).
- Official starter repos: `github.com/twbs/examples` (sass-js, vite, webpack, react-nextjs, color-modes).
