# Setup

Install options, page skeleton, dist file variants, global prerequisites, the JS plugin contract, framework integration, RTL.

## Page skeleton (CDN)

Minimal correct page. CSS in `<head>`, JS bundle before `</body>`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Bootstrap demo</title>
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"
          integrity="sha384-..." crossorigin="anonymous">
  </head>
  <body>
    <h1>Hello, world!</h1>
    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/js/bootstrap.bundle.min.js"
            integrity="sha384-..." crossorigin="anonymous"></script>
  </body>
</html>
```

Copy the real `integrity` sha384 hashes from `getbootstrap.com/docs/5.3/getting-started/introduction/#cdn-links` for the exact version — hashes change per version. Alternative CDNs (cdnjs, unpkg) serve the same files; if their SRI hash for a file differs from jsDelivr's, do not use that CDN.

## Install options

| Channel | Command / URL | Notes |
|---|---|---|
| CDN (jsDelivr) | `https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/...` | Zero build. Compiled CSS+JS only. |
| npm | `npm i bootstrap@5.3.8` | Source Sass + JS. Popper is a peer for dropdowns/tooltips/popovers: `npm i @popperjs/core`. |
| Compiled zip | `bootstrap-5.3.8-dist.zip` from GitHub releases | Drop-in `css/` + `js/`, no docs/source. |
| Source zip | `v5.3.8.zip` | Sass sources; needs a Sass compiler + Autoprefixer. |
| ASP.NET Core | Project templates ship it under `wwwroot/lib/bootstrap/` (managed by libman) | Update via `libman.json`; do not hand-edit files under `lib/`. |
| Composer / RubyGems / NuGet | Also published | NuGet package is CSS/JS drop-in; prefer libman/npm in ASP.NET Core. |

## Dist file variants — pick one CSS and one JS

- CSS: `bootstrap.min.css` (everything) · `bootstrap-grid.min.css` (grid + flex utils only) · `bootstrap-reboot.min.css` (reset only) · `bootstrap-utilities.min.css` (utilities only) · `bootstrap.rtl.min.css` (RTL builds of each).
- JS: `bootstrap.bundle.min.js` (**plugins + Popper — the default choice**) · `bootstrap.min.js` (plugins only; load `popper.min.js` yourself *before* it if you use dropdowns/tooltips/popovers) · `bootstrap.esm.min.js` (ESM for `<script type="module">`; needs an import map or bundler to resolve `@popperjs/core`) · `js/dist/*.js` (individual UMD plugins for bundlers).
- Never load both the bundle and separate Popper, nor bundle + plain together.
- No JS needed at all if you only use CSS components (grid, cards, badges, forms without validation JS…). Plugins that need JS: accordion/collapse, alerts (dismiss), button toggles, carousel, dropdowns*, modal, navbar (collapse/offcanvas), tabs, offcanvas, scrollspy, toasts, tooltips*, popovers* (* = also Popper).

## Global prerequisites (Bootstrap assumes all four)

1. **HTML5 doctype** — `<!doctype html>`.
2. **Viewport meta** — `<meta name="viewport" content="width=device-width, initial-scale=1">` (mobile-first rendering + touch zoom).
3. **`box-sizing: border-box`** is set globally. Third-party widgets that need `content-box` must override it locally on their own selector.
4. **Reboot** — normalization layer built on normalize.css; see [content-forms](content-forms.md) § Reboot.

## JS plugin contract

All plugins live on the `bootstrap` global (or as named ESM exports: `import { Modal, Tooltip } from 'bootstrap'`).

### Data API (declarative, default)

```html
<button data-bs-toggle="modal" data-bs-target="#demoModal">Open</button>
```

- `data-bs-toggle` selects the plugin; `data-bs-target` (or `href` on `<a>`) points at the element it controls.
- Options are passed as `data-bs-*` attributes on the **controlled** element (camelCase option → kebab-case attribute: `autohide` → `data-bs-autohide`). Options passed to the JS constructor win over data attributes.
- Disable the whole data API: `document.body.addEventListener` is not needed — call `bootstrap.EventHandler` off; in practice just omit the attributes.

### Programmatic API

```js
const el = document.querySelector('#demoModal')
const modal = bootstrap.Modal.getOrCreateInstance(el, { backdrop: 'static' })
modal.show()
bootstrap.Modal.getInstance(el)   // null if never constructed
modal.dispose()                   // destroy; must re-instantiate afterwards
```

- Constructors accept an element **or a CSS selector string**: `new bootstrap.Modal('#demoModal')`.
- `getOrCreateInstance` is the idiomatic accessor — avoids double-instantiation bugs.
- All `show`/`hide` methods are **asynchronous**: they return before the CSS transition ends. Calling a method on a transitioning component is ignored. Chain work off the `shown`/`hidden` event, not after the call.
- `dispose()` while a transition runs, or on an element you're about to remove from the DOM, must happen **before** removal (esp. tooltips/popovers — they leave orphan tip elements otherwise).
- Change defaults for every future instance via the static `Default`: `bootstrap.Tooltip.Default.customClass = 'app-tooltip'`.

### Events

```js
el.addEventListener('show.bs.modal', e => { if (notAllowed) e.preventDefault() })
el.addEventListener('shown.bs.modal', () => input.focus())
```

- Naming: `{show|shown|hide|hidden}.bs.{component}` (+ component-specific ones: `slide/slid.bs.carousel`, `closed.bs.alert`, `hidePrevented.bs.modal`, `inserted.bs.tooltip`).
- Infinitive (`show.`, `hide.`) fires at invocation start and is cancelable with `preventDefault()`; past form (`shown.`, `hidden.`) fires after the transition completes.
- Events fire on the component's element and bubble. jQuery users must bind with jQuery (`$(el).on('show.bs.modal', ...)`) — native `addEventListener` won't see jQuery-triggered events and vice versa; Bootstrap detects jQuery and cooperates, but don't mix binding styles for one element.

## Frameworks (React / Vue / Angular / Blazor)

Bootstrap **CSS** works under any framework. Bootstrap **JS** assumes it owns the DOM; a virtual-DOM/diffing framework mutating the same nodes produces stuck dropdowns, ghost modals, orphan tooltips.

- React → **React Bootstrap** · Vue 3 → **BootstrapVueNext** · Angular → **ng-bootstrap** or **ngx-bootstrap**. These reimplement the JS in the framework; you still load Bootstrap's CSS.
- **Blazor** (see `aspnet-core-blazor`): no official wrapper. Preferred order:
  1. CSS-only usage — Blazor toggles classes itself (`class="collapse @(open ? "show" : null)"`, conditional rendering for modals). Most components need nothing more.
  2. `IJSRuntime` interop around the programmatic API for the genuinely JS-bound pieces (tooltip/popover/toast): init in `OnAfterRenderAsync(firstRender)`, and `dispose()` in `DisposeAsync` **before** Blazor removes the node.
  3. Never let the data API and Blazor both drive the same element's visibility.
- Server-rendered stacks (MVC / Razor Pages / Django / Rails) have no conflict — use the data API freely.

## RTL

- `<html dir="rtl" lang="ar">` + swap the stylesheet for `bootstrap.rtl.min.css`. JS bundle is unchanged.
- Utilities are logical already: `ms-*`/`me-*`/`ps-*`/`pe-*` = start/end, `text-start`/`text-end`, `float-start`/`float-end` — never `left/right` names.

## Accessibility baseline

- Bootstrap's docs markup encodes the a11y contract — copy it whole (togglers with `aria-controls`/`aria-expanded`/`aria-label`, `.visually-hidden` text in icon-only buttons and spinners, `role="alert"`/`role="status"` where documented).
- Animated components respect `prefers-reduced-motion: reduce` out of the box (transitions collapse to instant).
- Color alone never conveys meaning — pair contextual colors with text or `.visually-hidden` labels.
