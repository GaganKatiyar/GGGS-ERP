# ADR-12: One modular monolith, one database

- **Status:** Accepted (2026-10-07)
- **Context:** [PeopleOS target architecture](../architecture/peopleos-target.md),
  [existing vs target](../architecture/existing-vs-target.md)
- **Supersedes:** ADR-02 (service granularity), ADR-03 (database per service), and the
  cross-service half of [ADR-06](0006-messaging.md)

## Decision

PeopleOS ships as **one deployable backend** and **one transactional SQL Server database**:

- `Host/Gggs.Api` serves the REST API (`/api/v1/*`), the OIDC endpoints (`/connect/*`), the BFF
  session endpoints (`/bff/*`) and the SPA's static files.
- `Host/Gggs.Worker` runs background work: Hangfire jobs and the outbox dispatcher.
- Both hosts share `Data/Gggs.Data`, which holds a single `AppDbContext` and a single migration
  history.
- Business capabilities live in `Modules/<Name>`. A module has two projects:
  - `Gggs.Modules.<Name>` contains its internals (domain, application, infrastructure, endpoints).
  - `Gggs.Modules.<Name>.Contracts` is the only thing other modules may reference.
- Each module owns its own SQL schema(s). Cross-module foreign keys are allowed because there is
  only one database.

The 11 services, 11 databases, the YARP gateway, the replicated `ref.EmployeeSnapshot` tables and
every service-to-service HTTP call are retired.

## Why

**The product is one tightly linked domain.** The PeopleOS chain runs Employee → Skills → Allocation
→ Timesheet → Utilization → Cost → Profitability, and the reverse chain runs Demand → Forecast →
Hiring. Every link crosses what used to be a service boundary. Across services, each link was an
event, a snapshot table and an eventual-consistency window. In one process, each link is a method
call inside a transaction.

**The distributed overhead was being paid without the benefit.** There is one team, no production
traffic, and no module with a scaling profile different enough to need its own process. Meanwhile:

- The local stack needed 8 processes to sign in, and more to see any cross-service effect happen.
- Five copies of the employee snapshot had to be kept in step.
- Payroll called Time over HTTP in the middle of a run.

**On-premise installs get simpler.** ADR-11 commits us to customer-run servers. Two processes and
one database are far easier to install, back up and upgrade than twelve of each.

**Extraction stays possible.** The `.Contracts` boundary, schema-per-module and architecture tests
mean a module can still be lifted into its own service later. To do that, re-implement its contract
interfaces over HTTP and its events over the existing broker adapters.

## What we keep from the old design

- Tenancy (ADR-01): pooled by default, with a dedicated database on demand. A dedicated tenant now
  gets one database instead of eleven.
- Keys (ADR-08), compliance packs (ADR-09), frontend stack (ADR-10) and deployment profiles (ADR-11).
- ADR-06's outbox and inbox. The default transport becomes an in-process dispatcher. The RabbitMQ
  and Service Bus adapters remain for external integrations.

## What we give up

- Independent deployment and scaling per module.
- Fault isolation: a runaway query in one module can hurt every module.

**Mitigations:**
- Per-module schemas.
- Architecture tests that fail the build on illegal cross-module references.
- Heavy work (payroll runs, forecasting, report generation) stays in the Worker, not in request
  threads.
