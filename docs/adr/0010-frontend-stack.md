# ADR-10: Nx workspace, standalone Angular, PrimeNG behind a wrapper

- **Status:** Accepted (2026-09-23)
- **Context:** Architecture §11

## Decision

- **Nx monorepo** with one application (`hrms-web`) and libraries split `core` / `shared` /
  `features`, with lint-enforced boundaries between them.
- **Standalone components, signals and the new control flow.** No NgModules. The "core module,
  shared module, feature modules" structure from the requirements is expressed as `provideCore()`,
  shared libraries and lazily loaded feature routes.
- **PrimeNG** as the component kit, used only through wrappers in `shared/ui` and
  `shared/data-table`.
- **NgRx SignalStore** per feature; global stores only for session, tenant, permissions and
  notifications.
- **No micro-frontends** for now.

## Why

**PrimeNG over Angular Material + AG Grid.** An HRMS is mostly dense tables, filters and forms.
PrimeNG ships the lazy DataTable, TreeTable, OrgChart, Calendar and Timeline that this product needs
on day one; assembling the same set from Material plus AG Grid is weeks of work, and AG Grid's
useful features are paid. The trade-off is accessibility: Material's baseline is better, and PrimeNG
has gaps against the WCAG 2.2 AA target.

That trade-off is managed rather than accepted: **features never import PrimeNG directly.** They use
`shared/ui` and `shared/data-table`, so an accessibility fix lands in one wrapper instead of two
hundred call sites, each wrapper carries an axe-core check, and swapping the kit later is a
rewrite of one library rather than of the application.

**Nx over a plain Angular CLI workspace** for the enforced module boundaries and affected-only
builds. With twenty feature areas, "did anyone import payroll from the employee feature" has to be a
lint rule, not a review habit.

**No micro-frontends.** Lazy-loaded Nx libraries give the same code separation without the runtime
and versioning cost. Revisit only if separate teams need to deploy the front end independently.

## Consequences

- PrimeNG's theme tokens drive tenant branding through CSS variables, within contrast guardrails so
  a customer's brand colour cannot make the UI unreadable.
- Accessibility must be tested continuously, not audited once at the end.
- The wrapper layer is extra code that reviewers must insist on: a direct `p-table` in a feature is
  a review rejection, and lint rules back that up.
