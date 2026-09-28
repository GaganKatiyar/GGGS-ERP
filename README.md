# GGGS HRMS

A multi-tenant SaaS HRMS ERP: Angular front end, ASP.NET Core services, SQL Server, deployable to
Azure **and** to a customer's own servers (IIS or Docker).

The architecture this is built from lives in
[`docs/architecture/architecture.md`](docs/architecture/architecture.md). Read §3 (multi-tenancy),
§9 (tenant isolation) and §22 (roadmap) before writing code.

**Status: Phase 1 well under way.** Platform (tenants), Identity (users, roles, local sign-in) and
Core HR (organization structure, employees) are built and tested end to end, including a working
BFF gateway and a full Angular admin console: Organization, the complete Employee lifecycle,
Users/Roles administration (invite, complete-invitation, role permission editor), and tenant
settings. **Audit v1** is also built: every tenant-owned entity change is staged as an
`AuditEntryIntegrationEvent` in the writing service's own outbox
(`TenantAuditSaveChangesInterceptor`), `Gggs.Platform.Worker` consumes it into `platform.AuditLog`,
and tenant admins/auditors read it back through `GET /api/v1/audit/logs` (no frontend viewer yet).
**Document v1** is also built: a new `Gggs.Documents` service (`doc.DocumentType`/`doc.Document`)
handles upload, list-by-owner, download and delete through `IFileStorage` (the local file-share
adapter locally; Azure Blob in the cloud profile), gated by `document.file.*` permissions - no
malware scanning yet (nothing populates `ScanStatus` beyond `Skipped`), no re-upload-as-new-version.
Its frontend now exists too: an upload/list/download/delete section on the Employee detail page,
and document type administration at `/admin/document-types` (a type has to exist before anything
can be uploaded). **Notification v1** completes Phase 1's backend service list: a new
`Gggs.Notification` service renders `{{ key }}`-placeholder templates (not Scriban - every published
version had multiple high/critical advisories when this was built, so this ships plain substitution
instead, swappable behind the same renderer signature later) into in-app rows and queued emails,
sent through a plain-SMTP `IEmailSender` that logs instead of failing when no mail server is
configured. `Gggs.Notification.Worker` is its first real consumer:
`UserInvitedIntegrationEvent` (new - Identity previously raised `UserInvitedDomainEvent` into a
buffer nothing drained) now reaches an actual "welcome, set your password" notification. A user's
own inbox (`GET /api/v1/notifications`, mark read/read-all, unread count) and template
administration are both built; no frontend screen, no SMS/WhatsApp/push (Phase 1's own scope is
email + in-app only), no delivery-webhook tracking. 388 backend tests and 61 frontend tests pass.
See [docs/runbooks/local-dev-login.md](docs/runbooks/local-dev-login.md) to run the whole stack and
sign in. Every Phase 1 backend service from the roadmap now exists; what is left in Phase 1 is the
audit log and notification-inbox frontend screens, and the Business units/locations Organization
screens.

Known gaps: the Bicep templates are unvalidated and still need private endpoints and Front Door
(see [infra/README.md](infra/README.md)); `dotnet test` cannot run the suites on this SDK (see
below); the async tenant-provisioning saga (Platform → RabbitMQ → Identity.Worker), audit capture
(any service → RabbitMQ → `Platform.Worker`) and notification dispatch (Identity →
RabbitMQ → `Notification.Worker`) are all built but need a running broker, so none of them is
exercised by the local-dev-login path - see that runbook for what does and does not reach
`platform.AuditLog` / an actual inbox row without one.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.400+ | Pinned in `global.json` |
| SQL Server | 2019+ | A local instance, or Docker, or set `GGGS_TEST_SQL` |
| Node.js | 22+ | For the Angular workspace (arrives in Phase 0/1) |
| Docker | optional | Only needed if you have no local SQL Server |

## Getting started

```bash
dotnet build backend/Gggs.slnx          # build everything
pwsh tools/run-tests.ps1                 # build and run every test project
pwsh tools/run-tests.ps1 -SkipIntegration   # skip the suites that need SQL Server
```

Running the sample service:

```bash
dotnet run --project backend/Services/Sample/Gggs.Sample.Api
```

### Why tests are run through a script

`dotnet test` is not used. xunit v3 runs on Microsoft.Testing.Platform, and on this SDK
(10.0.401, MTP 2.4.0) `dotnet test` reports "Zero tests ran" while the same assembly runs its full
suite when executed directly. `tools/run-tests.ps1` runs the test executables, which behaves
identically locally and in CI. Revisit when the SDK and xunit line up again.

## Repository layout

```
backend/
  BuildingBlocks/     shared libraries every service composes (see below)
  Services/           one folder per service; Sample is the Phase 0 harness
  Gateway/ Aspire/ Agents/     BFF, local orchestration, on-prem connectors (Phase 1+)
frontend/             Angular (Nx) workspace
database/             RLS scripts, seed data, compliance packs, maintenance jobs
tests/                unit, architecture, tenant-isolation and integration suites
infra/                Bicep, Docker, on-prem release bundle, operational scripts
docs/                 architecture, ADRs, runbooks, domain notes
tools/                scripts and generators
```

### Building blocks

| Project | What it owns |
|---|---|
| `Domain` | Entity bases, sequential GUID keys, domain events, domain exceptions |
| `MultiTenancy` | `ITenantContext`, tenant middleware, tenant registry, platform scope |
| `Application` | `ICurrentUser`, `IUnitOfWork`, paging contracts |
| `Security` | Permission catalog, `[RequirePermission]`, data scopes, step-up authentication |
| `Persistence` | `GggsDbContext`, tenant/audit interceptors, RLS helpers, Dapper factory |
| `Web` | Problem details, correlation ids, deny-by-default endpoint validation |
| `Caching` | Tenant-scoped cache over HybridCache; no raw-key API exists |
| `Files` | `IFileStorage` — Azure Blob (Cloud) and file share (OnPremise) |
| `Secrets` | `ISecretStore` — Key Vault (Cloud) and an encrypted local file (OnPremise) |
| `Observability` | Serilog, OpenTelemetry over OTLP, health checks |
| `Jobs` | Hangfire with the tenant carried from enqueue to execution, plus per-tenant fan-out |
| `Messaging` | Integration events, transactional outbox and inbox, Service Bus and RabbitMQ transports |

### Deployment profiles

The platform ships to Azure **and** to customer-run servers from one codebase.
`Gggs:Platform:DeploymentProfile` (`Cloud` | `OnPremise`) selects the adapters at startup, and
**application code never references a cloud vendor SDK** — `DeploymentProfileRuleTests` fails the
build if it does. See [ADR-11](docs/adr/0011-deployment-profiles.md).

Local development runs the **OnPremise** profile on purpose: those adapters get exercised daily, and
nobody needs Azure credentials to start a service.

```bash
docker compose -f infra/docker/compose.yml up -d    # SQL Server, Redis, RabbitMQ, MinIO, Seq
```

## Frontend

An Nx workspace (`frontend/`) with Angular 22 standalone components, signals and PrimeNG.

```bash
cd frontend
npm ci
npx nx serve hrms-web                      # dev server
npx nx run-many -t lint test build --all   # the full gate
```

To actually sign in against the running backend rather than only build the SPA, see
[docs/runbooks/local-dev-login.md](docs/runbooks/local-dev-login.md): three services, one seed
command, no RabbitMQ or Redis required.

| Library | Holds |
|---|---|
| `@gggs/core/auth` | Session loaded from the BFF, `authGuard`, tenant switching |
| `@gggs/core/permissions` | `PermissionStore`, `*gggsHasPermission`, `permissionGuard` |
| `@gggs/core/tenant` | Tenant context and contrast-checked branding |
| `@gggs/core/http` | Credentials, CSRF, correlation-id and error interceptors |
| `@gggs/core/layout` | Shell and the permission-filtered navigation manifest |
| `@gggs/shared/ui` | Page header, empty state and other primitives |
| `@gggs/shared/data-table` | Server-paged table wrapping PrimeNG |
| `@gggs/shared/models` | API and session contracts |
| `@gggs/features/*` | One lazily loaded library per feature area |

**Rules the lint enforces** (`@nx/enforce-module-boundaries`, see [ADR-10](docs/adr/0010-frontend-stack.md)):

- Features may use `core` and `shared`, **never another feature**. Two features needing the same
  thing promote it to `shared`.
- `shared` may not reach back into `core` or a feature.
- Only the app composes features together.
- Features never import PrimeNG directly — they use `shared/ui` and `shared/data-table`, so an
  accessibility fix lands in one wrapper rather than in every screen.

**Permission-gated UI is not a security control.** `*gggsHasPermission` and `permissionGuard` decide
what is worth showing; the API enforces the same permission on every call. A hidden button is one
devtools click from being visible.

The navigation sidebar is generated from `NAVIGATION` in `@gggs/core/layout`, filtered by the user's
permissions and the tenant's licensed features. Adding a screen means adding an entry with its
permission — there is no separate menu to keep in step.

## The rules that matter

**1. The tenant comes from the token. Never from the client.** No request DTO has a `TenantId`; an
architecture test enforces it. Anything a caller sends that names a tenant is ignored and logged.

**2. Isolation is layered, and each layer assumes the others may fail.**

| Layer | Mechanism |
|---|---|
| Token | `tid` claim, validated |
| Middleware | `TenantContextMiddleware` resolves and checks tenant status |
| EF Core | Global query filters applied by convention to every `ITenantOwned` entity |
| Interceptor | Stamps `TenantId` on insert; refuses cross-tenant reads and writes |
| SQL Server | Row-level security filter **and** block predicates, via `SESSION_CONTEXT` |
| Schema | Composite `(TenantId, Id)` foreign keys |

The database layers cover hand-written SQL, Dapper and any bug in the layers above. They are
verified in `tests/Gggs.TenantIsolation.Tests` against a real SQL Server, including that raw SQL
with no tenant predicate returns **zero** rows.

**3. New tables are isolated and auditable by default.** Derive from `TenantAggregateRoot`, and the
conventions add the tenant filter, soft delete, audit columns, rowversion and the clustered
`(TenantId, Id)` index. After adding a tenant-owned table, re-run
`migrationBuilder.EnableTenantRowLevelSecurity(...)` **with the complete table list** - the security
policy is replaced as a whole.

**4. Endpoints are deny-by-default.** Every endpoint declares `[RequirePermission(...)]` or
`[AllowAnonymous]`. The host refuses to start otherwise.

**5. Out-of-scope records return 404, not 403.** A 403 would confirm the record exists.

## Conventions

- Permissions: `module.resource.action` (`leave.request.approve`), declared in `Permissions` and
  never renamed once released.
- Database: lowercase schema, PascalCase singular tables, `PK_`/`UX_`/`IX_`/`FK_` prefixes, UTC
  `datetime2(3)`, `CreatedBy`/`CreatedDate`/`ModifiedBy`/`ModifiedDate`/`IsDeleted`/`IsActive`.
- API: `/api/v{n}/{module}/{resource}`, plural kebab-case, camelCase JSON, task-based endpoints for
  state changes (`POST .../{id}/cancel`).
- C#: file-scoped namespaces, `sealed` by default, `Async` suffix, records for DTOs.

Full naming table: §21 of the architecture document.

## Contributing

Every pull request must pass: build, unit tests, architecture tests, the tenant-isolation suite,
`dotnet format --verify-no-changes`, and the vulnerable-package check. A change that adds a
tenant-owned table, an endpoint or a permission should also add the test that proves it is isolated.
