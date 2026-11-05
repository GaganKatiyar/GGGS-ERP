# Design system

The Nuts and Bolts front end has one design system, defined in global partials and a small set of
shared components. Screens build from those pieces and do not restyle them. The visual direction
is called "depth": surfaces float slightly above the page on soft layered shadows and rise
toward the pointer. Frosted glass is limited to the chrome that floats over content. Gradients
are used for accents only, and motion is brief and always optional.

## Where it lives

| What | Where |
|---|---|
| Tokens (colour, elevation, gradients, glass, motion, spacing, type, z-index) | `frontend/apps/hrms-web/src/styles/_tokens.scss` |
| Element defaults (in `@layer gggs-elements`), focus, reduced motion | `styles/_base.scss` |
| Buttons, forms, tables, panels, alerts and badges | `styles/_buttons.scss`, `_forms.scss`, `_table.scss`, `_layout.scss`, `_feedback.scss`, `_xp.scss` |
| Depth helpers: `.elevated`, `.elevated--interactive`, `.glass`, `.rise-in`; the signed-out layout `.auth` (sign-in, invitation) | `styles/_depth.scss` |
| App shell: rail, app launcher, header, module tabs | `styles/_shell.scss` (global, because it exceeds the per-component style budget) |
| PrimeNG dialogs, drawers, toasts and tags | `styles/_overlays.scss` |
| PrimeNG preset bridged to the tokens | `apps/hrms-web/src/app/app.config.ts` (`GggsPreset`) |
| Shared components | `libs/shared/ui` (`gggs-kpi-card`, `gggs-page-header`, `gggs-empty-state`, `gggs-chart`, `gggs-form-dialog`, `gggs-drawer`, `gggsTilt`), `libs/shared/data-table` |

## Colour

The palette is the GGGS brand, taken from the company site so the two read as one family.

| Token | Light | Dark | Use |
|---|---|---|---|
| `--gggs-color-brand` | `#16202B` charcoal | `#0B1117` | Rail, start of the brand gradient |
| `--gggs-color-primary` | `#D81324` brand red | `#FF5A63` coral | Actions, links, the current page and tab |
| `--gggs-color-primary-subtle` | `#FEF0F1` | `#2A1A1F` | Selected rows, icon tiles; red text on it passes AA |
| `--gggs-color-accent` | `#B00F1D` | `#FF7A82` | Accent text; meets AA on white |
| `--gggs-color-accent-bright` | `#FF5A63` coral | `#FF5A63` | Gradients and chart fills |
| `--gggs-color-focus` | `#16202B` | `#C9D2DC` | Focus rings and focused field borders |
| `--gggs-color-surface` / `-raised` | `#F7F9FA` / `#FFFFFF` | `#10161D` / `#16202B` | Page background / cards |
| `--gggs-color-text` / `-muted` | `#1B2733` / `#55677A` | `#E8EDF2` / `#9AA9B9` | Body text / secondary text |
| `--gggs-color-border` | `#DDE3E9` | `#26323F` | Hairlines |

**Focus is charcoal, not red.** With red as the action colour, a red ring on the field someone
is typing in would read as a validation error, so focus uses `--gggs-color-focus`. Red is for
actions, the current page and real errors. Errors keep their own darker red
(`--gggs-color-danger`, `#B3261E`) and always come with a message or label, so they are never
told apart by colour alone.

The success, warning and danger tokens keep their AA-tuned values for text. The site has no dark
theme, so the dark palette is derived from it: charcoal surfaces, with coral standing in for the
red, which is too dark to read on charcoal.

**Type.** Headings (`h1`–`h4`, dialog and drawer titles) use the site's Plus Jakarta Sans
(`--gggs-font-family-display`, self-hosted from `@fontsource-variable/plus-jakarta-sans`). Body
text, figures and controls stay Inter.

Dark mode is the `.gggs-dark` class on `<html>`. `public/theme-boot.js` applies the saved theme
before Angular starts, so a dark-theme user never sees a light flash. It is a file rather than an
inline script because the CSP allows `'self'` only. `ThemeStore` (`libs/core/layout`) takes over
once the shell loads.

**Palettes.** The palette button in the header (next to the light/dark switch) lets each person
choose a colour palette. The choice applies at once and is kept per browser in
`localStorage['gggs-palette']`.

| Palette | Class on `<html>` | Looks like |
|---|---|---|
| GGGS brand (default) | none | Charcoal menu, brand red |
| GGGS brand, light menu | `gggs-palette-brand-light-menu` | White menu with charcoal icons in light mode; the dark menu in dark mode |
| Classic blue | `gggs-palette-classic` | Navy menu, electric blue, cyan accent; Inter headings |

Each palette is a block of token overrides at the end of `_tokens.scss`, in light and dark. To add
one, add its key and label to `PALETTES` in `theme.ts`, add a matching `gggs-palette-<key>` block,
and add the key to `public/theme-boot.js`. Rail and launcher styles take their translucent fills
and focus outline from `--gggs-color-nav-overlay` and `--gggs-color-nav-focus`, and the text on the
active tile from `--gggs-color-nav-on-active`. Use these rather than writing white directly, so
the rail works whether it is dark or light. Charts recolour by themselves, because they re-read
the tokens when a class on `<html>` changes.

## Elevation

Elevation comes in five levels. Each level is a tight contact shadow plus a soft ambient one,
tinted charcoal. `--gggs-shadow-highlight` is the lit top edge of a raised surface.

| Level | Token | Used for |
|---|---|---|
| 1 | `--gggs-shadow-1` | Resting cards, panels, tables, buttons |
| 2 | `--gggs-shadow-2` | A button under the pointer |
| 3 | `--gggs-shadow-3` | A card under the pointer |
| 4 | `--gggs-shadow-4` | Popovers, menus, a card being dragged, toasts |
| 5 | `--gggs-shadow-5` | Dialogs and drawers |

The older names `--gggs-shadow-xs`, `-sm`, `-md` and `-lg` are aliases for levels 1 to 4.

**Hover lift.** Cards (`gggs-kpi-card`, metric panels, `a.panel`, link and button `.xp__card`s)
rise by `--gggs-lift` (2px) to level 3 under the pointer. Raised buttons rise by half that amount
and press down on click; solid PrimeNG buttons (`gggs-button`) do the same. Ghost, link, text
and outlined buttons stay flat. Table rows never lift; a hovered row
gets a tint and an accent mark at its edge instead.

## Glass

`--gggs-glass-bg`, `--gggs-glass-border`, `--gggs-glass-blur`, or the `.glass` helper. Glass is
used only for chrome that floats over content: the header, the module tabs and the dimmed mask
behind dialogs. It is never used on cards, forms or tables, where the page
showing through would cost legibility.

Do not put glass inside the header. The header has its own `backdrop-filter`, which makes it a
*backdrop root*: a blurred element inside it can only blur the header itself, so the page behind
would show through sharp. This is why the user menu and the search dropdown are opaque, at
elevation 4.

Every glass surface has an opaque fallback in `@supports not (backdrop-filter: …)`.

## Gradients

| Token | Where |
|---|---|
| `--gggs-gradient-brand` (charcoal → deep red → red, the site's hero) | Logo, the cover band on My profile |
| `--gggs-gradient-primary` | Primary button face. In dark mode it runs the other way, so the lighter end is always on top. |
| `--gggs-gradient-accent` (red → coral) | Tab underline, KPI hover edge, progress and breakdown bars |
| `--gggs-gradient-tile` | Icon tiles on KPI cards and empty states, avatar (behind initials, when there is no photo) |
| `--gggs-gradient-nav` | Rail background |
| `--gggs-gradient-wash` | One faint light at the top of each page, which scrolls away with the content |

No gradient sits behind body text.

## Motion

- Tokens: `--gggs-ease-out`, `--gggs-duration-fast` (120ms), `--gggs-duration` (180ms) and
  `--gggs-duration-slow` (260ms). `--gggs-transition` and `--gggs-transition-fast` combine a
  duration with the easing. They are complete values, so never append another easing after them.
- Only transform, opacity, box-shadow and colours animate.
- `.rise-in` fades a block up once, when it first renders. To stagger siblings, set `--i` on each
  (`[style.--i]="index"`). Page headers and the home dashboard use it.
- Reduced motion: `_base.scss` sets `--gggs-lift` to 0, removes entrance delays and shortens every
  transition to effectively nothing. Chart animation and `gggsTilt` check the same preference
  themselves.
- Nothing loops except loading spinners and skeleton shimmer.

## Components

**`gggs-kpi-card`** takes these inputs:

- `label`, `value`, `hint`, `icon`
- `trend` with `trendIsGood`: the arrow sits in a coloured pill, and screen readers hear
  "Up, a decline." or similar
- `series`: recent values, oldest first, drawn as a sparkline in inline SVG with no chart library
- `progress`: 0–100, drawn as a meter

Pass `series` and `progress` only when the page already has those figures; never make up a
number for decoration. The home dashboard takes `series` from a metric's `trend` and the arrow
from its last two points, coloured by the metric's `higherIsBetter`.

**Profile photos.** An employee's photo shows in a circle with `object-fit: cover`; without one,
their initials sit on `--gggs-gradient-tile`. The header avatar and My profile read it from
`MyPhotoStore` (`libs/core/auth`), whose URL carries the photo id, so a new photo is a new URL and
an old one can be cached for good. My profile frames its large photo in a red ring that throws a
glow below it. The browser crops a chosen picture to a centred square at 512 px before uploading it.

**`gggsTilt`** tilts an element up to ±3° toward a fine pointer. Each pointer move schedules at most
one animation frame. It does nothing on touch screens or for users who prefer reduced motion. It
is for a few hero cards, currently the home dashboard's headline row, and should not be put on
list items.

**`gggs-chart`** handles all chart styling. A single line series gets a fill that fades to the
floor; several series are drawn unfilled so none hides another. Points are hollow, the grid is
dashed, tooltips use the tokens, and hovering anywhere in a column shows every series' value
there. Pages pass labels and series only.

**KPI grids.** Use `.xp__kpis` or `.kpi-row`. Do not add a per-feature copy.

**Plain tables sort themselves.** The shell (`libs/core/layout` `table-sort.ts`) makes the column
headers of every plain `<table>` in the page sortable. It sorts the rows already on screen and
makes no request. Clicking a header, or pressing Enter or Space on it, cycles ascending,
descending, then the page's own order. Columns are read as numbers (₹, %, h, days and commas
included), dates or text, with blank cells always last. The header shows `aria-sort` and an
arrow. A table is skipped when it is not safe to reorder: no `<thead>`, more than one body,
merged cells, or inputs in its rows (an editable grid). Header cells are skipped when they are
empty, hold a control, are `.actions`, or carry `data-nosort`. The shared data table sorts on the
server and is not affected.

## Rail and app launcher

Every page starts with the rail, and the « / » tab on the menu's edge opens the full menu over it:

- **Icons:** the rail described below. Resting the pointer on a module's icon, or focusing it, pops
  its pages out beside the rail after about 150 ms. Right steps into the pop-out, and Escape or
  Left returns to the icon.
- **Open menu:** a `--gggs-sidebar-width` panel floating over the header and the page, which keep
  their position and width, so the panel covers the header logo while it is open. It lists every
  module with its name. A module with pages is a disclosure button that opens them, indented,
  beneath it. The current module opens by itself and others stay open once opened. Settings lists
  its pages under category captions (`sectionsOf`). A search at the top filters modules and pages
  (`filterMenu`). The keys work as in a tree: Up and Down move, Right opens a module or steps into
  it, Left closes it or steps back up, and Home and End jump to the ends.

The menu closes when a page is chosen (including the page already showing), on a click outside it,
on Escape, or when keyboard focus leaves it. It is not remembered: reloading starts with the rail.
There is no scrim, so a click on the page closes the menu and also does what it would have done.

Below 48rem the menu is hidden and the header logo opens the launcher. Both forms mark each
module's entry page with `data-nav-module`, which the role audit crawls. The module tabs under the
header stay in both.

The rail shows one icon per module, built from the permission-filtered navigation manifest
(`navigation.ts`). Each icon opens its module on its first page (`entryRoute`); the module's pages
are the tabs under the header. The names stay in the DOM, visually hidden, so every icon keeps its
accessible name, and its `title` shows the name on hover. Arrow keys, Home and End move between
the icons.

The grid button at the top of the rail, or the logo at the start of the header, opens the app
launcher. It is the rail opening out: the same charcoal gradient, `--gggs-launcher-width` wide, over
the rail and a dimmed page (`--gggs-shadow-5`), with every module as a tile and a search. The
search (`searchNavigation`) matches module names, and from two characters also page names, each
with its module. Enter opens the first result. It is a modal dialog: focus starts in the search,
Tab stays inside, and Escape, the back button or a click on the page closes it and returns focus
to what opened it. On a phone the rail is hidden and the header logo is the way into the launcher,
which then fills the width.

## Settings

Every master list and setting lives in one group, **Settings**, the last icon on the rail, rather
than as a tab at the end of each module. Its entries carry a `section` (one of `SETTINGS_SECTIONS`:
Organization, Time & attendance, Payroll & finance, Hiring & onboarding, Exits, Projects, Lookups,
Access & security, Platform). The Settings home (`/settings`) shows a card per section with the
pages the user may open and a search across them; `?section=<key>` scrolls to that card and rings
it. Inside a page, the tabs are its own section's pages only (`moduleTabs(group, entry)`), and the
breadcrumb reads Settings / Section / Page. A module that gave pages up names its section in
`settingsSection`, and its tab strip ends in a gear "Settings" link to that card. Pages keep their
routes when they move, so links and guards are unchanged. The rail, the launcher and the Settings
home all read `VisibleNavigation`, so they always agree on what someone can reach.

## Module tabs

A module with many pages, such as My Space, has more tabs than fit across the screen. The row scrolls
sideways in three ways:

- With the mouse resting within 64px of either end, it glides that way, faster nearer the end, and
  stops when the pointer moves off the edge.
- A vertical mouse wheel scrolls it sideways.
- Touch screens swipe it.

The scrollbar is hidden. Instead, an end with more tabs beyond it fades out. The current page's tab
is scrolled into view after each navigation. Gliding is mouse-only and is off for people who
prefer reduced motion; the wheel still works for them. This is the `gggsEdgeScroll` directive
(`libs/core/layout` `edge-scroll.ts`), which any sideways-scrolling row can reuse.

## Checks

- `npm run stylelint` rejects raw colours. It applies the strict-value rule to the `background`
  shorthand, so write gradients and layered backgrounds as longhands (`background-image`,
  `-size`, `-repeat`, `-attachment`). Inline component styles in `.ts` files are not linted, so
  review them by eye.
- `node tools/ui-audit/audit.mjs --variants=desktop,mobile,dark` (with the app on :4200) crawls
  every route. It checks for overflow, clipped text, console errors, and dialogs that fall off
  screen, and runs axe. Run it after any change to these partials.
- Component styles have a 4 kB budget. Styles that every screen needs go in a global partial.
