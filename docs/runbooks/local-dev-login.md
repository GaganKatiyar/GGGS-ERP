# Signing in to the Angular app locally

A working login end to end: SQL Server → Platform.Api → Identity.Api → Gateway.Bff → the Angular
dev server, with a real tenant and a real tenant-admin account. No RabbitMQ, no Redis, no Docker -
none of them are needed to reach a sign-in page and see the app shell.

## What this does and does not cover

**Covers**: signing in as a tenant administrator and seeing the app shell, dashboard and
navigation with a full permission set.

**Does not cover**: Organization or Employee screens. The `employees` route exists in
`app.routes.ts` but currently loads the dashboard's routes as a placeholder - there is no
dedicated feature library for either module yet (`frontend/libs/features/` only has `dashboard`).
The backend for both (§ Core HR in `docs/architecture/architecture.md`) is built and tested through
its own API and Swagger/OpenAPI; this runbook only gets you to the point of an authenticated
browser session.

## One-time setup

1. A local SQL Server reachable at `Server=localhost;Integrated Security=True` (the same one the
   test suite uses - see the repository README's prerequisites).
2. `npm ci` inside `frontend/` if you have not already.

## Every time: three terminals + one seed command

```bash
# 1. Seed a tenant and a tenant-admin account directly in the databases (creates them if they
#    do not exist, applies migrations, and is safe to re-run - it leaves an existing tenant/user
#    alone rather than erroring).
dotnet run --project tools/DevSeed -- acme admin@acme.example "Passw0rd!2026"

# 2. Platform (terminal 1)
cd backend/Services/Platform/Gggs.Platform.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5102 dotnet run

# 3. Identity (terminal 2)
cd backend/Services/Identity/Gggs.Identity.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5101 dotnet run

# 4. Gateway (terminal 3)
cd backend/Gateway/Gggs.Gateway.Bff
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5100 dotnet run

# 5. Angular (terminal 4)
cd frontend
npx nx serve hrms-web
```

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
