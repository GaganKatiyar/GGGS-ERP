# GGGS HRMS

A multi-tenant SaaS HRMS ERP: Angular front end, ASP.NET Core services, SQL Server, deployable to
Azure **and** to a customer's own servers (IIS or Docker).

The architecture this is built from lives in
[`docs/architecture/architecture.md`](docs/architecture/architecture.md). Read §3 (multi-tenancy),
§9 (tenant isolation) and §22 (roadmap) before writing code.

**Status: Phase 0 (foundation).** The building blocks, the proving harness and the test gates are in
place. Phase 1 (Tenant, Identity, Organization, Employee) starts next.

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
