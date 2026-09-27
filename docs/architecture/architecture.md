# GGGS HRMS — Multi-Tenant SaaS HRMS ERP: Architecture & Implementation Plan

## Context

`C:\Gagan\GGGSERP` is empty. This is a new build: a multi-tenant SaaS HRMS ERP on Angular + ASP.NET Core microservices + SQL Server, deployed on Azure. This document is the architecture baseline a team can build from. Once it is approved, implementation starts with **Phase 0 (foundation)** and then **Phase 1** (Tenant, Identity, Organization, Employee), as described in §22.

### Key decisions at a glance (ADR summary)

| # | Decision | Choice | Main alternative rejected |
|---|---|---|---|
| ADR-01 | Tenancy model | **Hybrid: pooled shared DB + shared schema (TenantId + SQL RLS) by default, with dedicated DB per tenant for Enterprise/regulated tenants** | Schema-per-tenant (migration and ops pain) |
| ADR-02 | Service granularity | **Ten domain/support services plus a BFF gateway**, grouped by business capability, not one per module | 15+ micro-services (too much distributed overhead) |
| ADR-03 | Data ownership | Database per service. Modules inside a service get their own SQL schema | Shared HR database (creates coupling) |
| ADR-04 | Identity | **Platform OIDC server (OpenIddict + ASP.NET Core Identity)** that federates to each tenant's Entra ID / OIDC / SAML | Using Entra ID directly as the only IdP |
| ADR-05 | Browser auth | **BFF pattern**: httpOnly cookie in the browser, tokens kept server-side | Tokens stored in the SPA |
| ADR-06 | Messaging | Azure Service Bus (prod) / RabbitMQ (dev) through **MassTransit + transactional outbox/inbox** | Kafka (not needed at this scale) |
| ADR-07 | Workflow | **Custom declarative approval engine** (JsonLogic conditions) + Hangfire timers | Elsa / Camunda / Temporal |
| ADR-08 | Keys | `UNIQUEIDENTIFIER` sequential (COMB) PK, **clustered index on (TenantId, Id)**, composite same-tenant FKs | `bigint IDENTITY` (conflicts when moving a tenant to another DB) |
| ADR-09 | Compliance | **Versioned, effective-dated country "Compliance Packs"** (data + formulas), not hard-coded logic | Country logic written in code |
| ADR-10 | Frontend | Angular (latest LTS, standalone + signals) in an **Nx monorepo**, **PrimeNG**, NgRx SignalStore | Angular Material + AG Grid; headless + Tailwind |
| ADR-11 | Hosting | **Dual deployment profile**: Azure Container Apps for SaaS *and* a supported on-prem profile (IIS/Docker Compose, SQL Server, RabbitMQ, file storage). Every infrastructure dependency sits behind a port with two adapters | Azure-only PaaS |
| ADR-12 | Reporting | Event-fed **read-model DB** + read replicas. Fabric/Synapse later | Reporting directly on OLTP databases |
| ADR-13 | First compliance pack | **India** (PF, ESI, PT, gratuity, TDS old/new regime, Form 24Q, ECR) — the densest rule set, so it validates the pack model hardest | UAE/GCC, UK, US |

Target runtime: **.NET 10 (LTS)**, **EF Core 10**, **SQL Server 2022 / Azure SQL**, **Angular latest LTS**.

Licensing to check in Phase 0: MediatR, AutoMapper and MassTransit v9+ now need commercial licenses. The default here is **Mapperly** (MIT, source-generated) and plain handler interfaces. For MassTransit, either budget the license or use **Wolverine** (MIT). QuestPDF (community licence below revenue threshold), Hangfire (LGPL core) and Duende (commercial, which is why OpenIddict is chosen) also need a check.

---

## 1. Product overview

**GGGS HRMS** is a hire-to-retire HR platform sold as SaaS to many customer organizations.

- **Tenant** = a customer (subscription + isolation boundary).
- **Legal Entity** = a company *inside* a tenant. A group with 3 registered companies is **1 tenant with 3 legal entities**. This matters for payroll, statutory registration, payslip branding and bank files. Tenant ≠ company.
- **Editions** (feature flags per module): *Core* (Org, Employee, ESS, Attendance, Leave), *Professional* (+Payroll, Expenses, Assets, Performance), *Enterprise* (+Recruitment, Learning, Advanced Workflow, Analytics, AI, dedicated DB, custom SSO, data residency).
- **Personas**: Platform Super Admin/Support (our staff), Tenant Admin, HR Admin, HR Manager, Payroll Admin, Finance User, Department Manager, Team Lead, Employee, Auditor. Candidates and external interviewers are *non-user* actors reached through tokenized links.
- **Design principles**: tenant isolation in every layer; everything configurable per tenant (policies, workflows, templates, custom fields, compliance); effective-dated HR data (history is always kept); approvals are generic; APIs return only the data the caller needs.

## 2. Functional modules → bounded contexts

| Module | Bounded context / service | Phase | Notable design points |
|---|---|---|---|
| Organization (legal entity, BU, branch, location, dept, designation, grade, cost center) | Core HR | 1 | Dept tree uses `hierarchyid`; grades carry salary bands; optional position management |
| Employee Management | Core HR | 1 | Core record + **effective-dated `EmploymentRecord`** (job history); PII fields encrypted; custom fields |
| Asset Management | Core HR (module `asset`) | 4 | Split into its own service only if it grows (e.g. into a full ITAM) |
| Attendance, Shift, Roster, Overtime, Biometric | Time | 2 | Raw punches → processed `AttendanceDay`; on-prem device connector |
| Leave, Holiday calendar | Time | 2 | **Append-only leave ledger**; balance is a projection |
| Payroll, Tax, Compliance packs | Payroll | 4 | Isolated for security and heavy compute; period locking |
| Recruitment/ATS, Onboarding, Performance, Learning | Talent | 3 | Careers portal is a separate public app |
| Expense, Travel | Spend | 4 | Multi-currency; reimbursement via payroll or bank batch |
| Workflow (approvals, delegation, SLA) | Workflow | 2 (v1), 6 (advanced) | One engine for every module |
| Notifications | Notification | 1 (email/in-app), 2+ | Template per tenant/locale/channel |
| Documents | Document | 1 | Blob storage per tenant, malware scan, retention |
| Reports & Analytics, Dashboards | Reporting | 2 (basic), 5 | Event-fed read model |
| Integrations, Import/Export, Webhooks, Public API | Integration Hub | 2 (import), 5 | Adapter pattern |
| Tenant, Subscription, Feature flags, Settings | Platform | 1 | Tenant catalog + provisioning saga |
| Identity, Users, Roles, Permissions, SSO, MFA | Identity | 1 | OIDC server, SCIM |
| Audit & Compliance | Audit (inside Platform deployable, own DB) | 1 | Fed by outbox; ledger tables |
| ESS / MSS | **Not services**: UI compositions + BFF aggregation endpoints | 2 | |

## 3. Multi-tenant architecture

### 3.1 Comparison

| Criterion | 1. Shared DB + shared schema (TenantId) | 2. Shared DB + schema per tenant | 3. DB per tenant |
|---|---|---|---|
| Isolation strength | Logical. Relies on code + RLS | Logical (schema permissions) | Physical. Strongest |
| Cost per tenant | Lowest | Low | Highest (pools reduce it) |
| Onboarding speed | Instant (a row insert) | Create schema + objects | Provision DB (minutes) |
| Schema migrations | Once per service DB | N × schemas. EF Core handles this poorly (model cache per schema, `__EFMigrationsHistory` per schema) | N × DBs. Needs fan-out tooling, but it is well understood |
| Cross-tenant platform analytics | Easy | Hard | Hard |
| Noisy neighbour | Real risk | Real risk (same DB) | Isolated |
| Per-tenant backup/restore | Hard (PITR + selective copy) | Moderate | Trivial |
| Per-tenant encryption key / residency | Needs app-level keys | Hard | Native (TDE CMK per DB, region per DB) |
| Enterprise sales requirement ("our own DB") | ✗ | ~ | ✓ |
| Operating 1000s of tenants | ✓ | ✗ (object explosion) | ~ (elastic pools) |

### 3.2 Recommendation: **"Pool by default, silo on demand"**

- **Standard tenants**: shared database per service, shared schema, `TenantId` on every tenant-owned row, enforced by **four independent layers**: token → EF global filter → **SQL Server Row-Level Security** → composite FKs.
- **Enterprise/regulated tenants**: the *same schema* in a **dedicated database per service** (in the same or a regional elastic pool), with an optional customer-managed TDE key and a region chosen for data residency.
- Code is identical in both tiers. TenantId columns and RLS stay on in dedicated DBs too, so promoting a tenant is a data copy, not a code change.
- **Tenant Catalog** (Platform DB) maps `TenantId → service → connection secret name / shard`. The `ITenantConnectionResolver` in each service reads it (cached in Redis and in-memory for 5 minutes).
- Schema-per-tenant is rejected. It combines the cost of a DB per tenant (N migrations) with the weak isolation of a shared DB, and EF Core supports it poorly.
- **Promotion path** (pooled → dedicated): an online copy job filtered by TenantId → catch up from change tracking → short write freeze for the tenant → switch the catalog entry → purge the pooled rows. This is documented as a runbook, and the random GUID keys make it collision-free.

### 3.3 Tenant resolution (never trust the client)

1. The user goes to `acme.gggshr.com` or `app.gggshr.com`. The subdomain or email domain is used **only for login discovery** (which IdP to send the user to).
2. The Identity Service authenticates the user (local or federated) and issues a token with `tid` taken from the **user's membership record in the Identity DB**.
3. The BFF attaches the access token. Each service's `TenantContextMiddleware` reads `tid` from the **validated JWT only**. Any `TenantId` in route/body/header is ignored. DTOs have no TenantId property at all.
4. The middleware checks the tenant is `Active` (cached) and the plan allows the module (feature gate) → HTTP 403 `tenant_suspended` / `feature_not_enabled`.
5. Users with memberships in several tenants (consultants, platform support) **switch tenant through token exchange**. A new token is issued for the new `tid` after a membership check, and the SPA state is fully reset.

### 3.4 Tenant lifecycle (orchestrated saga in Platform service)

`Requested → Provisioning → Active ⇄ Suspended → Offboarding → Archived → Purged`

Provisioning steps, each idempotent: create the tenant record → allocate a DB (pooled: none; dedicated: create from template) → publish `TenantProvisioned` → each service seeds its defaults (roles & permissions, leave types, workflow templates, notification templates, compliance pack binding, document types) → create the Tenant Admin user plus an invite → mark Active. If a step fails it retries, and after a timeout the tenant sits in `ProvisioningFailed` with the reason visible to operators.

---

## 4. High-level system architecture

```
                 ┌──────────────────────────────────────────────────────────┐
 Browser (SPA) ─►│ Azure Front Door + WAF (TLS, geo, bot, rate limit, CDN)   │◄─ Careers portal (public)
 Mobile app   ─► └───────────────┬──────────────────────────────────────────┘◄─ Partner API (APIM, Phase 5)
                                 │
                   ┌─────────────▼─────────────┐        ┌─────────────────────┐
                   │ BFF / API Gateway (YARP)  │◄──────►│ Identity Service     │◄─► Tenant IdPs (Entra ID,
                   │ cookie session, token     │  OIDC  │ (OpenIddict)         │    Okta, Google, SAML)
                   │ handling, routing,        │        └─────────────────────┘
                   │ aggregation (ESS/MSS)     │
                   └──┬───┬───┬───┬───┬───┬───┬┘   REST (internal, mTLS/managed identity)
     ┌────────────────┘   │   │   │   │   │   └──────────────────────┐
┌────▼────┐ ┌─────▼──┐ ┌──▼───┐ ┌──▼────┐ ┌▼──────┐ ┌──────────┐ ┌─▼──────────┐ ┌───────────┐
│Platform │ │Core HR │ │ Time │ │Payroll│ │Talent │ │  Spend   │ │ Workflow   │ │ Reporting │
│(+Audit) │ │        │ │      │ │       │ │       │ │          │ │            │ │ (read     │
└────┬────┘ └───┬────┘ └──┬───┘ └──┬────┘ └──┬────┘ └────┬─────┘ └─────┬──────┘ │  model)   │
     │  own DB  │ own DB  │ own DB  │ own DB  │ own DB    │ own DB      │ own DB └─────┬─────┘
     └──────────┴────┬────┴─────────┴─────────┴───────────┴─────────────┘             │
                     │  Integration events via Outbox → Azure Service Bus (topics)     │
                     ▼                                                                 │
     ┌──────────────┐ ┌──────────────┐ ┌────────────────┐                              │
     │ Notification │ │  Document    │ │ Integration Hub│──► Email/SMS/WhatsApp, banks, │
     │ (+SignalR)   │ │ (Blob)       │ │ (adapters)     │    ERP, tax, job boards,      │
     └──────────────┘ └──────────────┘ └───────▲────────┘    biometric connector        │
                                               │                                        │
 Shared infra: Azure SQL (elastic pools, failover groups) · Redis · Blob Storage · Key Vault ·
 App Configuration · Service Bus · Azure SignalR · App Insights/OpenTelemetry · Hangfire per service
```

Each service deploys as **two containers from one codebase**: `*.Api` (HTTP) and `*.Worker` (consumers + Hangfire jobs), so they scale independently.

## 5. Microservice boundaries

### 5.1 Evaluation of the candidate list

| Candidate | Separate service? | Rationale |
|---|---|---|
| Identity | **Yes** | Security boundary, different change rate, token issuer |
| Tenant | **Yes → "Platform"** (tenant, subscription, features, settings, catalog, **audit store**) | Platform-level, cross-tenant by nature, small |
| Organization + Employee | **Merged → "Core HR"** | Tightly coupled (every employee change touches org references). Splitting them would create chatty calls and distributed transactions for a hire |
| Attendance + Leave | **Merged → "Time"** | They share calendars, shifts and day status. Leave changes the attendance day. Payroll consumes both as "time" |
| Payroll | **Yes** | Most sensitive data, heavy batch compute, strict access, separate compliance cadence |
| Recruitment + Onboarding + Performance + Learning | **Merged → "Talent"** | Moderate size each. Split Recruitment out later if the careers portal traffic or team size justifies it |
| Expense + Travel | **Merged → "Spend"** | Travel settles into expenses. Same finance approvers |
| Asset | **Module inside Core HR** | Small, employee-centric. Extraction trigger: ITAM features or a separate team |
| Workflow | **Yes** | Cross-cutting engine used by every service |
| Notification | **Yes** | Cross-cutting, provider integrations, high volume, async |
| Document | **Yes** | Storage, scanning and retention concerns shared by all |
| Reporting | **Yes** | Read model, different scaling and query patterns |
| Integration Hub | **Yes** | Keeps external providers out of domain services |
| ESS / MSS | **No** | These are views over the other services (BFF aggregation) |

**Result: 11 deployable services + BFF** (Identity, Platform, Core HR, Time, Payroll, Talent, Spend, Workflow, Notification, Document, Reporting, Integration Hub). Phase 1 ships 6 of them (Identity, Platform, Core HR, Workflow-stub, Notification, Document) + BFF.

**Modular inside, micro outside**: each service is internally modular (one schema and one feature folder per module), so it can be split later without redesign.

### 5.2 Database ownership

- One logical database per service (`gggs_corehr`, `gggs_time`, …), all in an elastic pool per environment/region. **No cross-database joins and no shared tables.**
- Services that need another service's data keep a **local replicated read table** fed by events. For example Time, Payroll, Talent, Spend and Workflow each keep `ref.EmployeeSnapshot (TenantId, EmployeeId, Code, DisplayName, DepartmentId, BranchId, LegalEntityId, ManagerEmployeeId, GradeId, Status, UserId, Version)`, built from Core HR `Employee*` events. Consumers check the `Version` field so out-of-order events cannot overwrite newer data.

### 5.3 Communication

| Use | Style | Examples |
|---|---|---|
| UI queries/commands | Sync REST via BFF | All `/api/v1/*` |
| Service needs fresh data inside a request, and it is not in its snapshot | Sync REST (internal, typed HttpClient + resilience pipeline, 2 s timeout) | Workflow → Core HR "manager chain" if the snapshot is stale |
| State changes other services react to | **Async integration events** (pub/sub topics) | `EmployeeHired`, `EmployeeTransferred`, `LeaveRequestApproved`, `PayrollRunFinalized` |
| Commands to another service that can wait | Async command (queue) | `StartWorkflow`, `SendNotification`, `GenerateDocument` |
| Long-running processes | Orchestrated saga (MassTransit state machine) | Tenant provisioning, hire→onboarding, payroll run, employee exit/F&F |
| Real-time UI push | SignalR (Azure SignalR Service) through Notification service | Approval counters, job completion |

**Event envelope** (header): `MessageId, TenantId, CorrelationId, CausationId, OccurredAt, SchemaVersion, ActorUserId`. The payload never contains more PII than consumers need (IDs + display fields). Consumers call the API for sensitive details.

### 5.4 Distributed transactions, outbox, idempotency, retries

- **No 2PC/MSDTC.** Each service commits locally, and cross-service consistency is eventual, handled by sagas with **compensation**. Example: a payroll run that fails after marking expenses "reimbursed" publishes `PayrollRunReverted`, and Spend reopens the claims.
- **Transactional outbox**: the domain change and the outgoing message are saved in **the same SQL transaction** (`messaging.OutboxMessage`). A delivery service relays them to the broker. This guarantees "saved ⇒ eventually published".
- **Inbox / idempotent consumers**: `messaging.InboxState` keyed by `(MessageId, ConsumerName)` deduplicates. Handlers are also designed to be idempotent (upserts by natural key, version checks).
- **API idempotency**: every non-idempotent `POST` accepts an `Idempotency-Key` header. `(TenantId, UserId, Key)` → stored response for 24 h (SQL table + Redis lock). A replay with a different body returns 422.
- **Retry strategy**
  - HTTP: `Microsoft.Extensions.Http.Resilience` (Polly v8) — timeout → retry ×3 exponential backoff with jitter (**only for idempotent/GET or requests with an Idempotency-Key**) → circuit breaker (50% failure over 30 s, 30 s break).
  - Messaging: immediate retry ×3 → delayed redelivery (1 m, 5 m, 30 m) → **dead-letter queue**, with an ops dashboard for replay. Poison messages raise an alert.
  - External providers: per-adapter policies + provider failover (e.g. SMS provider A → B).
- **Failure handling**: dashboards render widget by widget (one failing widget does not fail the page). If Workflow is down, leave submission still succeeds and the `StartWorkflow` command waits in the outbox. Stale snapshot data carries a "last synced" timestamp, and a nightly full-sync reconciliation job catches missed events.

---

## 6. Database architecture

### 6.1 Physical layout

| Database | Schemas |
|---|---|
| `gggs_platform` | `platform`, `audit` (audit could move to its own DB when volume grows), `messaging` |
| `gggs_identity` | `iam`, OpenIddict tables, `messaging` |
| `gggs_corehr` | `org`, `emp`, `cfg`, `asset`, `messaging` |
| `gggs_time` | `time`, `leave`, `ref`, `messaging` |
| `gggs_payroll` | `pay`, `comp` (compliance), `ref`, `messaging` |
| `gggs_talent` | `rec`, `onb`, `perf`, `lms`, `ref`, `messaging` |
| `gggs_spend` | `exp`, `trv`, `ref`, `messaging` |
| `gggs_workflow` | `wf`, `ref`, `messaging` |
| `gggs_notification` | `ntf`, `messaging` |
| `gggs_document` | `doc`, `messaging` |
| `gggs_reporting` | `rpt` (facts/dims), `messaging` |
| `gggs_integration` | `int`, `messaging` |

Every database also has a `security` schema (RLS predicate + policy) and Hangfire's `hangfire` schema where jobs run.

### 6.2 Table conventions (apply to every tenant-owned table)

```sql
Id            UNIQUEIDENTIFIER NOT NULL,              -- app-generated SQL-Server-ordered COMB GUID
TenantId      UNIQUEIDENTIFIER NOT NULL,
-- ... business columns ...
IsActive      BIT            NOT NULL CONSTRAINT DF_<T>_IsActive DEFAULT (1),   -- business enable/disable
IsDeleted     BIT            NOT NULL CONSTRAINT DF_<T>_IsDeleted DEFAULT (0),  -- soft delete
DeletedBy     UNIQUEIDENTIFIER NULL,
DeletedDate   DATETIME2(3)   NULL,
CreatedBy     UNIQUEIDENTIFIER NOT NULL,              -- UserId (system user for jobs)
CreatedDate   DATETIME2(3)   NOT NULL CONSTRAINT DF_<T>_CreatedDate DEFAULT (SYSUTCDATETIME()),
ModifiedBy    UNIQUEIDENTIFIER NULL,
ModifiedDate  DATETIME2(3)   NULL,
RowVersion    ROWVERSION     NOT NULL,                -- optimistic concurrency → ETag
CONSTRAINT PK_<T> PRIMARY KEY NONCLUSTERED (Id),
CONSTRAINT UX_<T>_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)   -- tenant data physically co-located
```

- **All timestamps are UTC** `datetime2(3)`. Business dates (`LeaveDate`, `AttendanceDate`) are `date`, interpreted in the employee's work-location time zone (IANA id stored on `Location`).
- **Same-tenant FKs**: child → parent FKs are composite `(TenantId, ParentId) → Parent(TenantId, Id)`, so the database itself **cannot** link a row to another tenant's parent. In EF Core this is configured by convention with `HasPrincipalKey(p => new { p.TenantId, p.Id })`.
- **Soft delete**: `IsDeleted` + EF named query filter. Unique indexes are **filtered** `WHERE IsDeleted = 0` so a code can be reused after deletion. Hard delete happens only through retention/anonymization jobs. `IsActive` means something different (e.g. an inactive department still appears in history).
- **Indexes**: every non-clustered index starts with `TenantId` (RLS predicates and filters are sargable). Common patterns: `(TenantId, <FK>) INCLUDE (...)`, `(TenantId, Status, <Date>)`, filtered `WHERE IsDeleted = 0`.
- **History**: HR-critical tables (`emp.Employee`, `emp.EmploymentRecord`, `pay.EmployeeSalary`, `emp.EmployeeBankAccount`) are **SQL Server system-versioned temporal tables** (a "state as of" query is free) *and* are also audited.
- **Effective dating**: job, salary, policy assignment and org structures use `EffectiveFrom`/`EffectiveTo` (`date`, `EffectiveTo` NULL = open), with a check constraint and a no-overlap rule enforced in the domain + a unique filtered index on `IsCurrent = 1`.
- **Custom fields**: `CustomFields NVARCHAR(MAX)` JSON column (`CHECK (ISJSON(CustomFields) = 1)`) + a `cfg.CustomFieldDefinition` table. A field marked *searchable* gets a persisted computed column (`JSON_VALUE`) + index, created by a controlled admin job (max 10 per entity). EAV was rejected because of join explosion and poor list performance. A pure JSON column without computed columns was rejected because it cannot be indexed.
- **Hierarchies**: `org.Department.OrgPath HIERARCHYID` (descendant queries for data scope). The reporting manager chain is materialized in `emp.ReportingLine (TenantId, EmployeeId, ManagerEmployeeId, Depth)`, a closure table rebuilt on events, for fast "my team incl. indirect" queries.
- **Large append-only tables** (`time.AttendancePunch`, `audit.AuditLog`, `ntf.OutboundMessage`, `audit.ApiLog`) are **partitioned monthly** on the date column, with a sliding window for archival.
- **Sensitive columns** (national ID, tax ID, bank account, passport): encrypted at the application level with a **per-tenant data key** (envelope encryption; key held in Key Vault) + an HMAC **blind index** column for exact-match lookup/uniqueness. See §17.

### 6.3 Row-Level Security (backstop layer)

```sql
CREATE SCHEMA security;
GO
CREATE FUNCTION security.fn_TenantAccessPredicate(@TenantId UNIQUEIDENTIFIER)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS AccessResult
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER)
       OR IS_MEMBER(N'rls_bypass') = 1;          -- only migrator / platform-ops principals
GO
CREATE SECURITY POLICY security.TenantIsolationPolicy
    ADD FILTER PREDICATE security.fn_TenantAccessPredicate(TenantId) ON emp.Employee,
    ADD BLOCK  PREDICATE security.fn_TenantAccessPredicate(TenantId) ON emp.Employee AFTER INSERT,
    ADD BLOCK  PREDICATE security.fn_TenantAccessPredicate(TenantId) ON emp.Employee AFTER UPDATE
    -- ... generated for every tenant-owned table by a migration helper
WITH (STATE = ON, SCHEMABINDING = ON);
```

The app login is **not** a member of `rls_bypass` and does not own the tables (owners bypass RLS). The session context is set on every connection open (§9).

---

## 7. Core database tables

The standard columns from §6.2 are implied everywhere and marked **[STD]**. "T" = TenantId. FK = composite same-tenant FK unless noted. Platform-global tables (no TenantId) are marked **[GLOBAL]**.

### 7.1 Full DDL — foundation tables

```sql
-- ===================== gggs_platform =====================
CREATE TABLE platform.Tenant (                                   -- [GLOBAL] the tenant registry itself
    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenant PRIMARY KEY CLUSTERED,
    Code             VARCHAR(32)     NOT NULL,                   -- 'acme' → login discovery/subdomain
    Name             NVARCHAR(200)   NOT NULL,
    Status           TINYINT         NOT NULL,                   -- 1 Provisioning 2 Active 3 Suspended 4 Offboarding 5 Archived 6 Purged
    IsolationTier    TINYINT         NOT NULL,                   -- 1 Pooled 2 Dedicated
    DataRegion       VARCHAR(32)     NOT NULL,                   -- residency, e.g. 'centralindia','uaenorth','westeurope'
    CountryCode      CHAR(2)         NOT NULL,
    DefaultTimeZone  VARCHAR(64)     NOT NULL,                   -- IANA
    DefaultCurrency  CHAR(3)         NOT NULL,
    DefaultLocale    VARCHAR(10)     NOT NULL,
    BrandingJson     NVARCHAR(MAX)   NULL CHECK (ISJSON(BrandingJson)=1),  -- logo doc id, colors
    IsActive BIT NOT NULL DEFAULT 1, IsDeleted BIT NOT NULL DEFAULT 0, DeletedBy UNIQUEIDENTIFIER NULL, DeletedDate DATETIME2(3) NULL,
    CreatedBy UNIQUEIDENTIFIER NOT NULL, CreatedDate DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy UNIQUEIDENTIFIER NULL, ModifiedDate DATETIME2(3) NULL, RowVersion ROWVERSION NOT NULL
);
CREATE UNIQUE INDEX UX_Tenant_Code ON platform.Tenant(Code) WHERE IsDeleted = 0;
CREATE INDEX IX_Tenant_Status ON platform.Tenant(Status) INCLUDE (Name, IsolationTier);

CREATE TABLE platform.TenantDatabase (                           -- tenant catalog: routing
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES platform.Tenant(Id),
    ServiceName VARCHAR(50) NOT NULL,                            -- 'corehr','time',...
    ConnectionSecretName VARCHAR(200) NOT NULL,                  -- Key Vault secret name, never the string
    ShardName VARCHAR(100) NOT NULL,                             -- 'pool-ci-01' or 'acme-dedicated'
    SchemaVersion VARCHAR(50) NULL,
    CONSTRAINT UX_TenantDatabase UNIQUE (TenantId, ServiceName)
);

CREATE TABLE platform.[Plan]            (Id UNIQUEIDENTIFIER PK, Code VARCHAR(32) UNIQUE, Name NVARCHAR(100), Edition TINYINT, MaxEmployees INT NULL, PriceJson NVARCHAR(MAX), IsPublic BIT, [STD-without-TenantId]);
CREATE TABLE platform.PlanFeature       (PlanId FK, FeatureCode VARCHAR(100), LimitValue INT NULL, PRIMARY KEY (PlanId, FeatureCode));
CREATE TABLE platform.Subscription      (Id PK, TenantId FK→Tenant UNIQUE-when-active, PlanId FK, Status TINYINT /*Trial,Active,PastDue,Cancelled*/, StartDate DATE, EndDate DATE NULL, TrialEndsOn DATE NULL, BillingRef VARCHAR(100), SeatLimit INT, [STD]);
CREATE TABLE platform.SubscriptionUsage (TenantId, UsageDate DATE, ActiveEmployees INT, ActiveUsers INT, StorageBytes BIGINT, ApiCalls BIGINT, PRIMARY KEY (TenantId, UsageDate));
CREATE TABLE platform.FeatureFlag       ([GLOBAL] Code VARCHAR(100) PK, Description, DefaultEnabled BIT, IsModule BIT);
CREATE TABLE platform.TenantFeature     (TenantId, FeatureCode, IsEnabled BIT, Source TINYINT /*Plan,Override,Beta*/, ExpiresOn DATE NULL, [STD], PK (TenantId, FeatureCode));
CREATE TABLE platform.TenantSetting     (Id, TenantId, Module VARCHAR(50), [Key] VARCHAR(100), ValueJson NVARCHAR(MAX), [STD], UX (TenantId, Module, [Key]));
CREATE TABLE platform.TenantDomain      (Id, TenantId, Domain VARCHAR(255), IsVerified BIT, VerificationToken, [STD], UX (Domain) WHERE IsDeleted=0);

-- ===================== gggs_identity =====================
CREATE TABLE iam.[User] (
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    UserName        NVARCHAR(256) NOT NULL,
    NormalizedUserName NVARCHAR(256) NOT NULL,
    Email           NVARCHAR(256) NOT NULL,
    NormalizedEmail NVARCHAR(256) NOT NULL,
    EmailConfirmed  BIT NOT NULL DEFAULT 0,
    PhoneNumber     VARCHAR(20) NULL,
    PasswordHash    NVARCHAR(MAX) NULL,               -- NULL for SSO-only users
    SecurityStamp   NVARCHAR(100) NOT NULL,           -- rotate → invalidates sessions
    UserType        TINYINT NOT NULL,                 -- 1 TenantUser 2 PlatformUser 3 ApiClient(service principal)
    EmployeeId      UNIQUEIDENTIFIER NULL,            -- link to Core HR (no cross-DB FK)
    Status          TINYINT NOT NULL,                 -- 1 Invited 2 Active 3 Locked 4 Disabled
    MfaEnabled      BIT NOT NULL DEFAULT 0,
    MfaMethod       TINYINT NULL,                     -- TOTP, Email, WebAuthn
    LockoutEnd      DATETIMEOFFSET NULL,
    AccessFailedCount INT NOT NULL DEFAULT 0,
    PasswordChangedDate DATETIME2(3) NULL,
    LastLoginDate   DATETIME2(3) NULL,
    PermissionVersion INT NOT NULL DEFAULT 1,         -- bump → cache invalidation
    PreferredLocale VARCHAR(10) NULL, TimeZone VARCHAR(64) NULL,
    /* [STD audit/soft-delete columns] */
    CONSTRAINT PK_User PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_User_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
);
CREATE UNIQUE INDEX UX_User_Tenant_Email ON iam.[User](TenantId, NormalizedEmail) WHERE IsDeleted = 0;
CREATE UNIQUE INDEX UX_User_Tenant_Employee ON iam.[User](TenantId, EmployeeId) WHERE EmployeeId IS NOT NULL AND IsDeleted = 0;
CREATE INDEX IX_User_NormalizedEmail ON iam.[User](NormalizedEmail) INCLUDE (TenantId, Status);   -- login discovery (runs under bypass principal)

CREATE TABLE iam.Permission (                         -- [GLOBAL] catalog owned by the platform, seeded from code
    Id INT NOT NULL CONSTRAINT PK_Permission PRIMARY KEY,
    Code VARCHAR(150) NOT NULL CONSTRAINT UX_Permission_Code UNIQUE,   -- 'leave.request.approve'
    Module VARCHAR(50) NOT NULL, Resource VARCHAR(50) NOT NULL, Action VARCHAR(50) NOT NULL,
    Description NVARCHAR(300) NOT NULL,
    IsSensitive BIT NOT NULL DEFAULT 0,               -- requires step-up MFA / extra audit
    SupportsDataScope BIT NOT NULL DEFAULT 1,
    FeatureCode VARCHAR(100) NULL,                    -- hidden if module not in plan
    IsPlatformOnly BIT NOT NULL DEFAULT 0
);

CREATE TABLE iam.Role (
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL, NormalizedName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    IsSystem BIT NOT NULL DEFAULT 0,                  -- seeded from template; name/permissions locked where required
    TemplateCode VARCHAR(50) NULL,                    -- 'HR_ADMIN'... for upgrades of default roles
    ParentRoleId UNIQUEIDENTIFIER NULL,               -- hierarchy: inherits parent's permissions
    /* [STD] */
    CONSTRAINT PK_Role PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_Role_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_Role_ParentRole FOREIGN KEY (TenantId, ParentRoleId) REFERENCES iam.Role(TenantId, Id)
);
CREATE UNIQUE INDEX UX_Role_Tenant_Name ON iam.Role(TenantId, NormalizedName) WHERE IsDeleted = 0;

CREATE TABLE iam.RolePermission (
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    RoleId UNIQUEIDENTIFIER NOT NULL,
    PermissionId INT NOT NULL CONSTRAINT FK_RolePermission_Permission REFERENCES iam.Permission(Id),
    DataScope TINYINT NOT NULL,                       -- 1 Own 2 DirectReports 3 AllReports 4 Department(+sub) 5 Branch 6 LegalEntity 7 BusinessUnit 8 Tenant
    /* [STD] */
    CONSTRAINT PK_RolePermission PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_RolePermission_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_RolePermission_Role FOREIGN KEY (TenantId, RoleId) REFERENCES iam.Role(TenantId, Id)
);
CREATE UNIQUE INDEX UX_RolePermission ON iam.RolePermission(TenantId, RoleId, PermissionId) WHERE IsDeleted = 0;

CREATE TABLE iam.UserRole (
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL, RoleId UNIQUEIDENTIFIER NOT NULL,
    ScopeType TINYINT NULL,                           -- optional anchor: Branch/Department/LegalEntity/BU
    ScopeId UNIQUEIDENTIFIER NULL,                    -- e.g. HR Manager for Branch X only
    ValidFrom DATE NOT NULL, ValidTo DATE NULL,       -- time-boxed grants (auditor, acting manager)
    /* [STD] */
    CONSTRAINT PK_UserRole PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_UserRole_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_UserRole_User FOREIGN KEY (TenantId, UserId) REFERENCES iam.[User](TenantId, Id),
    CONSTRAINT FK_UserRole_Role FOREIGN KEY (TenantId, RoleId) REFERENCES iam.Role(TenantId, Id)
);
CREATE UNIQUE INDEX UX_UserRole ON iam.UserRole(TenantId, UserId, RoleId, ScopeType, ScopeId) WHERE IsDeleted = 0;
CREATE INDEX IX_UserRole_Role ON iam.UserRole(TenantId, RoleId) WHERE IsDeleted = 0;

-- ===================== gggs_corehr =====================
CREATE TABLE emp.Employee (
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    EmployeeCode    NVARCHAR(30)  NOT NULL,           -- tenant numbering scheme (cfg.NumberSequence)
    FirstName       NVARCHAR(100) NOT NULL, MiddleName NVARCHAR(100) NULL, LastName NVARCHAR(100) NULL,
    DisplayName     NVARCHAR(200) NOT NULL,
    WorkEmail       NVARCHAR(256) NULL,
    PersonalEmailEnc VARBINARY(512) NULL,             -- PII encrypted
    MobileEnc       VARBINARY(256) NULL,
    MobileHash      BINARY(32) NULL,                  -- blind index
    DateOfBirthEnc  VARBINARY(256) NULL,
    BirthMonthDay   SMALLINT NULL,                    -- MMDD, non-identifying, for birthday widgets
    GenderCode      VARCHAR(20) NULL,                 -- tenant lookup
    MaritalStatusCode VARCHAR(20) NULL,
    NationalityCode CHAR(2) NULL,
    PhotoDocumentId UNIQUEIDENTIFIER NULL,
    EmployeeStatus  TINYINT NOT NULL,                 -- 1 PreBoarding 2 Probation 3 Confirmed 4 OnNotice 5 Exited 6 Absconded
    DateOfJoining   DATE NOT NULL,
    OriginalHireDate DATE NULL,                       -- rehires
    ConfirmationDate DATE NULL, ExitDate DATE NULL,
    UserId          UNIQUEIDENTIFIER NULL,
    CustomFields    NVARCHAR(MAX) NULL CONSTRAINT CK_Employee_CustomFields CHECK (ISJSON(CustomFields) = 1),
    /* [STD] */
    CONSTRAINT PK_Employee PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_Employee_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = emp.EmployeeHistoryTemporal));   -- + ValidFrom/ValidTo PERIOD columns
CREATE UNIQUE INDEX UX_Employee_Code  ON emp.Employee(TenantId, EmployeeCode) WHERE IsDeleted = 0;
CREATE UNIQUE INDEX UX_Employee_WorkEmail ON emp.Employee(TenantId, WorkEmail) WHERE WorkEmail IS NOT NULL AND IsDeleted = 0;
CREATE INDEX IX_Employee_Status ON emp.Employee(TenantId, EmployeeStatus) INCLUDE (DisplayName, EmployeeCode, DateOfJoining) WHERE IsDeleted = 0;
CREATE INDEX IX_Employee_Name   ON emp.Employee(TenantId, DisplayName) WHERE IsDeleted = 0;
CREATE INDEX IX_Employee_Birthday ON emp.Employee(TenantId, BirthMonthDay) WHERE IsDeleted = 0 AND EmployeeStatus < 5;

CREATE TABLE emp.EmploymentRecord (                   -- effective-dated job information
    Id UNIQUEIDENTIFIER NOT NULL, TenantId UNIQUEIDENTIFIER NOT NULL,
    EmployeeId UNIQUEIDENTIFIER NOT NULL,
    EffectiveFrom DATE NOT NULL, EffectiveTo DATE NULL, IsCurrent BIT NOT NULL,
    LegalEntityId UNIQUEIDENTIFIER NOT NULL, BusinessUnitId UNIQUEIDENTIFIER NULL,
    BranchId UNIQUEIDENTIFIER NOT NULL, LocationId UNIQUEIDENTIFIER NULL,
    DepartmentId UNIQUEIDENTIFIER NOT NULL, DesignationId UNIQUEIDENTIFIER NOT NULL,
    GradeId UNIQUEIDENTIFIER NULL, CostCenterId UNIQUEIDENTIFIER NULL, PositionId UNIQUEIDENTIFIER NULL,
    ReportingManagerId UNIQUEIDENTIFIER NULL,         -- Employee.Id
    FunctionalManagerId UNIQUEIDENTIFIER NULL,        -- dotted line
    EmploymentType TINYINT NOT NULL,                  -- Permanent, Contract, Intern, Consultant
    WorkerCategoryCode VARCHAR(30) NULL,
    ProbationEndDate DATE NULL, NoticePeriodDays SMALLINT NULL,
    ChangeType TINYINT NOT NULL,                      -- Hire, Transfer, Promotion, Redesignation, ManagerChange, Correction
    ChangeReason NVARCHAR(500) NULL,
    /* [STD] */
    CONSTRAINT PK_EmploymentRecord PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UX_EmploymentRecord_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_EmploymentRecord_Employee   FOREIGN KEY (TenantId, EmployeeId)   REFERENCES emp.Employee(TenantId, Id),
    CONSTRAINT FK_EmploymentRecord_Department FOREIGN KEY (TenantId, DepartmentId) REFERENCES org.Department(TenantId, Id),
    CONSTRAINT FK_EmploymentRecord_Manager    FOREIGN KEY (TenantId, ReportingManagerId) REFERENCES emp.Employee(TenantId, Id),
    -- ... same pattern for LegalEntity, Branch, Designation, Grade, CostCenter
    CONSTRAINT CK_EmploymentRecord_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE UNIQUE INDEX UX_EmploymentRecord_Current ON emp.EmploymentRecord(TenantId, EmployeeId) WHERE IsCurrent = 1 AND IsDeleted = 0;
CREATE INDEX IX_EmploymentRecord_Timeline ON emp.EmploymentRecord(TenantId, EmployeeId, EffectiveFrom DESC);
CREATE INDEX IX_EmploymentRecord_Dept     ON emp.EmploymentRecord(TenantId, DepartmentId) INCLUDE (EmployeeId, DesignationId) WHERE IsCurrent = 1 AND IsDeleted = 0;
CREATE INDEX IX_EmploymentRecord_Manager  ON emp.EmploymentRecord(TenantId, ReportingManagerId) INCLUDE (EmployeeId) WHERE IsCurrent = 1 AND IsDeleted = 0;
CREATE INDEX IX_EmploymentRecord_Branch   ON emp.EmploymentRecord(TenantId, BranchId) INCLUDE (EmployeeId) WHERE IsCurrent = 1 AND IsDeleted = 0;
```

### 7.2 Remaining tables (compact spec)

Format: **Table** — key columns · FKs · unique (UX) · indexes (IX). All have [STD] + TenantId unless marked [GLOBAL].

**Identity (`iam`)**
- `UserLogin` — UserId, Provider, ProviderKey, ProviderDisplayName · FK User · UX (Provider, ProviderKey) global.
- `TenantIdentityProvider` — Type (EntraID/OIDC/SAML), DisplayName, Authority/MetadataUrl, ClientId, ClientSecretRef (Key Vault), AllowedDomains, JitProvisioning BIT, DefaultRoleId, EnforceSso BIT · UX (TenantId, DisplayName).
- `UserSession` — UserId, SessionKey (hash), DeviceName, UserAgent, IpAddress, AuthMethod, MfaSatisfiedDate, CreatedDate, LastSeenDate, ExpiresDate, RevokedDate, RevokedReason · IX (TenantId, UserId, RevokedDate).
- `RefreshToken` — handled by OpenIddict `OpenIddictTokens` (reference tokens, rotation, revocation), linked to SessionId.
- `PasswordPolicy` — per tenant: MinLength (≥12), RequireComplexity, HistoryCount, MaxAgeDays (0 = none), LockoutThreshold, LockoutMinutes, IdleTimeoutMinutes, AbsoluteSessionHours, MfaRequirement (Off/Admins/All) · UX (TenantId).
- `PasswordHistory` — UserId, PasswordHash, CreatedDate · IX (TenantId, UserId, CreatedDate DESC).
- `UserMfaCredential` — UserId, Type, SecretEnc / WebAuthnCredentialId, PublicKey, LastUsedDate.
- `ApiClient` — ClientId, Name, AllowedScopes, SecretHash, IpAllowList, ExpiresDate (tenant-scoped public API clients).
- `ScimToken` — for Entra SCIM provisioning, hashed.
- `SupportAccessGrant` — GrantedByUserId (tenant admin), PlatformUserId, Reason, ValidFrom, ValidTo, Scope (read-only/full) · IX (TenantId, PlatformUserId, ValidTo).

**Organization (`org`, Core HR)**
- `LegalEntity` — Code, Name, RegistrationNo, TaxRegistrationJson (country-specific IDs), CountryCode, Currency, AddressJson, LogoDocumentId, CompliancePackCode · UX (TenantId, Code).
- `BusinessUnit` — Code, Name, HeadEmployeeId · UX (TenantId, Code).
- `Branch` — LegalEntityId FK, Code, Name, AddressJson, CountryCode, StateCode, TimeZone, GeoLat, GeoLng, GeoFenceMeters · UX (TenantId, Code) · IX (TenantId, LegalEntityId).
- `Location` — BranchId FK, Code, Name, Type (Office/Site/Remote) · UX (TenantId, Code).
- `Department` — Code, Name, ParentDepartmentId FK(self), OrgPath HIERARCHYID, HeadEmployeeId, CostCenterId FK, BusinessUnitId FK · UX (TenantId, Code) · IX (TenantId, OrgPath) · IX (TenantId, ParentDepartmentId).
- `Designation` — Code, Title, JobFamily, JobLevel · UX (TenantId, Code).
- `Grade` — Code, Name, Rank INT, SalaryBandMin, SalaryBandMax, Currency · UX (TenantId, Code).
- `CostCenter` — Code, Name, GLCode, OwnerEmployeeId, ParentCostCenterId · UX (TenantId, Code).
- `Position` (optional) — Code, DesignationId, DepartmentId, BranchId, IsBudgeted, Status (Vacant/Filled/Frozen) · UX (TenantId, Code).
- `Skill` — Name, Category · UX (TenantId, Name).

**Employee (`emp`)**
- `EmployeeAddress` — EmployeeId FK, Type (Current/Permanent), AddressLine1Enc…, City, StateCode, CountryCode, PostalCode.
- `EmergencyContact` — EmployeeId FK, Name, Relationship, PhoneEnc, IsPrimary.
- `EmployeeDependent` — EmployeeId FK, Name, Relationship, DateOfBirthEnc, IsNominee, NominationPercent.
- `EmployeeEducation` — EmployeeId FK, Degree, Institution, Specialization, StartYear, EndYear, Grade, DocumentId.
- `EmployeeExperience` — EmployeeId FK, Employer, Title, FromDate, ToDate, LastCtcEnc NULL, DocumentId.
- `EmployeeSkill` — EmployeeId FK, SkillId FK, Proficiency (1–5), IsPrimary · UX (TenantId, EmployeeId, SkillId).
- `EmployeeBankAccount` (temporal) — EmployeeId FK, BankName, BranchCode (IFSC/SWIFT/IBAN routing), AccountNumberEnc, AccountNumberLast4, AccountNumberHash, AccountHolderName, PaymentMode, IsPrimary, VerifiedStatus · UX (TenantId, EmployeeId) WHERE IsPrimary=1.
- `EmployeeIdentityDocument` — EmployeeId FK, DocTypeCode (PAN, Aadhaar, SSN, Emirates ID, Passport…), NumberEnc, NumberHash, NumberLast4, IssueDate, ExpiryDate, DocumentId · UX (TenantId, DocTypeCode, NumberHash).
- `EmployeeTaxProfile` — EmployeeId FK, CountryCode, TaxRegimeCode, TaxIdEnc, ResidencyStatus, AttributesJson (pack-defined).
- `EmployeeEvent` — EmployeeId FK, EventType (Hired, Confirmed, Transferred, Promoted, Resigned, Exited, Rehired…), EffectiveDate, PayloadJson, SourceRef · IX (TenantId, EmployeeId, EffectiveDate). This is the business-readable history timeline; temporal tables provide the field-level history.
- `EmployeeExit` — EmployeeId FK, ResignationDate, RequestedLastDay, ApprovedLastDay, ExitType, ReasonCode, IsRehireEligible, WorkflowInstanceId, ClearanceStatus, FnFStatus.
- `ReportingLine` — closure table (EmployeeId, ManagerEmployeeId, Depth) · PK (TenantId, ManagerEmployeeId, EmployeeId) · IX (TenantId, EmployeeId).

**Configuration (`cfg`)**
- `CustomFieldDefinition` — EntityType, FieldKey, Label (i18n JSON), DataType, OptionsJson, IsRequired, IsSearchable, IsSensitive, ValidationJson, SectionCode, DisplayOrder · UX (TenantId, EntityType, FieldKey).
- `LookupType` / `LookupValue` — tenant picklists (gender, blood group, exit reasons…) · UX (TenantId, TypeCode, Code).
- `NumberSequence` — EntityType, Prefix, NextValue, Padding, ResetPolicy · UX (TenantId, EntityType). Values are allocated with `UPDATE … OUTPUT` for concurrency.

**Asset (`asset`)**
- `AssetCategory` — Code, Name, DepreciationMethod, UsefulLifeMonths, RequiresSerial.
- `Asset` — AssetTag, CategoryId FK, Make, Model, SerialNumber, PurchaseDate, PurchaseCost, Currency, VendorName, WarrantyEndDate, BranchId, Status (InStock/Assigned/InRepair/Retired/Lost), Condition · UX (TenantId, AssetTag) · UX (TenantId, CategoryId, SerialNumber) · IX (TenantId, Status).
- `AssetAssignment` — AssetId FK, EmployeeId, AssignedDate, ExpectedReturnDate, ReturnedDate, ConditionOut, ConditionIn, AcknowledgedDate, WorkflowInstanceId · UX (TenantId, AssetId) WHERE ReturnedDate IS NULL · IX (TenantId, EmployeeId).
- `SoftwareLicense` — Product, Vendor, LicenseType (per seat/per device), TotalSeats, ExpiryDate, CostPerSeat · `LicenseAssignment` — LicenseId FK, EmployeeId, AssignedDate, RevokedDate.
- `AssetMaintenance` — AssetId FK, Type, Date, Cost, Vendor, Notes.

**Time — Attendance (`time`)**
- `Shift` — Code, Name, StartTime, EndTime, BreakMinutes, IsNightShift, GraceInMinutes, GraceOutMinutes, FullDayMinMinutes, HalfDayMinMinutes, WeeklyOffPatternJson · UX (TenantId, Code).
- `ShiftRoster` — EmployeeId, RosterDate DATE, ShiftId FK, IsWeeklyOff, Source (Pattern/Manual/Swap) · UX (TenantId, EmployeeId, RosterDate) · IX (TenantId, RosterDate, ShiftId).
- `AttendancePolicy` — Name, RulesJson (late-mark → half-day rules, OT eligibility/multipliers, min hours, geo-fence required, allowed punch sources, regularization limits per month), Version, EffectiveFrom.
- `PolicyAssignment` (shared pattern for attendance/leave/expense policies) — PolicyType, PolicyId, TargetType (Tenant/LegalEntity/Branch/Department/Grade/EmploymentType/Employee), TargetId, Priority, EffectiveFrom/To · IX (TenantId, PolicyType, TargetType, TargetId).
- `AttendancePunch` (partitioned by month, no soft delete, append-only) — EmployeeId, PunchTimeUtc, Direction (In/Out/Auto), Source (Web/Mobile/Biometric/Import/Manual), DeviceId, Latitude, Longitude, IpAddress, SelfieDocumentId NULL, ExternalRef, IsValid · **Clustered (TenantId, EmployeeId, PunchTimeUtc)** · UX (TenantId, Source, ExternalRef) WHERE ExternalRef IS NOT NULL (device idempotency).
- `AttendanceDay` — EmployeeId, AttendanceDate, ShiftId, FirstIn, LastOut, WorkedMinutes, BreakMinutes, LateMinutes, EarlyOutMinutes, OvertimeMinutes, Status (Present/Absent/HalfDay/OnLeave/WeeklyOff/Holiday/OnDuty), LeaveRequestId NULL, IsRegularized, IsLocked (payroll lock), ComputedVersion · UX (TenantId, EmployeeId, AttendanceDate) · IX (TenantId, AttendanceDate, Status).
- `AttendanceRegularization` — EmployeeId, AttendanceDate, RequestedIn, RequestedOut, ReasonCode, Remarks, Status, WorkflowInstanceId · IX (TenantId, EmployeeId, AttendanceDate).
- `OvertimeRequest` — EmployeeId, Date, Minutes, Reason, Status, WorkflowInstanceId, ApprovedMinutes.
- `BiometricDevice` — SerialNumber, Vendor, Model, BranchId, ConnectorId, ApiKeyHash, LastSeenDate, Status · UX (TenantId, SerialNumber).
- `DeviceUserMap` — DeviceId FK, DeviceUserCode, EmployeeId · UX (TenantId, DeviceId, DeviceUserCode).
- `HolidayCalendar` — Name, Year, CountryCode, StateCode · `Holiday` — CalendarId FK, HolidayDate, Name, IsOptional, IsRestricted · UX (TenantId, CalendarId, HolidayDate) · `HolidayCalendarAssignment` — CalendarId, TargetType (Branch/Location/LegalEntity), TargetId.

**Time — Leave (`leave`)**
- `LeaveType` — Code, Name, IsPaid, Unit (Day/HalfDay/Hour), ColorHex, GenderRestriction, RequiresAttachmentAfterDays, IsCompOff, IsLop · UX (TenantId, Code).
- `LeavePolicy` — Name, Version, EffectiveFrom · `LeavePolicyRule` — PolicyId FK, LeaveTypeId FK, AccrualFrequency (Monthly/Quarterly/Yearly/OnJoining), AccrualAmount, AccrualTiming (Start/End), ProRataOnJoining BIT, MaxBalance, CarryForwardMax, CarryForwardExpiryMonths, EncashmentAllowed, EncashmentMaxDays, EligibleAfterDays (probation), MinNoticeDays, MaxConsecutiveDays, MinDaysPerRequest, AllowHalfDay, AllowNegativeBalance, NegativeLimit, SandwichRule, CountHolidays, CountWeeklyOffs · UX (TenantId, PolicyId, LeaveTypeId).
- `LeaveLedger` (**append-only source of truth**) — EmployeeId, LeaveTypeId, PeriodYear, TransactionType (Opening/Accrual/Debit/Credit/Reversal/Lapse/Encash/CarryForward/Adjustment), Days DECIMAL(6,2), TransactionDate, SourceType, SourceId, Remarks · IX (TenantId, EmployeeId, LeaveTypeId, PeriodYear) · UX (TenantId, SourceType, SourceId, TransactionType) for idempotent postings.
- `LeaveBalance` (projection, rebuilt from the ledger) — EmployeeId, LeaveTypeId, PeriodYear, Opening, Accrued, Taken, PendingApproval, Adjusted, Encashed, Lapsed, CarriedForward, Available (computed), RowVersion · UX (TenantId, EmployeeId, LeaveTypeId, PeriodYear).
- `LeaveRequest` — RequestNo, EmployeeId, LeaveTypeId, FromDate, ToDate, FromSession, ToSession (First/Second half), TotalDays, Reason, AttachmentDocumentId, Status (Draft/Pending/Approved/Rejected/Cancelled/Withdrawn), WorkflowInstanceId, AppliedByUserId (on-behalf) · IX (TenantId, EmployeeId, FromDate) · IX (TenantId, Status, FromDate).
- `LeaveRequestDay` — LeaveRequestId FK, LeaveDate, DayFraction, IsCounted · UX (TenantId, LeaveRequestId, LeaveDate) · IX (TenantId, LeaveDate) (overlap checks, team calendar).
- `LeaveEncashmentRequest` — EmployeeId, LeaveTypeId, Days, Status, WorkflowInstanceId, PayrollInputId.
- `CompOffGrant` — EmployeeId, WorkedDate, Days, ExpiryDate, Status.

**Payroll (`pay`, `comp`)**
- `comp.CompliancePack` [GLOBAL] — Code (e.g. `IN`, `AE`, `GB`), Name, CountryCode, Version, EffectiveFrom, EffectiveTo, Status (Draft/Published), Checksum · UX (Code, Version).
- `comp.StatutoryRule` [GLOBAL] — PackId FK, RuleCode (`IN.PF.EMPLOYEE`, `IN.PT.KA`, `AE.GRATUITY`), RuleType (Formula/Slab/Lookup), ExpressionText (sandboxed formula DSL), ParametersJson, EffectiveFrom · `comp.TaxSlab` [GLOBAL] — PackId, RegimeCode, FromAmount, ToAmount, Rate, FixedAmount, Surcharge/Cess JSON.
- `pay.TenantStatutoryConfig` — LegalEntityId, RuleCode, IsEnabled, OverridesJson (registration numbers, employer rates where allowed) · UX (TenantId, LegalEntityId, RuleCode).
- `PayGroup` — Code, LegalEntityId, Frequency (Monthly/SemiMonthly/BiWeekly/Weekly), Currency, CompliancePackCode, PayDayRule, AttendanceCutoffDay · UX (TenantId, Code).
- `PayPeriod` — PayGroupId FK, PeriodCode (`2026-09`), StartDate, EndDate, CutoffDate, PayDate, Status (Open/InputsLocked/Processing/Closed) · UX (TenantId, PayGroupId, PeriodCode).
- `SalaryComponent` — Code, Name, Kind (Earning/Deduction/EmployerContribution/Reimbursement/Informational), CalcType (Fixed/PercentOf/Formula/Slab/Statutory), Formula (DSL, e.g. `BASIC * 0.4`), StatutoryRuleCode, IsTaxable, TaxExemptionCode, IsProRated, AffectsLop, PayslipOrder, GLAccountCode, IsActive · UX (TenantId, Code).
- `SalaryStructure` — Code, Name, LegalEntityId, CTCBasis · `SalaryStructureComponent` — StructureId FK, ComponentId FK, DefaultFormula, DisplayOrder · UX (TenantId, StructureId, ComponentId).
- `EmployeeSalary` (temporal, effective-dated) — EmployeeId, PayGroupId, StructureId, AnnualCTC DECIMAL(18,2), Currency, EffectiveFrom, EffectiveTo, IsCurrent, RevisionReason, RevisionWorkflowInstanceId · UX (TenantId, EmployeeId) WHERE IsCurrent=1 · IX (TenantId, EmployeeId, EffectiveFrom DESC).
- `EmployeeSalaryComponent` — EmployeeSalaryId FK, ComponentId FK, MonthlyAmount, AnnualAmount, IsOverride.
- `PayrollInput` — PayPeriodId, EmployeeId, ComponentId, Amount, Quantity, Source (Manual/Import/Expense/Leave/Overtime/Bonus), SourceRef, Status · UX (TenantId, PayPeriodId, EmployeeId, ComponentId, Source, SourceRef).
- `PayrollRun` — RunNo, PayGroupId, PayPeriodId, RunType (Regular/OffCycle/Supplementary/FnF/Reversal), Status (Draft/Calculating/Calculated/InReview/Approved/Finalized/PaymentInitiated/Paid/Reverted), EmployeeCount, GrossTotal, NetTotal, EmployerCostTotal, CalculatedDate, FinalizedDate, WorkflowInstanceId, InitiatedByUserId, PackVersion · UX (TenantId, PayGroupId, PayPeriodId, RunType) WHERE RunType='Regular' AND Status<>'Reverted'.
- `PayrollRunEmployee` — RunId FK, EmployeeId, Status (Pending/Calculated/Error/Hold/Excluded), PaidDays, LopDays, Gross, TotalDeductions, Net, EmployerContributions, InputSnapshotJson (attendance, salary, pack version used → reproducibility), ErrorMessage · UX (TenantId, RunId, EmployeeId).
- `PayrollRunLine` — RunEmployeeId FK, ComponentId, Amount, YtdAmount, CalcTraceJson (explains the calculation) · IX (TenantId, RunEmployeeId).
- `Payslip` — RunEmployeeId FK, EmployeeId, PayPeriodId, DocumentId, PublishedDate, IsPasswordProtected · IX (TenantId, EmployeeId, PayPeriodId).
- `TaxDeclaration` — EmployeeId, FinancialYear, RegimeCode, DeclarationJson, Status, LockedDate · `TaxProof` — DeclarationId FK, SectionCode, Amount, DocumentId, VerificationStatus.
- `EmployeeLoan` — EmployeeId, Type, Principal, InterestRate, Emi, StartPeriod, Outstanding, Status · `LoanRepayment` — LoanId, PayrollRunEmployeeId, Amount.
- `BankPaymentBatch` — RunId FK, LegalEntityId, BankFormatCode, FileDocumentId, Status, TotalAmount, IntegrationRef · `BankPaymentLine` — BatchId, EmployeeId, Amount, AccountNumberLast4, Status, FailureReason.
- `GLJournal` — RunId, LegalEntityId, JournalDate, Status, ExportedDate, ExternalRef · `GLJournalLine` — JournalId, GLAccountCode, CostCenterCode, Debit, Credit.
- `FullAndFinalSettlement` — EmployeeId, ExitId, LastWorkingDay, LeaveEncashDays, GratuityAmount, NoticeRecovery, NetPayable, RunId, Status.

**Talent (`rec`, `onb`, `perf`, `lms`)**
- `rec.JobRequisition` — ReqNo, Title, DepartmentId, DesignationId, BranchId, PositionId, Openings, EmploymentType, BudgetMin/Max, HiringManagerId, RecruiterUserId, Justification, Status, WorkflowInstanceId · UX (TenantId, ReqNo).
- `rec.JobPosting` — RequisitionId FK, Title, DescriptionHtml (sanitized), Location, IsInternal, IsPublic, PublishedDate, ClosesDate, Slug · UX (TenantId, Slug) · `rec.PostingChannel` — PostingId, Channel (Careers/LinkedIn/Indeed/Naukri/Referral), ExternalRef, Status.
- `rec.Candidate` — FirstName, LastName, EmailHash + EmailEnc, PhoneEnc, CurrentEmployer, TotalExperienceMonths, Source, ConsentGivenDate, ConsentExpiryDate, AnonymizedDate · UX (TenantId, EmailHash).
- `rec.CandidateResume` — CandidateId FK, DocumentId, ParsedJson, ParserVersion.
- `rec.PipelineStage` — Code, Name, Order, StageType (Screen/Interview/Offer/Hired/Rejected) · UX (TenantId, Code).
- `rec.Application` — CandidateId FK, PostingId FK, StageId FK, Status, AppliedDate, RejectionReasonCode, ReferredByEmployeeId · UX (TenantId, CandidateId, PostingId) · IX (TenantId, PostingId, StageId).
- `rec.Interview` — ApplicationId FK, RoundName, ScheduledStart, ScheduledEnd, Mode, MeetingLink, Status · `rec.InterviewPanelist` — InterviewId, EmployeeId/ExternalEmail · `rec.InterviewFeedback` — InterviewId, PanelistId, ScorecardJson, OverallRating, Recommendation (StrongHire/Hire/NoHire), SubmittedDate · UX (TenantId, InterviewId, PanelistId).
- `rec.Offer` — ApplicationId FK, OfferNo, DesignationId, GradeId, AnnualCTC, Currency, JoiningDate, ExpiryDate, DocumentId, Status (Draft/PendingApproval/Sent/Accepted/Declined/Revoked), WorkflowInstanceId, AcceptedDate.
- `onb.OnboardingTemplate` — Name, AppliesToJson (legal entity/branch/designation filters) · `onb.OnboardingTemplateTask` — TemplateId, Title, TaskType (Form/Document/ITAccount/Asset/Orientation/Acknowledge/Custom), AssigneeRole (NewHire/Manager/HR/IT/Admin), DueOffsetDays, IsMandatory.
- `onb.OnboardingCase` — EmployeeId, OfferId, TemplateId, StartDate, Status, CompletionPercent · `onb.OnboardingTask` — CaseId FK, TemplateTaskId, AssigneeUserId, DueDate, Status, CompletedDate, OutputRef.
- `perf.PerformanceCycle` — Name, Type (Annual/HalfYearly/Quarterly), StartDate, EndDate, Status, PhasesJson (goal-setting, self, manager, calibration, release dates), TemplateId.
- `perf.ReviewTemplate` — Name, SectionsJson (goals weight, competencies, questions), RatingScaleId · `perf.RatingScale` — Name, LevelsJson.
- `perf.KpiLibrary` — Code, Name, Unit, Direction (HigherBetter/LowerBetter), DepartmentId.
- `perf.Goal` — EmployeeId, CycleId, ParentGoalId (cascading), Title, Description, KpiId, Weight, TargetValue, CurrentValue, Unit, DueDate, Status, Visibility · IX (TenantId, EmployeeId, CycleId) · `perf.GoalCheckIn` — GoalId, Date, Value, Comment, ByUserId.
- `perf.Review` — CycleId, EmployeeId, ManagerEmployeeId, Stage, FinalRating, CalibratedRating, ReleasedDate · UX (TenantId, CycleId, EmployeeId).
- `perf.ReviewResponse` — ReviewId FK, ReviewerEmployeeId, ReviewerType (Self/Manager/Peer/DirectReport/Skip), AnswersJson, Rating, SubmittedDate · UX (TenantId, ReviewId, ReviewerEmployeeId, ReviewerType).
- `perf.FeedbackRequest` (360) — ReviewId, RequestedFromEmployeeId, Status, DueDate, IsAnonymous.
- `lms.Course` — Code, Title, Type (Online/Classroom/External), DurationHours, ContentUrl/DocumentId, CertificationId · `lms.TrainingProgram` — Name, CourseIdsJson, Audience · `lms.TrainingSession` — CourseId, TrainerName, StartDate, EndDate, Venue, Capacity · `lms.Enrollment` — EmployeeId, CourseId/SessionId, Status, AssignedByUserId, DueDate, CompletedDate, Score · UX (TenantId, EmployeeId, SessionId) · `lms.Certification` — Name, Issuer, ValidityMonths · `lms.EmployeeCertification` — EmployeeId, CertificationId, IssuedDate, ExpiryDate, DocumentId · IX (TenantId, ExpiryDate).

**Spend (`exp`, `trv`)**
- `exp.ExpenseCategory` — Code, Name, GLAccountCode, ReceiptRequiredAbove, RequiresAttendees, IsMileage, MileageRate · UX (TenantId, Code).
- `exp.ExpensePolicy` — Name, RulesJson (per grade/category daily & per-claim limits, per diem by city tier) + PolicyAssignment.
- `exp.ExpenseClaim` — ClaimNo, EmployeeId, Title, TravelRequestId NULL, Currency, TotalAmount, ApprovedAmount, Status (Draft/Submitted/Approved/Rejected/PartiallyApproved/Reimbursed), WorkflowInstanceId, ReimbursementMode (Payroll/Bank), ReimbursedDate · UX (TenantId, ClaimNo) · IX (TenantId, EmployeeId, Status).
- `exp.ExpenseLine` — ClaimId FK, CategoryId FK, ExpenseDate, Merchant, Amount, Currency, ExchangeRate, BaseAmount, ReceiptDocumentId, PolicyViolationsJson, ApprovedAmount, CostCenterId.
- `exp.Reimbursement` — ClaimId FK, Mode, PayrollInputId / BankPaymentRef, Amount, PaidDate.
- `trv.TravelRequest` — RequestNo, EmployeeId, Purpose, TripType (Domestic/International), FromDate, ToDate, EstimatedCost, Currency, Status, WorkflowInstanceId · `trv.TravelItinerary` — TravelRequestId, Segment, FromCity, ToCity, Mode, DepartDate, BookingRef, Cost · `trv.TravelAdvance` — TravelRequestId, Amount, Currency, Status, DisbursedDate, SettledAmount · `trv.TravelSettlement` — TravelRequestId, AdvanceTotal, ExpenseTotal, NetPayable, NetRecoverable, RecoveryMode, Status.

**Workflow (`wf`)**
- `WorkflowDefinition` — Code (`LEAVE_REQUEST`, `EXPENSE_CLAIM`, `PAYROLL_RUN`…), Name, EntityType, OwningService, IsSystem · UX (TenantId, Code).
- `WorkflowDefinitionVersion` — DefinitionId FK, Version INT, Status (Draft/Published/Retired), DefinitionJson, PublishedDate · UX (TenantId, DefinitionId, Version).
- `WorkflowStep` — VersionId FK, StepKey, Name, Order, ApproverRuleType, ApproverRuleJson, ApprovalMode (Any/All/Quorum), QuorumCount, ConditionJson (JsonLogic "enter step if"), SlaHours, ReminderHours, EscalationRuleJson, OnTimeout (Escalate/AutoApprove/AutoReject/Notify), AllowDelegation, AllowSendBack.
- `WorkflowInstance` — VersionId FK, EntityType, EntityId, SourceService, InitiatorUserId, SubjectEmployeeId, Status (Running/Approved/Rejected/Cancelled/Error), CurrentStepKey, ContextJson (amount, days, dept…), CorrelationId, StartedDate, CompletedDate · UX (TenantId, EntityType, EntityId) WHERE Status='Running' · IX (TenantId, InitiatorUserId, Status).
- `WorkflowTask` — InstanceId FK, StepKey, AssigneeUserId, OriginalAssigneeUserId, DelegationId NULL, Status (Pending/Approved/Rejected/SentBack/Delegated/Escalated/Skipped/Cancelled), DueDate, ActedDate, Comment · **IX (TenantId, AssigneeUserId, Status, DueDate)** (the approvals inbox) · IX (TenantId, Status, DueDate) (SLA scanner).
- `WorkflowActionLog` — InstanceId, TaskId, Action, ActorUserId, OnBehalfOfUserId, Comment, OccurredDate (append-only).
- `Delegation` — FromUserId, ToUserId, StartDate, EndDate, DefinitionCodesJson NULL (all), Reason · IX (TenantId, FromUserId, StartDate, EndDate).
- `ApproverGroup` / `ApproverGroupMember` — named groups (e.g. "Finance Approvers – Branch X").

**Notification (`ntf`)**
- `NotificationEvent` [GLOBAL] — EventCode, Module, AvailableVariablesJson, DefaultChannels, IsMandatory (e.g. security alerts cannot be muted).
- `NotificationTemplate` — TenantId NULL = platform default (the RLS predicate permits NULL for reads), EventCode, Channel, Locale, Subject, BodyTemplate (Scriban), Version, IsActive · UX (TenantId, EventCode, Channel, Locale).
- `ChannelConfiguration` — Channel, ProviderCode, SettingsJson, SecretRef, FromAddress/SenderId, IsEnabled, DailyQuota · UX (TenantId, Channel, ProviderCode).
- `NotificationPreference` — UserId, EventCode, Channel, IsEnabled, DigestMode · UX (TenantId, UserId, EventCode, Channel).
- `OutboundMessage` (partitioned monthly) — Channel, RecipientUserId, RecipientAddressEnc, EventCode, TemplateVersion, Status (Queued/Sent/Delivered/Failed/Suppressed), Attempts, NextAttemptDate, ProviderCode, ProviderMessageId, IdempotencyKey, Error · UX (TenantId, IdempotencyKey) · IX (Status, NextAttemptDate).
- `InAppNotification` — UserId, Title, Body, Link, Category, IsRead, ReadDate, CreatedDate, ExpiresDate · IX (TenantId, UserId, IsRead, CreatedDate DESC).
- `DeviceToken` — UserId, Platform (Web/Android/iOS), Token, LastSeenDate.

**Document (`doc`)**
- `DocumentType` — Code, Name, Category (Employee/Company/Policy/Payroll/Recruitment), AllowedExtensions, MaxSizeMb, RequiresExpiry, RetentionDays, RetentionTrigger (Upload/Exit/Expiry), ViewPermissionCode, IsEmployeeUploadable, IsSensitive · UX (TenantId, Code).
- `Document` — DocumentGroupId (all versions), Version INT, IsCurrentVersion, OwnerType, OwnerId, DocumentTypeId FK, FileName, ContentType, SizeBytes, StorageKey, Sha256, ScanStatus (Pending/Clean/Infected/Failed), Sensitivity, ExpiryDate, RetentionUntil, LegalHold BIT, UploadedByUserId, Source (Upload/Generated/Integration) · IX (TenantId, OwnerType, OwnerId, DocumentTypeId) · IX (TenantId, ExpiryDate) WHERE ExpiryDate IS NOT NULL · IX (RetentionUntil) WHERE LegalHold=0.
- `DocumentTemplate` — Code, Name, Category, EngineType (HtmlScriban→PDF / DOCX merge), BodyTemplate, HeaderFooterJson, Locale, Version · UX (TenantId, Code, Locale, Version).
- `DocumentAcknowledgement` — DocumentId, EmployeeId, AcknowledgedDate, IpAddress · UX (TenantId, DocumentId, EmployeeId).
- `DocumentAccessLog` — DocumentId, UserId, Action (View/Download/Share), IpAddress, OccurredDate (append-only).

**Audit (`audit`, in `gggs_platform` or its own DB)**
- `AuditLog` — **ledger table (append-only)**, Id BIGINT, TenantId, OccurredDate, UserId, ImpersonatorUserId, ActorType (User/System/ApiClient/Support), IpAddress, UserAgent, Service, Action (Create/Update/Delete/Approve/Login/Export…), EntityType, EntityId, OldValuesJson, NewValuesJson, ChangedFieldsCsv, RequestId, CorrelationId, Outcome · partitioned monthly · clustered (TenantId, OccurredDate, Id) · IX (TenantId, EntityType, EntityId, OccurredDate) · IX (TenantId, UserId, OccurredDate).
- `SecurityEvent` — TenantId NULL, UserId, EventType (LoginSuccess/LoginFailed/Lockout/MfaFailed/PermissionDenied/TenantViolation/TokenReuse), IpAddress, DetailsJson, Severity.
- `SensitiveDataAccess` — "who viewed salary/bank/national ID": UserId, SubjectEmployeeId, DataCategory, Endpoint, OccurredDate.
- `DataExportLog` — UserId, ExportType, Filters, RowCount, DocumentId, OccurredDate.
- `ApiLog` (sampled, 30–90 days) — Method, Route template (no query PII), StatusCode, DurationMs, ClientId, TenantId, CorrelationId.
- `RetentionPolicy` — EntityType, CountryCode NULL, RetainDays, Trigger, Action (Anonymize/Delete/Archive), LegalBasis · UX (TenantId, EntityType, CountryCode).
- `DataSubjectRequest` — SubjectType (Employee/Candidate), SubjectId, RequestType (Export/Erasure/Rectification), Status, DueDate, ResultDocumentId.

**Reporting (`rpt`)** — `DimDate`, `DimOrgUnit`, `EmployeeCurrent` (denormalized), `HeadcountDailySnapshot` (TenantId, SnapshotDate, LegalEntityId, BranchId, DepartmentId, GradeId, Gender, EmploymentType, Headcount, Joiners, Leavers), `AttendanceDailyFact`, `LeaveFact`, `PayrollFact` (by component, per period, cost center), `RecruitmentFunnelFact`, `ExpenseFact`, `PerformanceFact`; plus `ReportDefinition` (system + tenant-saved), `ScheduledReport`, `ExportJob`. Clustered columnstore on large facts, clustered by (TenantId, date).

**Integration (`int`)** — `Connector` [GLOBAL] (Code, Category, CapabilitiesJson), `TenantConnection` (ConnectorCode, ConfigJson, SecretRef, Status, LastSyncDate), `ExternalIdMap` (SystemCode, EntityType, InternalId, ExternalId · UX (TenantId, SystemCode, EntityType, ExternalId)), `WebhookSubscription` (EventCodes, TargetUrl, SecretRef, IsActive), `WebhookDelivery` (SubscriptionId, EventId, Attempt, StatusCode, NextAttemptDate), `ImportJob` (EntityType, FileDocumentId, Status, TotalRows, SuccessRows, ErrorRows, Mode Validate/Commit) + `ImportJobError` (RowNumber, Field, Message), `SyncRun` (ConnectionId, Direction, Started, Finished, Stats, Error), `AttendanceConnector` (on-prem agent registration, KeyHash, Version, LastHeartbeat).

**Every service DB** — `messaging.OutboxMessage`, `messaging.OutboxState`, `messaging.InboxState`, `api.IdempotencyRecord (TenantId, UserId, Key, RequestHash, ResponseJson, StatusCode, ExpiresDate)`, `ref.EmployeeSnapshot` (consumers only).

---

## 8. Authentication & authorization

### 8.1 Identity provider options

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| A. Entra ID (workforce) as the only IdP | No auth code to build | Every customer would need Entra. Hard to model `tid` for non-Entra customers. Weak control of per-tenant password/lockout policy | ✗ |
| B. Entra External ID (CIAM) | Managed, MFA built in | Custom tenancy claims need API connectors. Per-tenant federation is awkward. Vendor lock-in | ~ |
| C. Duende IdentityServer | Mature, full-featured | Commercial licence | ~ |
| **D. OpenIddict + ASP.NET Core Identity (self-hosted OIDC server)** | OSS, full control of tenancy claims, per-tenant federation, token exchange, SCIM | We own security hardening | **✓ Chosen** |

The Identity Service is the **only token issuer** that APIs trust. Each tenant's Entra ID / Okta / Google / SAML IdP is an *upstream* federation configured in `iam.TenantIdentityProvider`. Because we issue the token, `tid` and the permission version always come from our own data, never from the upstream IdP.

### 8.2 Flows

- **Web (BFF)**: SPA → `/bff/login?tenant=acme` → BFF starts an OIDC Authorization Code + PKCE flow with Identity → (optional redirect to the tenant's Entra ID) → Identity issues tokens → the BFF keeps tokens in a server-side session store (Redis, encrypted) and gives the browser a `__Host-gggs` cookie (`HttpOnly; Secure; SameSite=Strict`). The BFF proxies `/api/*` and attaches the bearer token. **The browser never sees a token.**

**As built (local accounts, no OIDC server yet).** The shape above is the target; what runs today
reaches the same guarantee by a shorter route, and is written here because it is what a developer
will find in the code:

1. The SPA's own `/sign-in` page collects email, password and organisation code and `POST`s them to
   `/bff/login`. There is no redirect to start, because with local accounts there is no upstream
   provider to redirect to.
2. The gateway calls Identity's `POST /api/v1/auth/sign-in`, and captures the `__Host-gggs-identity`
   cookie Identity issues. That cookie is the **upstream credential**.
3. The credential is stored server-side in `BffSessionStore` (Redis, or in-process memory with a
   startup warning when Redis is absent), keyed by 256 bits of randomness. The store holds a hash of
   that key, so a dump of the cache is not a set of usable sessions.
4. The browser receives only `__Host-gggs-session`: `HttpOnly`, `Secure`, `SameSite=Strict`, root
   path, no domain, no expiry. **The browser never holds a token or an upstream cookie** - asserted
   on the wire by `BffSessionTests`, including that the upstream `Set-Cookie` is dropped rather than
   relayed.
5. `GET /bff/user` returns Identity's `/api/v1/me/session` - user, tenant, features and effective
   permissions - read through on every call rather than cached in the session, so a role change or a
   suspended tenant bites on the next page load.
6. `/api/*` is proxied by YARP, which attaches the stored credential per request.

When the OIDC server ships, step 2 becomes an authorization-code exchange and the credential becomes
an access token with a refresh token beside it. Steps 3 to 6 do not change, which is why the
credential is handled in one place (`IdentityClient`) rather than inline.

Two session protections beyond the cookie attributes: an **idle timeout** (30 minutes, as a sliding
cache expiry) inside an **absolute ceiling** (12 hours, checked explicitly because a sliding window
would otherwise renew past it), and the session is **bound to the browser that signed in** by a
user-agent hash. A key presented from elsewhere ends the session rather than being allowed through,
and a test replays one to prove it.

Signing out deletes the store record, so the cookie cannot be replayed afterwards. That is the
difference between revoking a session and asking a browser to forget one.

**Not yet reachable through the gateway: every service except Identity.** Platform and the services
after it authenticate with JWT bearer tokens, and the gateway holds an Identity session cookie, which
they will not accept. The proxy routes therefore cover Identity only. Closing this is the same piece
of work as OpenIddict token issuance - it is not a separate gap, and it is the reason the
platform-admin console cannot be wired up yet.
- **Mobile**: Auth Code + PKCE in the system browser. Refresh tokens are stored in the secure enclave.
- **Service-to-service**: Azure managed identity / client credentials with per-service audiences. Internal calls also forward the user token (or an OBO-exchanged downscoped token) so the user's context flows downstream.
- **Public API (Phase 5)**: client credentials per `iam.ApiClient`, scoped to a single tenant, fronted by Azure API Management.
- **Access token**: JWT, 10 min, audience per service. Claims: `sub` (UserId), `tid`, `emp` (EmployeeId), `sid` (SessionId), `pv` (permission version), `amr`, `acr`, `auth_time`, `roles` (codes only, for UI hints). **Permissions are not put in the token** (too large, and they would go stale).
- **Refresh token**: rotating, one-time use, reuse detection (reuse → revoke the whole session family + `SecurityEvent`). Sliding 8 h, absolute 12 h (tenant-configurable).
- **Logout**: BFF revokes the refresh token, deletes the session and front-channel logs out of the upstream IdP. "Log out all devices" rotates `SecurityStamp`.

### 8.3 Account security

- **Password policy** (per tenant, NIST 800-63B aligned): min 12, breached-password check (k-anonymity HIBP API or offline list), history 5, no forced rotation by default (configurable), Argon2id/PBKDF2 (Identity v3 hashing with high iteration count).
- **Lockout**: 5 failures → 15 min (configurable), progressive delay, per-IP throttling on `/connect/token` and login. Generic error messages (no user enumeration).
- **MFA**: TOTP, email OTP (fallback), WebAuthn/passkeys (Phase 2). Tenant policy: Off / Admins & sensitive roles / Everyone. SSO users: accept the upstream `amr=mfa` or enforce our own.
- **Step-up auth**: sensitive permissions (`IsSensitive = 1`, e.g. viewing bank details, finalizing payroll, exporting PII, changing roles) require `auth_time` within the last 15 min with MFA. The API returns `401` with `WWW-Authenticate: ... error="insufficient_user_authentication"`, and the SPA triggers re-auth.
- **Sessions**: `iam.UserSession` lists active sessions (device, IP, last seen); users/admins can revoke them. Idle timeout 30 min, enforced by the BFF.
- **Provisioning**: invite link (single-use, 72 h), JIT from SSO (domain match + default role), **SCIM 2.0** endpoint for Entra ID/Okta user and group sync (Phase 2).

### 8.4 Authorization model (RBAC + permissions + data scope + field-level)

**Permission code** = `module.resource.action`, e.g. `employee.profile.view`, `employee.bank.view`, `leave.request.approve`, `payroll.run.finalize`, `admin.role.manage`. The catalog is defined in code (`Permissions.cs` constants), seeded into `iam.Permission`, and **tenants cannot create permissions**, only compose roles from them.

**Data scope** on each role-permission grant: `Own · DirectReports · AllReports · Department(+sub-depts) · Branch · LegalEntity · BusinessUnit · Tenant`. `UserRole.ScopeType/ScopeId` can anchor a role to a specific org unit (e.g. HR Manager *for Branch Pune*).

**Effective permissions** = union over active `UserRole`s (including inherited parent roles) → `Dictionary<permissionCode, DataScope[] + anchors>`. They are cached in Redis at `t:{tid}:perm:{userId}:{pv}` and invalidated by bumping `PermissionVersion` (event `UserPermissionsChanged`).

**Evaluation pipeline in every service**:
1. `UseAuthentication` → JWT validated (issuer, audience, lifetime, signing key via JWKS).
2. `TenantContextMiddleware` → `ITenantContext` from `tid`. Tenant status and feature check.
3. `[RequirePermission("leave.request.approve")]` → dynamic `IAuthorizationPolicyProvider` → `PermissionAuthorizationHandler` loads the effective permissions (Redis, falling back to an Identity internal API).
4. **Data scope** → `IDataScopeService.GetEmployeeFilter(permission)` returns an expression / SQL fragment used in queries (list endpoints), e.g. `Own → e.Id == me`; `AllReports → EXISTS ReportingLine where Manager = me`; `Department → OrgPath.IsDescendantOf(@path)`.
5. **Resource check** for single-entity endpoints → `EnsureCanAccessEmployeeAsync(employeeId, permission)`. On failure, return **404, not 403**, so existence is not leaked.
6. **Field-level**: response DTO properties marked `[Sensitive("employee.bank.view", Mask.Last4)]`. A serialization modifier masks or omits them unless the permission is held. Sensitive reads are written to `audit.SensitiveDataAccess`.
7. **Segregation of duties** (in the workflow + domain): initiator ≠ approver. Payroll preparer ≠ finalizer. A user cannot change their own roles or salary.

### 8.5 Default role templates (seeded per tenant, editable copies)

| Role | Key permissions (abridged) | Default data scope |
|---|---|---|
| Platform Super Admin | platform.* (tenants, plans, features, health). **No tenant HR data** without a `SupportAccessGrant` | Platform |
| Tenant Admin | admin.* (users, roles, settings, workflows, templates, integrations), org.* | Tenant |
| HR Admin | org.*, employee.* (incl. sensitive), leave/attendance admin, documents, onboarding | Tenant |
| HR Manager | employee.view/edit, approvals, reports | Branch / LegalEntity (anchored) |
| Department Manager | team view, leave/attendance/expense approve, goals & reviews | AllReports + Department |
| Team Lead | team view, leave/attendance approve | DirectReports |
| Employee | self.* (profile, leave apply, attendance, payslip, expenses, goals, assets) | Own |
| Payroll Admin | payroll.* except finalize-own, salary view/edit, statutory config | LegalEntity |
| Finance User | expense.approve/reimburse, payroll.view-summary, GL export, travel advance | LegalEntity |
| Auditor | *.view + audit.log.view, **read-only**, time-boxed (`ValidTo`) | Tenant |

The authoritative list is now code: `Gggs.Identity.Application.Provisioning.RoleTemplates`, which
pairs each template with its exact permission codes and data scopes. It lives in Identity rather
than in the Platform service that drives provisioning, because a list of role names in a service
that cannot see the permission catalog is a list nobody can check.

Two implemented decisions differ from the abridged table above, and both are deliberate:

- **No seeded role gets `employee.bank.*`, `employee.identity-document.*` or any `*.export`
  permission** - not even Tenant Admin, and not HR Admin despite the row above. Those reach a
  person's money, their government identifiers, or every record at once. A customer grants them in
  one edit; nobody receives them because of a default we shipped.
- **Only Tenant Admin gets `admin.role.manage`.** Whoever can change what a role means can grant
  themselves anything, so exactly one seeded role has it, and a unit test asserts that.

Seeding runs in `Gggs.Identity.Worker` from the `TenantProvisioned` event and is idempotent in a
specific way: a repeat run creates only missing roles and **never re-grants permissions on a role
that already exists**. Topping a role back up to the template would let the redelivery of an old
message silently undo an administrator's decision to narrow it.

#### Bootstrapping the first administrator

The one circularity in the model: only a tenant's own administrator can invite anyone, and at
provisioning nobody is signed in. Seeding creates the administrator's account in `Invited` status
**with no invitation token**, because nothing can deliver one yet and a token minted here would be a
live credential its owner never received.

`POST /api/v1/tenants/{tenantId}/administrator-invitation` (Identity, permission
`platform.tenant.manage`, audited) issues one on demand. Its reach is deliberately tiny: it works
only for an account still in `Invited` status that holds the seeded administrator role, so it stops
working the moment the customer completes the invitation, and it exposes no HR data. When the
Notification service lands, the invitation is sent at seeding time and this endpoint becomes a
support tool rather than the normal path.

Still missing, and needed before platform staff can use any of this in anger: **platform users have
no permission source**. `PermissionResolver` reads tenant role grants, and `Role.Grant` refuses
platform-only codes by design, so no account can currently hold `platform.*` outside the test
harness.

---

## 9. Tenant isolation strategy (defence in depth)

| Layer | Control |
|---|---|
| 1. Identity | `tid` issued from the Identity DB membership. Tenant switch only via token exchange |
| 2. Edge/BFF | Strips any client `X-Tenant-*` headers. Per-tenant rate limits |
| 3. API | `TenantContextMiddleware` (claim only). DTOs contain no TenantId. Unknown/other-tenant IDs → 404 |
| 4. Application | `ITenantContext` required by repositories. `TenantMismatchException` if an entity's TenantId ≠ context |
| 5. EF Core | Named global query filters (`"Tenant"`, `"SoftDelete"`) applied by convention to every `ITenantOwned` entity. `SaveChanges` interceptor stamps TenantId on insert and forbids changing it |
| 6. SQL Server | **RLS filter + block predicates** using `SESSION_CONTEXT('TenantId')` set on every connection open (read-only). Also covers **Dapper** and any hand-written SQL |
| 7. Schema | Composite same-tenant FKs make cross-tenant references impossible |
| 8. Cache | All Redis keys prefixed `gggs:{env}:t:{tid}:`. Cache wrapper API requires the tenant context (no raw key API) |
| 9. Messaging | TenantId in the envelope header. Consume filter sets the tenant context before the handler runs. Messages without TenantId are rejected unless the consumer is marked `[PlatformScope]` |
| 10. Jobs | Hangfire job args carry TenantId. `TenantJobFilter` restores the context. Recurring jobs fan out *per tenant* (one child job per tenant for fairness and isolation) |
| 11. Files | Blob container per tenant (`t-{tenantId}`). SAS scoped to a single blob, 5 min, read-only. The Document service checks `Document.TenantId` + permission before issuing a SAS |
| 12. Search / AI | Search index documents carry `tenantId`; the filter is injected server-side (never by the client). Dedicated tenants get a dedicated index |
| 13. Reporting | Read-model tables carry TenantId + RLS. Power BI Embedded (if used) with an RLS role bound to `tid` |
| 14. Tests | Automated **cross-tenant test suite**: for every endpoint (discovered by reflection from the route table), seed tenants A and B, call as A with B's IDs → expect 404/empty. Runs in CI and blocks merge |
| 15. Monitoring | `TenantViolation` security events → alert. Periodic query that looks for FK/tenant anomalies |

**Key code (BuildingBlocks.MultiTenancy)**

```csharp
public interface ITenantContext { Guid TenantId { get; } bool IsResolved { get; } }

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContextAccessor accessor, ITenantRegistry registry)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var tid = Guid.Parse(ctx.User.FindFirstValue(GggsClaims.TenantId)
                      ?? throw new UnauthorizedAccessException("Token has no tenant."));
            var tenant = await registry.GetAsync(tid, ctx.RequestAborted);        // cached
            if (tenant is null || tenant.Status != TenantStatus.Active)
                throw new TenantUnavailableException(tid);
            accessor.Set(tid);
        }
        await next(ctx);
    }
}

// Sets SESSION_CONTEXT for RLS on every physical connection open (EF + Dapper share the factory).
public sealed class TenantSessionContextInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    private const string Sql = "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @tid, @read_only = 1;";

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        if (!tenant.IsResolved) return;                       // platform-scope code paths use a separate connection string/principal
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        var p = cmd.CreateParameter(); p.ParameterName = "@tid"; p.Value = tenant.TenantId; cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    // + synchronous ConnectionOpened override with the same body
}

// Base DbContext: named filters (EF Core 10) applied to every ITenantOwned / ISoftDeletable entity.
protected override void OnModelCreating(ModelBuilder b)
{
    foreach (var et in b.Model.GetEntityTypes())
    {
        if (typeof(ITenantOwned).IsAssignableFrom(et.ClrType))
            b.Entity(et.ClrType).HasQueryFilter(QueryFilters.Tenant, BuildTenantFilter(et.ClrType));   // e => e.TenantId == _tenant.TenantId
        if (typeof(ISoftDeletable).IsAssignableFrom(et.ClrType))
            b.Entity(et.ClrType).HasQueryFilter(QueryFilters.SoftDelete, BuildNotDeletedFilter(et.ClrType));
    }
}
```

`SqlConnection` pooling calls `sp_reset_connection` when a pooled connection is reused, which clears `SESSION_CONTEXT`, so the interceptor re-sets it on each open. An integration test verifies this.

**Platform-scope operations** (cross-tenant usage metrics, migrations, login discovery by email) run under a **separate DB principal** in the `rls_bypass` role, used only by code in `*.Platform*` namespaces. An architecture test enforces that, and every such operation is logged.

---

## 10. API architecture

### 10.1 Conventions

- Base path `/api/v{n}/{module}/{resource}`. The BFF routes by module prefix to the owning service. Versioning with `Asp.Versioning` (URL segment). Additive changes don't bump the version. A breaking change → v2 side by side, with v1 deprecated after 6 months (`Sunset` header).
- Resources: plural, kebab-case (`/leaves/requests`, `/employees/{id}/bank-accounts`). JSON camelCase, ISO-8601 dates, `DateOnly` for business dates, enums as strings.
- **State transitions are task-based**, not generic PATCH: `POST /leaves/requests/{id}/cancel`, `POST /payroll/runs/{id}/finalize`.
- **Lists**: `?page=1&pageSize=25&sort=-dateOfJoining,displayName&search=ra&departmentId=…&status=Active`. Max pageSize 200. Response `{ items, page, pageSize, totalCount }`, where `totalCount` is optional (`includeTotal=false`) for large sets. **Keyset/cursor pagination** (`?after=<cursor>`) for high-volume feeds (punches, audit, notifications).
- **Projection**: list DTOs are thin. Detail DTOs are separate. Sensitive sub-resources get their own endpoints and permissions (`/employees/{id}/bank-accounts`, `/employees/{id}/identity-documents`, `/payroll/employees/{id}/salary`).
- **Concurrency**: `ETag` = base64 RowVersion. `PUT`/transition endpoints require `If-Match` → `412` on a mismatch.
- **Idempotency**: `Idempotency-Key` on POST (required for payroll, payments, imports).
- **Errors**: RFC 9457 `ProblemDetails` with `type`, `title`, `status`, `detail`, `traceId`, `errorCode` (e.g. `LEAVE_INSUFFICIENT_BALANCE`), `errors{}` for validation. Error messages are localizable by `errorCode`.
- **Long-running work**: `202 Accepted` + `Location: /api/v1/jobs/{id}`, with completion pushed over SignalR.
- **Bulk**: `POST /employees/bulk` (≤500 synchronous) or the import framework (async, any size).
- **Headers**: `X-Correlation-Id` (generated if absent, echoed back), W3C `traceparent`.
- **Swagger/OpenAPI**: one OpenAPI document per service (built-in `Microsoft.AspNetCore.OpenApi` + Scalar/Swagger UI in non-prod), aggregated at the BFF in dev. Security scheme documented. Every endpoint lists its required permission in its description (generated from the attribute). The Angular client is generated from OpenAPI (`openapi-generator` / `ng-openapi-gen`) into `libs/shared/api-clients`.

### 10.2 Endpoint catalog (primary)

| Service | Base routes |
|---|---|
| Platform | `/api/v1/tenants` (platform admin: CRUD, suspend, activate, promote-tier), `/api/v1/subscriptions`, `/api/v1/features`, `/api/v1/settings/{module}`, `/api/v1/audit/logs` (tenant-scoped search), `/api/v1/jobs/{id}` |
| Identity | `/api/v1/users` (+ `/invite`, `/{id}/roles`, `/{id}/sessions`, `/{id}/lock`, `/{id}/reset-mfa`), `/api/v1/roles` (+ `/{id}/permissions`), `/api/v1/permissions` (catalog), `/api/v1/identity-providers`, `/api/v1/me` (profile, sessions, MFA enrolment, effective permissions), `/connect/*` (OIDC), `/scim/v2/*` |
| Core HR | `/api/v1/legal-entities`, `/business-units`, `/branches`, `/locations`, `/departments` (+ `/tree`), `/designations`, `/grades`, `/cost-centers`, `/positions`; `/api/v1/employees` (+ `/{id}/employment` timeline, `/{id}/transfer`, `/{id}/promote`, `/{id}/confirm`, `/{id}/exit`, `/{id}/contacts`, `/education`, `/experience`, `/skills`, `/bank-accounts`, `/identity-documents`, `/tax-profile`, `/history`), `/api/v1/employees/org-chart`, `/api/v1/custom-fields`, `/api/v1/lookups`; `/api/v1/assets` (+ `/{id}/assign`, `/{id}/return`), `/api/v1/assets/licenses` |
| Time | `/api/v1/attendance/punches` (+ `/me/check-in`, `/me/check-out`), `/attendance/days`, `/attendance/regularizations`, `/attendance/overtime`, `/attendance/shifts`, `/attendance/rosters`, `/attendance/policies`, `/attendance/devices`, `/attendance/device-ingest` (device API key auth); `/api/v1/leaves/types`, `/leaves/policies`, `/leaves/balances` (+ `/me`), `/leaves/requests` (+ `/{id}/cancel`, `/withdraw`), `/leaves/ledger`, `/leaves/encashments`, `/leaves/team-calendar`, `/api/v1/holidays/calendars` |
| Payroll | `/api/v1/payroll/pay-groups`, `/pay-periods`, `/components`, `/structures`, `/employees/{id}/salary` (+ `/revisions`), `/inputs` (+ `/import`), `/runs` (+ `/{id}/calculate`, `/{id}/employees`, `/{id}/submit`, `/{id}/finalize`, `/{id}/revert`, `/{id}/bank-file`, `/{id}/gl-journal`), `/payslips` (+ `/me`), `/tax/declarations`, `/tax/proofs`, `/loans`, `/fnf`, `/statutory-config`, `/reports/{code}` |
| Talent | `/api/v1/recruitment/requisitions`, `/postings`, `/candidates`, `/applications` (+ `/{id}/move-stage`), `/interviews` (+ `/{id}/feedback`), `/offers` (+ `/{id}/send`, `/{id}/accept`); `/api/v1/onboarding/templates`, `/cases`, `/tasks`; `/api/v1/performance/cycles`, `/goals` (+ `/{id}/check-ins`), `/reviews` (+ `/{id}/responses`), `/feedback-requests`, `/kpis`; `/api/v1/learning/courses`, `/sessions`, `/enrollments`, `/certifications`; public `/api/v1/public/careers/{tenantCode}/postings` (anonymous, rate-limited) |
| Spend | `/api/v1/expenses/categories`, `/policies`, `/claims` (+ `/{id}/submit`, `/{id}/lines`, `/{id}/reimburse`), `/reports`; `/api/v1/travel/requests` (+ `/{id}/advances`, `/{id}/settlement`) |
| Workflow | `/api/v1/workflows/definitions` (+ `/{id}/versions`, `/publish`), `/workflows/instances/{id}` (timeline), `/workflows/tasks` (inbox: `?status=Pending`) (+ `/{id}/approve`, `/reject`, `/send-back`, `/reassign`), `/workflows/delegations`; internal `/internal/workflows/start` |
| Notification | `/api/v1/notifications` (in-app list, `/{id}/read`, `/read-all`, `/unread-count`), `/notifications/templates` (+ `/{id}/preview`), `/notifications/preferences`, `/notifications/channels`; SignalR hub `/hubs/notifications` |
| Document | `/api/v1/documents` (+ `/upload-requests`, `/{id}/download`, `/{id}/versions`, `/{id}/acknowledge`), `/documents/types`, `/documents/templates` (+ `/{id}/render`), `/documents/expiring` |
| Reporting | `/api/v1/reports/catalog`, `/reports/{code}/run`, `/reports/saved`, `/reports/scheduled`, `/reports/exports`, `/api/v1/dashboards/{persona}/widgets/{widget}` |
| Integration | `/api/v1/integrations/connectors`, `/connections`, `/webhooks`, `/imports` (+ `/{id}/validate`, `/commit`, `/errors`), `/exports`, `/api/v1/integrations/attendance-connectors` |
| BFF | `/bff/login`, `/bff/logout`, `/bff/user`, `/bff/ess/home`, `/bff/mss/team-summary` (aggregations) |

### 10.3 Per-service code structure (Clean Architecture + vertical-slice features)

```
Gggs.Time.Domain          entities, value objects, domain services, domain events (no EF, no HTTP)
Gggs.Time.Application     Features/Leave/Requests/{Create,Cancel,Get,List}/  → Request/Response DTOs, Validator, Handler
                          Abstractions (repositories, ports), Mapping (Mapperly), Authorization (permission constants)
Gggs.Time.Infrastructure  EF DbContext, configurations, repositories, Dapper query services, migrations, outbox, adapters
Gggs.Time.Api             Controllers, filters, Program.cs (composition root)
Gggs.Time.Worker          MassTransit consumers, Hangfire recurring jobs (accruals, attendance processing)
Gggs.Time.Contracts       Published integration events + internal API client (NuGet package for consumers)
```

Rules (enforced by NetArchTest): Domain references nothing; Application references Domain + BuildingBlocks abstractions; Api/Worker reference Application + Infrastructure; no service references another service's internals (only `*.Contracts`).

**Writes** go through EF Core aggregates + a repository per aggregate root. **Reads** (lists, dashboards, search) go through Dapper/EF projection **query services** (`ILeaveRequestQueries`), with no repositories on the read path.

### 10.4 Worked example — Leave Request (template for every module)

```csharp
// ---------- Api/Controllers/LeaveRequestsController.cs ----------
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/leaves/requests")]
[Produces("application/json")]
public sealed class LeaveRequestsController(ILeaveRequestService service, ILeaveRequestQueries queries) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Leave.RequestView)]
    public Task<PagedResult<LeaveRequestListItem>> List([FromQuery] LeaveRequestListQuery query, CancellationToken ct)
        => queries.ListAsync(query, ct);                              // data scope applied inside

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Leave.RequestView)]
    public async Task<ActionResult<LeaveRequestResponse>> Get(Guid id, CancellationToken ct)
        => this.WithETag(await queries.GetAsync(id, ct));             // 404 if not found OR out of scope

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permissions.Leave.RequestCreate)]
    [ProducesResponseType<LeaveRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LeaveRequestResponse>> Create(CreateLeaveRequestRequest request, CancellationToken ct)
    {
        var created = await service.SubmitAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Permissions.Leave.RequestCancel)]
    public Task<LeaveRequestResponse> Cancel(Guid id, CancelLeaveRequestRequest request, [FromHeader(Name = "If-Match")] string etag, CancellationToken ct)
        => service.CancelAsync(id, request, ETag.ToRowVersion(etag), ct);
}

// ---------- Application/Features/Leave/Requests/Contracts.cs ----------
public sealed record CreateLeaveRequestRequest(
    Guid? EmployeeId,                 // null = self; otherwise needs leave.request.create-on-behalf
    Guid LeaveTypeId,
    DateOnly FromDate, DaySession FromSession,
    DateOnly ToDate,   DaySession ToSession,
    string? Reason, Guid? AttachmentDocumentId);

public sealed record LeaveRequestResponse(
    Guid Id, string RequestNo, EmployeeRef Employee, LeaveTypeRef LeaveType,
    DateOnly FromDate, DaySession FromSession, DateOnly ToDate, DaySession ToSession,
    decimal TotalDays, string? Reason, LeaveRequestStatus Status,
    Guid? WorkflowInstanceId, IReadOnlyList<LeaveDayResponse> Days, DateTime CreatedDate);

public sealed record LeaveRequestListItem(Guid Id, string RequestNo, EmployeeRef Employee, string LeaveTypeName,
    DateOnly FromDate, DateOnly ToDate, decimal TotalDays, LeaveRequestStatus Status);

// ---------- Validator (FluentValidation) — shape only; business rules live in the domain ----------
public sealed class CreateLeaveRequestValidator : AbstractValidator<CreateLeaveRequestRequest>
{
    public CreateLeaveRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate);
        RuleFor(x => x).Must(x => x.ToDate.DayNumber - x.FromDate.DayNumber <= 366).WithErrorCode("LEAVE_RANGE_TOO_LONG");
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

// ---------- Application service ----------
public sealed class LeaveRequestService(
    ICurrentUser user, IDataScopeService scope, ILeavePolicyResolver policies, IWorkCalendar calendar,
    ILeaveRequestRepository requests, ILeaveBalanceRepository balances, INumberSequence numbers,
    IUnitOfWork uow, TimeProvider clock, ILogger<LeaveRequestService> log) : ILeaveRequestService
{
    public async Task<LeaveRequestResponse> SubmitAsync(CreateLeaveRequestRequest req, CancellationToken ct)
    {
        var employeeId = req.EmployeeId ?? user.EmployeeId ?? throw new ForbiddenException("NO_EMPLOYEE_PROFILE");
        if (employeeId != user.EmployeeId)
            await scope.EnsureCanAccessEmployeeAsync(employeeId, Permissions.Leave.RequestCreateOnBehalf, ct);

        var rule    = await policies.ResolveRuleAsync(employeeId, req.LeaveTypeId, req.FromDate, ct);
        var days    = await calendar.GetWorkDaysAsync(employeeId, req.FromDate, req.ToDate, ct);    // roster, weekly-offs, holidays
        var balance = await balances.GetForUpdateAsync(employeeId, req.LeaveTypeId, rule.PeriodFor(req.FromDate), ct);
        var overlap = await requests.HasOverlapAsync(employeeId, req.FromDate, req.ToDate, ct);

        var request = LeaveRequest.Submit(                                  // all business rules enforced here → DomainRuleException(code)
            await numbers.NextAsync("LeaveRequest", ct), employeeId, user.UserId, req.LeaveTypeId,
            new LeavePeriod(req.FromDate, req.FromSession, req.ToDate, req.ToSession),
            req.Reason, req.AttachmentDocumentId, rule, days, balance, overlap, clock);

        requests.Add(request);                   // raises LeaveRequestSubmitted domain event
        await uow.SaveChangesAsync(ct);          // single tx: request + balance.Pending + outbox(StartWorkflow, LeaveRequestSubmitted) + audit entry

        log.LogInformation("Leave request {LeaveRequestId} submitted for employee {EmployeeId} ({Days} days)",
            request.Id, employeeId, request.TotalDays);
        return request.ToResponse();             // Mapperly
    }
}

// ---------- Exception → ProblemDetails mapping (BuildingBlocks.Web, IExceptionHandler) ----------
// ValidationException        → 400 (errors{})
// DomainRuleException        → 422 (errorCode, e.g. LEAVE_INSUFFICIENT_BALANCE)
// NotFoundException / out-of-scope / other-tenant → 404
// ForbiddenException         → 403
// ConcurrencyException       → 412 (If-Match) / 409
// TenantMismatchException    → 404 to the client + SecurityEvent + alert
// TenantUnavailable/FeatureDisabled → 403 with errorCode
// Unhandled                  → 500, generic message, full detail only in logs (traceId correlates)
```

**Cross-cutting pieces every module gets for free** (BuildingBlocks): the `[RequirePermission]` policy provider, `[Idempotent]` filter, validation filter (auto-runs validators), `IExceptionHandler`, correlation-ID middleware, Serilog request logging (route template, status, duration, tid, uid, **no bodies**), `AuditSaveChangesInterceptor`, `TenantSaveChangesInterceptor`, `DomainEventDispatcher` → outbox, `IClock` / `TimeProvider`, `PagedResult<T>`, `ISortFilterParser` with a per-query sortable-field whitelist.

Module-specific DTO sets follow the same pattern: `Create*/Update*Request`, `*Response` (detail), `*ListItem`, `*ListQuery` (paging + filters), transition requests (`Approve*Request { Comment }`). Examples: `CreateEmployeeRequest` (personal + employment + optional custom fields) → `EmployeeResponse` with **no** sensitive fields; `EmployeeBankAccountResponse { BankName, AccountNumberMasked, IfscOrSwift, IsPrimary }`; `PayrollRunResponse { Status, Totals, EmployeeCount, ErrorCount }`; `PayslipListItem { Period, NetPay (only for self or payroll perm), DownloadUrl }`.

---

## 11. Angular architecture

### 11.1 Decisions

- **Angular latest LTS, standalone components, signals, new control flow, zoneless change detection** where the libraries support it. The requested "Core module / Shared module / Feature modules" are implemented as the modern equivalent: `core/` **providers** (`provideCore()`), shared **libraries** and lazy-loaded **feature route files** (`loadChildren: () => import(...).then(m => m.LEAVE_ROUTES)`). There are no NgModules.
- **Nx monorepo** for module boundaries (`enforce-module-boundaries` lint tags: `type:feature|ui|data-access|util`, `scope:leave|payroll|shared`), affected builds and caching.
- **UI kit (decided)**: **PrimeNG** — the richest ERP component set (lazy DataTable, TreeTable, Calendar, OrgChart, Timeline), on a design-token theme so tenant branding is a CSS-variable swap. All PrimeNG usage is wrapped in `libs/shared/ui` and `libs/shared/data-table`, so features never import PrimeNG directly; that keeps the kit replaceable and lets us fix accessibility gaps in one place. Each wrapped component gets an axe-core check in CI, since PrimeNG's a11y is weaker than Material's.
- **State**: NgRx **SignalStore** per feature (`withEntities`, `rxMethod` for API calls). Global stores only for session, tenant, permissions and notifications.
- **i18n**: Transloco (runtime language switch per user, lazy-loaded translation scopes per feature). RTL support (Arabic). Tenant-overridable labels.
- **No micro-frontends initially.** Nx lazy libs give the same code separation. Adopt Native Federation only if independent team deploys become necessary.

### 11.2 Core runtime pieces

| Concern | Implementation |
|---|---|
| Auth | `AuthService` over the BFF (`/bff/user` → session signal). `withCredentials`. No token handling in the SPA. Session-expiry watcher → re-login dialog |
| HTTP interceptors (functional) | `correlationIdInterceptor`, `csrfHeaderInterceptor` (`X-CSRF: 1`), `errorInterceptor` (ProblemDetails → toast/inline, 401 → login, 403 feature → upsell page, step-up → re-auth), `loadingInterceptor` (per-request context token), `retryInterceptor` (GET only, network errors) |
| Tenant context | `TenantStore` (id, name, branding, locale, time zone, currency, enabled features). Applies theme CSS vars. Tenant switcher calls `/bff/switch-tenant` → full reload |
| Permissions | `PermissionStore` (effective permission set from `/bff/user`). `hasPermission()` function, `*gggsHasPermission` structural directive, `[gggsDisableUnless]`. **UI hiding is only for UX; the API always enforces** |
| Guards | `authGuard`, `permissionGuard` (route `data.permissions`, any/all), `featureGuard` (plan module), `unsavedChangesGuard`, `tenantActiveGuard` |
| Routing | Shell layout route + lazy feature routes. Route `data.breadcrumb`. Route-level providers for feature stores |
| Real-time | `RealtimeService` (SignalR) → notification badge, approval counts, job completion |
| Errors | Global `ErrorHandler` → logs to the App Insights JS SDK (no PII) + friendly error page |

### 11.3 Shared UI library (reuse is the main productivity lever)

- `gggs-data-table`: server-side paging/sort/filter bound to a `ListQuery` signal, column config (type, format, sortable, filter type, permission-gated columns, sticky), row selection + **bulk action bar**, column chooser, **saved views** (per user), density, CSV/XLSX **export** (server-side job for large sets), row actions menu, empty/error/skeleton states built in.
- `gggs-filter-bar` + `gggs-advanced-search` (drawer with query builder, chips for active filters, URL-synced query params so views are shareable).
- `gggs-form-*`: field wrappers (label, hint, error from ProblemDetails `errors{}` mapped to controls), date/date-range with business-date semantics, employee picker (async search), org-unit tree picker, currency input, file upload (to an upload-request SAS with progress + scan status).
- **Dynamic forms**: `gggs-dynamic-form` renders from a JSON schema (built from `cfg.CustomFieldDefinition` + server-provided form metadata) into typed reactive forms, with sections, conditional visibility, validators and read-only per permission. Used for custom fields, onboarding forms, review templates and workflow request forms.
- Overlays: `DialogService` (modal/drawer), `ConfirmService` (with typed consequence text, e.g. "Finalize payroll for 1,240 employees?"), `ToastService`.
- States: `gggs-skeleton` (table/card/form variants), `gggs-empty-state` (illustration + primary action), `gggs-error-state` (retry + trace id), `gggs-page-header` (title, breadcrumb, actions), `gggs-status-badge`, `gggs-avatar`, `gggs-kpi-card`, `gggs-chart` (ECharts wrapper), `gggs-timeline`, `gggs-approval-trail`.

### 11.4 Frontend folder structure

```
frontend/                                  (Nx workspace)
├─ apps/
│  ├─ hrms-web/                            main ERP SPA
│  │  └─ src/app/{app.config.ts, app.routes.ts, shell/}
│  ├─ hrms-web-e2e/                        Playwright
│  └─ careers-portal/                      public careers site (Phase 3, SSR)
├─ libs/
│  ├─ core/
│  │  ├─ auth/            (auth.service, session.store, auth.guard)
│  │  ├─ tenant/          (tenant.store, theming, feature.guard)
│  │  ├─ permissions/     (permission.store, has-permission.directive, permission.guard)
│  │  ├─ http/            (interceptors, problem-details.mapper, api-config)
│  │  ├─ realtime/        (signalr.service)
│  │  ├─ i18n/            (transloco config, locale/date/currency pipes)
│  │  └─ layout/          (shell, sidebar, header, breadcrumb, command-palette, notification-panel, tenant-switcher)
│  ├─ shared/
│  │  ├─ ui/              (buttons, badges, cards, states, dialogs, charts)
│  │  ├─ data-table/
│  │  ├─ forms/           (field wrappers, pickers, uploaders)
│  │  ├─ dynamic-form/
│  │  ├─ api-clients/     (generated from OpenAPI per service)
│  │  ├─ models/
│  │  └─ util/            (date-only, money, query-params, file)
│  └─ features/
│     ├─ dashboard/{platform,tenant-admin,manager,employee}/
│     ├─ organization/    each feature → feature/ (routes + pages), data-access/ (store + facade), ui/ (dumb components)
│     ├─ employees/
│     ├─ attendance/ leave/ payroll/ recruitment/ onboarding/ performance/
│     ├─ expenses/ travel/ assets/ learning/
│     ├─ self-service/    (my-* pages composing other features' ui libs)
│     ├─ manager/         (team pages)
│     ├─ approvals/       (unified inbox)
│     ├─ workflow-admin/ notifications/ documents/ reports/
│     ├─ admin/           (users, roles, settings, custom fields, templates, integrations, audit)
│     └─ platform-admin/  (tenants, plans, features, health) — separate route tree, platform users only
├─ tools/  (generators: nx g @gggs/plugin:feature leave)
└─ nx.json, tsconfig.base.json, eslint.config.mjs
```

---

## 12. UI/UX architecture

### 12.1 Layout (desktop-first, responsive down to tablet/mobile)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ☰ [Tenant logo] Acme Group ▾(switcher*)   🔍 Search people, pages, actions… (Ctrl+K)   ✓3  🔔5  ? (RB)▾│
├──────────────┬───────────────────────────────────────────────────────────────────────┤
│ ⌂ Home       │ Home › Leave › Requests                                                 │
│ ✓ Approvals 3│ Leave requests                          [Export ▾] [Import] [+ Apply]   │
│ 👤 My Space ▸│ ┌ Filters: Status(Pending) · Dept(Eng) · Date(This month) [+ More] [Save view]┐│
│ 👥 My Team  ▸│ ├───┬──────────┬──────────┬───────────┬──────┬─────────┬───────────┤  │
│ 🏢 Org      ▸│ │ ☐ │ Employee │ Type     │ Dates     │ Days │ Status  │ ⋯         │  │
│ 🧑 Employees │ │ ☐ │ Priya S. │ Casual   │ 24–26 Sep │ 3    │ Pending │ ⋯         │  │
│ ⏱ Time      ▸│ └───┴──────────┴──────────┴───────────┴──────┴─────────┴───────────┘  │
│ 💰 Payroll  ▸│  2 selected: [Approve] [Reject]            ‹ 1 2 3 … 12 ›  25/page ▾   │
│ 📊 Reports   │                                                                         │
│ ⚙ Admin     ▸│                                                                         │
│ « collapse   │                                                                         │
└──────────────┴───────────────────────────────────────────────────────────────────────┘
```

- The **sidebar is generated from a navigation manifest** (`nav.config.ts`: item → route, icon, permission(s), feature flag, badge source), filtered by permissions and plan. Groups are collapsible. Collapses to an icon rail, and becomes a drawer on mobile.
- **Header**: tenant name/logo (switcher visible only if the user has ≥2 memberships or is platform support), **command palette** global search (employees via a search endpoint scoped to data scope; pages; actions like "Apply leave"; recent items), approvals counter, notification bell (drawer with tabs All/Mentions/System, mark-all-read, real-time), help, and a user menu (profile, language, time zone, theme light/dark, sessions, logout).
- **Page anatomy (list)**: page header (title, breadcrumb, primary + secondary actions) → saved-view tabs → filter bar → data table → pagination. **Detail**: summary header card (avatar, key facts, status, actions) + tabs (Overview, Job, Personal, Documents, Assets, History…) + right-side activity/approval timeline. **Forms**: sectioned with sticky save bar, inline validation, dirty-state guard. Long processes (hire, payroll run, performance cycle) use a **stepper wizard**.
- **Unified Approvals Inbox**: one place for all workflow tasks (leave, attendance regularization, expense, travel, payroll, requisition, offer…), with filters by type, SLA-due highlighting, a quick-view drawer showing request details + balance/impact context, and approve/reject/bulk-approve with comments.
- **Import**: download template → upload → server validates → **preview grid with row-level errors** → fix & re-upload or commit valid rows → async commit with a progress notification → downloadable error report.
- **Export**: current view (filters + columns) → ≤5k rows immediate, larger → background job + notification + download link (expires in 24 h). Every export is audited. PII columns only appear if the user holds the permission.
- **Bulk actions**: selection across pages ("select all 1,240 matching"), confirmation that states counts and consequences, partial-failure result summary.
- **States**: skeleton loaders that match the layout (not spinners) for the first load. Empty states with context-specific call to action ("No leave types yet — create your first leave type"). Error states with retry + trace id. Offline banner.
- **Accessibility & quality**: WCAG 2.2 AA, full keyboard navigation (table row navigation, shortcuts `g h` Home, `g a` Approvals), visible focus, 4.5:1 contrast in both themes, screen-reader labels, reduced motion. Density modes (comfortable/compact — ERP users prefer compact). Locale-aware dates, numbers and currency. Time zone shown for punches.
- **Design tokens**: `--gggs-color-primary` (tenant-brandable within contrast guardrails), neutral scale, semantic status colours, spacing 4-pt scale, typography scale, elevation, radius. Light/dark themes.

### 12.2 Dashboards (widget framework)

Each widget is a standalone component with `{ id, permission, feature, dataSource, refresh, size }`. The layout is per persona with user-level personalization (reorder/hide). Each widget loads independently (its own skeleton/error). Data comes from Reporting read models (aggregates, cached 5 min) or small live counts.

| Dashboard | Widgets (data source) |
|---|---|
| Platform Admin | Total / active / trial / suspended tenants; subscriptions by plan & MRR; renewals due; total users & DAU/MAU; employees under management; system health (service health checks, queue depth, DLQ count, error rate, p95 latency via App Insights API); usage per tenant (API calls, storage); top tenants by load; recent provisioning failures |
| Tenant Admin / HR | Headcount (by LE/branch/dept, trend); new joiners this month; exits & **attrition % (rolling 12 m)**; today's attendance (present/absent/late/on leave); leave taken & upcoming; payroll status of current period + cost trend; pending approvals by type & overdue; open positions & pipeline; upcoming birthdays & work anniversaries (next 7/30 days); probation confirmations due; document expiries; onboarding in progress |
| Manager | Team size & composition; who's in/out today; team leave calendar (next 2 weeks); pending approvals (with quick actions); late arrivals this week; goals progress; review cycle status; team expense spend; overtime |
| Employee | Check-in/out card with today's hours; attendance summary this month; leave balances by type + "Apply"; last payslip (net masked until click) + download; my pending requests & their approval stage; my goals progress; announcements; notifications; holidays upcoming; my assets; tasks (onboarding, acknowledgements, reviews) |

---

## 13. Workflow architecture

### 13.1 Engine choice

| Option | Pros | Cons |
|---|---|---|
| **Custom declarative approval engine (chosen)** | Fits HR approval semantics (manager chain, org-based approvers, delegation, SLA). Tenant-configurable via a simple JSON model + UI. Easy to secure per tenant | We build and maintain it (bounded scope: approvals, not general orchestration) |
| Elsa Workflows 3 | Visual designer, .NET native | Heavyweight general engine. Multi-tenant isolation of definitions/instances needs work. Designer is hard for HR users |
| Camunda / Temporal | Powerful orchestration | Separate platform to operate. Overkill for approvals. Tenancy is DIY |

Technical sagas (payroll run, provisioning) use **MassTransit state machines**. Business approvals use the **Workflow service**.

### 13.2 Model

```jsonc
// wf.WorkflowDefinitionVersion.DefinitionJson — LEAVE_REQUEST v3 (tenant-specific)
{
  "trigger": { "entityType": "LeaveRequest" },
  "variables": ["days", "leaveTypeCode", "employee.gradeRank", "employee.departmentId"],
  "steps": [
    { "key": "manager", "name": "Reporting manager",
      "approvers": { "type": "ReportingManager", "level": 1 },
      "mode": "Any", "slaHours": 24, "reminderHours": 12,
      "onTimeout": { "action": "Escalate", "to": { "type": "ReportingManager", "level": 2 } },
      "skipIf": { "==": [{ "var": "approver.isInitiator" }, true] } },
    { "key": "hod", "name": "Head of department",
      "enterIf": { ">": [{ "var": "days" }, 3] },                       // JsonLogic
      "approvers": { "type": "DepartmentHead" }, "mode": "Any", "slaHours": 48 },
    { "key": "hr", "name": "HR review",
      "enterIf": { "in": [{ "var": "leaveTypeCode" }, ["MATERNITY", "SABBATICAL"]] },
      "approvers": { "type": "Role", "roleTemplate": "HR_MANAGER", "scope": "EmployeeBranch" },
      "mode": "Any", "slaHours": 48 }
  ],
  "options": { "allowWithdraw": true, "allowSendBack": true, "notifyInitiatorOnEachStep": false }
}
```

**Approver rule types**: `ReportingManager(level n)`, `FunctionalManager`, `DepartmentHead`, `BranchHead`, `CostCenterOwner`, `Role(template/role id, scope relative to the subject employee)`, `SpecificUser`, `ApproverGroup`, `EmployeeField` (e.g. the project manager custom field), `Expression` (advanced, Phase 6). **Modes**: Any / All / Quorum(n). Steps are sequential. Parallel branches come in Phase 6.

### 13.3 Runtime flow

1. The source service (e.g. Time) saves `LeaveRequest(Status = Pending)` and writes the `StartWorkflow{ DefinitionCode, EntityType, EntityId, SubjectEmployeeId, Context }` command to the outbox, in one transaction.
2. Workflow selects the **published version** (the instance stays pinned to that version for life), evaluates `enterIf` against the context, resolves approvers (from `ref.EmployeeSnapshot` / org data), applies **delegations** active today, removes the initiator (SoD), deduplicates, and creates `WorkflowTask`s. It publishes `WorkflowTaskAssigned` → Notification (email + in-app + push).
3. The approver acts: `POST /workflows/tasks/{id}/approve` (permission `workflow.task.act`, and the task must be assigned to the caller or a delegate). The action is logged and the next step is evaluated.
4. On completion Workflow publishes `WorkflowCompleted{ EntityType, EntityId, Outcome, FinalApprover }`. Time consumes it (idempotent) → `LeaveRequest.Approve()` → ledger debit → `LeaveRequestApproved` → attendance days marked OnLeave, notifications, reporting.
5. **SLA**: a Hangfire recurring job every 5 min (per-tenant fan-out) queries `IX(TenantId, Status, DueDate)` for overdue tasks → reminder / escalate / auto-action per step config, logged in `WorkflowActionLog`.
6. **Withdraw/cancel**: the initiator cancels → `CancelWorkflow` → open tasks cancelled.
7. **Reassign on org change**: `EmployeeManagerChanged` → pending tasks assigned to the old manager for that subject are re-resolved (configurable).
8. **Failure**: if no approver resolves (e.g. vacant manager), fall back to the tenant's `DefaultApprover` role and raise a `WorkflowConfigurationWarning` for admins. The instance never dead-ends silently.

**Admin UI**: definition list per module, form-based step editor (Phase 2: sequential steps, approver rule picker, condition builder over declared variables), a **simulation** ("who would approve a 5-day leave for employee X?"), versioning/publish, and the instance timeline viewer.

---

## 14. Notification architecture

```
Domain services ──(integration events / SendNotification cmd via outbox)──► Notification.Worker
   1. Map event → NotificationEvent code (e.g. LEAVE_REQUEST_APPROVED)
   2. Resolve recipients (user ids; role-based via Identity API; external email for candidates)
   3. Per recipient × channel: mandatory? user preference? tenant channel enabled? quiet hours → defer
   4. Select template: tenant(locale) → tenant(default locale) → platform(locale) → platform(en)
   5. Render (Scriban, sandboxed: no file/network access; whitelisted variables per event)
   6. Persist OutboundMessage (IdempotencyKey = eventId+recipient+channel)  ── in-app: InAppNotification + SignalR push
   7. Channel dispatcher (per-channel queues, KEDA-scaled) → Provider adapter (Integration layer ports)
        Email: Azure Communication Services / SendGrid / tenant SMTP
        SMS: Twilio / MSG91 / ACS    WhatsApp: Meta Cloud API / Twilio (pre-approved templates)
        Push: FCM / APNs / Web Push
   8. Delivery webhooks → status updates; retries (backoff, 5 attempts), failover provider, suppression list (bounces)
```

- **Tenant-specific templates** with preview and test-send, versioned. Platform defaults ship for every event in the supported locales.
- **Privacy**: SMS, WhatsApp and push carry **no sensitive content** (e.g. "Your September payslip is available" + deep link, never amounts). Email bodies avoid salary figures by default (configurable per template, with a warning).
- **Controls**: per-tenant daily quotas and rate limits (to stop a runaway bulk send), digest mode (daily summary of approvals), unsubscribe for non-mandatory events, bounce/complaint handling.
- **Branding**: tenant logo/colours injected into the email layout, with custom from-address after domain verification (SPF/DKIM).

---

## 15. Document architecture

- **Storage abstraction** `IFileStorage` (Azure Blob; S3 and local-disk adapters for on-prem/dev). **Container per tenant** `t-{tenantId}` (a clean isolation boundary, per-tenant lifecycle policies, easy tenant purge), with key layout `{ownerType}/{ownerId}/{documentGroupId}/v{version}/{sanitizedFileName}`.
- **Upload flow**: `POST /documents/upload-requests {documentTypeCode, ownerType, ownerId, fileName, size, contentType}` → permission + type rules (extension allowlist, size, owner access) → a `Document` row (ScanStatus=Pending) + **write-only SAS to the quarantine container**, 10 min → the client uploads directly → Defender for Storage malware scan result (Event Grid) → the Document worker verifies magic bytes vs declared type, computes SHA-256, **moves to the tenant container**, sets `Clean` → publishes `DocumentAvailable`. Infected files are deleted, a security event is raised and the uploader is notified. Small server-generated files (payslips) are written directly.
- **Download**: `GET /documents/{id}/download` → tenant + permission + owner-scope check → `DocumentAccessLog` → **302 to a read-only SAS for that blob, valid 5 min**, with `Content-Disposition: attachment`. High-sensitivity documents stream through the API instead (no SAS exposure). The API never renders HTML/SVG inline.
- **Versioning**: new uploads to the same `DocumentGroupId` increment `Version`. Only the current version shows by default. The full history is available to permitted users.
- **Generated documents**: `DocumentTemplate` (Scriban HTML + CSS) → PDF (QuestPDF for structured docs such as payslips; headless Chromium/Playwright renderer for rich HTML letters) → stored as a `Document` with `Source=Generated`. Offer letters, appointment/relieving/experience letters, payslips (optionally password-protected), certificates. E-signature adapter (DocuSign / Adobe Sign / local e-sign) in Phase 5.
- **Expiry tracking**: a daily per-tenant job queries `IX(TenantId, ExpiryDate)` → notifications at 30/15/7/0 days to the employee + HR. The dashboard shows an "expiring documents" widget. Visa/work-permit expiry can trigger a workflow task.
- **Retention**: `RetentionUntil` is computed from `DocumentType.RetentionDays` + trigger (upload/exit/expiry). A nightly retention job deletes or anonymizes unless `LegalHold`. Blob soft-delete (14 days) + versioning. Immutable (WORM) policies for statutory archives (payroll registers).
- **Policy documents** with **acknowledgement tracking** (`DocumentAcknowledgement`) and re-acknowledgement on a new version.
- **Encryption**: SSE with platform-managed keys. Enterprise tier can use customer-managed keys (encryption scope per tenant container).

---

## 16. Audit architecture

### 16.1 Capture

Three complementary sources, all landing in the Audit store:

1. **Data audit** — an EF Core `SaveChanges` interceptor walks the change tracker for entities marked `[Auditable]`, building `OldValues`/`NewValues`/`ChangedFields`. Properties marked `[Sensitive]` record **"changed"** rather than the value (or a hash), so the audit log never becomes a second copy of the PII. The audit record is written to the **outbox in the same transaction** as the business change and relayed to the Audit service — so the audit trail cannot drift from the data, and no cross-database transaction is needed.
2. **Business/action audit** — explicit `IAuditService.RecordAsync(action, entity, details)` for things the change tracker cannot express: approvals, logins, exports, downloads, payroll finalization, role grants, impersonation, permission denials.
3. **Access audit** — sensitive reads (`audit.SensitiveDataAccess`) and document downloads, written by the field-masking layer and the Document service.

### 16.2 Record contents (required fields for every entry)

`TenantId · UserId · ImpersonatorUserId · ActorType · IpAddress · UserAgent · OccurredDate (UTC) · Service · Action · EntityType · EntityId · OldValuesJson · NewValuesJson · ChangedFieldsCsv · RequestId · CorrelationId (W3C trace id) · Outcome (Success/Failure/Denied)`

Correlation: the BFF generates `X-Correlation-Id` if absent, OpenTelemetry propagates `traceparent` across HTTP and messages, and both land on the audit row and every log entry — one id ties together UI action → API → events → jobs → audit.

### 16.3 Storage, integrity and access

- `audit.AuditLog` is a **SQL Server ledger (append-only) table**, monthly-partitioned. The application's DB principal has INSERT and SELECT only (no UPDATE/DELETE), enforced by grants.
- **Retention tiers**: hot in SQL for 13 months → monthly partition export to immutable (WORM) blob (Parquet) for 7–10 years, configurable per tenant/country → purge. Security events keep 2 years hot.
- **Access**: tenant admins and Auditors see their tenant's audit through `/api/v1/audit/logs` (filters: date, user, entity, action; export is itself audited). Platform staff can see only platform-level events unless a `SupportAccessGrant` exists.
- **Alerting**: `TenantViolation`, repeated `PermissionDenied`, bulk export, off-hours payroll change, and privileged role grants raise alerts.

### 16.4 Privacy, retention and data subject rights

| Capability | Design |
|---|---|
| Data retention | `audit.RetentionPolicy` per entity type and country → nightly job: Anonymize / Delete / Archive. Statutory minimums (e.g. payroll records) win over deletion requests |
| Employee privacy | Field-level permissions + masking; PII encrypted with per-tenant keys; PII never in logs, URLs or analytics events; access to sensitive fields is logged and visible to the employee ("who viewed my data" — optional per tenant) |
| Candidate privacy | Consent captured with an expiry. Non-hired candidates are auto-anonymized after the retention window (configurable, e.g. 180 days / 2 years) |
| Data export (DSAR) | `POST /api/v1/privacy/requests {type: Export, subjectId}` → a job assembles every service's data for the subject (each service exposes an internal `/internal/privacy/export?subjectId`) → an encrypted ZIP (JSON + documents) delivered as a Document with an expiring link |
| Deletion / anonymization | Erasure = **pseudonymization**: replace names, contact details and identity numbers with `REDACTED-{hash}`, delete documents not under legal hold, keep the financial/statutory skeleton (payroll totals with the employee code) for the statutory period. Fully irreversible; logged; requires two-person approval |
| Crypto-shredding | Per-tenant data keys mean that destroying a tenant key renders encrypted PII unrecoverable — used at tenant offboarding alongside row deletion |
| Tenant offboarding | Export everything → 30-day grace (suspended, read-only) → purge rows + drop the blob container + revoke keys → **certificate of deletion** generated |

---

## 17. Security architecture

| Area | Controls |
|---|---|
| **Authentication** | OIDC/PKCE, BFF cookies (`__Host-`, HttpOnly, Secure, SameSite=Strict), short access tokens, rotating refresh tokens with reuse detection, MFA + step-up, lockout, breached-password check, no user enumeration, JWKS rotation |
| **Authorization** | Deny by default (every endpoint needs `[RequirePermission]`; a startup check fails the build/boot if any non-anonymous endpoint lacks one), permissions + data scope + field-level, resource checks, SoD rules, 404 instead of 403 for out-of-scope resources |
| **Tenant isolation** | The 15 layers in §9, with automated cross-tenant tests in CI |
| **SQL injection** | EF Core parameterization; Dapper with parameters only; no dynamic SQL concatenation (sort/filter fields are whitelist-mapped, not interpolated); Roslyn analyzers + CodeQL; RLS limits blast radius even on a successful injection |
| **XSS** | Angular's contextual escaping; `innerHTML` banned by lint (bypasses need review); server-side HTML sanitization (Ganss.Xss) for rich text (job descriptions, templates); strict CSP with nonces, `object-src 'none'`, `frame-ancestors 'none'`; uploaded files are never served inline from the app origin (separate storage domain, attachment disposition) |
| **CSRF** | BFF same-origin cookies with SameSite=Strict + a required custom header (`X-CSRF: 1`) + ASP.NET antiforgery for form posts. APIs accept bearer tokens only (not cookies) |
| **CORS** | The SPA is same-origin with the BFF → no CORS. Internal APIs allow no browser origins. The careers portal and the public API have an explicit allowlist per tenant, with no wildcards and no credentials |
| **File upload** | Extension + MIME + magic-byte validation, size caps, malware scanning in quarantine, filename sanitization, no execution, download via attachment SAS, per-tenant container, image re-encoding for avatars |
| **API abuse / rate limits** | Front Door WAF (OWASP rules, bot protection, geo filtering); ASP.NET rate limiter — per user/tenant token bucket (e.g. 100 rps burst), stricter fixed windows on login (5/min/IP), export, import and public endpoints; per-tenant quotas from the plan; payload size limits; pagination caps; query complexity limits on reports |
| **Secrets** | Azure Key Vault + managed identity. No secrets in appsettings, code or pipelines (only Key Vault references). Rotation runbooks; git-secrets/Gitleaks in CI; connection strings resolved at runtime from the catalog |
| **Encryption** | TLS 1.2+ everywhere (HSTS, TLS 1.3 preferred); TDE at rest (CMK for Enterprise tenants); **application-level envelope encryption with a per-tenant DEK** (wrapped by a Key Vault KEK) for national ID, tax ID, bank account, passport, DOB, personal contacts; HMAC blind indexes for lookups; Always Encrypted considered but rejected as the primary mechanism because it blocks payroll computation and pattern search |
| **PII protection** | Separate DTOs per sensitivity level, masking attributes, sensitive endpoints with their own permissions and step-up, no PII in logs/telemetry (Serilog destructuring policy + a log-scrubbing test), no PII in URLs/query strings, redaction in error messages, data minimization in events |
| **Audit** | §16, including denials and reads of sensitive data |
| **Application hardening** | Security headers (CSP, HSTS, X-Content-Type-Options, Referrer-Policy, Permissions-Policy), no stack traces to clients, health endpoints unauthenticated but contentless, Swagger UI disabled in production, dependency scanning (Dependabot + `dotnet list package --vulnerable` + npm audit) blocking on high severity, container image scanning (Trivy), SAST (CodeQL), DAST (OWASP ZAP baseline against QA), annual external pen test, OWASP **ASVS L2** as the target, threat modelling per module at design time |
| **Operational** | Least-privilege DB users per service (no `db_owner` at runtime; a separate migration principal), private endpoints for SQL/Redis/Storage/Service Bus, no public DB access, JIT/PIM for production access, break-glass accounts with alerting, immutable backups |

---

## 18. Performance & scalability

**Targets**: 100+ tenants and 10k+ employees at launch, architecture able to reach millions of employee records. SLOs: p95 < 300 ms for list/detail reads, < 800 ms for writes, dashboard first paint < 2 s, payroll for 10k employees < 15 min, attendance processing for 100k employee-days < 10 min, 99.9% availability.

| Lever | Detail |
|---|---|
| Indexing | Clustered on (TenantId, Id); every index leads with TenantId; covering + filtered indexes for hot queries (§7); monthly index/statistics maintenance (Ola Hallengren-style jobs); Query Store enabled with automatic plan-regression correction |
| Pagination | Offset paging with capped page size for UI grids; **keyset pagination** for punches, audit, notifications and exports; `totalCount` optional |
| Filtering/projection | Whitelisted sort/filter fields mapped to expressions; `AsNoTracking` + `Select` into DTOs everywhere on the read path; `AsSplitQuery` for multi-collection includes; compiled queries for the hottest paths; Dapper for wide list/report queries |
| Caching (Redis) | Tenant settings, feature flags, permission sets, lookups, org tree, policies, holiday calendars — 5–30 min TTL with event-driven invalidation; short-TTL output cache for dashboard widgets; distributed locks (RedLock) for payroll runs and sequence allocation; **every key namespaced by tenant**; stampede protection (single-flight) |
| Local cache | `HybridCache` (L1 memory + L2 Redis) for near-static platform data (permission catalog, compliance packs) |
| Background processing | Hangfire per service (SQL storage) for schedules; MassTransit consumers for event work; **per-tenant fan-out** so one large tenant cannot starve others; concurrency limits per tenant; KEDA scaling on queue depth |
| Async APIs | 202 + job resource for payroll, imports, exports, bulk operations, document generation; SignalR push on completion |
| Bulk operations | `SqlBulkCopy` for imports and attendance ingestion; `ExecuteUpdate`/`ExecuteDelete` for set-based updates; batched saves (500-row chunks) with per-chunk transactions and resumable jobs |
| Read/write separation | Azure SQL read scale-out (`ApplicationIntent=ReadOnly`) for reports, exports and dashboards on Business Critical tiers; the Reporting service always reads replicas/read model; writes stay on the primary |
| Reporting read model | Facts built from events (and nightly reconciliation), clustered columnstore for large facts, pre-aggregated daily snapshots (headcount, attendance). Beyond ~50M fact rows or complex BI needs, publish to Microsoft Fabric / Synapse with tenant RLS and (optionally) Power BI Embedded |
| Data volume plan | Punches: 10k employees ≈ 10M rows/year → monthly partitions, 25 months hot, then archive. At 1M employees (1B rows/year) punch ingestion moves to a dedicated partitioned store with per-region sharding by tenant |
| Payload | Response compression (Brotli), ETag/304 for reference data, HTTP/2, CDN for static assets, lazy-loaded Angular routes, virtual scrolling in large grids, image thumbnails for avatars |
| Multi-tenant fairness | Per-tenant rate limits and job concurrency caps; noisy-neighbour detection (per-tenant DTU/RU telemetry) → move a heavy tenant to a dedicated DB; elastic pools sized with headroom |
| Testing | k6/NBomber load scripts per phase against QA with a seeded 50k-employee tenant; regression gates on p95; EF query plan review for every new list endpoint |

---

## 19. Integration architecture

**Principle**: domain services depend only on **ports** (`IEmailSender`, `ISmsSender`, `IPayrollBankFileWriter`, `IAccountingExporter`, `IJobBoardPublisher`, `IIdentityProviderSync`, `IFileStorage`). Adapters live in the **Integration Hub** (or the Notification service for messaging channels), configured per tenant in `int.TenantConnection` with secrets in Key Vault. Swapping a provider is a configuration change, not a code change in HR modules.

| Integration | Direction | Approach |
|---|---|---|
| Entra ID / Okta / Google / SAML | In | OIDC/SAML federation per tenant + **SCIM 2.0** inbound provisioning (users, groups → roles); optional Graph sync for photos/managers; group-to-role mapping rules |
| Email | Out | ACS / SendGrid / tenant SMTP adapters; delivery webhooks; domain verification (SPF/DKIM) for custom from-addresses |
| SMS | Out | Twilio / MSG91 / ACS; DLT/sender-id compliance fields per country |
| WhatsApp | Out (+In later) | Meta Cloud API / Twilio; pre-approved template catalog per tenant; opt-in tracking; session-window rules |
| Biometric / access-control devices | In | Two paths: (a) **on-prem connector agent** (`Gggs.AttendanceConnector`, a Windows service that talks to ZKTeco/eSSL/Matrix SDKs on the LAN and pushes batches over HTTPS with a device key + HMAC and store-and-forward buffering); (b) **direct device push** to `/attendance/device-ingest` for cloud-capable devices. Both are idempotent on `ExternalRef`, deduplicated, and mapped through `DeviceUserMap`. Health: heartbeat + "device offline" alerts |
| Accounting / ERP (Tally, D365 BC, SAP, QuickBooks, Xero, Zoho) | Out | GL journal export per payroll run and expense reimbursement: adapter per system (API) or file (CSV/XML/JSON) with a mapping table for cost centers and GL codes; retry + reconciliation report |
| Banking / payments | Out (+In) | Salary payment files per bank format (ISO 20022 pain.001, NACHA, bank-specific H2H), or payment APIs; encrypted file delivery (SFTP adapter) ; status file ingestion updates `BankPaymentLine`; **maker-checker + hash verification before release** |
| Tax / statutory portals | Out | Return and challan files generated from compliance packs (e.g. India PF ECR, ESI, Form 24Q; UAE WPS SIF); versioned per filing period; download + optional API filing where available |
| Storage providers | Both | `IFileStorage` adapters (Azure Blob default, S3, on-prem SMB for hosted installs) |
| Job boards / recruitment portals | Both | Publish postings (LinkedIn, Indeed, Naukri) and ingest applications via API or mailbox parsing; referral links; careers-page embed |
| Assessment / BGV / e-sign | Both | Adapter interfaces reserved (HackerRank, e-sign, background verification) — Phase 5 |
| Customer-owned systems | Both | **Public REST API** (APIM, client credentials, per-tenant scopes, rate-limited) + **outbound webhooks** (HMAC-signed, retried with backoff, subscription-managed) + scheduled CSV import/export via SFTP drop |

**Import/export framework** (used by every module): template download (per tenant, including custom fields) → upload → structural + business validation → error preview (row/field/message) → commit (async, chunked, resumable) → summary report + audit. Exports use the same column metadata.

---

## 20. DevOps architecture

| Environment | Purpose | Data | Infra |
|---|---|---|---|
| Local | Development | Seeded demo tenants | .NET Aspire AppHost: SQL Server, Redis, RabbitMQ, Azurite, Mailpit, Seq, plus all services |
| Dev | Integration of merged code | Synthetic | Container Apps (small), shared SQL elastic pool, auto-deploy from `main` |
| QA | Test + automated E2E, perf | Synthetic + 50k-employee load tenant | Same topology as prod, smaller |
| UAT | Customer acceptance, training | Anonymized copy of a pilot tenant | Production-like, separate subscription |
| Production | Live | Real | Multi-zone, elastic pools, failover groups, WAF, private endpoints |
| DR | Recovery | Geo-replicated | Secondary region, warm standby (IaC + geo-replicas), quarterly drills |

### 20.1 Two deployment profiles (decided: both are supported)

The product ships in two profiles from the same codebase and the same container images. The profile is chosen by configuration (`Gggs:DeploymentProfile = Cloud | OnPremise`), and **every infrastructure dependency is a port with two adapters**, registered by profile in `Gggs.ServiceDefaults`.

| Dependency | Cloud (SaaS) | On-premise / IIS |
|---|---|---|
| Compute | Azure Container Apps (revisions, KEDA) | IIS + ASP.NET Core Module (one site/app pool per service) **or** Docker Compose on Windows/Linux |
| Reverse proxy / WAF | Azure Front Door + BFF | IIS/ARR or Nginx + BFF (WAF is the customer's) |
| Database | Azure SQL (elastic pools, failover groups) | SQL Server 2019+ on the customer's estate (Always On AG optional) |
| Messaging | Azure Service Bus | **RabbitMQ** (shipped as a container/service in the installer) |
| Cache / session | Azure Cache for Redis | Redis container (or a single-node Redis service) |
| File storage | Azure Blob (container per tenant) | `IFileStorage` → filesystem/SMB or MinIO, same key layout, per-tenant folder |
| Secrets | Key Vault + managed identity | `ISecretStore` → DPAPI/Windows Credential Store-backed encrypted file, or HashiCorp Vault if the customer has one |
| Config / feature flags | Azure App Configuration | Config files + a DB-backed flag provider (same `IFeatureManager` interface) |
| Telemetry | App Insights / Azure Monitor (OTLP) | **Seq + Prometheus/Grafana** or any OTLP collector — the app only emits OpenTelemetry |
| Real-time | Azure SignalR Service | Self-hosted SignalR with the Redis backplane |
| Malware scanning | Defender for Storage | ClamAV/ICAP adapter in the same quarantine → scan → promote flow |
| Background jobs | Hangfire (SQL) — identical | Hangfire (SQL) — identical |
| Identity | Same Identity service; tenant federation to Entra/Okta/AD FS | Same, plus optional on-prem AD via LDAP/ADFS |

Consequences that must be honoured from Phase 0: no direct SDK calls to Azure services from domain or application code (an architecture test enforces it); no reliance on managed identity in shared code paths; SQL features used must exist in SQL Server 2019 box (RLS, temporal, partitioning and ledger tables all do); **a second CI/CD path** produces a versioned on-prem release bundle (images + Compose/IIS scripts + migration bundles + an installer/upgrade script + an offline licence/config file), and a "single-tenant on-prem" smoke environment runs in CI nightly. Multi-region residency, elastic pools and auto-failover are cloud-only features and are documented as such.

- **Repo**: single monorepo (`frontend/`, `backend/`, `infra/`, `database/`, `tests/`, `docs/`) with path-filtered pipelines. Trunk-based development with short-lived branches and PR review; conventional commits; semantic versioning per service.
- **CI (per PR)**: restore/build → unit tests → integration tests (**Testcontainers**: SQL Server, Redis, RabbitMQ) → **architecture tests** (NetArchTest: layering, no cross-service references, every entity implements `ITenantOwned`, every controller action has a permission attribute) → **tenant isolation test suite** → lint/format (`dotnet format`, ESLint, Stylelint) → Angular unit tests + affected builds (Nx) → CodeQL + dependency scan + Gitleaks → container build + Trivy scan + SBOM. All must pass to merge.
- **CD**: build images once → push to ACR → deploy Dev automatically → QA on a green nightly → UAT/Prod with approval gates. **Blue-green via Container Apps revisions** with traffic splitting (10% → 100%) and automatic rollback on error-rate alerts. Smoke tests after each deploy.
- **Database migrations**: EF Core **migration bundles** executed as a pre-deployment job per service; **expand → migrate → contract** so old and new code run side by side (add nullable → backfill job → switch code → drop later). Dedicated-tenant databases are migrated by a fan-out job over the tenant catalog, with per-tenant status and retry. Every migration is reviewed for locking/online index behaviour. Rollback = a forward fix (destructive changes never ship in the same release as the code that needs them).
- **Configuration**: `appsettings.{Env}.json` for non-secret defaults + **Azure App Configuration** (with feature flags and labels per environment) + **Key Vault references**; nothing secret in the repo; per-service managed identity.
- **Observability**: OpenTelemetry SDK for traces/metrics/logs → Azure Monitor / App Insights (with an OTLP option for Grafana/Tempo). Serilog structured logging enriched with `TenantId, UserId, CorrelationId, Service, Version`. Dashboards: golden signals per service, queue depth, DLQ count, job failures, payroll run durations, login failures, per-tenant usage. Alerts on SLO burn rate, DLQ > 0, health-check failures, high 5xx, auth anomalies. **Distributed tracing** across HTTP + Service Bus + Hangfire (context propagation in message headers).
- **Health**: `/health/live` (process), `/health/ready` (SQL, Redis, bus, Key Vault), `/health/startup`. Container Apps probes use them; a synthetic availability test runs from outside.
- **Backup/DR**: Azure SQL PITR 35 days + LTR (weekly 12 w, monthly 12 m, yearly 7 y); **auto-failover groups** to the secondary region (RPO ≈ 5 s, RTO ≈ 1 h target); blob GRS + versioning + soft delete + WORM for archives; Key Vault soft-delete + purge protection; IaC in git so the environment can be rebuilt; **documented runbooks**: region failover, single-tenant restore (PITR to a side DB → copy that tenant's rows → validate → swap), corrupted-payroll-run recovery, DLQ replay, key rotation. Restore drills every quarter, with results recorded.

---

## 21. Complete folder structure

```
C:\Gagan\GGGSERP\
├─ .github/workflows/            ci-backend.yml, ci-frontend.yml, cd-deploy.yml, codeql.yml, migrations.yml
├─ docs/
│  ├─ adr/                       0001-tenancy-model.md, 0002-service-boundaries.md, ...
│  ├─ architecture/              this document, diagrams (C4), event-catalog.md, permission-catalog.md
│  ├─ api/                       generated OpenAPI snapshots
│  ├─ runbooks/                  failover.md, tenant-restore.md, payroll-recovery.md, key-rotation.md
│  └─ domain/                    payroll-rules-{country}.md, leave-rules.md, glossary.md
├─ frontend/                     (Nx workspace — see §11.4)
├─ backend/
│  ├─ Gggs.sln
│  ├─ Directory.Build.props      LangVersion, Nullable, TreatWarningsAsErrors, analyzers
│  ├─ Directory.Packages.props   central package management (one version per package)
│  ├─ BuildingBlocks/
│  │  ├─ Gggs.BuildingBlocks.Domain/           Entity, AggregateRoot, ValueObject, DomainEvent, Result, Guard
│  │  ├─ Gggs.BuildingBlocks.Application/      ICurrentUser, IUnitOfWork, PagedResult, validation, IAuditService
│  │  ├─ Gggs.BuildingBlocks.MultiTenancy/     ITenantContext, middleware, registry, connection resolver, job/message filters
│  │  ├─ Gggs.BuildingBlocks.Security/         permissions catalog, policy provider, handlers, data scope, masking, encryption
│  │  ├─ Gggs.BuildingBlocks.Persistence/      TenantDbContext base, interceptors (tenant/audit/outbox/session-context),
│  │  │                                         conventions, soft delete, Dapper factory, repository base, migration helpers (RLS)
│  │  ├─ Gggs.BuildingBlocks.Messaging/        MassTransit config, outbox/inbox, envelope, idempotent consumer base
│  │  ├─ Gggs.BuildingBlocks.Web/              exception handler, ProblemDetails, versioning, OpenAPI, rate limits, ETag,
│  │  │                                         [Idempotent], correlation, health checks, Serilog setup
│  │  ├─ Gggs.BuildingBlocks.Jobs/             Hangfire setup, tenant fan-out scheduler, job filters
│  │  ├─ Gggs.BuildingBlocks.Files/            IFileStorage + Azure/S3/local adapters
│  │  ├─ Gggs.BuildingBlocks.Observability/    OpenTelemetry, enrichers, metrics
│  │  └─ Gggs.BuildingBlocks.Testing/          WebApplicationFactory, Testcontainers fixtures, tenant test harness, builders
│  ├─ Gateway/
│  │  └─ Gggs.Gateway.Bff/                     YARP routes, OIDC + cookie auth, session store, aggregation endpoints, CSRF
│  ├─ Services/
│  │  ├─ Identity/     Gggs.Identity.{Api,Application,Domain,Infrastructure,Worker,Contracts}
│  │  ├─ Platform/     Gggs.Platform.{Api,Application,Domain,Infrastructure,Worker,Contracts}      (tenant, subscription, audit)
│  │  ├─ CoreHr/       Gggs.CoreHr.{Api,Application,Domain,Infrastructure,Worker,Contracts}        (org, emp, cfg, asset)
│  │  ├─ Time/         Gggs.Time.{...}                                                             (attendance, leave)
│  │  ├─ Payroll/      Gggs.Payroll.{...}                                                          (+ Gggs.Payroll.Engine)
│  │  ├─ Talent/       Gggs.Talent.{...}                                                           (rec, onb, perf, lms)
│  │  ├─ Spend/        Gggs.Spend.{...}                                                            (exp, trv)
│  │  ├─ Workflow/     Gggs.Workflow.{...}
│  │  ├─ Notification/ Gggs.Notification.{...}
│  │  ├─ Document/     Gggs.Document.{...}
│  │  ├─ Reporting/    Gggs.Reporting.{...}
│  │  └─ Integration/  Gggs.Integration.{...}
│  ├─ Agents/
│  │  └─ Gggs.AttendanceConnector/             on-prem biometric agent (Windows service + installer)
│  └─ Aspire/
│     ├─ Gggs.AppHost/                         local orchestration of every service + dependencies
│     └─ Gggs.ServiceDefaults/                 shared host wiring (OTel, health, resilience, service discovery)
├─ database/
│  ├─ security/                                RLS policy generator scripts, DB role/grant scripts per service
│  ├─ seed/                                    permissions, role templates, lookup data, demo tenant
│  ├─ compliance-packs/                        IN/1.0/pack.json, AE/1.0/pack.json ... (versioned, checksum-verified)
│  ├─ maintenance/                             index/stats jobs, partition sliding window
│  └─ Gggs.Reporting.Database/                 optional SDK-style .sqlproj for the reporting model
├─ tests/
│  ├─ unit/           Gggs.{Service}.UnitTests
│  ├─ integration/    Gggs.{Service}.IntegrationTests            (Testcontainers + WebApplicationFactory)
│  ├─ Gggs.TenantIsolation.Tests/              cross-tenant probes over every route
│  ├─ Gggs.Architecture.Tests/                 NetArchTest rules
│  ├─ Gggs.Contract.Tests/                     event schema compatibility (published vs consumed)
│  └─ load/                                    k6 scenarios per module
├─ infra/
│  ├─ bicep/          modules/{sql,redis,servicebus,containerapp,storage,keyvault,appconfig,frontdoor,signalr,monitor}
│  │                  environments/{dev,qa,uat,prod}.bicepparam
│  ├─ docker/         Dockerfile per service (multi-stage), docker-compose.yml for a lightweight local run
│  ├─ onprem/         release bundle: compose.onprem.yml, iis/{web.config templates, app-pool setup.ps1},
│  │                  install.ps1, upgrade.ps1, prerequisites check, offline config template, RabbitMQ/Redis/MinIO defaults
│  └─ scripts/        provision-tenant.ps1, rotate-keys.ps1, restore-tenant.ps1
└─ tools/             code generators (new service, new module), OpenAPI client generation, data seeders
```

### Naming conventions

| Area | Convention | Example |
|---|---|---|
| C# projects/namespaces | `Gggs.{Service}.{Layer}` / `.Features.{Module}.{Feature}` | `Gggs.Time.Application.Features.Leave.Requests` |
| C# members | PascalCase types/methods, `_camelCase` private fields, `I`-prefixed interfaces, `Async` suffix on async methods, `sealed` by default, `record` for DTOs | `LeaveRequestService.SubmitAsync` |
| DTOs | `{Verb}{Entity}Request`, `{Entity}Response`, `{Entity}ListItem`, `{Entity}ListQuery` | `CreateLeaveRequestRequest` |
| Domain events / integration events | `{Entity}{PastTenseVerb}` + `IntegrationEvent`, versioned namespace `...Contracts.V1` | `LeaveRequestApprovedIntegrationEvent` |
| Topics / queues | `gggs.{service}.{entity}.{event}` / `gggs.{service}.{consumer}` | `gggs.time.leave-request.approved` |
| DB schemas/tables/columns | lowercase schema, PascalCase singular table, `Id` PK, `{Entity}Id` FK | `leave.LeaveRequest.EmployeeId` |
| DB objects | `PK_`, `UX_`, `IX_`, `FK_{Child}_{Parent}`, `CK_`, `DF_` | `IX_Employee_Status` |
| Permissions | `{module}.{resource}.{action}` lowercase dotted | `payroll.run.finalize` |
| Feature flags | `{Module}.{Feature}` PascalCase dotted | `Payroll.MultiCurrency` |
| REST | plural kebab-case resources, camelCase JSON, verbs only for transitions | `POST /api/v1/leaves/requests/{id}/cancel` |
| Angular files | kebab-case (Angular style guide, no forced `.component` suffix but **one choice, applied everywhere**), stores `*.store.ts`, services `*.service.ts`, routes `*.routes.ts` | `leave-request-list.ts` |
| Angular selectors / CSS | `gggs-` prefix, BEM-ish utility-free component styles | `<gggs-data-table>` |
| Redis keys | `gggs:{env}:t:{tenantId}:{area}:{key}` | `gggs:prod:t:9f..:perm:3c..:v7` |
| Blob keys | `t-{tenantId}/{ownerType}/{ownerId}/{groupId}/v{n}/{file}` | |
| Git | `feat/…`, `fix/…`, `chore/…` branches; conventional commits; PR title = changelog line | |

---

## 22. Development roadmap

Assumed team: 4 backend, 3 frontend, 1 QA automation, 1 DevOps, 1 BA/HR SME, part-time UX and architect. Two-week sprints. Durations are planning estimates, not commitments.

### Phase 0 — Foundation (4–5 weeks)
- **Deliver**: monorepo, solution skeleton, BuildingBlocks (tenancy, security, persistence, web, messaging, jobs, testing), Aspire local stack, CI/CD to Dev, IaC for Dev/QA, OpenAPI + client generation, Angular shell on **PrimeNG** with the theme-token layer (tenant branding) plus data-table and dynamic-form skeletons, logging/tracing, the tenant-isolation test harness, the permission catalog, and the service template generator.
- **Dual-profile groundwork** (because on-prem is in scope): `IFileStorage`, `ISecretStore`, message transport, cache, telemetry and feature-flag ports each with a Cloud and an OnPremise adapter; the architecture test banning direct Azure SDK use outside adapters; the second pipeline that produces the **on-prem release bundle** (images, Compose/IIS scripts, migration bundle, install/upgrade script) and a nightly single-tenant on-prem smoke environment.
- **Exit criteria**: a "hello module" service passes the full CI gate, including RLS and cross-tenant tests; one command runs the whole stack locally; the same build deploys green to both Dev (Azure) and the on-prem smoke box.

### Phase 1 — Tenant, Identity, Organization, Employee (10–12 weeks)
| | |
|---|---|
| Modules | Platform (tenant, subscription, feature flags, settings), Identity (users, roles, permissions, SSO, MFA, sessions), Core HR (org structures + employee), Document v1, Notification v1 (email + in-app), Audit v1 |
| Tables | `platform.*`, `iam.*`, `org.*`, `emp.*`, `cfg.*`, `doc.*`, `ntf.*`, `audit.*` (§7) |
| APIs | `/tenants`, `/subscriptions`, `/features`, `/users`, `/roles`, `/permissions`, `/identity-providers`, `/me`, `/connect/*`, all `/org` resources, `/employees` + sub-resources, `/documents`, `/notifications`, `/audit/logs` |
| UI | Login + SSO + MFA + password reset; app shell; platform-admin tenant console; tenant onboarding wizard; org structure CRUD (incl. department tree); employee list, 360° profile, hire wizard, transfer/promote/confirm/exit; employee documents; custom fields admin; users & roles admin; audit log viewer; tenant-admin dashboard v1 |
| Dependencies | Phase 0. Identity before everything; Core HR before Time |
| Sequence | Platform+Identity (parallel with the Angular shell) → org → employee → documents/notifications → dashboards |
| Testing | Unit (domain rules), integration per endpoint, **tenant isolation suite**, auth/permission matrix tests, E2E: provision tenant → invite admin → create org → hire employee → employee logs in; migration rehearsal; security review of the auth flow |

### Phase 2 — Attendance, Leave, ESS/MSS, Workflow v1 (10–12 weeks)
| | |
|---|---|
| Modules | Time (shifts, roster, punches, attendance processing, regularization, overtime, holidays), Leave (types, policies, ledger, balances, requests), Workflow engine v1 (sequential multi-level, manager-chain/role approvers, conditions, delegation, SLA reminders), ESS & MSS, Reporting v1, Import framework, SCIM |
| Tables | `time.*`, `leave.*`, `wf.*`, `rpt.*` (headcount, attendance, leave facts), `int.ImportJob*` |
| APIs | `/attendance/*`, `/leaves/*`, `/holidays/*`, `/workflows/*`, `/reports/*` (core set), `/imports` |
| UI | Check-in/out widget, my attendance, attendance admin grid + corrections, shift & roster management, leave apply/cancel with a balance-aware form, leave balances, team calendar, holiday calendars, **unified approvals inbox**, manager team dashboard, employee dashboard, workflow configuration admin, first report set + exports |
| Dependencies | Employee + org (Phase 1); workflow must land before leave approvals |
| Sequence | Workflow v1 → leave (policy → ledger → request → approval) → attendance (shift/roster → punch → day processing) → ESS/MSS → reports |
| Testing | Heavy domain unit tests (accrual, carry-forward, pro-rata, sandwich rules, night shifts, DST/time zones), month-end attendance job at 100k employee-days, workflow scenario tests (delegation, escalation, SoD), E2E ESS journeys, load test on punch ingestion |

### Phase 3 — Recruitment, Onboarding, Performance (10–12 weeks)
| | |
|---|---|
| Modules | Recruitment/ATS, careers portal (public SSR app), Onboarding, Performance (goals, cycles, self/manager/360 reviews), Learning v1 |
| Tables | `rec.*`, `onb.*`, `perf.*`, `lms.*` |
| APIs | `/recruitment/*`, `/public/careers/*`, `/onboarding/*`, `/performance/*`, `/learning/*` |
| UI | Requisition + approval, posting management, candidate pipeline (kanban), interview scheduling + scorecards, offer generation/approval/acceptance, hire-from-offer, onboarding checklists (HR/IT/manager/new hire views), goal setting with cascade, review forms, calibration view, my performance |
| Dependencies | Workflow (requisition/offer approvals), Document templates (offer letters), Notification, Employee (hire hand-off) |
| Testing | Candidate → offer → employee E2E; public portal security tests (anonymous endpoints, rate limits, file uploads); review visibility rules (who can see whose ratings) |

### Phase 4 — Payroll, Expenses, Travel, Assets (12–16 weeks — the highest-risk phase)
| | |
|---|---|
| Modules | Payroll (structures, components, inputs, runs, payslips, tax, loans, F&F, bank files, GL), the **India compliance pack v1**, Expenses, Travel, Assets |
| India pack v1 contents | EPF (employee/employer split, EPS, admin charges, wage ceiling and the "restrict to ceiling" option, UAN), ESI (wage limit, contribution periods), **professional tax per state** (slab tables per state, half-yearly states handled), LWF, gratuity accrual + payment on exit, **TDS under both old and new regimes** (slabs, surcharge, cess, rebate, standard deduction, Chapter VI-A declarations and proofs, perquisites, HRA exemption, projected annual tax spread over the remaining months), leave encashment and bonus taxation, arrears with Form 10E data, statutory outputs: **PF ECR text file, ESI contribution file, PT challan data, Form 16 Part B inputs and quarterly Form 24Q (annexures)**, plus the Indian financial-year calendar (Apr–Mar) and rounding rules |
| Tables | `pay.*`, `comp.*`, `exp.*`, `trv.*`, `asset.*` |
| APIs | `/payroll/*`, `/expenses/*`, `/travel/*`, `/assets/*` |
| UI | Salary structure setup, employee salary + revision workflow, payroll input entry/import, **payroll run console** (calculate → variance review vs prior period → errors → approve → finalize → payslips → bank file → GL), payslip viewer/download, tax declaration + proof upload, expense claim + receipts, travel request/advance/settlement, asset register + assign/return |
| Dependencies | Attendance & leave (LOP, paid days), Workflow (salary revision, run approval, expense), Documents (payslips), Finance integration |
| Sequence | Components/structures → salary assignment → engine + compliance pack → run lifecycle → payslips/bank/GL → expenses → travel → assets |
| Testing | **Parallel run against the customer's existing payroll for 2–3 cycles with 100% reconciliation**; golden-file tests per compliance rule; rounding and pro-rata edge cases (mid-month joiners/leavers, arrears, negative net); idempotency and reversal tests; segregation-of-duties tests; 10k-employee performance run; penetration test of payroll endpoints |

### Phase 5 — Reports, Analytics, Integrations (8–10 weeks)
- Full report catalog per module, report builder with saved/scheduled reports, analytics dashboards (attrition, cost, diversity, funnel, absenteeism), the reporting read model completed with backfill, exports at scale.
- Integration Hub: public API + APIM, outbound webhooks, accounting/banking/tax adapters, job-board connectors, SFTP scheduled exchanges, biometric connector GA, e-sign.
- Data retention/DSAR automation, tenant offboarding, admin self-service for connections.

### Phase 6 — Advanced workflow, automation, AI (10–12 weeks)
- Workflow: visual designer, parallel branches, conditional routing UI, quorum approvals, auto-actions, cross-module orchestrations (exit clearance), workflow analytics (bottlenecks, SLA breaches).
- Automation: rule engine ("when probation ends in 7 days → create a task + notify"), scheduled bulk actions, no-code custom entities (evaluate).
- AI features per §24, starting with the policy Q&A assistant and payroll/attendance anomaly detection.

**Continuous across phases**: accessibility audits, performance gates, security reviews per module, documentation, pilot-tenant feedback, localization of new strings.

---

## 23. MVP scope

**MVP = Phase 0 + Phase 1 + Phase 2** (≈ 6–7 months with the assumed team): the smallest package a customer will actually pay for.

**In**: multi-tenant provisioning and subscription/feature gating; SSO (Entra ID) + local login with MFA; roles and permissions; full org structure; employee master with documents, custom fields and history; attendance (web/mobile punch, shifts, rosters, regularization, biometric import via CSV/connector) ; leave (policies, accruals, balances, applications, approvals, holiday calendars); configurable approval workflow with delegation and escalation; ESS and MSS; unified approvals inbox; notifications (email + in-app); employee/manager/HR dashboards; ~15 core reports with export; import framework; audit log; English + one additional locale.

**Out of MVP** (explicitly): payroll, expenses, travel, assets, recruitment, onboarding automation, performance, learning, public API, WhatsApp, advanced analytics, AI, workflow designer UI, mobile native app (the responsive web app covers it).

**Pilot plan**: two or three design-partner tenants of different sizes (say 50, 500, 3,000 employees) onboarded in UAT with real org data; weekly feedback; success criteria = 95% of leave/attendance transactions completed without support, p95 latency within SLO, zero cross-tenant findings in the security review, and data migration completed within one day per tenant.

---

## 24. Future AI architecture

**Placement**: a dedicated **AI Gateway service** (Phase 6). No model ever touches a database directly.

```
Angular assistant ──► AI Gateway ──► Model provider (Claude API: claude-opus-5 / claude-sonnet-5 / claude-haiku-4-5,
                         │            or an Azure-hosted model where residency demands it — provider abstraction)
                         ├── Tool layer: calls existing REST APIs **with the user's own token** → permissions,
                         │   data scope, field masking and audit all apply unchanged
                         ├── RAG: per-tenant index (tenant-id filter injected server-side; a dedicated index for
                         │   Enterprise tenants) over policy documents, handbooks and the help centre
                         ├── Guardrails: PII redaction before the prompt, prompt-injection defences (retrieved text is
                         │   data, never instructions; strict tool allowlist; confirmation required for any write),
                         │   output validation, token/cost quotas per tenant, full audit of prompts and tool calls
                         └── No training on customer data; zero-retention provider settings; opt-in per tenant & feature
```

| Feature | Technique | Guardrail |
|---|---|---|
| HR chatbot / policy Q&A | RAG over the tenant's policy documents, with citations | Answers only from retrieved tenant content; "I don't know" + route to HR; never quotes another employee's data |
| Employee HR assistant | Tool calling over ESS APIs ("apply 2 days casual leave next Tuesday") | Acts as the user; every write shows a confirmation card; the API still enforces policy |
| Natural-language reports | LLM → **validated query DSL** against the report metadata catalog → Reporting service executes | Never generates raw SQL; the user's data scope applies; the result shows the interpreted filters |
| Resume screening | Embedding + structured match against requisition criteria, producing a ranked shortlist with reasons | **Assistive only, never auto-reject**; bias monitoring; protected attributes excluded from features; human decision recorded; EU AI Act high-risk obligations (transparency, oversight, logging) |
| Attrition insights | Tabular ML (gradient boosting) on tenure, engagement, comp ratio, movement, leave and manager-change features | Aggregate-first presentation; individual risk visible only to HR with permission; explainability (SHAP-style drivers); never used for adverse action alone |
| Attendance anomalies | Statistical/rule detection (buddy punching, impossible geo travel, pattern shifts) | Flags for review, not automatic penalties |
| Payroll anomalies | Variance vs prior period per component/employee, outlier detection, new-component checks, statutory sanity checks | Blocks finalization with warnings; reviewer must acknowledge each flag |
| Document generation | LLM drafts from templates + structured data | Human review before send; templates remain the source of formatting; no model-invented figures (values come from the data, not the model) |
| Sentiment analysis | Survey/feedback aggregation | **k-anonymity ≥ 5** before showing any group result; no per-employee sentiment scoring; opt-in |
| HR analytics assistant | NL → report DSL + chart suggestion | Same scope and audit as reports |

**Data privacy rules**: tenant isolation in the vector store is enforced server-side (the client never supplies the filter); prompts are scrubbed of national IDs, bank details and salary unless the user holds the permission *and* the feature is enabled; every AI interaction is written to the audit log (prompt hash, tools called, entities touched); tenants can disable AI entirely; model and provider are configurable per tenant for residency.

---

## 25. Risks and technical trade-offs

| # | Risk / trade-off | Impact | Mitigation |
|---|---|---|---|
| 1 | **Payroll compliance depth** — India first, and Indian rules are dense and change with each Union Budget (slabs, ceilings, state PT tables) | Wrong pay, legal exposure | Compliance packs (versioned, effective-dated, data-driven), an Indian payroll SME on the team, golden-file tests per rule, **parallel runs for 2–3 cycles per pilot tenant**, a named owner and a budget-season update process, statutory outputs validated against the real portals before go-live |
| 2 | **Distributed-system overhead** with 12 services and a small team | Slower delivery, hard debugging | Fewer, capability-sized services; shared BuildingBlocks; Aspire for local dev; tracing from day one. If delivery drags, run Core HR + Time + Workflow in **one process** ("modular monolith mode" — same code, fewer deployables) and split later |
| 3 | **Pooled database**: noisy neighbours and per-tenant restore | Latency spikes, slow recovery for one tenant | Per-tenant rate/concurrency limits, usage telemetry, promotion to a dedicated DB, a scripted tenant-restore runbook, elastic pools with headroom |
| 4 | **Eventual consistency surprises** (a new employee not yet visible in Time) | Confusing UX, support tickets | Snapshot events consumed within seconds; UI shows "syncing" where relevant; nightly reconciliation; synchronous fallback reads for critical paths |
| 5 | **Tenant isolation defect** | Existential | Four enforcement layers + an automated cross-tenant suite in CI + pen testing + alerting on `TenantViolation` |
| 6 | **Workflow engine scope creep** toward a general BPM | Endless build | Written scope: sequential approvals with conditions, delegation and SLA. Anything beyond goes to a MassTransit saga or Phase 6 with an explicit decision |
| 7 | **Custom fields and configurability** hurting performance and upgrades | Slow lists, brittle migrations | JSON + indexed computed columns with a hard cap, no tenant-defined tables in v1, config versioning |
| 8 | **Third-party licence changes** (MediatR, AutoMapper, MassTransit) | Cost or rework | Thin abstractions around them, Mapperly + plain handlers by default, a Phase 0 licence decision with Wolverine as the fallback |
| 9 | **BFF complexity** vs tokens in the SPA | More moving parts | Accepted: PII in an HR system justifies it. YARP + cookie auth is a small, well-trodden amount of code |
| 10 | **Effective-dated data and time zones** (retro transfers, back-dated leave, DST, night shifts crossing midnight) | Subtle wrong results | UTC storage + explicit work-location time zone, `DateOnly` for business dates, retro-change recalculation jobs, an extensive edge-case test suite |
| 11 | **Data residency** demands (EU/UAE/India) | Blocked deals | Region-per-tenant from day one in the catalog (`Tenant.DataRegion`); the stack deploys per region; no cross-region tenant data |
| 12 | **Report load on OLTP** | Slow transactions | Read model + read replicas; heavy exports always asynchronous; report query complexity limits |
| 13 | **Migration of legacy HR data** per tenant | Slow onboarding, bad data | The import framework is a first-class Phase 2 feature; templates per entity; validate-then-commit; a documented migration playbook |
| 14 | **AI features** (privacy, bias, regulation) | Legal and trust risk | Opt-in, tenant-isolated retrieval, no training on customer data, assistive-only for employment decisions, full audit, EU AI Act alignment |
| 15 | **Single monorepo + many pipelines** | Long CI times | Path filters, Nx affected, test parallelization, build-once/promote |
| 16 | **Dual deployment profile** (cloud + on-prem/IIS) doubles the infrastructure surface: two adapter sets, two pipelines, customer-run SQL/RabbitMQ versions, upgrades you don't control | ~10–15% extra effort in Phase 0 and a permanent support burden; risk of cloud-only shortcuts breaking on-prem silently | Ports with two adapters from day one, the architecture test banning direct Azure SDK use, a nightly on-prem smoke environment in CI, a documented minimum-version matrix, a signed release bundle with an idempotent upgrade script, and cloud-only features (multi-region residency, elastic pools, auto-failover) explicitly listed as unavailable on-prem |
| 17 | **PrimeNG accessibility gaps** against the WCAG 2.2 AA target | Rework late, or a failed accessibility audit | All PrimeNG use is wrapped in `shared/ui`; axe-core checks per wrapped component in CI; an accessibility audit at the end of Phase 1 while the component set is still small |

---

## Verification (how we prove each phase works)

1. **Local**: `dotnet run --project backend/Aspire/Gggs.AppHost` brings up every service, SQL, Redis, the broker and the SPA; the Aspire dashboard shows health and traces. `npx nx serve hrms-web` for frontend work.
2. **Automated gates** (all must be green to merge): `dotnet test` (unit + integration with Testcontainers), `Gggs.TenantIsolation.Tests` (cross-tenant probes over every route), `Gggs.Architecture.Tests`, `nx affected -t lint test build`, Playwright E2E on the seeded demo tenant, and a `dotnet ef migrations bundle` dry run applied to a fresh database.
3. **Manual smoke per phase**: provision a tenant from the platform console → invite an admin → SSO login → build the org → hire an employee → that employee logs in → applies leave → the manager approves from the inbox → the balance and attendance day update → the audit log shows every step with one correlation id.
4. **Isolation proof**: log in as tenant A and attempt tenant B's IDs on every module (expect 404), call the DB directly without `SESSION_CONTEXT` (expect zero rows), try a cross-tenant SAS (expect denial).
5. **Performance gate per phase**: k6 run against QA with a 50k-employee tenant; p95 must stay within the SLOs in §18; payroll and attendance batch jobs must stay inside their windows.
6. **Security gate per phase**: `/security-review` on the diff, dependency and container scans, ZAP baseline against QA, and a permission-matrix test run for every new endpoint.

## Decisions confirmed and still open

**Confirmed**: hosting = **Azure SaaS + supported on-prem/IIS profile** (ADR-11, §20.1) · UI kit = **PrimeNG**, wrapped behind `shared/ui` (ADR-10) · first compliance pack = **India** (ADR-13, Phase 4).

**Still open (needed in Phase 0 unless noted)**
1. Messaging library licence: MassTransit v9 (commercial) vs Wolverine (MIT) — both satisfy the outbox/inbox design; decide in Phase 0 week 1.
2. Whether on-prem customers are single-tenant only (recommended) or may run multi-tenant themselves — affects licensing and the installer.
3. Minimum supported on-prem versions (SQL Server 2019 vs 2022, Windows Server, Docker availability) — 2022 would let ledger tables stay on everywhere.
4. Locales for the MVP beyond English (Hindi? RTL needed only if GCC is an early market).
5. Whether the pilot tenants need legacy-data migration in the MVP window (drives how early the import framework must be production-grade).

