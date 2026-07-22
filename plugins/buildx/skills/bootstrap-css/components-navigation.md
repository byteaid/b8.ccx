# Components — Navigation

Navbar, navs & tabs (Tab plugin), dropdowns (Popper), scrollspy.

## Navbar

Canonical responsive navbar (collapses below `lg`, hamburger toggler):

```html
<nav class="navbar navbar-expand-lg bg-body-tertiary">
  <div class="container-fluid">
    <a class="navbar-brand" href="#">Brand</a>
    <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#mainNav"
            aria-controls="mainNav" aria-expanded="false" aria-label="Toggle navigation">
      <span class="navbar-toggler-icon"></span>
    </button>
    <div class="collapse navbar-collapse" id="mainNav">
      <ul class="navbar-nav me-auto mb-2 mb-lg-0">
        <li class="nav-item"><a class="nav-link active" aria-current="page" href="#">Home</a></li>
        <li class="nav-item"><a class="nav-link" href="#">Features</a></li>
        <li class="nav-item dropdown">
          <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown" aria-expanded="false">More</a>
          <ul class="dropdown-menu">
            <li><a class="dropdown-item" href="#">Action</a></li>
          </ul>
        </li>
      </ul>
      <form class="d-flex" role="search">
        <input class="form-control me-2" type="search" placeholder="Search" aria-label="Search">
        <button class="btn btn-outline-success" type="submit">Search</button>
      </form>
    </div>
  </div>
</nav>
```

Rules:

- `.navbar` + `.navbar-expand{-sm|-md|-lg|-xl|-xxl}` (omit `-bp` = never collapse; omit the class = always collapsed). Responsive collapsing rides the Collapse plugin.
- Wrap contents in a `.container*` — controls horizontal width and toggler alignment.
- Color scheme: background utility (`bg-body-tertiary`, `bg-primary`, custom) + `data-bs-theme="dark"` **on the navbar** for light-on-dark text (v5.3 way; `.navbar-dark` is deprecated).
- Placement: `.fixed-top`, `.fixed-bottom` (add body padding to compensate), `.sticky-top`.
- Brand image: `<img>` inside `.navbar-brand` with explicit width/height + `.d-inline-block .align-text-top`.
- Text-only: `.navbar-text`. Scrollable menu region: `.navbar-nav-scroll` + `style="--bs-scroll-height: 100px"`.
- **Offcanvas navbar**: replace `.collapse.navbar-collapse` with a `.offcanvas .offcanvas-end` block (with `.offcanvas-header`/`.offcanvas-body`) and point the toggler at it with `data-bs-toggle="offcanvas"`; `.navbar-expand-lg` still expands it inline from `lg` up.

## Navs, tabs, pills

Base nav (list markup preferred; flex-based, no active styling by itself):

```html
<ul class="nav nav-tabs">
  <li class="nav-item"><a class="nav-link active" aria-current="page" href="#">Active</a></li>
  <li class="nav-item"><a class="nav-link" href="#">Link</a></li>
  <li class="nav-item"><a class="nav-link disabled" aria-disabled="true">Disabled</a></li>
</ul>
```

- Styles: `.nav-tabs` · `.nav-pills` · `.nav-underline` (v5.3). Layout: `.justify-content-center/end`, `.flex-column` (vertical), `.nav-fill` (proportional fill), `.nav-justified` (equal width). Works on `<nav>` with plain `.nav-link` children too.

### Tab plugin (dynamic panes)

```html
<ul class="nav nav-tabs" role="tablist">
  <li class="nav-item" role="presentation">
    <button class="nav-link active" id="home-tab" data-bs-toggle="tab" data-bs-target="#home"
            type="button" role="tab" aria-controls="home" aria-selected="true">Home</button>
  </li>
  <li class="nav-item" role="presentation">
    <button class="nav-link" id="profile-tab" data-bs-toggle="tab" data-bs-target="#profile"
            type="button" role="tab" aria-controls="profile" aria-selected="false">Profile</button>
  </li>
</ul>
<div class="tab-content">
  <div class="tab-pane fade show active" id="home" role="tabpanel" aria-labelledby="home-tab" tabindex="0">…</div>
  <div class="tab-pane fade" id="profile" role="tabpanel" aria-labelledby="profile-tab" tabindex="0">…</div>
</div>
```

- `data-bs-toggle="tab"` (or `"pill"` / `"list"` for pills and list-groups — same plugin). Initial pane needs `.show.active` and its trigger `.active`.
- JS: `bootstrap.Tab.getOrCreateInstance(triggerEl).show()`. Events on the trigger: `show/shown/hide/hidden.bs.tab` (with `e.target` incoming, `e.relatedTarget` outgoing).
- The ARIA roles above (`tablist/tab/tabpanel`, `aria-selected`) are part of the contract — the plugin keeps them in sync.

## Dropdowns

Requires Popper (bundle or separate). Single button dropdown:

```html
<div class="dropdown">
  <button class="btn btn-secondary dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false">
    Menu
  </button>
  <ul class="dropdown-menu">
    <li><a class="dropdown-item" href="#">Action</a></li>
    <li><a class="dropdown-item active" aria-current="true" href="#">Active</a></li>
    <li><a class="dropdown-item disabled" aria-disabled="true">Disabled</a></li>
    <li><hr class="dropdown-divider"></li>
    <li><h6 class="dropdown-header">Header</h6></li>
    <li><a class="dropdown-item" href="#">Separated</a></li>
  </ul>
</div>
```

- Wrapper direction classes: `.dropdown` (down) · `.dropup` · `.dropend` · `.dropstart` · `.dropdown-center` / `.dropup-center`.
- Split button: normal `.btn` + a second `.btn .dropdown-toggle .dropdown-toggle-split` carrying the toggle.
- Menu alignment: `.dropdown-menu-end` (right-align), responsive variants `.dropdown-menu-lg-end` etc. — responsive alignment requires `data-bs-display="static"` on the toggler (disables Popper positioning).
- Menu content can be text (`.dropdown-item-text`, padded wrappers) or forms (`<form class="px-4 py-3">`).
- Dark menu: `data-bs-theme="dark"` on the parent (`.dropdown-menu-dark` deprecated).
- Options (data attrs on the toggler): `data-bs-offset="10,20"`, `data-bs-boundary`, `data-bs-reference="parent"` (split buttons), `data-bs-auto-close="true|inside|outside|false"`, `data-bs-popper-config`.
- JS: `bootstrap.Dropdown.getOrCreateInstance(toggler)` — `.toggle()/.show()/.hide()/.update()/.dispose()`. Events `show/shown/hide/hidden.bs.dropdown` fire on the toggler; `e.clickEvent` present on click-triggered ones.
- Hover-open is not supported; dropdowns are click-driven by design.

## Scrollspy

Highlights nav links matching the scrolled section. Attach to the scrolling element (body or a scrollable div):

```html
<nav id="toc" class="navbar bg-body-tertiary px-3">
  <ul class="nav nav-pills">
    <li class="nav-item"><a class="nav-link" href="#s1">First</a></li>
    <li class="nav-item"><a class="nav-link" href="#s2">Second</a></li>
  </ul>
</nav>
<div data-bs-spy="scroll" data-bs-target="#toc" data-bs-smooth-scroll="true"
     class="scrollspy-example" tabindex="0" style="height: 300px; overflow-y: auto;">
  <h4 id="s1">First</h4><p>…</p>
  <h4 id="s2">Second</h4><p>…</p>
</div>
```

- Requirements: the target nav needs resolvable `href="#id"` anchors into the spied region; the spied element must actually scroll (`overflow-y: auto` + height) and be focusable (`tabindex="0"`); nested navs highlight parents automatically.
- v5.3 uses IntersectionObserver; tune with `data-bs-root-margin` / `data-bs-threshold`. `data-bs-smooth-scroll="true"` animates anchor jumps.
- After adding/removing DOM inside the spied area call `bootstrap.ScrollSpy.getInstance(el).refresh()`.
- Event: `activate.bs.scrollspy` on the spied element.
