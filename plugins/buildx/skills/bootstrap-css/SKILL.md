---
name: bootstrap-css
description: Bootstrap 5.3 (v5.3.8) frontend toolkit reference. Install (CDN/npm), page skeleton, the 12-column flexbox grid (6 breakpoints, containers, gutters), utilities and helpers, forms + validation, every component with its markup and JS-plugin contract (data API, programmatic API, events), dark mode via data-bs-theme, and Sass / CSS-variable / utility-API customization. NOT for bootstrapping a repo/docs (see `development-documentation`).
when_to_use: |
  - Trigger keywords: Bootstrap 5, getbootstrap, bootstrap.min.css, bootstrap.bundle.min.js, data-bs-toggle, data-bs-theme, navbar, modal, offcanvas, toast, tooltip, popover, accordion, carousel, form-control, was-validated, is-invalid, container/row/col, col-md-*, row-cols, g-*, mt-*/px-*, d-flex, text-bg-*, bg-body-tertiary, btn-primary, card, list-group, dropdown-menu, nav-tabs, scrollspy, collapse, stretched-link, visually-hidden, vstack/hstack, $theme-colors, utility API, Popper.
  - Task shapes: scaffold a page with Bootstrap; build/fix a responsive grid layout; pick utility classes; author a navbar/modal/dropdown/toast/tooltip with correct a11y markup; wire or debug a JS plugin (init, options, methods, events); add form validation states; enable or scope dark mode; override theme colors or add a custom color/utility via Sass; integrate Bootstrap in ASP.NET Core MVC/Razor Pages/Blazor.
user-invocable: false
paths: ["**/bootstrap*.css", "**/bootstrap*.scss", "**/bootstrap.bundle*.js", "**/*.scss"]
---

# Bootstrap 5.3

Reference for Bootstrap v5.3.8. Canonical docs: `getbootstrap.com/docs/5.3/`. Source: `github.com/twbs/bootstrap` tag `v5.3.8`. Bootstrap Icons is a **separate** package (`bootstrap-icons`) — not covered here.

## Mental model

- Bootstrap = compiled CSS (classes + `--bs-*` CSS custom properties) + optional dependency-free JS plugins. The CSS works with any stack; the JS plugins **own the DOM nodes they manage**.
- **Mobile-first**: all breakpoints are `min-width`. An un-infixed class (`.col-6`, `.mt-3`) applies from `xs` up; a `-{bp}` infix (`.col-md-6`, `.mt-lg-3`) applies from that breakpoint **up**.
- Six grid tiers: `xs` <576 · `sm` ≥576 · `md` ≥768 · `lg` ≥992 · `xl` ≥1200 · `xxl` ≥1400 (px).
- Three customization layers, cheapest first: utility classes in markup → `--bs-*` CSS variables at runtime → Sass variables/maps at build time. See [customize](customize.md).
- Color modes (v5.3): `data-bs-theme="dark"` on `<html>` themes everything; on any element it themes that subtree only.
- Grid hierarchy is strict: `container` → `row` → `col-*` → content. Gutters are column padding offset by negative row margins.
- Utility classes carry `!important` — they beat component and custom CSS by design.
- JS plugin contract is uniform: data API (`data-bs-toggle="..."`) for zero-JS default behavior; programmatic API (`bootstrap.Modal.getOrCreateInstance(el)`) for control; lifecycle events `show/shown/hide/hidden.bs.{component}` (the infinitive fires before, is cancelable via `preventDefault()`; the participle fires after the CSS transition).

## Non-negotiable rules

1. **HTML5 doctype + responsive viewport meta** are required — without them styling is broken and mobile rendering is wrong. See [setup](setup.md).
2. **Pin one version everywhere (5.3.8)** — CSS and JS versions must match; with CDN links always keep the `integrity` + `crossorigin="anonymous"` attributes, copied from the official docs for that exact version.
3. **`bootstrap.bundle.min.js` already contains Popper.** Never load the bundle plus a separate Popper, and never load both `bootstrap.js` and the bundle.
4. **Tooltips, popovers, toasts and (non-autoplay) carousels require manual JS initialization** — they are opt-in for performance. Everything else works via data attributes alone.
5. **Never mix Bootstrap's JS plugins with a framework that owns the same DOM** (React/Vue/Angular/Blazor). Use the framework wrapper (React Bootstrap, BootstrapVueNext, ng-bootstrap) or keep Bootstrap CSS-only and drive classes from the framework. Blazor guidance: [setup](setup.md) § Frameworks.
6. **Never edit Bootstrap's source or dist files.** Override Sass variables before the import, or override `--bs-*` variables in your own stylesheet. See [customize](customize.md).
7. **Forms validation pattern is fixed**: `novalidate` on the form + `.was-validated` toggled on submit for client-side; `.is-invalid`/`.is-valid` per control for server-side. Feedback text lives in `.invalid-feedback` **after** the control. See [content-forms](content-forms.md).
8. **Prefer an existing utility over ad-hoc CSS**; when a utility is missing, add it through the utility API so it gets responsive/state variants for free — don't hand-write one-off classes.
9. **Accessibility markup is part of the component contract** — `aria-label` on togglers/close buttons, `.visually-hidden` text for icon-only controls, documented `role`s on progress/alerts/toasts. Copy the canonical markup; don't strip attributes.
10. **Use color-mode-aware tokens** — `bg-body`, `bg-body-tertiary`, `text-body-secondary`, `*-subtle`, `*-emphasis`, `border-color: var(--bs-border-color)` — instead of fixed grays (`.bg-light`, `.text-muted`, `#f8f9fa`), or dark mode breaks. See [customize](customize.md) § Color modes.

## Sub-file map

| File | When to read |
|---|---|
| [setup](setup.md) | Install (CDN/npm/ASP.NET Core), page skeleton, dist file variants, globals (doctype/viewport/Reboot), JS plugin contract (data API, programmatic API, events, dispose), framework/Blazor integration, RTL |
| [layout](layout.md) | Breakpoints, containers, the 12-column grid, auto-layout, row-cols, nesting, column alignment/order/offset, gutters, CSS-Grid opt-in |
| [content-forms](content-forms.md) | Reboot highlights, typography, images, tables; all form controls (input/select/checks/switches/range/input-group/floating labels), form layout, validation |
| [utilities-helpers](utilities-helpers.md) | Spacing notation, display, flex, text, colors/background, borders, sizing, position, shadows, overflow, z-index; helpers (ratio, stacks, stretched-link, visually-hidden, focus ring, icon link) |
| [components-static](components-static.md) | CSS-only components: buttons, button group, badge, breadcrumb, card, list group, pagination, placeholders, progress, spinners, close button |
| [components-navigation](components-navigation.md) | Navbar (collapse + offcanvas variants), navs & tabs (Tab plugin), dropdowns (Popper), scrollspy |
| [components-interactive](components-interactive.md) | Alerts (dismissible), collapse, accordion, carousel |
| [components-overlays](components-overlays.md) | Modal, offcanvas, toasts, tooltips & popovers; z-index scale; sanitizer |
| [customize](customize.md) | Sass import order + variable/map overrides, `--bs-*` CSS variables, color modes (dark + custom), utility API, npm build |

## Quick decision matrix

| Symptom / Need | Read |
|---|---|
| "Blank page skeleton / which files do I include?" | [setup](setup.md) |
| "Columns don't align / layout breaks at a size" | [layout](layout.md) |
| "Style a form / show validation errors" | [content-forms](content-forms.md) |
| "Which class sets margin/color/alignment?" | [utilities-helpers](utilities-helpers.md) |
| "Build a navbar / tabs / dropdown" | [components-navigation](components-navigation.md) |
| "Dialog / sidebar / notification / hint on hover" | [components-overlays](components-overlays.md) |
| "Tooltip/toast does nothing" | [components-overlays](components-overlays.md) (init is manual) |
| "Dropdown/modal stuck open under React/Blazor" | [setup](setup.md) § Frameworks |
| "Dark mode / theme colors / brand palette" | [customize](customize.md) |
| "Element looks wrong in dark mode" | [customize](customize.md) § Color modes + rule 10 |
| "Need a utility Bootstrap doesn't ship" | [customize](customize.md) § Utility API |

## Cross-skill references

- `aspnet-core-blazor` — component model that must own the DOM; keep Bootstrap CSS-only there or interop deliberately (see [setup](setup.md) § Frameworks).
- `aspnet-core-mvc` / `aspnet-core-razor-pages` — project templates ship Bootstrap under `wwwroot/lib/bootstrap`; serving static assets.
- `playwright-dotnet` — E2E tests against Bootstrap markup: prefer roles (`GetByRole`) which Bootstrap's canonical a11y markup provides.

## Upstream sources

- Docs: <https://getbootstrap.com/docs/5.3/> (per-page "View on GitHub" links to `site/src/content/docs/**` at tag `v5.3.8`).
- Examples: <https://getbootstrap.com/docs/5.3/examples/> · Repo: <https://github.com/twbs/bootstrap>.
