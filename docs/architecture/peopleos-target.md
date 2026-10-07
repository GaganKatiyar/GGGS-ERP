# PeopleOS: target architecture

PeopleOS is a multi-tenant HRMS, HCM, Project Management, Resource Management and workforce
intelligence platform. Its primary market is IT, software and consulting companies, and it can be
configured for other industries. This document describes **the target**.
[existing-vs-target.md](existing-vs-target.md) records where the code stands today and what happens
to each piece. [ADR-12](../adr/0012-modular-monolith.md) records why it is one monolith.

The tenancy, isolation, security, audit and compliance-pack design in
[architecture.md](architecture.md) (§3, §6.2–6.3, §8, §9, §16, §17) still applies, with one change:
"one database per service" becomes **one database**.

---

## 1. Shape

```
Browser (Angular + PrimeNG, httpOnly session cookie)
   │
   ▼
Gggs.Api  ── one process ────────────────────────────────────────────────┐
   │  /bff/*        session cookie ⇄ server-held tokens                  │
   │  /connect/*    OpenIddict (Identity module)                         │
   │  /api/v1/*     every module's endpoints                             │
   │  /*            SPA static files                                     │
   │                                                                     │
   │  Modules (in-process, call each other only through .Contracts)      │
   │                                                                     │
   ▼                                                                     │
AppDbContext ── one SQL Server database, one schema per module ◄─────────┤
   ▲                                                                     │
   │                                                                     │
Gggs.Worker ── one process: Hangfire jobs + outbox dispatcher ───────────┘
```

### Repository layout

```
backend/
  BuildingBlocks/            cross-cutting libraries (tenancy, security, persistence, messaging, ...)
  Data/Gggs.Data/            AppDbContext, migrations, model-driven RLS, seeding
  Host/Gggs.Api/             web host + BFF
  Host/Gggs.Worker/          background host
  Modules/<Name>/
    Gggs.Modules.<Name>/             internals: Domain/ Application/ Infrastructure/ Endpoints/
    Gggs.Modules.<Name>.Contracts/   public surface: lookup interfaces, DTOs, integration events
frontend/                    Nx workspace: core/*, shared/*, features/<module>
tests/                       architecture, tenant isolation, per-module unit + integration
```

### Module rules (enforced by `tests/Gggs.Architecture.Tests`)

1. A module references BuildingBlocks and **other modules' `.Contracts` only**.
2. `.Contracts` assemblies reference nothing but BuildingBlocks.Domain and BuildingBlocks.Application.
3. Reads across modules go through a contract interface, e.g. `IEmployeeDirectory` or
   `IOrgStructure`. Nobody copies another module's data into their own table.
4. Side effects across modules go through integration events. They are written to
   `messaging.OutboxMessage` in the same transaction and dispatched in-process by the Worker.
5. Every module implements `IModule`:

   ```csharp
   public interface IModule
   {
       string Name { get; }
       void AddServices(IServiceCollection services, IConfiguration configuration);
       void MapEndpoints(IEndpointRouteBuilder endpoints);
   }
   ```

   Both hosts discover modules and call these methods. No host lists a module's repositories by hand.
6. Entity configurations live in the module. `AppDbContext` applies them from every registered
   module assembly.
7. Every `ITenantOwned` entity is covered by the SQL row-level security policy. The policy is
   generated from the EF model, so a new table cannot be forgotten.

---

## 2. Module catalogue

| Group | Module | Schema | API prefix (`/api/v1/...`) | Milestone |
|---|---|---|---|---|
| Platform | Platform (tenant, subscription, features, settings, audit) | `platform` | `tenants`, `settings`, `audit`, `features` | M1 |
| | Identity (users, roles, permissions, OIDC, MFA, SSO) | `iam`, `oidc` | `auth`, `users`, `roles`, `me` | M1 |
| | MasterData | `mdm` | `master-data` | M2 |
| | Workflow | `wf` | `workflows` | M3 |
| | Notification | `ntf` | `notifications` | M3 |
| | Documents | `doc` | `documents` | M3 |
| | Policies | `pol` | `policies` | M3 |
| | Integration | `int` | `integrations` | M11 |
| Core HR | Organization | `org` | `organizations/*` | M2 |
| | Employee (Core HR, Employee 360 aggregation) | `emp` | `employees` | M2 |
| | Workforce (plans, manpower requests) | `wfp` | `workforce` | M8 |
| | Recruitment (ATS) | `rec` | `recruitment` | M8 |
| | Onboarding (pre-boarding included) | `onb` | `onboarding` | M8 |
| | Separation (resignation, clearance, F&F, alumni) | `sep` | `separation` | M10 |
| Time | Attendance | `att` | `attendance` | M4 |
| | Shift (shifts, rosters, swaps) | `shf` | `shifts` | M4 |
| | Leave (types, policies, ledger, holidays) | `lv` | `leave` | M4 |
| | Timesheet | `ts` | `timesheets` | M6 |
| Pay | Payroll | `pay` | `payroll` | M7 |
| | Compliance (versioned statutory rules, filings) | `cmp` | `compliance` | M7 |
| | Compensation | `comp` | `compensation` | M7 |
| | Benefits | `ben` | `benefits` | M7 |
| | Expense (expense + travel) | `exp` | `expenses` | M10 |
| Talent | Performance | `perf` | `performance` | M9 |
| | Goals (OKR/KPI) | `goal` | `goals` | M9 |
| | Learning | `lms` | `learning` | M9 |
| | Skills | `skl` | `skills` | M6 |
| | Career (paths, promotion, succession) | `car` | `career` | M9 |
| | Reward (catalog, recognition, wallet, budget) | `rwd` | `rewards` | M10 |
| | Engagement (surveys, eNPS) | `eng` | `engagement` | M10 |
| Service | Helpdesk | `hd` | `helpdesk` | M10 |
| | Assets | `ast` | `assets` | M10 |
| Delivery | Client (clients, contacts) | `crm` | `clients` | M5 |
| | Contract | `ctr` | `contracts` | M5 |
| | Project (team, phases, tasks, sprints, RAID, CRs, meetings) | `prj` | `projects`, `project-tasks`, `project-risks`, `project-issues` | M5 |
| | Resource (profile, requests, allocation, calendar, bench, forecast, capacity, demand) | `res` | `resources`, `resource-requests`, `resource-allocations`, `resource-forecast`, `capacity`, `demand`, `bench` | M6 |
| | Utilization | `utl` | `utilization` | M6 |
| | ProjectFinancials (budget, cost, revenue, billing, profitability) | `pfin` | `project-financials` | M7 |
| Insight | Reporting | `rpt` | `reports` | M11 |
| | AI | `ai` | `ai` | M12 |

Shared infrastructure schemas: `messaging` (outbox and inbox), `security` (the RLS predicate and
policy), `hangfire`.

---

## 3. Data conventions

These carry over from architecture.md §6.2:

- Keys are sequential `uniqueidentifier`, clustered on `(TenantId, Id)`, with same-tenant composite
  foreign keys.
- Each row carries `TenantId`, `CreatedBy`, `CreatedDate`, `ModifiedBy`, `ModifiedDate`,
  `IsActive`, `IsDeleted` and a rowversion.
- Business records that change over time are effective-dated (`EffectiveFrom`/`EffectiveTo`).
- Payroll, HR and project history is never overwritten. A change closes the current row and opens a
  new one.
- Sensitive columns are encrypted at rest and masked unless the caller holds the field permission.
  This covers PAN, UAN, ESIC, bank details, salary, tax, performance, disciplinary and benefits data.
- Dropdown values come from MasterData lookup tables, never from enums baked into the UI. Statutory
  rules are versioned rows; payroll results keep the rule version they were calculated with.

---

## 4. API conventions

- Routes are `/api/v1/{module}/{resource}`, plural kebab-case, with camelCase JSON. State changes
  are task endpoints (`POST .../{id}/approve`).
- Every list endpoint supports paging, sorting and filtering through the shared `PageRequest`.
- Every endpoint declares `[RequirePermission]` or `[AllowAnonymous]`, and the host refuses to start
  otherwise.
- Out-of-scope records return 404, not 403.
- **Error envelope** (spec §129). Every error response has this shape and never includes exception
  details:

  ```json
  { "success": false, "code": "VALIDATION_ERROR", "message": "Validation failed",
    "errors": [ { "field": "email", "message": "..." } ], "traceId": "..." }
  ```

- Correlation ids flow from `X-Correlation-Id` into logs, traces, outbox messages and the audit log.

---

## 5. Security

- The tenant comes from the token, never from the request.
- Isolation is layered:
  - token claim
  - tenant middleware
  - EF query filter
  - save interceptor
  - SQL row-level security (filter and block predicates)
  - composite foreign keys
- Permissions are `module.resource.action`, declared centrally in
  `Gggs.BuildingBlocks.Security.Permissions`. In the monolith they are evaluated in-process from
  Identity's tables, with a short tenant-scoped cache.
- Data scope (own, team, department, legal entity, tenant) and field-level permissions decide which
  rows and columns a caller sees.
- The browser holds only an httpOnly `__Host-` session cookie. Tokens stay server-side, and
  state-changing calls need the CSRF header.

---

## 6. Workflow, notifications, background work

- **Workflow (M3)** is a declarative engine supporting:
  - sequential, parallel and conditional steps
  - escalation, delegation, SLA timers
  - rejection and resubmission

  Each module starts an instance through `IWorkflowStarter` (in Workflow.Contracts) and reacts to
  the `WorkflowApproved` and `WorkflowRejected` events.
- **Notifications** support in-app and email now, with SMS, WhatsApp and push behind the same
  channel port.
- **Background work** runs in `Gggs.Worker` on Hangfire, with the tenant carried from enqueue to
  execution (`TenantJobFilter`). Scheduled work includes:
  - leave accrual
  - reward and certification expiry
  - utilization roll-ups
  - resource forecasting
  - compliance reminders
  - report generation
  - payroll runs

---

## 7. AI (M12)

The LLM never sees SQL or tables. All access is layered like this:

```
SQL ── module services ── AI tool layer (typed, permission-checked tools) ── LLM ── response
```

- **Read tools** include `GetEmployee`, `GetResourceAvailability` and `GetProjectRisks` (the full
  list is spec §89). Each one runs as the calling user, so the same permissions and data scope apply.
- **Action tools** include `ApplyLeave`, `SubmitTimesheet` and `PrepareResourcePlan`. They produce a
  draft or recommendation (`AIRecommendation`/`AIAction`). Anything with external or destructive
  effect needs explicit human approval (`AIActionApproval`).
- AI never makes final hiring, rating, reward or assignment decisions.
- Conversations, tool calls and usage are logged, and sensitive prompt content is not retained
  unless it is needed.

---

## 8. Frontend

- Nx workspace, Angular standalone components, signals, PrimeNG.
- Features may depend on `core` and `shared`, but never on another feature. Features never import
  PrimeNG directly; they go through `shared/ui` and `shared/data-table`.
- The navigation tree follows spec §117. It is generated from the `NAVIGATION` manifest and filtered
  by permission and by the tenant's enabled features.
- Every listing uses `gggs-data-table`, which provides search, filter, sort, paging, column choice
  and export.
