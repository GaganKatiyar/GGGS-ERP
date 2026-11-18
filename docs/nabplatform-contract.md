# NaBPlatform contract (shared by GGGS-ERP-PLATFORM and GGGS-ERP)

NaBPlatform is the platform console's database. The HR application (repository **GGGS-ERP**) reads
it directly with its own connection string (`ConnectionStrings:PlatformDb`). The platform app never
connects to the HR database. A change to anything below needs a matching change in **both** repos,
released together. The same file is kept in both repos; edit both.

## Who writes what

| Table | Written by | Read by |
|---|---|---|
| `platform.Tenant` | Platform | HR (registry, sign-in discovery, suspension) |
| `platform.Plan` | Platform (catalogue sync at start) | HR (features) |
| `platform.Subscription` (incl. `FeatureOverrides` JSON) | Platform | HR (features / modules) |
| `platform.ProvisioningRequest` | Platform inserts; **HR updates** `Status`, `Result`, `CompletedDate` | both |
| `hr.TenantSeedState` | HR | Platform ("setting up") |
| `hr.TenantUsage` | HR (hourly) | Platform |
| `hr.TenantAdministrator` | HR (hourly and after an invitation) | Platform |
| `hr.JobHealth` (one row, Id = 1) | HR (every 5 minutes) | Platform |
| `platform.PlatformStaff`, `PlatformRole`, `PlatformRoleAssignment`, `PlatformAuditEntry`, `DedicatedDatabase` | Platform | Platform only |

## SQL logins

| Login | Database | Can do | Used by |
|---|---|---|---|
| `gggs_admin` | NaBPlatform and the HR database | owner (`db_owner`; in the HR database also `rls_bypass`) | platform console server, migrations, DevSeed, support |
| `gggs_tenant` | NaBPlatform only | `SELECT` on `platform.Tenant`, `Plan`, `Subscription`, `ProvisioningRequest`; `UPDATE (Status, Result, CompletedDate)` on `platform.ProvisioningRequest`; `SELECT, INSERT, UPDATE, DELETE` on schema `hr` | HR API and worker (`ConnectionStrings:PlatformDb`) |
| `gggs_app` | HR database only | `db_datareader`, `db_datawriter`; never owner, never `rls_bypass` | HR API and worker (`ConnectionStrings:AppDb`) |

`gggs_tenant` is granted table by table, so a new platform table stays invisible to the HR app until it
is added to this contract and to `scripts/nabplatform-logins.sql` (GGGS-ERP-PLATFORM). The HR
database's logins are created by `infra/sql/peopleos-logins.sql` (GGGS-ERP). Both scripts are safe to
re-run and never change an existing password.

## Columns the HR app relies on

- `platform.Tenant`: `Id`, `Code`, `Name`, `Status` (tinyint: 1 Provisioning, 2 Active, 3 Suspended,
  4 Offboarding, 5 Archived, 6 Purged), `IsolationTier` (1 Pooled, 2 Dedicated), `CountryCode`,
  `DataRegion`, `DefaultTimeZone`, `DefaultCurrency`, `DefaultLocale`.
- `platform.Plan`: `Id`, `Code`, `FeatureCodes` (JSON array of feature codes, e.g. `["CoreHr","Time"]`).
- `platform.Subscription`: `Id`, `TenantId`, `PlanId`, `Status` (tinyint: 1 Trial, 2 Active, 3 PastDue,
  4 Cancelled, 5 Expired — 1–3 are live), `StartDate`, `TrialEndsOn`, `EndDate`, `SeatLimit`,
  `FeatureOverrides` (JSON array of `{ FeatureCode, IsEnabled, ExpiresOn, Reason }`).
- `platform.ProvisioningRequest`: `Id`, `TenantId`, `Kind` (tinyint: 1 AddAdministrator,
  2 ResendAdministratorInvitation, 3 RetryJob, 4 SeedOrganisation), `Email`, `DisplayName`, `JobId`,
  `Status` (1 Pending, 2 Done, 3 Failed), `Result`, `RequestedDate`, `CompletedDate`.

## What the HR worker does (job `platform-sync`, every ~30 seconds)

1. For each `Pending` request, oldest first:
   - **SeedOrganisation**: raise `TenantProvisionedIntegrationEvent` in the HR database's outbox
     (the HR modules seed the organisation; Identity creates the administrator and emails the
     invitation), insert `hr.TenantSeedState`, mark `Done`.
   - **AddAdministrator** / **ResendAdministratorInvitation**: run in the tenant through
     `TenantBootstrapService`, mark `Done` (or `Failed` with the reason), refresh `hr.TenantAdministrator`.
   - **RetryJob**: requeue the Hangfire job, mark `Done` / `Failed`.
2. Every 5 minutes: write `hr.JobHealth`. Hourly: `hr.TenantUsage` and `hr.TenantAdministrator`.

The HR app caches each tenant's status and features for at most 60 seconds, so status, plan and module
changes made in the console take effect within a minute without any message.
