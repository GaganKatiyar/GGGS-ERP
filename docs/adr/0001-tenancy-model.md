# ADR-01: Pooled database by default, dedicated database on demand

- **Status:** Accepted (2026-09-23)
- **Context:** Architecture §3

## Decision

Tenant-owned rows carry a `TenantId` and share one database per service. Enterprise and regulated
tenants get the **same schema in a dedicated database**, selected per tenant through the tenant
catalog. Schema-per-tenant is rejected.

## Why

Three models were compared on isolation, cost, migration effort, per-tenant restore, residency and
how they behave at a thousand tenants (§3.1).

- **Shared schema** is the only model where onboarding a tenant is an insert and a schema change is
  one migration. Its weakness is that isolation depends on getting the code right, which is why the
  isolation design has four independent layers rather than one.
- **Dedicated database** is what enterprise buyers ask for, and it is the only way to offer a
  customer-managed encryption key, a specific region, or a restore of one tenant without touching
  others. It costs more and multiplies migrations, so it is not the default.
- **Schema per tenant** combines the migration cost of a database per tenant with the weaker
  isolation of a shared one, and EF Core supports it poorly (model cache and migration history are
  both per-schema). It earns nothing.

Because both supported tiers use the identical schema, with `TenantId` and row-level security
present in both, promoting a tenant is a data move rather than a code path. That is what keeps the
option genuinely open instead of theoretical.

## Consequences

- Per-tenant point-in-time restore on the pooled tier needs a documented runbook (restore to a side
  database, copy that tenant's rows), not a button. Tenants who need better buy the dedicated tier.
- Noisy neighbours are managed with per-tenant rate and job-concurrency limits, plus promotion to a
  dedicated database when telemetry justifies it.
- Every tenant-owned table must be listed in the row-level security policy. Forgetting one is the
  main failure mode, so adding a table re-runs the policy with the full list, and the isolation test
  suite runs on every pull request.
