# Module sign-off checklist

We test the app one module at a time, starting with **HR Portal**. When a screen looks good and works, mark it **Looks good**. When every screen in a module is Looks good, the module is signed off.

The modules and screens follow the left menu of the HR app (`frontend/libs/core/layout/src/lib/navigation.ts`). Log in as the tenant admin (`gggs` / `admin@gggs.example`) unless a row says otherwise. "My …" screens need a login that is linked to an employee.

## How to mark a screen

| Status | Meaning |
|---|---|
| Not tested | Nobody has gone through it yet |
| In progress | Being tested now, or fixes are being made |
| Needs fixes | Tested; something is wrong (write what in Notes) |
| Looks good | Tested and confirmed: it works and looks right |

Fill in **Tested on** (date) and, under **Notes**, anything found or fixed.

## Summary

| # | Module | Screens | Status | Signed off on |
|---|---|---|---|---|
| 1 | HR Portal | 12 (+ sub-screens) | Looks good | 2026-11-18 |
| 2 | Settings | 37 | Not tested | |
| 3 | Dashboard and Approvals | 2 | Not tested | |
| 4 | My Profile & Documents | 5 | Not tested | |
| 5 | My Time & Leave | 3 | Not tested | |
| 6 | My Pay & Benefits | 6 | Not tested | |
| 7 | My Appraisal | 3 | Not tested | |
| 8 | My Growth | 2 | Not tested | |
| 9 | Star Reward | 2 | Not tested | |
| 10 | My Workplace | 6 | Not tested | |
| 11 | My Team | 4 | Not tested | |
| 12 | Appraisal Admin | 5 | Not tested | |
| 13 | Learning & Development | 5 | Not tested | |
| 14 | Recruitment | 7 | Not tested | |
| 15 | Finance & Accounts | 17 | Not tested | |
| 16 | General Administration | 4 | Not tested | |
| 17 | Exits | 7 | Not tested | |
| 18 | Clients | 3 | Not tested | |
| 19 | Projects | 9 | Not tested | |
| 20 | Resources | 8 | Not tested | |
| 21 | Analytics, Calendar and AI Assistant | 3 | Not tested | |
| 22 | Studio | 13 | Not tested | |

---

## 1. HR Portal

Menu: **HR Portal**. Signed off on 2026-11-18.

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 1.1 | Directory | `/employees` | Looks good | 2026-11-18 | |
| 1.1a | → Employee 360 (open a person) | `/employees/{id}` | Looks good | 2026-11-18 | Fact cards made compact (option C); tab row now scrolls with arrows and the mouse wheel |
| 1.1b | → Employee 360 tabs: Overview, Employment, Organization, Personal, Family & emergency, Education, Experience, Statutory & bank, History | `/employees/{id}` | Looks good | 2026-11-18 | |
| 1.1c | → Employee 360 module tabs: Onboarding, Attendance, Leave, Projects, Allocation, Timesheet, Skills, Payroll, Compensation, Benefits, Performance, Goals, Learning, Career, Rewards, Documents, Assets, Expenses, Loans, Helpdesk, Accounts | `/employees/{id}` | Looks good | 2026-11-18 | Leave tab shows where the policy comes from (cost center or engagement type) |
| 1.1d | → Hire employee | `/employees/new` | Looks good | 2026-11-18 | |
| 1.1e | → Hire drafts | `/employees/drafts` | Looks good | 2026-11-18 | |
| 1.2 | Org chart | `/employees/org-chart` | Looks good | 2026-11-18 | |
| 1.3 | Org structure | `/org/structure` | Looks good | 2026-11-18 | Department → function → role → level |
| 1.4 | Onboarding | `/onboarding` | Looks good | 2026-11-18 | |
| 1.4a | → Onboarding case (open a joiner) | `/onboarding/cases/{id}` | Looks good | 2026-11-18 | Known gap, accepted for now: the employee cannot upload joining documents; HR uploads them on Employee 360 → Documents |
| 1.5 | Attendance | `/attendance` | Looks good | 2026-11-18 | |
| 1.6 | Leave requests | `/leave/requests` | Looks good | 2026-11-18 | |
| 1.7 | Skills | `/skills` | Looks good | 2026-11-18 | |
| 1.8 | Career paths | `/career` | Looks good | 2026-11-18 | |
| 1.9 | Engagement | `/engagement` | Looks good | 2026-11-18 | |
| 1.10 | Work anniversaries | `/engagement/anniversaries` | Looks good | 2026-11-18 | |
| 1.11 | Birthday wishes | `/engagement/birthdays` | Looks good | 2026-11-18 | |
| 1.12 | Policies | `/policies` | Looks good | 2026-11-18 | |

## 2. Settings

Menu: **Settings** (the cog), or the Settings link at the end of a module's tab row. Grouped as the cards on the Settings page.

| # | Card | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|---|
| 2.1 | Organization | Companies | `/org/companies` | Not tested | | |
| 2.2 | Organization | Cost centers | `/org/cost-centers` | Not tested | | |
| 2.3 | Organization | Business units | `/org/business-units` | Not tested | | |
| 2.4 | Organization | Departments & functions | `/org/departments` | Not tested | | |
| 2.5 | Organization | Locations | `/org/locations` | Not tested | | |
| 2.6 | Organization | Roles | `/org/roles` | Not tested | | |
| 2.7 | Organization | Designations | `/org/designations` | Not tested | | |
| 2.8 | Organization | Levels | `/org/levels` | Not tested | | |
| 2.9 | Time & attendance | Shifts & rosters | `/attendance/shifts` | Not tested | | |
| 2.10 | Time & attendance | Leave types | `/leave/types` | Not tested | | |
| 2.11 | Time & attendance | Leave policies | `/leave/policies` | Not tested | | |
| 2.12 | Time & attendance | Leave policy assignment | `/leave/policy-assignment` | In progress | 2026-11-18 | Cost centers tab added: everyone policy plus per-engagement-type policy, falling back to the organisation default |
| 2.13 | Time & attendance | Holidays | `/leave/holidays` | Not tested | | |
| 2.14 | Time & attendance | Timesheet settings | `/timesheets/settings` | Not tested | | |
| 2.15 | Payroll & finance | Payroll settings | `/payroll/settings` | Not tested | | |
| 2.16 | Payroll & finance | Salary structures | `/payroll/structures` | Not tested | | |
| 2.17 | Payroll & finance | Salary bands | `/compensation/bands` | Not tested | | |
| 2.18 | Payroll & finance | Loan types | `/loans/types` | Not tested | | |
| 2.19 | Payroll & finance | Statutory rules | `/compliance/rules` | Not tested | | |
| 2.20 | Hiring | Recruitment settings | `/recruitment/settings` | Not tested | | |
| 2.21 | Joiners & leavers | Onboarding checklists | `/onboarding/templates` | Not tested | | |
| 2.22 | Joiners & leavers | Separation policy | `/separation/policy` | Not tested | | |
| 2.23 | Joiners & leavers | Offboarding checklists | `/separation/offboarding-checklists` | Not tested | | |
| 2.24 | Joiners & leavers | Exit survey | `/separation/exit-survey` | Not tested | | |
| 2.25 | Projects | Project settings | `/projects/settings` | Not tested | | |
| 2.26 | Lookups | Master data | `/admin/master-data` | Not tested | | |
| 2.27 | Access & security | Users | `/admin/users` | Not tested | | |
| 2.28 | Access & security | Roles | `/admin/roles` | Not tested | | |
| 2.29 | Access & security | Access overview | `/admin/access` | Not tested | | |
| 2.30 | Access & security | Audit log | `/admin/audit` | Not tested | | |
| 2.31 | Platform | General settings | `/admin/settings` | Not tested | | |
| 2.32 | Platform | Dashboard banner | `/engagement/banner` | Not tested | | |
| 2.33 | Platform | Imports | `/admin/data/imports` | Not tested | | |
| 2.34 | Platform | Import employees | `/employees/import` | Not tested | | |
| 2.35 | Platform | About Nuts and Bolts | `/admin/about` | Not tested | | |
| 2.36 | Organization | Structure import | `/org/import` | Not tested | | Department / function / role / level from CSV or markdown |
| 2.37 | (search) | Settings search box | `/settings` | Not tested | | |

## 3. Dashboard and Approvals

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 3.1 | Dashboard | `/dashboard` | Not tested | | |
| 3.2 | Approvals | `/approvals` | Not tested | | |

## 4. My Profile & Documents

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 4.1 | My profile | `/me/profile` | Not tested | | |
| 4.2 | My documents | `/me/documents` | Not tested | | View, download and sign only; no upload |
| 4.3 | My assets | `/me/assets` | Not tested | | |
| 4.4 | My policies | `/me/policies` | Not tested | | |
| 4.5 | My preferences | `/me/preferences` | Not tested | | |

## 5. My Time & Leave

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 5.1 | My attendance | `/me/attendance` | Not tested | | |
| 5.2 | My leave | `/me/leave` | Not tested | | Shows the leave policy and where it comes from |
| 5.3 | My timesheet | `/me/timesheet` | Not tested | | |

## 6. My Pay & Benefits

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 6.1 | My payslips | `/me/payslips` | Not tested | | |
| 6.2 | My tax declaration | `/me/tax-declaration` | Not tested | | |
| 6.3 | My compensation | `/me/compensation` | Not tested | | |
| 6.4 | My benefits | `/me/benefits` | Not tested | | |
| 6.5 | My expenses | `/me/expenses` | Not tested | | |
| 6.6 | My loans | `/me/loans` | Not tested | | |

## 7. My Appraisal

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 7.1 | My goals | `/me/goals` | Not tested | | |
| 7.2 | My reviews | `/me/reviews` | Not tested | | |
| 7.3 | My feedback | `/me/feedback` | Not tested | | |

## 8. My Growth

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 8.1 | My skills | `/me/skills` | Not tested | | |
| 8.2 | My career | `/me/career` | Not tested | | |

## 9. Star Reward

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 9.1 | My rewards | `/me/rewards` | Not tested | | |
| 9.2 | Manage rewards | `/rewards` | Not tested | | |

## 10. My Workplace

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 10.1 | My onboarding | `/me/onboarding` | Not tested | | |
| 10.2 | Jobs & referrals | `/me/jobs` | Not tested | | |
| 10.3 | My interviews | `/me/interviews` | Not tested | | |
| 10.4 | My surveys | `/me/surveys` | Not tested | | |
| 10.5 | My tickets | `/me/tickets` | Not tested | | |
| 10.6 | My resignation | `/me/separation` | Not tested | | |

## 11. My Team

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 11.1 | Team overview | `/team` | Not tested | | Needs a login that manages people |
| 11.2 | Team attendance | `/team/attendance` | Not tested | | |
| 11.3 | Team leave | `/team/leave` | Not tested | | |
| 11.4 | Team timesheets | `/team/timesheets` | Not tested | | |

## 12. Appraisal Admin

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 12.1 | Review cycles | `/performance` | Not tested | | |
| 12.2 | Goals | `/goals` | Not tested | | |
| 12.3 | Improvement plans | `/performance/pips` | Not tested | | |
| 12.4 | Promotions | `/career/promotions` | Not tested | | Moved here from HR Portal |
| 12.5 | Succession | `/career/succession` | Not tested | | Moved here from HR Portal |

## 13. Learning & Development

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 13.1 | My learning | `/me/learning` | Not tested | | |
| 13.2 | Learning overview | `/learning` | Not tested | | |
| 13.3 | Courses | `/learning/courses` | Not tested | | |
| 13.4 | Enrollments | `/learning/enrollments` | Not tested | | |
| 13.5 | Certificates | `/learning/certificates` | Not tested | | |

## 14. Recruitment

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 14.1 | Manpower requests | `/workforce/requests` | Not tested | | |
| 14.2 | Requirements | `/recruitment/requirements` | Not tested | | Includes the New requirement form |
| 14.3 | Candidates | `/recruitment/candidates` | Not tested | | Board and list views |
| 14.4 | Interviews | `/recruitment/interviews` | Not tested | | |
| 14.5 | Offers | `/recruitment/offers` | Not tested | | |
| 14.6 | Referrals & incentives | `/recruitment/referrals` | Not tested | | |
| 14.7 | Recruitment analytics | `/recruitment/analytics` | Not tested | | |

## 15. Finance & Accounts

| # | Group | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|---|
| 15.1 | Payroll | Payroll overview | `/payroll` | Not tested | | |
| 15.2 | Payroll | Payroll runs | `/payroll/runs` | Not tested | | |
| 15.3 | Payroll | Salaries | `/payroll/salaries` | Not tested | | |
| 15.4 | Payroll | Inputs | `/payroll/inputs` | Not tested | | |
| 15.5 | Payroll | Loans | `/loans` | Not tested | | |
| 15.6 | Payroll | Payslips | `/payroll/payslips` | Not tested | | |
| 15.7 | Payroll | Tax declarations | `/payroll/declarations` | Not tested | | |
| 15.8 | Compensation | Salary revisions | `/compensation/revisions` | Not tested | | |
| 15.9 | Compensation | Variable pay | `/compensation/variable-pay` | Not tested | | |
| 15.10 | Compensation | Equity | `/compensation/equity` | Not tested | | |
| 15.11 | Expenses | Expenses | `/expenses` | Not tested | | |
| 15.12 | Project finance | Financials | `/projects/financials` | Not tested | | |
| 15.13 | Project finance | Billing | `/projects/billing` | Not tested | | |
| 15.14 | Compliance | Compliance overview | `/compliance` | Not tested | | |
| 15.15 | Compliance | Filing calendar | `/compliance/filings` | Not tested | | |
| 15.16 | Compliance | Licences and certificates | `/compliance/registrations` | Not tested | | |
| 15.17 | Payroll | A full payroll run, start to payslip | `/payroll/runs` | Not tested | | End-to-end flow, not a single screen |

## 16. General Administration

| # | Group | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|---|
| 16.1 | Inventory & assets | Assets | `/assets` | Not tested | | |
| 16.2 | Support | Helpdesk | `/helpdesk` | Not tested | | |
| 16.3 | Support | Knowledge base | `/helpdesk/knowledge` | Not tested | | |
| 16.4 | Records | Documents | `/documents` | Not tested | | All employees' documents, letter templates, document types |

## 17. Exits

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 17.1 | Separations | `/separation` | Not tested | | |
| 17.2 | Clearance | `/separation/clearance` | Not tested | | |
| 17.3 | Offboarding | `/separation/offboarding` | Not tested | | |
| 17.4 | Disciplinary | `/separation/disciplinary` | Not tested | | |
| 17.5 | Absconding | `/separation/absconding` | Not tested | | |
| 17.6 | Retrenchment | `/separation/retrenchment` | Not tested | | |
| 17.7 | Attrition | `/separation/analytics` | Not tested | | |

## 18. Clients

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 18.1 | Clients | `/clients` | Not tested | | |
| 18.2 | Contacts | `/clients/contacts` | Not tested | | |
| 18.3 | Contracts | `/contracts` | Not tested | | |

## 19. Projects

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 19.1 | Projects | `/projects` | Not tested | | |
| 19.2 | Tasks | `/projects/tasks` | Not tested | | |
| 19.3 | Board | `/projects/board` | Not tested | | |
| 19.4 | Milestones | `/projects/milestones` | Not tested | | |
| 19.5 | Sprints | `/projects/sprints` | Not tested | | |
| 19.6 | Change requests | `/projects/change-requests` | Not tested | | |
| 19.7 | RAID log | `/projects/raid` | Not tested | | |
| 19.8 | Portfolio | `/projects/portfolio` | Not tested | | |
| 19.9 | Service levels | `/projects/service-levels` | Not tested | | |

## 20. Resources

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 20.1 | Resource directory | `/resources` | Not tested | | |
| 20.2 | Requests | `/resources/requests` | Not tested | | |
| 20.3 | Allocation calendar | `/resources/calendar` | Not tested | | |
| 20.4 | Capacity & demand | `/resources/capacity` | Not tested | | |
| 20.5 | Bench | `/resources/bench` | Not tested | | |
| 20.6 | Utilization | `/resources/utilization` | Not tested | | |
| 20.7 | Timesheet approvals | `/timesheets/approvals` | Not tested | | |
| 20.8 | Transfers | `/resources/transfers` | Not tested | | |

## 21. Analytics, Calendar and AI Assistant

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 21.1 | Analytics | `/analytics` | Not tested | | |
| 21.2 | Calendar | `/calendar` | Not tested | | |
| 21.3 | AI Assistant | `/ai` | Not tested | | |

## 22. Studio

| # | Screen | URL | Status | Tested on | Notes |
|---|---|---|---|---|---|
| 22.1 | Overview | `/studio` | Not tested | | |
| 22.2 | Email senders | `/studio/email-senders` | Not tested | | |
| 22.3 | Email templates | `/admin/notifications` | Not tested | | |
| 22.4 | Email log | `/studio/email-log` | In progress | 2026-11-18 | Log settings (keep 30/60/90 days, failed only), retries, failure alerts, Resend all failed |
| 22.5 | Text messages | `/studio/sending` | Not tested | | |
| 22.6 | Single sign-on | `/admin/identity-providers` | Not tested | | |
| 22.7 | Password policy | `/admin/password-policy` | Not tested | | |
| 22.8 | Connectors | `/studio/connectors` | Not tested | | |
| 22.9 | Work accounts | `/studio/accounts` | Not tested | | |
| 22.10 | Automations | `/studio/automations` | Not tested | | |
| 22.11 | Automation log | `/studio/deliveries` | Not tested | | |
| 22.12 | Workflows | `/admin/workflows` | Not tested | | |
| 22.13 | API keys | `/studio/api-keys` | Not tested | | |
