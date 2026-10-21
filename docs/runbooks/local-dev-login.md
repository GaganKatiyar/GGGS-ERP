# Running PeopleOS locally and signing in

A working stack end to end: SQL Server, the API host, the worker host and the Angular dev server,
with a real tenant and a real tenant-administrator account. No RabbitMQ, no Redis and no Docker
are needed (ADR-12: one process serves the API, one runs background work, one database holds
everything).

## What you can use today (M1)

The foundation milestone ships the platform itself, not the business modules:

- signing in and out, and the application shell with the PeopleOS navigation;
- Administration: users (invite, complete the invitation from the link it returns,
  enable/disable/unlock, manage roles), roles (create, grant and revoke permissions), tenant
  settings (a raw module/key/JSON editor), and the audit log (`/admin/audit`, with the old/new
  value diff);
- the OIDC token endpoint (`/connect/token`) for non-browser clients.

Everything else in the navigation is marked planned and stays hidden until its milestone ships
(see [docs/roadmap.md](../roadmap.md)). Core HR (organization, employees) arrives in M2.

## One-time setup

1. A local SQL Server reachable at `Server=localhost;Integrated Security=True` (the same one the
   test suite uses - see the repository README's prerequisites).
2. `npm ci` inside `frontend/`.
3. `dotnet tool restore` inside `backend/` (for `dotnet ef`).

## Every time: one seed command, three terminals

```bash
# 1. Create/upgrade the database and provision a tenant with a working administrator. Safe to
#    re-run: it applies pending migrations and leaves an existing tenant and user alone.
dotnet run --project tools/DevSeed -- acme admin@acme.example "Passw0rd!2026"

# 2. The API (terminal 1) - http://localhost:5100
dotnet run --project backend/Host/Gggs.Api

# 3. The worker (terminal 2) - delivers outbox events (audit entries, tenant provisioning)
DOTNET_ENVIRONMENT=Development dotnet run --project backend/Host/Gggs.Worker

# 4. Angular (terminal 3) - http://localhost:4200, proxying /api and /bff to :5100
cd frontend
npx nx serve hrms-web
```

(PowerShell: `$env:DOTNET_ENVIRONMENT='Development'; dotnet run --project backend/Host/Gggs.Worker`.)

Open **http://localhost:4200/sign-in** and sign in with:

| Field | Value |
|---|---|
| Organisation code | `acme` |
| Work email | `admin@acme.example` |
| Password | `Passw0rd!2026` |

Change any of the three by passing different arguments to DevSeed (tenant code, email, password,
in that order).

### If port 5100 is taken

The API listens on 5100 because `frontend/apps/hrms-web/proxy.conf.json` proxies there. If
another application already holds the port, run the API elsewhere with
`ASPNETCORE_URLS=http://localhost:5199 dotnet run --project backend/Host/Gggs.Api --no-launch-profile`
and point the two proxy entries at the same port (do not commit that change).

## What the worker is for

Every write stages its audit entry (and any integration event) into `messaging.OutboxMessage` in
the same transaction. The worker delivers those to their consumers in-process. With the worker
stopped the application still works; the audit log simply stays empty until the worker runs, at
which point it catches up. On start-up the worker also synchronises the plan catalog and the
platform staff roles.

Since M3 the worker also:

- delivers notifications: in-app rows appear at once, and emails are queued and sent by the
  worker. Locally `Gggs:Notifications:EmailProvider` is `Log`, so emails - invitation links
  included - are written to the worker's console instead of being sent. Set it to `Smtp` (with
  `Gggs:Notifications:Smtp:*`) to send for real.
- runs the workflow SLA sweep every ten minutes (Hangfire, schema `hangfire`), escalating overdue
  approval tasks.
- completes approval-gated actions, such as publishing a policy once its approval comes through.

Since M4 it also:

- closes attendance every hour, storing the evaluated day for the last three days so late punches
  and approved corrections are picked up.
- runs leave accrual daily at 01:30 (crediting each month once) and lapses balances above the
  carry-forward limit in January. DevSeed seeds the leave catalog and credits the current month;
  **Leave > Leave policies > Run accrual** runs any month by hand.
- applies approved leave, regularizations and shift swaps.

Since M5 it also:

- sweeps contracts daily at 02:00: expires those past their end date (or rolls auto-renewing ones
  on) and reminds the owner 30 days before a renewal is due.
- records every open project's health at 02:45, so the dashboard shows a trend.
- applies approved contract activations and change requests, and takes leavers off project teams.

DevSeed adds a demo client (Northwind Retail) with an MSA, a SOW and the project NWCOM: five people
on the team, a board with a sprint running, milestones, a risk, an issue and a change request.

Since M6 it also:

- reminds everyone in scope who has not submitted last week's timesheet, every Monday at 04:00
  (`timesheet-reminders`).
- applies approved timesheets (posting their hours to project tasks) and approved project transfers.

DevSeed's `resourcing` step rates fourteen employee skills, sets up seven resource profiles with
rates, raises an open resource request on NWCOM and books eight approved weeks of timesheets, so
Resources, Utilization and the bench have data. Every tenant gets the standard skill catalogue
when it is provisioned.

Since M7 it also:

- runs the compliance sweep daily at 03:15 (`compliance-sweep`): generates the next 60 days' filings
  and reminds obligation owners a week before a filing is due and once it is overdue, and registration
  owners as expiry approaches.
- records every live project's financials at 03:00 (`project-financials`) and alerts the project
  manager when actual cost passes 90% and 100% of the budget.
- applies approved payroll runs, salary revisions, variable pay and benefit enrollments, and marks
  variable pay paid when the payroll that paid it is finalized.

Every tenant is seeded with India's statutory rules, the usual filing obligations, payroll components
and a standard structure, and four benefit plans. DevSeed's `money` step adds statutory and bank
details and salaries for the demo people, benefit enrollments, salary bands, last month's payroll
finalized with payslips, this month's run calculated, two registrations and NWCOM's budget, costs
and an invoice. It also gives the administrator the Payroll Administrator role - payroll's sensitive
permissions are not part of the Tenant Administrator role.

Since M8 it also:

- opens a requisition for every approved manpower request and fulfils the request when the
  requisition is filled; applies approved workforce plans, manpower requests and offers.
- starts onboarding for every hire, from the best-fitting checklist.
- runs the onboarding pass daily at 02:00 (`onboarding-daily`): moves pre-boarding joiners on at
  joining and reminds task owners of overdue tasks (again every three days).
- pays referral bonuses that have come due daily at 02:30 (`referral-bonuses`), as a payroll input,
  while both the referrer and the hire are still employed.

Every tenant gets a default onboarding checklist. DevSeed's `hiring` step approves this year's
workforce plan, raises two manpower requests (one approved into a requisition), opens a second
requisition, publishes a posting, adds an agency and six candidates across the pipeline with two
interviews, a scorecard and an offer out, and starts onboarding for Rohan Das with Priya Menon
(the administrator) as HR, IT and admin owner, so My onboarding tasks has work in it. It tops up the tenant's roles with the M8 permissions.

Since M9 it also:

- reminds employees and managers of reviews due within three days, daily at 03:15
  (`performance-reminders`).
- runs the learning pass daily at 02:45 (`learning-daily`): weekly reminders for overdue learning, a
  reminder 30 days before a certificate expires, and on expiry the certificate lapses and a mandatory
  course is assigned again.
- applies approved promotions whose date has come, daily at 00:05 (`career-promotions`), and applies
  approved promotions straight away when they are already effective.
- assigns joiners the mandatory courses set for joiners.

Every tenant gets four compliance courses. DevSeed's `talent` step adds a company goal, a department
goal and six aligned individual goals with check-ins; last half-year's review cycle released with
ratings and this half-year's open (two self-reviews in, and 360 feedback asked of the administrator);
learning assigned to everyone with a third completed and a security path; two career paths, two
succession plans, potential assessments for the 9-box and a promotion nomination waiting to be
submitted.

Since M10 it also:

- checks helpdesk SLAs every 30 minutes (`helpdesk-sla-sweep`): missed response and resolution
  targets are flagged and the assignee (or category owner) told; resolved tickets close themselves
  after seven days.
- opens scheduled surveys, closes those past their window and reminds people two days before a
  survey closes, daily at 01:15 (`engagement-survey-sweep`).
- awards work-anniversary points, lapses expired points and warns 30 days ahead, daily at 00:30
  (`rewards-sweep`); review and course points are awarded as those events arrive.
- exits people the day after their last working day and adds them to the alumni network, daily at
  01:00 (`separation-exit-sweep`). The exit also disables the leaver's sign-in and ends their
  sessions.
- reminds owners of overdue offboarding tasks every three days, daily at 03:30
  (`separation-offboarding-reminders`).
- looks for unexplained absence (the separation policy's number of working days in a row, after a
  day worked) and raises absconding alerts for HR and the manager, daily at 02:00
  (`separation-absence-detection`).
- proposes a separation for retirements (the last day of the month the retirement age is reached)
  and fixed-term contract ends within the policy's lead time, for HR to confirm, daily at 02:30
  (`separation-exit-proposals`). A declined proposal is not raised again for the same date.

Every tenant gets default asset, expense, helpdesk categories, five knowledge articles, recognition
types, reward rules and a catalogue. DevSeed's `experience` step registers twelve assets (eight
assigned, one in repair, a request waiting); four expense claims (paid, approved, pending, draft)
and two trips, one with an advance; five helpdesk tickets including a confidential grievance, with IT
routed to EMP00004; a closed pulse survey with seven anonymous responses and an open one for
everyone; six recognitions, a company budget and a redemption to fulfil; and Sara Khan (EMP00005) serving notice
with clearance open, plus two alumni. It tops up every template role with the M10 permissions.
Separation v2 seeds Sara's offboarding too: Rohan Das (EMP00006) named as successor, the standard
checklist and the handover found across modules. The separation policy (Services > Separation
policy) starts with Indian defaults: 60 days' notice (15 on probation), settlement within two
working days and gratuity within 30 days. Offboarding checklists are under Services > Offboarding
checklists and the steps waiting on you under Services > Offboarding tasks.
For involuntary exits it adds a show-cause notice waiting for Meera Iyer's (EMP00003) reply under
Services > Disciplinary, an absence alert for Arjun Nair (EMP00004) under Services > Absconding,
and Rohan Das's (EMP00006) fixed-term contract ending in 45 days, proposed on the Separation page
for HR to confirm. Retrenchment is under Services > Retrenchment; a contract end date is set from
an employee's Employment tab.

Since M11 it also sends report digests daily at 06:00 (`insight-report-digests`): weekly
subscriptions go out on Mondays and monthly ones on the 1st, only to people whose report access is
company-wide (or who hold executive access) when the digest is sent.

DevSeed's `insight` step adds a bank-transfer connection and a Tally journal connection (run them
from Administration > Integrations against a month with a finalized payroll) and a weekly executive
digest for the administrator. Analytics, search and the calendar read every other module, so they
fill up from the earlier demo steps. The search box in the header takes `/` as a shortcut.

### AI assistants (M12)

The assistants call the Anthropic Messages API. Without a key they are listed but switched off, and
the page says so. To switch them on for local development, set either:

- `Gggs:Ai:ApiKey` in user secrets for `backend/Host/Gggs.Api`
  (`dotnet user-secrets set "Gggs:Ai:ApiKey" "sk-ant-..." --project backend/Host/Gggs.Api`), or
- the `ANTHROPIC_API_KEY` environment variable before starting the API.

The model is `claude-opus-5-5` (`Gggs:Ai:Model`) with adaptive thinking. Server-side fallback is on:
if the model is overloaded, the API retries the same request on `claude-opus-4-8`
(`Gggs:Ai:FallbackModel`) without a second round trip. Each turn may run up to eight tool rounds
(`Gggs:Ai:MaxToolRounds`). Anything that would change data - applying for leave, raising a ticket,
giving recognition - comes back as a proposal with Approve and Reject buttons, and runs as the
person only when approved. Conversations are deleted after 30 days by the
`ai-conversation-retention` job at 02:40 (`Gggs:Ai:RetentionDays`). Assistant turns are limited to 12
a minute per person (`Gggs:RateLimiting:AssistantPerMinute`).

### Sign-in security (M12)

- **Authenticator app:** My Space > Sign-in security sets one up (any TOTP app) and hands out ten
  recovery codes once. After that, sign-in asks for a code after the password. The organisation's
  password policy decides who must have one (administrators only by default): such a person is sent
  to the set-up page after signing in until they do.
- **Step-up:** sensitive actions need an authentication in the last 15 minutes in production
  (`Gggs:Security:EnforceStepUp`, off in development). When it has lapsed, a "Confirm it's you"
  dialog asks for a code or the password and the action then goes ahead.
- **Single sign-on:** Administration > Single sign-on adds an OpenID Connect provider (Entra ID, Okta,
  Google Workspace, Keycloak). Register `https://<your host>/bff/sso/callback` as its redirect URI; set
  `Gggs:Sso:PublicOrigin` when the API is reached through a proxy under another address. The sign-in
  page shows a "Sign in with ..." button once the organisation code is entered. People are matched to
  existing accounts by verified email (invite them first); the provider never creates accounts.
  DevSeed adds an example Entra ID provider, switched off, with placeholder IDs.
- **Rate limits:** sign-in, the second step, step-up, the token endpoint, invitation completion and
  single sign-on allow 20 attempts a minute per address (`Gggs:RateLimiting:SignInPerMinute`), on
  top of the per-account lockout.

Uploaded documents are stored under `.local/files` (one folder per tenant), configured by
`Gggs:Storage:FileSystem:RootPath` in both hosts' development settings.

DevSeed links the administrator to the demo employee Priya Menon (HR), so My profile, My
documents, My policies and the approvals inbox have someone to act as. An approver cannot approve
their own request, so to try an approval workflow, sign in a second user linked to another
employee, or make Priya the approver of someone else's request.

## Why no broker or cache is needed

- Module events are delivered in-process by the worker (ADR-12). The RabbitMQ and Service Bus
  adapters remain in the code for future external integrations but are not registered.
- Redis is optional: the cache falls back to in-memory, and so do browser sessions - which is why
  restarting the API signs everyone out locally. The API logs a warning saying so.
- `Secure` cookies (including `__Host-gggs-session`) work over plain HTTP specifically on
  `localhost`, which browsers treat as a secure context. Use `localhost`, not `127.0.0.1`.

## Calling the API without the browser

```bash
# A session cookie, as the SPA gets one (the X-CSRF header is required on /bff and on any
# state-changing request that carries the cookie)
curl -c jar.txt -X POST -H "X-CSRF: 1" -H "Content-Type: application/json" \
  -d '{"email":"admin@acme.example","password":"Passw0rd!2026","tenantCode":"acme"}' \
  http://localhost:5100/bff/login
curl -b jar.txt http://localhost:5100/bff/user

# Or a bearer token, as an integration would
curl -X POST http://localhost:5100/connect/token \
  -d "grant_type=password&username=admin@acme.example&password=Passw0rd!2026&tenant_code=acme" \
  -d "client_id=peopleos-api&client_secret=development-only-gateway-secret&scope=gggs.api"
```

The OpenAPI document is at `http://localhost:5100/openapi/v1.json` in Development.
