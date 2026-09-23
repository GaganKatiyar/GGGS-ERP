# ADR-08: Sequential GUID keys, clustered on (TenantId, Id)

- **Status:** Accepted (2026-09-23)
- **Context:** Architecture §6.2

## Decision

Every tenant-owned table uses:

- `Id uniqueidentifier`, generated in the application as a **SQL-Server-ordered sequential GUID**
  (timestamp in the last six bytes),
- a **non-clustered** primary key on `Id`,
- a **clustered** unique index on `(TenantId, Id)`,
- composite foreign keys `(TenantId, ParentId)` → `(TenantId, Id)`.

## Why

`bigint IDENTITY` is smaller and faster, and it was rejected anyway: identity values collide when a
tenant's rows move between a pooled and a dedicated database, which ADR-01 treats as a routine
operation. Keys that survive being moved are worth eight extra bytes.

A random GUID would fragment every clustered index. `NEWSEQUENTIALID()` solves that but only
server-side, and we generate ids in the application so an aggregate has its identity before it is
saved. Hence a sequential GUID built for **SQL Server's** ordering, which sorts by the last six
bytes first - not for .NET's, which orders differently. `SequentialGuidTests` asserts that ordering
against SQL Server's own comparison rules, because a silent regression here would fragment every
index in the platform.

Clustering on `(TenantId, Id)` keeps one tenant's rows physically together, so tenant-scoped scans
stay cheap in a shared table. Making the foreign keys composite means the **database itself** cannot
link a row to another tenant's parent - isolation that holds even if every line of C# is wrong.

## Consequences

- Wider keys and wider non-clustered indexes than a bigint design. Accepted.
- Composite foreign keys need `HasPrincipalKey` configuration; it is applied by convention rather
  than per entity.
- Human-facing identifiers (`EmployeeCode`, `ClaimNo`) are separate, tenant-scoped sequences. Nobody
  should ever read a GUID aloud.
