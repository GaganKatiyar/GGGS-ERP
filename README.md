# PeopleOS

A multi-tenant, AI-ready HRMS, project management and resource management platform, aimed first
at IT, software and consulting companies. It is an Angular front end over **one modular monolith**
(an ASP.NET Core API host and a worker host) and **one SQL Server database**, deployable to Azure
and to a customer's own servers (IIS or Docker).

Start with:

- [docs/architecture/peopleos-target.md](docs/architecture/peopleos-target.md): the target
  architecture and module catalogue.
- [docs/adr/0012-modular-monolith.md](docs/adr/0012-modular-monolith.md): why it is one monolith.
- [docs/roadmap.md](docs/roadmap.md): what ships in which milestone.
- [docs/architecture/existing-vs-target.md](docs/architecture/existing-vs-target.md): the audit of
  the earlier GGGS HRMS codebase this was rebuilt from.

The tenancy, isolation and security design in
[docs/architecture/architecture.md](docs/architecture/architecture.md) (§3, §6.2–6.3, §8, §9) still
applies.

**Status: M8 (hiring: workforce planning, recruitment, onboarding) complete.**

- Platform (tenants, plans, subscriptions, settings, audit log) and Identity (users, roles,
  permissions, browser sessions, the OIDC token server) run as modules in a single API host.
- A single worker host delivers integration events in-process from the shared outbox and runs
  Hangfire jobs.
- Core HR: master data, the organization structure (companies through positions, with effective
  dating and a department tree), and employees: hire, Employee 360, transfers and promotions,
  confirmation, notice and exit, family, encrypted statutory and bank details, history, org chart,
  and CSV import with a validation preview.
- Platform services: approval workflows (sequential stages, parallel approvers, conditions, SLA
  escalation, delegation, send back and resubmit) with an approvals inbox; notifications in-app and
  by email through templates; versioned documents with generated letters and click-to-sign; and
  company policies with approval-gated publishing and acknowledgements.
- Time: shifts and rosters with swaps, attendance from web punches and devices with
  regularization, leave types and policies on a ledger (accrual job, carry-forward, probation,
  notice and sandwich rules), holiday calendars by location, and self-service and team views.
- Delivery: clients and contacts; contracts with approval-gated activation, expiry and renewal
  reminders; projects with lifecycle, team, phases, tasks on a Kanban board, dependencies,
  milestones, sprints (burndown, velocity), requirements, change requests through approval, the
  RAID log, meetings, a calendar, documents, and health judged on configurable thresholds.
- Resourcing: a skill catalogue with employee skills, targets and certifications (self-rated and
  manager-assessed) and a skill matrix; resource profiles with rates and capacity; an allocation
  calendar with over-allocation flagged, the bench, and release and approval-gated transfer between
  projects; resource requests with ranked matching (skills, availability, experience, location,
  cost) and a shortlist; capacity and skill demand against supply over the coming months; weekly
  timesheets checked against configurable rules, approved through the workflow, reopened for
  correction and closed by a period lock, with approved hours posted to tasks; and utilization by
  person, project, department, manager or location.
- Money: a compliance centre with versioned statutory rules (EPF, ESI, professional tax by state,
  income tax in both regimes, gratuity, labour welfare - seeded for India, edited by publishing new
  versions), a filing calendar with reminders, and licences and certificates watched for expiry;
  payroll with formula-driven components and structures, effective-dated salaries, variable inputs
  (including overtime from timesheets), Form 12BB declarations, monthly runs approved through the
  workflow and finalized into payslips that keep the rule versions they used, and the bank file,
  EPF ECR, ESI and PT returns, journal and Form 16 Part B; benefits with employee and employer
  shares, dependents and automatic enrollment; compensation bands, review cycles, revisions applied
  to payroll, variable pay paid through payroll and equity vesting; and project financials - budget,
  rate cards, costs, invoices, revenue by billing type, margin and forecast.
- Hiring: yearly workforce plans by department, location and job against live headcount, approved
  through the workflow; manpower requests checked against the plan and approved into requisitions;
  postings on the internal job board; one candidate profile per person (found again by email or
  mobile), sourced directly, through agencies or by employee referral; a pipeline board ranked by a
  transparent rule-based match (people make every decision); interviews with panels who give
  scorecard feedback from self-service; background verification; offers approved through the
  workflow, sent and accepted; and a hire that creates the employee, records the starting salary in
  payroll and pays the referrer's bonus after the qualifying period. Recruitment analytics cover time
  to hire, cost per hire, offer acceptance, sources and recruiters. Onboarding starts on hire from the
  best-fitting configurable checklist, with tasks owned by HR, IT, admin, the manager, a buddy or
  the joiner, due relative to joining (pre-boarding included), reminded when overdue.
- The frontend has the PeopleOS shell, a shared UI kit (KPI cards, charts, drawers, form dialogs,
  filter bar, timeline, tabs, an upgraded data table with column chooser and CSV export), and the
  Core HR and administration screens. Unbuilt modules stay hidden in the navigation.
- 591 backend tests pass, including architecture rules for module boundaries, a tenant-isolation
  suite against a real SQL Server, and end-to-end flows that run the worker's event delivery.
- Talent (goals, performance, learning, career) is next (M9).

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.400+ | Pinned in `global.json` |
| SQL Server | 2019+ | A local instance, or Docker, or set `GGGS_TEST_SQL` |
| Node.js | 22+ | For the Angular workspace |
| Docker | optional | Only needed if you have no local SQL Server |

## Getting started

```bash
dotnet build backend/Gggs.slnx                      # build everything
pwsh tools/run-tests.ps1                             # build and run every test project in the solution
pwsh tools/run-tests.ps1 -SkipIntegration           # skip the suites that need SQL Server
dotnet run --project tools/DevSeed                   # create the database and a demo tenant
dotnet run --project backend/Host/Gggs.Api           # the API on http://localhost:5100
dotnet run --project backend/Host/Gggs.Worker        # background delivery and jobs
```

To sign in through the Angular app, see
[docs/runbooks/local-dev-login.md](docs/runbooks/local-dev-login.md): one seed command and three
processes, with no broker or cache required.

### Why tests are run through a script

`dotnet test` is not used. xunit v3 runs on Microsoft.Testing.Platform, and on this SDK
(10.0.401, MTP 2.4.0) `dotnet test` reports "Zero tests ran" while the same assembly runs its full
suite when executed directly. `tools/run-tests.ps1` runs the test executables listed in
`backend/Gggs.slnx`. Revisit when the SDK and xunit line up again.

## Repository layout

```
backend/
  BuildingBlocks/     shared libraries the modules and hosts compose (see below)
  Data/Gggs.Data/     AppDbContext - the one database context - and its conventions
  Data/Gggs.Data.Migrations/   the single migration history
  Host/Gggs.Api/      the web host: API, browser sessions, OIDC, SPA
  Host/Gggs.Worker/   the background host: outbox delivery and Hangfire jobs
  Host/Gggs.Composition/       ModuleCatalog - the one list of modules
  Modules/<Name>/     one folder per business module: Gggs.Modules.<Name> (internals)
                      and Gggs.Modules.<Name>.Contracts (its only public surface)
frontend/             Angular (Nx) workspace
database/             RLS scripts, seed data, compliance packs, maintenance jobs
tests/                API integration, architecture, tenant-isolation and unit suites
infra/                Bicep, Docker, on-prem release bundle, operational scripts
docs/                 architecture, ADRs, roadmap, runbooks
tools/                DevSeed and the test runner
```

### Modules

A module owns its SQL schema(s) and is wired through `IModule`: services for both hosts, API-only
services and endpoints, and worker-only consumers and jobs. Modules call each other only through
`.Contracts` interfaces and integration events, never through each other's internals; the
architecture tests fail the build otherwise. Adding a module is one line in `ModuleCatalog`.

| Module | Schema | Owns |
|---|---|---|
| Platform | `platform` | Tenants, plans, subscriptions, tenant settings, the audit log |
| Identity | `iam`, `oidc` | Users, roles, permissions, platform staff, browser sessions, the token server |

### Building blocks

| Project | What it owns |
|---|---|
| `Domain` | Entity bases, sequential GUID keys, domain events, domain exceptions |
| `MultiTenancy` | `ITenantContext`, tenant middleware, tenant registry, platform scope |
| `Application` | `ICurrentUser`, `IUnitOfWork`, paging contracts |
| `Security` | Permission and feature catalogs, `[RequirePermission]`, data scopes, step-up authentication |
| `Persistence` | `GggsDbContext`, tenant/audit interceptors, model-driven RLS, Dapper factory |
| `Web` | Error envelope, correlation ids, deny-by-default endpoint validation, `IModule` |
| `Caching` | Tenant-scoped cache over HybridCache; no raw-key API exists |
| `Files` | `IFileStorage`: Azure Blob (Cloud) and file share (OnPremise) |
| `Secrets` | `ISecretStore`: Key Vault (Cloud) and an encrypted local file (OnPremise) |
| `Observability` | Serilog, OpenTelemetry over OTLP, health checks |
| `Jobs` | Hangfire with the tenant carried from enqueue to execution, plus per-tenant fan-out |
| `Messaging` | Integration events, transactional outbox and inbox, the in-process dispatcher |

### Deployment profiles

The platform ships to Azure **and** to customer-run servers from one codebase.
`Gggs:Platform:DeploymentProfile` (`Cloud` | `OnPremise`) selects the adapters at startup, and
**application code never references a cloud vendor SDK**; `DeploymentProfileRuleTests` fails the
build if it does. See [ADR-11](docs/adr/0011-deployment-profiles.md) and [infra/README.md](infra/README.md).

Local development runs the **OnPremise** profile on purpose: those adapters get exercised daily, and
nobody needs Azure credentials to start the application.

## Frontend

An Nx workspace (`frontend/`) with Angular standalone components, signals and PrimeNG.

```bash
cd frontend
npm ci
npx nx serve hrms-web                      # dev server, proxying /api and /bff to :5100
npx nx run-many -t lint test build --all   # the full gate
```

| Library | Holds |
|---|---|
| `@gggs/core/auth` | Session loaded from `/bff/user`, `authGuard` |
| `@gggs/core/permissions` | `PermissionStore`, `*gggsHasPermission`, `permissionGuard` |
| `@gggs/core/tenant` | Tenant context and contrast-checked branding |
| `@gggs/core/http` | Credentials, CSRF, correlation-id and error interceptors (`ApiError`) |
| `@gggs/core/layout` | Shell and the navigation manifest |
| `@gggs/shared/ui` | Page header, empty state and other primitives |
| `@gggs/shared/data-table` | Server-paged table wrapping PrimeNG |
| `@gggs/shared/models` | API and session contracts, including the error envelope |
| `@gggs/features/*` | One lazily loaded library per feature area |

**Rules the lint enforces** (`@nx/enforce-module-boundaries`, see [ADR-10](docs/adr/0010-frontend-stack.md)):

- Features may use `core` and `shared`, **never another feature**. Two features needing the same
  thing promote it to `shared`.
- `shared` may not reach back into `core` or a feature.
- Only the app composes features together.
- Features never import PrimeNG directly. They use `shared/ui` and `shared/data-table`, so an
  accessibility fix lands in one wrapper rather than in every screen.

**Permission-gated UI is not a security control.** `*gggsHasPermission` and `permissionGuard` decide
what is worth showing; the API enforces the same permission on every call. A hidden button is one
devtools click from being visible.

The sidebar is generated from `NAVIGATION` in `@gggs/core/layout`, filtered by the user's
permissions, the tenant's licensed features and whether the module is built yet. The manifest holds
the whole PeopleOS tree, and unbuilt entries carry `planned` and their milestone. Shipping a screen
means adding its route and deleting that flag; there is no separate menu to keep in step.

## The rules that matter

**1. The tenant comes from the credential, never from the client.** No request DTO has a
`TenantId`; an architecture test enforces it. Anything a caller sends that names a tenant is ignored
and logged.

**2. Isolation is layered, and each layer assumes the others may fail.**

| Layer | Mechanism |
|---|---|
| Credential | `tid` claim, from the user record, in the session or token |
| Middleware | `TenantContextMiddleware` resolves and checks tenant status |
| EF Core | Global query filters applied by convention to every `ITenantOwned` entity |
| Interceptor | Stamps `TenantId` on insert; refuses cross-tenant reads and writes |
| SQL Server | Row-level security filter **and** block predicates, via `SESSION_CONTEXT` |
| Schema | Composite `(TenantId, Id)` keys |

The database layers cover hand-written SQL, Dapper and any bug in the layers above. They are
verified in `tests/Gggs.TenantIsolation.Tests` against a real SQL Server, including that raw SQL
with no tenant predicate returns **zero** rows and that the policy covers exactly the tenant-owned
tables.

**3. New tables are isolated and auditable by default.** Derive from `TenantAggregateRoot`, and the
conventions add the tenant filter, soft delete, audit columns, rowversion, the clustered
`(TenantId, Id)` index and the row-level-security annotation. In the migration that adds the table,
call `migrationBuilder.SyncTenantRowLevelSecurity(TargetModel)`: the policy's table list is derived
from the model, so it cannot leave a table out.

**4. Endpoints are deny-by-default.** Every endpoint declares `[RequirePermission(...)]` or
`[AllowAnonymous]`. The host refuses to start otherwise.

**5. Out-of-scope records return 404, not 403.** A 403 would confirm the record exists.

**6. Modules meet only at their contracts.** One process makes reaching into another module easy,
and the architecture tests make it a build failure.

## Conventions

- Permissions: `module.resource.action` (`leave.request.approve`), declared in `Permissions` and
  never renamed once released. Features (`Features`) follow the same rule.
- Database: one schema per module, PascalCase singular tables, `PK_`/`UX_`/`IX_`/`FK_` prefixes,
  UTC `datetime2(3)`, `CreatedBy`/`CreatedDate`/`ModifiedBy`/`ModifiedDate`/`IsDeleted`/`IsActive`.
- API: `/api/v{n}/{module}/{resource}`, plural kebab-case, camelCase JSON, task-based endpoints for
  state changes (`POST .../{id}/cancel`). Errors use the envelope
  `{ success, code, message, errors[], traceId }` inside an RFC 9457 problem document.
- C#: file-scoped namespaces, `sealed` by default, `Async` suffix, records for DTOs.

## Contributing

Every pull request must pass: build, unit tests, architecture tests, the tenant-isolation suite,
the API integration suite, `dotnet format --verify-no-changes`, the frontend gate, and the
vulnerable-package check. A change that adds a tenant-owned table, an endpoint or a permission
should also add the test that proves it is isolated.
