# Components — Interactive

Alerts, collapse, accordion, carousel.

## Alerts

```html
<div class="alert alert-warning alert-dismissible fade show" role="alert">
  <strong>Heads up!</strong> Check the fields below. <a href="#" class="alert-link">Details</a>.
  <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
</div>
```

- Variants: `.alert-{primary|secondary|success|danger|warning|info|light|dark}`; links inside use `.alert-link`; extra content just uses headings (`.alert-heading`) and paragraphs.
- Dismissible = `.alert-dismissible` + close button with `data-bs-dismiss="alert"` + `.fade .show` for animation. **Dismissal removes the element from the DOM** — re-showing requires re-rendering it.
- JS: `bootstrap.Alert.getOrCreateInstance(el).close()`. Events: `close.bs.alert` (cancelable), `closed.bs.alert`.
- Icons: inline SVG + flex utilities (`d-flex align-items-center`) — no built-in icon slot.

## Collapse

```html
<a class="btn btn-primary" data-bs-toggle="collapse" href="#demo" role="button"
   aria-expanded="false" aria-controls="demo">Toggle</a>
<div class="collapse" id="demo">
  <div class="card card-body">Hidden content.</div>
</div>
```

- States: `.collapse` (hidden) · `.collapse.show` (shown) · `.collapsing` (transitioning — applied by the plugin, don't set manually).
- Horizontal: add `.collapse-horizontal` (child needs a set `width`).
- One trigger, many targets: `data-bs-target=".multi-collapse"` (class selector); many triggers, one target also works — keep `aria-expanded` on every trigger and `aria-controls` listing the ids.
- JS: `new bootstrap.Collapse(el, { toggle: false })` to instantiate without immediately toggling; methods `.show()/.hide()/.toggle()`. Events on the collapsed element: `show/shown/hide/hidden.bs.collapse`.

## Accordion

Collapse + canonical styling. Exclusive open via `data-bs-parent`:

```html
<div class="accordion" id="acc">
  <div class="accordion-item">
    <h2 class="accordion-header">
      <button class="accordion-button" type="button" data-bs-toggle="collapse"
              data-bs-target="#acc-one" aria-expanded="true" aria-controls="acc-one">
        Item #1
      </button>
    </h2>
    <div id="acc-one" class="accordion-collapse collapse show" data-bs-parent="#acc">
      <div class="accordion-body">First body.</div>
    </div>
  </div>
  <div class="accordion-item">
    <h2 class="accordion-header">
      <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse"
              data-bs-target="#acc-two" aria-expanded="false" aria-controls="acc-two">
        Item #2
      </button>
    </h2>
    <div id="acc-two" class="accordion-collapse collapse" data-bs-parent="#acc">
      <div class="accordion-body">Second body.</div>
    </div>
  </div>
</div>
```

- Default-open item: `.show` on its `.accordion-collapse`, **drop** `.collapsed` from its button, `aria-expanded="true"`.
- **Always-open** variant (multiple expanded at once): omit `data-bs-parent` on every `.accordion-collapse`.
- Flush (edge-to-edge, no outer borders/radius): `.accordion-flush` on the `.accordion`.
- Heading level (`h2`) is a placeholder — pick the level that fits the page outline.

## Carousel

```html
<div id="hero" class="carousel slide" data-bs-ride="carousel">
  <div class="carousel-indicators">
    <button type="button" data-bs-target="#hero" data-bs-slide-to="0" class="active" aria-current="true" aria-label="Slide 1"></button>
    <button type="button" data-bs-target="#hero" data-bs-slide-to="1" aria-label="Slide 2"></button>
  </div>
  <div class="carousel-inner">
    <div class="carousel-item active">
      <img src="…" class="d-block w-100" alt="…">
      <div class="carousel-caption d-none d-md-block"><h5>First</h5><p>Caption.</p></div>
    </div>
    <div class="carousel-item"><img src="…" class="d-block w-100" alt="…"></div>
  </div>
  <button class="carousel-control-prev" type="button" data-bs-target="#hero" data-bs-slide="prev">
    <span class="carousel-control-prev-icon" aria-hidden="true"></span>
    <span class="visually-hidden">Previous</span>
  </button>
  <button class="carousel-control-next" type="button" data-bs-target="#hero" data-bs-slide="next">
    <span class="carousel-control-next-icon" aria-hidden="true"></span>
    <span class="visually-hidden">Next</span>
  </button>
</div>
```

Rules:

- `.active` is required on exactly one `.carousel-item`; the outer element needs an `id` for controls/indicators.
- **Initialization**: non-autoplay carousels must be constructed manually (`new bootstrap.Carousel(el)`) or touch/swipe listeners aren't registered until a control is clicked. Autoplay: `data-bs-ride="carousel"` (auto-inits, cycles on load) vs `data-bs-ride="true"` (cycles after first manual interaction) — don't also construct those manually.
- Crossfade instead of slide: `.carousel-fade`. Dark controls/captions: `data-bs-theme="dark"` on the carousel (`.carousel-dark` deprecated).
- Options (data attrs or constructor): `interval` (ms, per-item override via `data-bs-interval` on the item), `touch`, `wrap`, `keyboard`, `pause` (`'hover'`|`false`).
- JS methods: `.cycle()/.pause()/.prev()/.next()/.to(i)`. Events on the carousel: `slide.bs.carousel` (cancelable) / `slid.bs.carousel`, both with `direction`, `from`, `to`.
- Nested carousels are unsupported. Autoplaying media carousels raise a11y concerns — prefer user-initiated cycling.
