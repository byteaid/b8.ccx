# Utilities & Helpers

Class notation for spacing, display, flex, text, colors, borders, sizing, position, misc; then the helper classes. All utilities carry `!important` and (where marked ⓡ) accept breakpoint infixes: `{util}-{bp}-{value}`.

## Spacing ⓡ

Format: `{property}{sides}-{size}` / `{property}{sides}-{bp}-{size}`.

- Property: `m` margin · `p` padding.
- Sides: `t` top · `b` bottom · `s` start (left in LTR) · `e` end · `x` horizontal · `y` vertical · *(blank)* all four.
- Size: `0` · `1` (.25rem) · `2` (.5rem) · `3` (1rem) · `4` (1.5rem) · `5` (3rem) · `auto` (margin only). Negative margins: `n` prefix (`.mt-n1`) — disabled by default, enable `$enable-negative-margins`.
- `mx-auto` centers fixed-width block elements. Gap (grid/flex containers): `.gap-{0..5}`, `.row-gap-*`, `.column-gap-*` ⓡ.

## Display ⓡ

`.d-{value}`: `none | inline | inline-block | block | grid | inline-grid | table | table-row | table-cell | flex | inline-flex`.

- Show/hide per tier: `d-none d-md-block` (hidden below md) · `d-md-none` (hidden from md up).
- Print: `.d-print-{value}` (e.g. `d-print-none`).

## Flex ⓡ (all responsive)

| Concern | Classes |
|---|---|
| Container | `d-flex`, `d-inline-flex` |
| Direction | `flex-row`, `flex-row-reverse`, `flex-column`, `flex-column-reverse` |
| Justify (main axis) | `justify-content-{start\|end\|center\|between\|around\|evenly}` |
| Align items (cross) | `align-items-{start\|end\|center\|baseline\|stretch}` |
| Align self | `align-self-{...same...}` |
| Align content (multi-line) | `align-content-{start\|end\|center\|between\|around\|stretch}` |
| Fill / grow / shrink | `flex-fill`, `flex-grow-{0\|1}`, `flex-shrink-{0\|1}` |
| Wrap | `flex-wrap`, `flex-nowrap`, `flex-wrap-reverse` |
| Order | `order-{0..5}`, `order-first`, `order-last` |
| Auto margins | `ms-auto` / `me-auto` push siblings apart |

## Text ⓡ (alignment only) & fonts

- Align: `text-start`, `text-center`, `text-end` ⓡ.
- Wrap: `.text-wrap`, `.text-nowrap`, `.text-break` (break long strings).
- Transform: `.text-lowercase/.text-uppercase/.text-capitalize`.
- Size: `.fs-{1..6}` (heading scale, not responsive). Weight/style: `.fw-{bold|bolder|semibold|medium|normal|light|lighter}`, `.fst-italic/.fst-normal`. Line height: `.lh-{1|sm|base|lg}`.
- Family: `.font-monospace`. Decoration: `.text-decoration-{underline|line-through|none}`. Reset color: `.text-reset`.

## Colors & background (color-mode aware)

Theme keys: `primary secondary success danger warning info light dark`.

- Text: `.text-{key}`, plus body tokens `.text-body`, `.text-body-secondary` (replaces `.text-muted`), `.text-body-tertiary`, `.text-body-emphasis`, `.text-black/.text-white`. Emphasis (stronger, adaptive): `.text-{key}-emphasis`.
- Background: `.bg-{key}`, subtle adaptive tints `.bg-{key}-subtle`, body layers `.bg-body`, `.bg-body-secondary`, `.bg-body-tertiary`. Gradient overlay: `.bg-gradient`.
- **Combined**: `.text-bg-{key}` sets background + a contrasting foreground in one class — prefer it over pairing `.bg-*`+`.text-*` by hand.
- Opacity variants: `.text-opacity-{25|50|75|100}` and `.bg-opacity-{10|25|50|75|100}` (modify the co-located `.text-*`/`.bg-*` via `--bs-text-opacity`/`--bs-bg-opacity`).
- Rule of thumb: for surfaces that must adapt to dark mode use `bg-body*` / `*-subtle`; for text use `text-body*` / `*-emphasis`. Fixed `.bg-light`/`.bg-dark` do not adapt.

## Borders & radius

- Add: `.border`, `.border-{top|end|bottom|start}`. Remove: `.border-0`, `.border-{side}-0`.
- Color: `.border-{key}` (+ `.border-{key}-subtle` adaptive). Width: `.border-{1..5}`. Opacity: `.border-opacity-{10|25|50|75}`.
- Radius: `.rounded`, `.rounded-{top|end|bottom|start}`, `.rounded-{0..5}`, `.rounded-circle`, `.rounded-pill`.

## Sizing

- Relative to parent: `.w-{25|50|75|100|auto}`, `.h-{...}`; `.mw-100`, `.mh-100`.
- Viewport: `.vw-100`, `.vh-100`, `.min-vw-100`, `.min-vh-100`.

## Position

- Mode: `.position-{static|relative|absolute|fixed|sticky}`.
- Placement: `.top-{0|50|100}`, `.bottom-*`, `.start-*`, `.end-*` (percent of edge offset).
- Centering trick: `.top-50 .start-50 .translate-middle` (or `.translate-middle-x/y`) — canonical for badges pinned on a corner.

## Misc utilities

- Shadows: `.shadow-none`, `.shadow-sm`, `.shadow`, `.shadow-lg`.
- Overflow: `.overflow-{auto|hidden|visible|scroll}` + `-x`/`-y` axes.
- Z-index: `.z-{n1|0|1|2|3}` (utility scale, distinct from the component scale in [components-overlays](components-overlays.md)).
- Object fit ⓡ: `.object-fit-{contain|cover|fill|scale|none}`.
- Opacity: `.opacity-{0|25|50|75|100}`.
- Interactions: `.user-select-{all|auto|none}`, `.pe-none`/`.pe-auto` (pointer-events).
- Visibility (keeps layout space): `.visible`, `.invisible`.
- Float ⓡ: `.float-{start|end|none}` (+ `.clearfix` helper on the parent).
- Vertical align (inline/table cells): `.align-{baseline|top|middle|bottom|text-top|text-bottom}`.
- Links: `.link-{key}` colored links with hover state; `.link-underline`, `.link-underline-{key}`, `.link-underline-opacity-{0..100}`, `.link-offset-{1..3}`; `.link-body-emphasis` adaptive link.

## Helpers (composite single-purpose classes)

| Helper | Use |
|---|---|
| `.ratio .ratio-{1x1\|4x3\|16x9\|21x9}` | Responsive aspect-ratio box for iframes/embeds/video: `<div class="ratio ratio-16x9"><iframe …></iframe></div>`. Custom: `style="--bs-aspect-ratio: 50%"`. |
| `.vstack` / `.hstack` | Shorthand flex stacks (vertical/horizontal) — pair with `.gap-{n}`; `.vr` draws a vertical rule inside an `.hstack`. |
| `.stretched-link` | Makes the containing block (e.g. a `.card`, which is `position: relative`… otherwise add `.position-relative`) fully clickable via its `::after`. One per container. |
| `.visually-hidden` | Hide visually, keep for screen readers. `.visually-hidden-focusable` reappears on focus (skip links). |
| `.text-truncate` | Single-line ellipsis; element needs a bounded width (block or flex child). |
| `.clearfix` | Clear floats on a parent. |
| `.focus-ring` | Opt-in focus ring styling for custom interactive elements; theme via `--bs-focus-ring-{color,width,offset}` or `.focus-ring-{key}`. |
| `.icon-link` | Aligns an inline SVG icon with link text (`gap`, underline handling); `.icon-link-hover` animates the icon on hover. |
| `.fixed-top` / `.fixed-bottom` | Viewport-fixed bars (remember body padding compensation). |
| `.sticky-top` / `.sticky-bottom` (+ `.sticky-{bp}-top`) | `position: sticky` from a breakpoint up. |
| Color & background helper | `.text-bg-{key}` (described above) lives here in the docs. |
| Colored links | `.link-{key}` family (above). |

## When a utility is missing

Don't write a bespoke class next to Bootstrap — extend `$utilities` through the utility API so the new class gets responsive/state/RTL variants generated consistently. See [customize](customize.md) § Utility API.
