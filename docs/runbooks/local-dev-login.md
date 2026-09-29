# Signing in to the Angular app locally

A working login end to end: SQL Server → Platform.Api → Identity.Api → Gateway.Bff → the Angular
dev server, with a real tenant and a real tenant-admin account. No RabbitMQ, no Redis, no Docker -
none of them are needed to reach a sign-in page and see the app shell.

## What this does and does not cover

**Covers**: signing in as a tenant administrator and using the app shell, dashboard,
Organization screens (legal entities, branches, departments, designations, grades, cost centers),
the full Employee lifecycle (directory, hire, transfer, promote, change manager, confirm, serve
notice, exit, rehire), employee documents (upload, download, delete, from a new section on the
Employee detail page), document type administration (`/admin/document-types` - create one before
anything can be uploaded anywhere), the audit log (`/admin/audit` - filter by entity type/id,
action, service and date range; click a row for the old/new value diff), user/role administration
(invite a user, complete their invitation from the link it hands back, enable/disable/unlock,
manage their roles, create a role and grant/revoke its permissions), tenant settings (a raw
module + key + JSON editor - no module has a defined settings schema yet), and the notification
inbox (`/notifications` - list, unread count badge in the header bell, mark one or all read) with
a full permission set. `tools/DevSeed`'s seeded admin now holds the document, audit and
notification permissions too, so all of this is reachable without hand-editing a role.

**Does not cover**: Business units and locations have no Organization screen, since neither sits
on the hire path. Notification template administration (as opposed to the inbox) has no frontend
screen yet - exercise it with `curl` or the OpenAPI page. Everything else in the roadmap beyond
Core HR and Identity (Time, Payroll, Talent, …) has no frontend at all yet.

## One-time setup

1. A local SQL Server reachable at `Server=localhost;Integrated Security=True` (the same one the
   test suite uses - see the repository README's prerequisites).
2. `npm ci` inside `frontend/` if you have not already.

## Every time: seven terminals + one seed command

```bash
# 1. Seed a tenant and a tenant-admin account directly in the databases (creates them if they
#    do not exist, applies migrations, and is safe to re-run - it leaves an existing tenant/user
#    alone rather than erroring). Only covers Platform and Identity - see the next step for the
#    other databases.
dotnet run --project tools/DevSeed -- acme admin@acme.example "Passw0rd!2026"

# CoreHr's, the Documents service's and the Notification service's own databases need their
# migrations applied once too (DevSeed does not touch any of them):
dotnet ef database update --project backend/Services/CoreHr/Gggs.CoreHr.Infrastructure \
  --startup-project backend/Services/CoreHr/Gggs.CoreHr.Infrastructure
dotnet ef database update --project backend/Services/Documents/Gggs.Documents.Infrastructure \
  --startup-project backend/Services/Documents/Gggs.Documents.Infrastructure
dotnet ef database update --project backend/Services/Notification/Gggs.Notification.Infrastructure \
  --startup-project backend/Services/Notification/Gggs.Notification.Infrastructure

# 2. Platform (terminal 1)
cd backend/Services/Platform/Gggs.Platform.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5102 dotnet run

# 3. Identity (terminal 2)
cd backend/Services/Identity/Gggs.Identity.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5101 dotnet run

# 4. Core HR - organization and employees (terminal 3)
cd backend/Services/CoreHr/Gggs.CoreHr.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5103 dotnet run

# 5. Documents - upload, list, download, delete (terminal 4)
cd backend/Services/Documents/Gggs.Documents.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5104 dotnet run

# 6. Notification - a user's own inbox and template administration (terminal 5)
cd backend/Services/Notification/Gggs.Notification.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5105 dotnet run

# 7. Gateway (terminal 6)
cd backend/Gateway/Gggs.Gateway.Bff
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5100 dotnet run

# 8. Angular (terminal 7)
cd frontend
npx nx serve hrms-web
```

`Gggs.Notification.Worker` is not in this list on purpose - see the next section.

(PowerShell: replace `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5102 dotnet run`
with `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5102'; dotnet run`.)

Open **http://localhost:4200/sign-in** and sign in with:

| Field | Value |
|---|---|
| Organisation code | `acme` |
| Work email | `admin@acme.example` |
| Password | `Passw0rd!2026` |

Change any of the three by passing different arguments to `DevSeed` (tenant code, email,
password, in that order) - it provisions whatever you ask for.

## Why this needs no message broker or cache

- `AddGggsMessaging`'s RabbitMQ connection is a lazy DI registration (`AddSingleton<IConnection>(factory
  => ...)`), only resolved when something actually publishes/consumes a message. Only the
  `*.Worker` processes (e.g. `Gggs.Identity.Worker`) do that; the `*.Api` processes only write to
  their own outbox table, so they start and serve requests fine with no broker reachable.
- Redis is optional everywhere: `AddGggsCaching` falls back to an in-memory `HybridCache`, and the
  Gateway's session store falls back to an in-memory store, when `ConnectionStrings:Redis` is empty.
- `Secure` cookies (including the `__Host-gggs-session` cookie) work over plain HTTP specifically
  on `localhost` - browsers treat it as a secure context. Using `127.0.0.1` or any other hostname
  instead would break this.

## Why the audit trail stays empty in this setup

Every write through Platform, Identity or Core HR's `Api` process stages an
`AuditEntryIntegrationEvent` into that service's own outbox (`TenantAuditSaveChangesInterceptor`) -
that part needs no broker, same as any other outbox write. What turns those staged messages into
rows a tenant admin can read from `GET /api/v1/audit/logs` is `Gggs.Platform.Worker`, which is a
`*.Worker` process like `Gggs.Identity.Worker`: it needs RabbitMQ (or Azure Service Bus) reachable,
same as the tenant-provisioning saga does, and this runbook does not start one. Rows stage; nothing
consumes them; `audit.AuditLog` (`platform` schema) stays empty until either `Gggs.Platform.Worker`
runs against a real broker, or a test seeds rows directly the way
`tests/Gggs.Platform.IntegrationTests/AuditEndpointTests.cs` does.

## Why inviting a user does not actually send an email in this setup

`UserManagementService.InviteAsync` stages a `UserInvitedIntegrationEvent` into Identity's own
outbox - same shape, same "needs no broker" story as the audit trail above. Turning that into an
in-app row and a queued email is `Gggs.Notification.Worker`'s job (`UserInvitedConsumer` →
`NotificationDispatchService`), and this runbook does not start it either. The invitation itself
still works: `InviteAsync` returns the token directly in its response, which is what
`complete-invitation` already relies on (see the "Covers" list above) - the notification is a
second, currently-inert path to the same link, not the only one. Even with the worker running, no
template exists for `user.invited` until one is created through `POST
/api/v1/notifications/templates` (Notification never ships a platform default - see
`NotificationTemplate`'s own remarks), and even with a template, no SMTP host is configured by
default, so `SmtpEmailSender` logs the rendered email instead of sending it.

## Why this bypasses the async tenant-provisioning saga

The real path is Platform (outbox) → RabbitMQ → `Identity.Worker` → `TenantSeedingService`, which
needs a running broker and a second process per service. `tools/DevSeed` writes the same rows
(`platform.Tenant`, `iam.Role`/`User`/`UserRole`) directly through each service's own `DbContext`,
including the two interceptors that make a real write pass SQL Server's row-level security
(`TenantSessionContextInterceptor` for `SESSION_CONTEXT`, `TenantAuditSaveChangesInterceptor` for
stamping `TenantId` on the new rows) - the same two interceptors the running services register.
See the tool's own comments in `tools/DevSeed/Program.cs` for the full reasoning.

## Gaps this surfaced (fixed as part of getting a real login working)

Every prior test of the sign-in path ran against `WebApplicationFactory`'s in-memory `TestServer`
with `ITenantRegistry` stubbed out, or against a `https://localhost` base address that satisfies
transport-security checks without any real TLS. Standing up real Kestrel processes over real HTTP,
talking to each other, surfaced three things nothing had exercised before:

1. **`RemoteTenantRegistry` (`Gggs.BuildingBlocks.Caching`) had no endpoint to call.** Platform
   never implemented `GET /internal/tenants/{id}` or `GET /internal/tenants/by-code/{code}` - the
   two routes the shared tenant-registry client every downstream service uses has always called.
   Added in `InternalTenantEndpoints.cs`, anonymous (there is no service-to-service credential in
   this codebase yet - tracked as a gap in the file's own doc comment, not something this pass
   invents a fix for).
2. **`RemoteTenantRegistry` deserialized the response with default `JsonSerializerOptions`,** while
   every API actually replies with camelCase properties and string enums (`AddGggsJson`). This
   always failed reading `TenantInfo.Status`/`IsolationTier` from a real host; nothing caught it
   because every test stubs `ITenantRegistry` instead of exercising this class's HTTP path. Fixed
   with an explicit, locally-defined `JsonSerializerOptions` mirroring the wire format (duplicated
   rather than referencing `Gggs.BuildingBlocks.Web`, to avoid a new cross-project dependency for
   two lines).
3. **OpenIddict refused every request over plain HTTP** (`This server only accepts HTTPS
   requests.`). `OpenIddictSetup.cs` already relaxes several other production requirements only in
   `IsDevelopment()` (the signing certificate, the gateway secret, the issuer) - added
   `DisableTransportSecurityRequirement()` to that same list, on the server builder only, guarded
   the same way.

None of these needed RabbitMQ, Redis or Docker - they were reachable the moment two real HTTP
processes actually talked to each other, which nothing before this had done.

A second pass, once the Organization and Employee screens above existed to actually drive it,
surfaced two more:

4. **The gateway had no route for Core HR at all** - only Identity and Platform were proxied.
   Every org/employee endpoint was reachable directly against `CoreHr.Api`'s own port (which is
   all its own integration tests ever do) but not through the gateway, which is the only door the
   browser has. Fixed with a `corehr` cluster and one route per resource prefix in
   `Gggs.Gateway.Bff/appsettings.json`.
5. **`gggs-data-table`'s PrimeNG bindings failed AOT compilation** the first time an app actually
   used the component (`[value]`/`[columns]` are typed as mutable arrays; the component's own
   `rows()`/`visibleColumns()` returned `readonly T[]`). Nothing had used it outside its own lib
   before the Employee and Legal Entity list screens did. Fixed with `.slice()` copies.

A third pass, building the Admin (users/roles) screens, surfaced two more of the same shape:

6. **`GET /api/v1/users/{id}/roles` did not exist.** `PUT .../roles` replaces a user's roles
   wholesale - there is no "add one role" endpoint - so a roles editor had no way to know what was
   already granted before it saved, which is exactly the kind of screen that clears access on
   first use rather than the second. Added, reusing the same
   `IRoleRepository.GetGrantsForUserAsync` the set-roles path already calls.
7. **The gateway never routed `/api/v1/auth/*` at all.** Identity's sign-in cookie flow and the
   BFF's own `/bff/login` never needed it, so nobody noticed that `complete-invitation`
   (deliberately `AllowAnonymous`, deliberately outside the BFF's OIDC exchange) had no path
   through the one door the browser has. Same shape as gap 4 above.
