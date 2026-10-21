# PeopleOS roadmap

Target: [architecture/peopleos-target.md](architecture/peopleos-target.md) · Audit:
[architecture/existing-vs-target.md](architecture/existing-vs-target.md) · Decision:
[ADR-12](adr/0012-modular-monolith.md)

## Definition of done (every milestone)

A module is done only when everything that applies to it exists and is tested:

- entity and migration
- API with validation, permission and data scope
- business logic, workflow hooks and audit
- UI with loading, empty and error states
- reports or dashboard tiles
- unit and integration tests
- tenant-isolation coverage (enforced by the architecture tests)
- docs

A screen on its own does not count as done.

## Milestones

| # | Scope | Depends on | Key outcomes |
|---|---|---|---|
| **M0** | Audit and target docs | — | Existing-vs-target matrix, target architecture, ADR-12 |
| **M1** | Monolith foundation | M0 | `Gggs.Api` + `Gggs.Worker` + one database; Platform and Identity modules; BFF folded in; model-driven RLS; in-process permissions, tenant registry and events; Hangfire; new navigation |
| **M2** | Core HR and UI kit | M1 | Organization (Job vs Position, effective dating), Employee (personal, contact, statutory, bank, dependents, lifecycle), MasterData, bulk import with preview; shared UI kit, data-table upgrade, Employee 360 shell |
| **M3** | Platform services | M2 | Workflow v2 (parallel, conditional, escalation, SLA, delegation), Notification (templates, in-app, email, channel port), Documents (templates, versions, e-sign port), Policies (versions, acknowledgement), approvals inbox |
| **M4** | Time | M3 | Shifts and rosters (swaps), Attendance (web, device, regularization, rules), Leave (types, policies, accrual job, ledger, sandwich and probation rules), holidays, ESS/MSS tiles |
| **M5** | Delivery: projects | M2, M3 | Client and contacts, Contract (MSA, SOW, PO, T&M), Project master and lifecycle, team and roles, phases, tasks (epic, story, bug), Kanban board, milestones, dependencies, sprints, requirements, change requests, RAID, meetings, project health |
| **M6** | Delivery: resources | M4, M5 | Skills and employee skills, resource profile, availability and capacity, requests, matching score, allocation with over-allocation detection, resource calendar, bench, release and transfer, demand vs supply, forecast; Timesheets (entry, approval, lock); Utilization and billability |
| **M7** | Money | M4, M6 | Payroll engine (versioned `StatutoryRule`: EPF, ESI, PT, TDS, gratuity), payslips, bank file; Compensation (bands, increments, variable pay); Benefits; Project Financials (cost rate × hours, revenue, billing, profitability, financial dashboard) |
| **M8** | Hiring | M3 | Workforce plans and manpower requests, Recruitment (requisition → offer, pipeline, interviews, scorecards, agencies, referrals), Pre-boarding and Onboarding checklists |
| **M9** | Talent | M5, M6 | Goals (OKR, KPI, cascading), Performance (cycles, reviews, 360, calibration, PIP, project evidence), Learning (courses, paths, certifications and expiry), Career paths, promotion, succession |
| **M10** | Employee experience | M3, M7 | Rewards (catalog, rules, recognition feed, wallet, ledger, redemption, budget, expiry job), Engagement (surveys, eNPS), Helpdesk (tickets, SLA, knowledge base), Assets (full lifecycle), Expense and travel, Separation (resignation, clearance, F&F, letters, exit interview, alumni) |
| **M11** | Insight and reach | all above | Analytics (HR, workforce, projects, resources, payroll, rewards), executive dashboards, global search, HR calendar, import/export framework, integration adapters (Entra ID, Google, biometric, accounting, bank, e-sign) |
| **M12** | AI and hardening | M11 | AI tool layer and assistants (HR command centre, workforce planner, resource manager, project, career, recruitment, performance and reward assistants) with human approval on actions; MFA and SSO federation; security and performance pass; end-to-end suites for the spec §134 critical flows |

Status lives in [existing-vs-target.md §10](architecture/existing-vs-target.md#10-migration-sequence).

## After M12: Separation v2 (India)

A research-backed deepening of the M10 Separation module, delivered in phases:

| Phase | Scope | Status |
|---|---|---|
| **A** | Separation policy and notice rules (grade, employment type, probation), buyout and early release, garden leave, leave blocked during notice, exit reason taxonomy and regretted flag, case timeline | Done |
| **B** | Offboarding checklist templates and tasks (owner roles, due relative to the last day, overdue reminders), handover to a successor (direct reports, project seats, project tasks, pending approvals), sign-in disabled on exit or at once | Done |
| **C** | Involuntary exits: termination with show-cause and enquiry, retrenchment compensation (IR Code), absconding detection from attendance, death with nominee, auto-opened retirement and contract-end cases | Planned |
| **D** | Statutory F&F through payroll: gratuity (4 years 240 days, death waiver, ceiling, tax split), pro-rata bonus, encashment cap, clawbacks, claims and loans, an off-cycle F&F payroll run, EPF/ESI exit columns, F&F and gratuity deadlines | Planned |
| **E** | Configurable exit survey, attrition analytics, alumni boomerang flag in Recruitment, AI tools | Planned |
