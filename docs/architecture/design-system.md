# Design system

The PeopleOS front end has one design system, defined in global partials and a small set of
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
| App shell: rail, flyout, header, module tabs | `styles/_shell.scss` (global, because it exceeds the per-component style budget) |
| PrimeNG dialogs, drawers, toasts and tags | `styles/_overlays.scss` |
| PrimeNG preset bridged to the tokens | `apps/hrms-web/src/app/app.config.ts` (`GggsPreset`) |
| Shared components | `libs/shared/ui` (`gggs-kpi-card`, `gggs-page-header`, `gggs-empty-state`, `gggs-chart`, `gggs-form-dialog`, `gggs-drawer`, `gggsTilt`), `libs/shared/data-table` |

## Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `--gggs-color-brand` | `#142B4A` navy | `#0B1A2E` | Rail, start of the brand gradient |
| `--gggs-color-primary` | `#2563EB` electric blue | `#7AA7FF` | Actions, links, the active state |
| `--gggs-color-accent` | `#0E7490` | `#22D3EE` | Accent text; meets AA on white |
| `--gggs-color-accent-bright` | `#06B6D4` cyan | `#22D3EE` | Gradients, glows and chart fills only. It fails contrast as text. |
| `--gggs-color-surface` / `-raised` | `#F5F7FB` / `#FFFFFF` | `#0F1623` / `#151C2B` | Page background / cards |
| `--gggs-color-text` / `-muted` | `#172033` / `#5B6B80` | `#E6EAF2` / `#9CA8BB` | Body text / secondary text |
| `--gggs-color-border` | `#E2E8F0` | `#263044` | Hairlines |

Muted text is `#5B6B80` rather than slate 500 (`#64748B`), because muted text also sits on the
page background, where slate 500 measures 4.4:1 and fails AA. The success, warning and danger
tokens keep their darker, AA-tuned values for text. The brighter brand shades are used only as
fills and in chart series.

Dark mode is the `.gggs-dark` class on `<html>`. `public/theme-boot.js` applies the saved theme
before Angular starts, so a dark-theme user never sees a light flash. It is a file rather than an
inline script because the CSP allows `'self'` only. `ThemeStore` (`libs/core/layout`) takes over
once the shell loads.

## Elevation

Elevation comes in five levels. Each level is a tight contact shadow plus a soft ambient one,
tinted navy. `--gggs-shadow-highlight` is the lit top edge of a raised surface.

| Level | Token | Used for |
|---|---|---|
| 1 | `--gggs-shadow-1` | Resting cards, panels, tables, buttons |
| 2 | `--gggs-shadow-2` | A button under the pointer |
| 3 | `--gggs-shadow-3` | A card under the pointer |
| 4 | `--gggs-shadow-4` | Popovers, menus, the module flyout, a card being dragged, toasts |
| 5 | `--gggs-shadow-5` | Dialogs and drawers |

The older names `--gggs-shadow-xs`, `-sm`, `-md` and `-lg` are aliases for levels 1 to 4.

**Hover lift.** Cards (`gggs-kpi-card`, metric panels, `a.panel`, link and button `.xp__card`s)
rise by `--gggs-lift` (2px) to level 3 under the pointer. Raised buttons rise by half that amount
and press down on click; solid PrimeNG buttons (`gggs-button`) do the same. Ghost, link, text
and outlined buttons stay flat. Table rows never lift; a hovered row
gets a tint and an accent mark at its edge instead.

## Glass

`--gggs-glass-bg`, `--gggs-glass-border`, `--gggs-glass-blur`, or the `.glass` helper. Glass is
used only for chrome that floats over content: the header, the module tabs, the module flyout,
and the dimmed mask behind dialogs. It is never used on cards, forms or tables, where the page
showing through would cost legibility.

Do not put glass inside the header. The header has its own `backdrop-filter`, which makes it a
*backdrop root*: a blurred element inside it can only blur the header itself, so the page behind
would show through sharp. This is why the user menu and the search dropdown are opaque, at
elevation 4.

Every glass surface has an opaque fallback in `@supports not (backdrop-filter: …)`.

## Gradients

| Token | Where |
|---|---|
| `--gggs-gradient-brand` (navy → blue → cyan) | Logo |
| `--gggs-gradient-primary` | Primary button face. In dark mode it runs the other way, so the lighter end is always on top. |
| `--gggs-gradient-accent` (blue → cyan) | Tab underline, KPI hover edge, progress and breakdown bars |
| `--gggs-gradient-tile` | Icon tiles on KPI cards and empty states, avatar |
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

## Compact rail

The rail can show icons only. The switch is at the foot of the rail, and the choice is kept per
browser in `localStorage['gggs-rail']`. It applies on desktop only; the phone drawer always shows
labels. In compact mode the labels stay in the DOM, visually hidden, so every entry keeps its
accessible name, and each entry's `title` shows the name on hover.

## Checks

- `npm run stylelint` rejects raw colours. It applies the strict-value rule to the `background`
  shorthand, so write gradients and layered backgrounds as longhands (`background-image`,
  `-size`, `-repeat`, `-attachment`). Inline component styles in `.ts` files are not linted, so
  review them by eye.
- `node tools/ui-audit/audit.mjs --variants=desktop,mobile,dark` (with the app on :4200) crawls
  every route. It checks for overflow, clipped text, console errors, and dialogs that fall off
  screen, and runs axe. Run it after any change to these partials.
- Component styles have a 4 kB budget. Styles that every screen needs go in a global partial.
