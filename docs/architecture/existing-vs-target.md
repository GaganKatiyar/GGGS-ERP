# Existing vs target: audit of GGGS HRMS against PeopleOS

Audit date: 2026-10-07, at commit `483d706` on `master`. The target is described in
[peopleos-target.md](peopleos-target.md), and the decision behind it in
[ADR-12](../adr/0012-modular-monolith.md).

**Classification key:**
- **KEEP**: correct and aligned with the target; carry it over unchanged.
- **REFACTOR**: correct, but reshape it for the monolith.
- **MERGE**: fold two implementations into one.
- **REPLACE**: rebuild it to the PeopleOS spec.
- **REMOVE**: delete it.
- **NEW**: it does not exist yet.

There is no production data, so the database starts fresh and nothing needs a data migration.

---

## 1. Architecture as found

| Aspect | As found |
|---|---|
| Deployables | 11 service APIs + 9 Workers + a YARP BFF gateway + a Sample service |
| Databases | 11 (`gggs_platform`, `gggs_identity`, `gggs_corehr`, `gggs_time`, `gggs_payroll`, `gggs_talent`, `gggs_spend`, `gggs_workflow`, `gggs_notification`, `gggs_document`, `gggs_reporting`) plus `gggs_sample` |
| Cross-service data | `ref.EmployeeSnapshot` copies in Time, Payroll, Spend, Workflow and Talent; `rpt.EmployeeRosterFact` in Reporting. All are fed by `EmployeeAssignmentChanged`/`EmployeeExited` events |
| Cross-service calls | Every API → Identity `GET /api/v1/me/permissions` on each request; every API → Platform `GET /internal/tenants/*`; Payroll → Time `GET /api/v1/attendance/days` in a run |
| Messaging | Own outbox/inbox (ADR-06), relayed to RabbitMQ (on-prem) or Service Bus (cloud); topic `gggs-events` |
| Local stack to sign in | 8 processes (Platform, Identity, CoreHr, Documents, Notification, Talent, BFF, SPA); Workers are not run, so audit, invites and offer-to-hire stay inert |
| Backend size | about 33k lines of C# in services (excluding migrations), 8.3k of them domain; about 6.3k in BuildingBlocks |

---

## 2. Module inventory and classification

### Backend services

| Service | What exists | Target module(s) | Action |
|---|---|---|---|
| BuildingBlocks (12 libs) | Domain primitives, tenancy, RLS, security catalog, persistence conventions, outbox/inbox, files, secrets, caching, jobs, observability, web | BuildingBlocks | **KEEP**/**REFACTOR**: in-process permissions and tenant registry, in-process transport, model-driven RLS, `IModule` |
| Identity | User, Role, UserRole, PasswordPolicy, platform staff and roles, OpenIddict (password and refresh grants), invite/complete-invitation | Identity | **REFACTOR** (move into module) |
| Platform | Tenant, TenantDatabase, TenantSetting, Plan, Subscription, AuditLog, provisioning saga | Platform | **REFACTOR**; the Tenant entity gains spec §7 fields (industry, GSTIN, PAN, CIN, timezone, currency, FY start) |
| Gateway.Bff | Cookie session, token refresh, CSRF guard, YARP proxy to 11 clusters, SPA hosting | Host/Gggs.Api | **MERGE** (the session pieces move into the API; the proxy is removed) |
| CoreHr: organization | LegalEntity, BusinessUnit, Branch, Location, Department, Designation, Grade, CostCenter (33 endpoints) | Organization | **REPLACE**: adds Division, ProfitCenter, Job vs Position, JobLevel, effective dating, department head and hierarchy |
| CoreHr: employee | Employee (12 fields), EmploymentRecord, lifecycle actions, bulk import (14 endpoints) | Employee | **REPLACE**: personal, contact, statutory (encrypted), bank, dependents, emergency contacts, Employee 360 |
| CoreHr: assets | AssetCategory, Asset, AssetAssignment (14 endpoints) | Assets | **REPLACE**: full lifecycle (procure → acknowledge → transfer → repair → return → dispose) |
| Time | Shift, roster, punches, attendance day, regularization, overtime, biometric devices; leave types, policies and rules, requests, balance, ledger, holidays (42 endpoints) | Attendance, Shift, Leave | **REPLACE** |
| Payroll | Salary components and slabs, structures, employee salary, pay groups and periods, runs, payslips (QuestPDF); PF, ESI, PT calc types (29 endpoints) | Payroll, Compliance | **REPLACE**. The calc-type logic and its tests are the reference for the new engine; statutory values move into versioned `StatutoryRule` rows |
| Talent | Requisition, posting, candidate, application, offer; onboarding templates, cases, tasks; performance cycles, goals, check-ins, reviews; courses, enrollments (61 endpoints) | Recruitment, Onboarding, Performance, Goals, Learning | **REPLACE** |
| Spend | Expense category, claim, line; travel request, advance, settlement (22 endpoints) | Expense | **REPLACE** |
| Workflow | Definition and version, instance, task, action log, delegation: sequential only (18 endpoints) | Workflow | **REPLACE**: adds parallel, conditional, escalation, SLA |
| Notification | In-app, template (`{{key}}` substitution), outbound email over SMTP | Notification | **REPLACE** |
| Documents | DocumentType, Document; upload, list, download, delete through `IFileStorage` | Documents | **REPLACE**: adds templates, merge fields, versions, expiry, acknowledgement, e-sign port |
| Reporting | Roster fact, daily headcount snapshot, one endpoint | Reporting | **REPLACE**; reads go through contracts and SQL views over the single DB |
| Sample | Phase 0 harness | — | **REMOVE** |
| `backend/Agents`, `backend/Aspire` | Empty folders | — | **REMOVE** |

### Absent: NEW modules

Client, Contract, Project (team, roles, phases, tasks, board, milestones, dependencies, sprints,
requirements, change requests, risks, issues, RAID, meetings), Resource (profile, availability,
requests, matching, allocation, calendar, forecast, capacity, demand, bench, release, transfer),
Timesheet, Utilization, Billing, ProjectFinancials, Workforce planning, Skills, Career and
succession, Reward and recognition, Engagement, Helpdesk, Policies, Benefits, Compensation,
Separation (resignation, clearance, F&F, exit interview, alumni), Compliance center, MasterData,
global search, and AI.

The only exit support today is a CoreHr `Employee.Exit` action and an `EmployeeExited` event.

---

## 3. API inventory (as found)

| Service | Route groups (`/api/v1/...`) | Endpoints |
|---|---|---|
| CoreHr | legal-entities, business-units, branches, locations, departments, designations, grades, cost-centers, employees, assets, assets/categories, reports | 62 |
| Time | attendance/{shifts,rosters,punches,days,regularizations,overtime-requests,devices}, leaves/{types,policies,balances,requests}, holidays/calendars, reports | 42 |
| Talent | recruitment/*, onboarding/*, performance/*, learning/* | 61 |
| Payroll | payroll/{components,structures,employees/{id}/salary,pay-groups,periods,runs,payslips} | 29 |
| Spend | spend/{categories,claims,travel-requests} | 22 |
| Workflow | workflows/{definitions,instances,tasks,delegations} | 18 |
| Identity | auth, users, roles, platform-staff, me, connect/token, tenant bootstrap | 28 |
| Platform | tenants, settings, audit, **`/internal/tenants` (anonymous)** | 15 |
| Notification | notifications, notifications/templates | 9 |
| Documents | documents, documents/types | 8 |
| Reporting | reporting/headcount-snapshots | 1 |
| BFF | bff/login, bff/logout, bff/user (+ switch-tenant used by the SPA) | 3+ |

All of these are retired with their service. Identity and Platform endpoints move into the monolith
unchanged, except `/internal/tenants`, which is deleted.

---

## 4. UI inventory (as found)

Nx workspace, Angular 21.2, PrimeNG 21.1, no chart library.

| Library | Screens | Action |
|---|---|---|
| core/auth, core/http, core/permissions, core/tenant, core/layout | session, interceptors, permission directive and guard, tenant branding, shell and navigation | **KEEP**; navigation rewritten to spec §117 |
| shared/ui | button, card, badge, empty state, form field, page header, confirm-dialog host, toast host | **KEEP**, then extend (KPI card, chart, status badge, drawer, form dialog, filter bar, date range, file upload, timeline, skeleton, approval panel) |
| shared/data-table | server-paged table | **REFACTOR**: search, sort, filter, column chooser, export |
| shared/models | DTOs for every domain | **REFACTOR**: pruned per module as each one is rebuilt |
| shared/employee-reference, org-reference, identity, documents, notifications | reference API clients and the employee picker | **MERGE** employee-reference with features/employees' own client; others are kept or rebuilt with their module |
| features/admin | users, invite, user roles, roles, role permissions, settings, document types, audit | **KEEP** (backend moves to Identity/Platform modules); document types return with Documents in M3 |
| features/notifications | inbox | Hidden until M3 |
| features/dashboard | 3 static placeholder cards | **REPLACE** |
| features/organization | 8 copy-paste CRUD pages (1,880 lines) | **REPLACE** with a config-driven CRUD page |
| features/employees | list, create, detail, bulk import, transfer, promote, change manager, rehire, confirm, exit, team overview | **REPLACE** (directory + Employee 360) |
| features/workflow, leave, attendance, payroll, spend, assets, recruitment, onboarding, performance, learning, reports | as listed in the routes | **REMOVE** now, **REPLACE** in their milestone |

---

## 5. Database inventory (as found)

| DB | Schemas → tables |
|---|---|
| gggs_platform | platform: Tenant, TenantDatabase, TenantSetting, Plan, Subscription, AuditLog |
| gggs_identity | iam: User, Role, UserRole, PasswordPolicy, PlatformStaffMember, PlatformRole, PlatformRoleAssignment · oidc: 4 OpenIddict tables |
| gggs_corehr | org: 8 tables · emp: Employee, EmploymentRecord · asset: 3 tables |
| gggs_time | time: 9 tables · leave: 8 tables · ref.EmployeeSnapshot |
| gggs_payroll | pay: 12 tables · ref.EmployeeSnapshot |
| gggs_talent | rec: 5 · onb: 4 · perf: 4 · lms: 2 · ref.EmployeeSnapshot |
| gggs_spend | exp: 6 · ref.EmployeeSnapshot |
| gggs_workflow | wf: 6 · ref.EmployeeSnapshot |
| gggs_notification | ntf: 3 |
| gggs_document | doc: 2 |
| gggs_reporting | rpt: 2 |

Every DB also has `messaging.OutboxMessage` and `messaging.InboxMessage`. There are no stored
procedures, views or functions apart from the RLS predicate. All 11 databases are dropped. The new
single database is created by one fresh migration stream in `Gggs.Data`.

---

## 6. Dependency map (retired)

```
CoreHr ──EmployeeAssignmentChanged/Exited──► Time, Payroll, Spend, Workflow, Talent*, Reporting
Talent ──OfferAccepted──► CoreHr (auto-hire)
Time, Spend ──StartWorkflow──► Workflow ──WorkflowApproved/Rejected──► Time, Spend
Identity ──UserInvited──► Notification
Platform ──TenantProvisioned──► Identity
every DbContext ──AuditEntry──► Platform (AuditLog)
every API ──HTTP──► Identity (permissions), Platform (tenant registry); Payroll ──HTTP──► Time
(*Talent does not consume EmployeeExited, so its snapshot never sees exits - a latent bug.)
```

In the monolith, each of these becomes either a call through a contract interface or an in-process
event delivered through the outbox.

---

## 7. Duplicate and dead-code report

| Finding | Where | Resolution |
|---|---|---|
| 5 identical employee snapshot tables plus a roster fact | Time, Payroll, Spend, Workflow, Talent, Reporting | Removed; `IEmployeeDirectory` contract |
| Two clients for `GET /api/v1/employees` | `features/employees/data/employees-api.ts`, `shared/employee-reference/employee-reference-api.ts` | One client in `shared/employee-reference` |
| 8 near-identical org CRUD pages | `features/organization/src/lib/*` | Generic CRUD page |
| About 43 pages hand-roll `<table>` while `gggs-data-table` exists (8 uses) | features/* | Every listing uses `gggs-data-table` |
| Dead menu links with no route | `/me/profile`, `/me/documents`, `/admin/custom-fields` | Removed from navigation |
| Routes with no menu entry | `/leave/types`; spend claims permission with no page | Rebuilt in their milestone |
| `@ngrx/signals` installed, never imported | frontend/package.json | Removed |
| Hangfire scheduler built, never used | `BuildingBlocks.Jobs` | Wired in `Gggs.Worker` |
| Dashboard hand-rolls card CSS | features/dashboard | Rebuilt with shared KPI cards |
| Permissions declared with no feature | `admin.custom-field`, `admin.identity-provider`, `employee.bank`, `document.template`, `report.schedule`, `audit.privacy-request` `employee.bank` is used since M2 (encrypted bank details on Employee 360); the rest stay reserved, since released names are never renamed |
| Stale on-prem compose (gateway and worker pointing at SampleDb) | `infra/onprem/compose.onprem.yml` | Rewritten for api and worker |
| Bicep list of 11 service DBs | `infra/bicep/modules/sql.bicep` | One database |

---

## 8. Security report

| Finding | Severity | Resolution |
|---|---|---|
| SQL row-level security is enabled only in Identity, Platform and Sample; CoreHr, Time, Payroll, Talent, Spend, Workflow, Notification, Documents and Reporting have **no RLS policy**, so the DB-layer backstop is missing for all HR data | High | The RLS policy is generated from the EF model for every `ITenantOwned` table; an arch test asserts coverage |
| `/internal/tenants/*` is anonymous and relies on network placement | Medium | Deleted; tenant lookup is in-process |
| Permissions fetched over HTTP on every request: availability coupling and an extra hop | Low | In-process provider with a short cache |
| Employee statutory and bank fields not modelled yet, so there is no encryption or masking path | Medium (before go-live) | Resolved in M2: PAN, UAN, ESIC, national ID and bank account are encrypted (Data Protection), shown as masked hints, and revealed only under `employee.identity-document.view` / `employee.bank.view` |
| Messaging relay requires a broker that the local stack does not run, so audit capture silently never reaches `AuditLog` locally | Medium | The in-process dispatcher in the Worker writes audit rows from the same DB |
| No MFA/SSO yet; OIDC federation pending | Medium (before go-live) | Identity roadmap (M3+) |

## 9. Performance report

No load testing or query profiling has been done, so these are structural observations only:

- Each request paid an HTTP round-trip for permissions, and cached tenant lookups were also remote.
  Both become in-process.
- Payroll runs fetched attendance over HTTP per run. This becomes a contract call in the same
  database.
- Many frontend pages load full lists client-side instead of using the server-paged table. Moving
  them to `gggs-data-table` fixes this.
- Reporting relied on event-fed copies. With one database, dashboards use projections or indexed
  views, and heavy roll-ups run as Worker jobs.

---

## 10. Migration sequence

| Step | Content | Status |
|---|---|---|
| M0 | This audit, the target architecture, ADR-12, roadmap | done |
| M1 | Monolith foundation: hosts, `Gggs.Data`, Platform and Identity modules, BFF folded in, BuildingBlocks changes, infra, tests, navigation | done (2026-10-14); retired code deleted after M2 (§11) |
| M2 | Organization, Employee, MasterData; UI component kit; Employee 360 shell | done (2026-10-14): org structure (11 record kinds, department tree), employees (hire, 360, assignment changes, confirm/notice/exit, family, statutory and bank, history, org chart, CSV import with preview), master data admin, My profile, My team |
| M3 | Workflow, Notification, Documents, Policies; approvals inbox | done (2026-10-14): staged approvals (parallel, conditional, SLA escalation, delegation, send-back/resubmit), in-app and email notifications, versioned documents with letters and click-to-sign, policies with approval-gated publishing and acknowledgements |
| M4 | Shifts, Attendance, Leave, Holidays; ESS/MSS | done (2026-10-15): shifts with weekly offs, assignments and roster overrides, shift swaps through approval; attendance from web punches and devices (CSV import), evaluated against the shift (grace, half day, missing punch) and closed nightly, regularization through approval; leave types and policies (monthly/yearly accrual job, carry-forward and lapse, probation, notice, longest stretch, negative balance, sandwich rule) on a ledger; holiday calendars by location; self-service, team and Employee 360 views |
| M5 | Client, Contract, Project | done (2026-10-15): clients and contacts; contracts (MSA, SOW, PO, SLA, retainer, T&M) with approval-gated activation, expiry, auto-renewal and renewal reminders; projects with lifecycle stages and history, team and roles, phases, tasks (epic to subtask) on a configurable Kanban board, dependencies with loop detection, milestones, sprints with burndown and velocity, requirements, change requests through approval, RAID log, meetings and actions, calendar, project documents, and health on configurable thresholds (nightly snapshot) |
| M6 | Skills, Resource, Timesheet, Utilization | done (2026-10-15): skill catalogue, employee skills with levels, targets and gaps, certifications and a skill matrix; resource profiles with cost and billing rates and weekly capacity; capacity per month from shift rosters, holidays, leave and allocations; allocation with over-allocation detection, an allocation calendar, the bench, release and approval-gated transfer; resource requests with a weighted matching score, shortlist and allocation onto the project team; capacity and skill demand-vs-supply forecast; weekly timesheets with rule checks, workflow approval, correction reopen and period lock, approved hours posted to project tasks and a Monday reminder for missing weeks; utilization and billability by employee, project, department, manager or location with a six-month trend |
| M7+ | See [roadmap.md](../roadmap.md) | planned |

The remaining old services (CoreHr, Time, Payroll, Talent, Spend, Workflow, Notification, Documents,
Reporting) are deleted in M1, together with their tests and frontend feature libraries. Nothing
keeps two implementations alive. The pre-rebuild code stays available as the git tag
`pre-peopleos` (commit `483d706`). Payroll's calc types and the Workflow engine in particular are
the reference for their rebuilds (M7 and M3).

---

## 11. Retired code

Deleted on 2026-10-14, after M2, with the owner's approval. Everything below is preserved in git
history at the `pre-peopleos` tag (commit `483d706`).

- `backend/Services/` (the nine business services, Sample, and what was left of Identity and
  Platform after their sources moved into `backend/Modules/`), `backend/Gateway/`, and the empty
  `backend/Agents/` and `backend/Aspire/`
- the per-service integration and unit test projects (their surviving tests live in
  `tests/Gggs.Api.IntegrationTests/` and the module unit-test projects)
- BuildingBlocks code with no remaining caller: `HttpPermissionProvider`, the JWT-bearer and remote
  permission extensions, `RemoteTenantRegistry`, and the package versions only those and the
  retired services used (JwtBearer, YARP, Mapperly, Scalar, QuestPDF, OpenIddict validation, Seq
  sink, HTTP resilience, VSTest runner)
- frontend feature libraries with no backend yet
  (`features/{notifications,workflow,leave,attendance,payroll,spend,assets,recruitment,onboarding,performance,learning,reports}`,
  `shared/documents`, `features/admin/.../documents`), the pre-M2 organization and employee pages,
  the old org and employee reference APIs, the legacy models in `shared/models` that only those used,
  and the unused `@ngrx/signals` dependency. Each module's UI is rebuilt in its milestone.

Kept on purpose: `shared/notifications`, which the shell's notification bell uses (switched off until
M3 rebuilds it), and the old `gggs_*` databases on the development SQL Server, which are data rather
than code and are dropped only on the owner's say-so.
