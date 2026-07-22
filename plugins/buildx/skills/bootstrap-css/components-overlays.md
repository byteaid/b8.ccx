# Components — Overlays

Modal, offcanvas, toasts, tooltips & popovers. All are JS plugins; tooltips/popovers also need Popper. Z-index scale at the end.

## Modal

```html
<button type="button" class="btn btn-primary" data-bs-toggle="modal" data-bs-target="#demoModal">Open</button>

<div class="modal fade" id="demoModal" tabindex="-1" aria-labelledby="demoModalLabel" aria-hidden="true">
  <div class="modal-dialog">
    <div class="modal-content">
      <div class="modal-header">
        <h1 class="modal-title fs-5" id="demoModalLabel">Title</h1>
        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
      </div>
      <div class="modal-body">…</div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
        <button type="button" class="btn btn-primary">Save</button>
      </div>
    </div>
  </div>
</div>
```

- Place the modal at a top level of the DOM (direct child of `<body>` ideally) — never inside a positioned/fixed ancestor or a responsive table.
- One modal at a time; nesting unsupported. Toggling between two modals: `data-bs-toggle="modal" data-bs-target="#other"` inside the open one (they swap, not stack).
- Dialog modifiers on `.modal-dialog`: sizes `.modal-sm/.modal-lg/.modal-xl` · `.modal-dialog-centered` · `.modal-dialog-scrollable` · fullscreen `.modal-fullscreen` and `.modal-fullscreen-{sm|md|lg|xl|xxl}-down`.
- Static backdrop (no click-outside close): `data-bs-backdrop="static"` (+ usually `data-bs-keyboard="false"`); a prevented close fires `hidePrevented.bs.modal`.
- Remove animation: drop `.fade` (and then `aria-hidden` handling is instant).
- Options: `backdrop` (`true|false|'static'`), `keyboard`, `focus`. JS: `bootstrap.Modal.getOrCreateInstance(el)` → `.show(relatedTarget?)/.hide()/.toggle()/.handleUpdate()` (call after height changes while open) `/.dispose()`.
- Events on the modal: `show/shown/hide/hidden.bs.modal` (`e.relatedTarget` = the trigger button on show). Vary content per trigger by reading `data-*` off `relatedTarget` in `show.bs.modal`.
- Focus is trapped inside; on `shown.bs.modal` focus your first input manually (autofocus doesn't fire).

## Offcanvas

```html
<button class="btn btn-primary" type="button" data-bs-toggle="offcanvas" data-bs-target="#demoOc" aria-controls="demoOc">Open</button>

<div class="offcanvas offcanvas-start" tabindex="-1" id="demoOc" aria-labelledby="demoOcLabel">
  <div class="offcanvas-header">
    <h5 class="offcanvas-title" id="demoOcLabel">Sidebar</h5>
    <button type="button" class="btn-close" data-bs-dismiss="offcanvas" aria-label="Close"></button>
  </div>
  <div class="offcanvas-body">…</div>
</div>
```

- Placement: `.offcanvas-start` (left) · `.offcanvas-end` · `.offcanvas-top` · `.offcanvas-bottom`.
- Responsive variants: `.offcanvas-{sm|md|lg|xl|xxl}` — content renders inline **at and above** that breakpoint, becomes an offcanvas drawer below it (the pattern behind offcanvas navbars and filter sidebars).
- Options: `data-bs-backdrop="true|false|static"`, `data-bs-scroll="true"` (allow body scroll while open), `data-bs-keyboard`. One offcanvas at a time; shares modal semantics (`hidePrevented.bs.offcanvas` on static backdrop).
- JS: `bootstrap.Offcanvas.getOrCreateInstance(el)` → `.show()/.hide()/.toggle()`; events `show/shown/hide/hidden.bs.offcanvas`.
- CSS `margin`/`translate` on the offcanvas element breaks the animation — position with the placement classes only.

## Toasts

```html
<div class="toast-container position-fixed bottom-0 end-0 p-3">
  <div id="demoToast" class="toast" role="alert" aria-live="assertive" aria-atomic="true">
    <div class="toast-header">
      <strong class="me-auto">App</strong>
      <small class="text-body-secondary">just now</small>
      <button type="button" class="btn-close" data-bs-dismiss="toast" aria-label="Close"></button>
    </div>
    <div class="toast-body">Saved successfully.</div>
  </div>
</div>
```

```js
bootstrap.Toast.getOrCreateInstance(document.getElementById('demoToast')).show()
```

- **Toasts never show themselves** — call `.show()` (data-attribute triggering exists only for dismiss). Autohide after 5 s by default; `data-bs-autohide="false"` / `data-bs-delay="8000"` to change.
- Stack by placing several toasts in one `.toast-container` (it manages spacing); position the container with position utilities (`top-0 start-50 translate-middle-x`, etc.).
- Color scheme: `text-bg-{key}` + `border-0` on the toast; single-line variant = `.d-flex` toast with body + close button.
- A11y: `role="alert" aria-live="assertive"` for important messages, `role="status" aria-live="polite"` for passive ones; with autohide, duplicate the action elsewhere on the page.
- Events: `show/shown/hide/hidden.bs.toast`.

## Tooltips

```js
document.querySelectorAll('[data-bs-toggle="tooltip"]')
  .forEach(el => new bootstrap.Tooltip(el))
```

```html
<button type="button" class="btn btn-secondary" data-bs-toggle="tooltip"
        data-bs-title="Tooltip text" data-bs-placement="top">Hover me</button>
```

- **Manual init is mandatory** (performance opt-in). Zero-length titles never display.
- Placement: `top|bottom|left|right|auto` via `data-bs-placement`. `data-bs-custom-class="app-tooltip"` for styling; theme via `--bs-tooltip-*` vars in that class.
- `data-bs-html="true"` enables HTML titles (sanitized — see Sanitizer). Overflow/clipping in input groups & button groups: set `container: 'body'`.
- Disabled elements don't emit events — wrap in a `<span tabindex="0">` and put the tooltip there.
- Hide before removing the element from the DOM; in SPAs call `.dispose()` on teardown or tips orphan.
- Options: `title` (string|element|function), `delay`, `trigger` (`'hover focus'` default — avoid `hover`-only, it's not keyboard-accessible), `offset`, `boundary`, `fallbackPlacements`, `popperConfig`.
- Methods: `.show()/.hide()/.toggle()/.enable()/.disable()/.setContent({'.tooltip-inner': 'new'})`. Events `show/shown/hide/hidden/inserted.bs.tooltip`.

## Popovers

Same engine as tooltips (title + body, click-triggered by default):

```html
<button type="button" class="btn btn-lg btn-danger" data-bs-toggle="popover"
        data-bs-title="Popover title" data-bs-content="Body content.">Click me</button>
```

```js
document.querySelectorAll('[data-bs-toggle="popover"]').forEach(el => new bootstrap.Popover(el))
```

- **Dismiss-on-next-click** pattern: `<a tabindex="0" role="button" data-bs-toggle="popover" data-bs-trigger="focus" ...>` — the only reliably accessible auto-dismiss.
- Options mirror tooltips plus `content` (string|element|function). Methods/events identical with `.bs.popover` suffix.
- Everything about containers, disabled wrappers, dispose-before-removal, and sanitize applies equally.

## Sanitizer

Tooltips and popovers sanitize any HTML option values with an allowlist (`allowList` option). `sanitize: false` turns it off — only for trusted content; consider delegating to DOMPurify via `sanitizeFn`. CSP note: Bootstrap's sanitizer requires no `unsafe-eval`; inline styles some components set (carousel, progress widths) may need attention under strict CSP.

## Z-index scale (component layering)

`dropdown 1000` < `sticky 1020` < `fixed 1030` < `offcanvas-backdrop 1040` < `offcanvas 1045` < `modal-backdrop 1050` < `modal 1055` < `popover 1070` < `tooltip 1080` < `toast 1090`. Custom overlays should slot into this scale (Sass `$zindex-*` variables), not invent `z-index: 99999`. The `.z-{n}` utilities ([utilities-helpers](utilities-helpers.md)) are for local stacking only.
