namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// The permission catalog, owned by the platform and seeded into <c>iam.Permission</c>.
/// <para>
/// Tenants compose roles out of these codes but cannot invent new ones: a permission only means
/// something if some endpoint checks it, so the catalog lives in code next to the endpoints.
/// Codes follow <c>module.resource.action</c> and never change once released - renaming one would
/// silently widen or drop access for every tenant that granted it.
/// </para>
/// </summary>
public static class Permissions
{
    /// <summary>Platform operations. Held only by our own staff, never by tenant users.</summary>
    public static class Platform
    {
        public const string TenantView = "platform.tenant.view";
        public const string TenantManage = "platform.tenant.manage";
        public const string TenantSuspend = "platform.tenant.suspend";
        public const string SubscriptionManage = "platform.subscription.manage";
        public const string FeatureManage = "platform.feature.manage";
        public const string HealthView = "platform.health.view";
        public const string SupportAccessRequest = "platform.support-access.request";

        /// <summary>Viewing and managing our own staff and what they may do. Held sparingly.</summary>
        public const string StaffView = "platform.staff.view";
        public const string StaffInvite = "platform.staff.invite";
        public const string StaffManage = "platform.staff.manage";

        /// <summary>Creating and editing platform roles - deciding what any platform account may do.</summary>
        public const string RoleManage = "platform.role.manage";
    }

    /// <summary>Tenant administration: users, roles, settings and configuration.</summary>
    public static class Admin
    {
        public const string UserView = "admin.user.view";
        public const string UserManage = "admin.user.manage";
        public const string UserInvite = "admin.user.invite";
        public const string UserLock = "admin.user.lock";
        public const string RoleView = "admin.role.view";
        public const string RoleManage = "admin.role.manage";
        public const string SettingView = "admin.setting.view";
        public const string SettingManage = "admin.setting.manage";
        public const string CustomFieldManage = "admin.custom-field.manage";
        public const string IdentityProviderManage = "admin.identity-provider.manage";
    }

    /// <summary>Organization structure.</summary>
    public static class Organization
    {
        public const string View = "org.structure.view";
        public const string Manage = "org.structure.manage";
        public const string LegalEntityManage = "org.legal-entity.manage";
        public const string DepartmentManage = "org.department.manage";
    }

    /// <summary>Employee records. Sensitive sub-resources have their own codes on purpose.</summary>
    /// <summary>
    /// Tenant-maintained lookup lists (spec §113): countries, currencies, employment types and the
    /// rest of every dropdown. Reading them is what any form needs; changing them is administration.
    /// </summary>
    public static class MasterData
    {
        public const string View = "master-data.lookup.view";
        public const string Manage = "master-data.lookup.manage";
    }

    public static class Employee
    {
        public const string View = "employee.profile.view";
        public const string Create = "employee.profile.create";
        public const string Edit = "employee.profile.edit";
        public const string Delete = "employee.profile.delete";
        public const string EmploymentManage = "employee.employment.manage";
        public const string HistoryView = "employee.history.view";
        public const string ExitManage = "employee.exit.manage";

        /// <summary>Bank details. Sensitive: step-up authentication plus access auditing.</summary>
        public const string BankView = "employee.bank.view";
        public const string BankManage = "employee.bank.manage";

        /// <summary>National identifiers, passports, tax ids. Sensitive.</summary>
        public const string IdentityDocumentView = "employee.identity-document.view";
        public const string IdentityDocumentManage = "employee.identity-document.manage";

        /// <summary>Exporting employee data in bulk is its own permission, and is always audited.</summary>
        public const string Export = "employee.profile.export";
    }

    /// <summary>The asset register: categories, the assets themselves, and assigning/returning
    /// them. A module inside Core HR, not its own service, per architecture.md §5.1 - small and
    /// employee-centric.</summary>
    public static class Asset
    {
        public const string CategoryManage = "asset.category.manage";

        public const string View = "asset.asset.view";

        /// <summary>Registering an asset, sending it for repair/retiring it, and
        /// assigning/returning it to an employee - one permission for the whole admin-side
        /// lifecycle, since it is always the same IT/admin role in this v1 slice.</summary>
        public const string Manage = "asset.asset.manage";
    }

    /// <summary>Employee self-service. Always scoped to the acting user's own records.</summary>
    public static class Self
    {
        public const string ProfileView = "self.profile.view";
        public const string ProfileEdit = "self.profile.edit";
        public const string AttendanceView = "self.attendance.view";
        public const string AttendancePunch = "self.attendance.punch";
        public const string LeaveView = "self.leave.view";
        public const string LeaveApply = "self.leave.apply";
        public const string PayslipView = "self.payslip.view";
        public const string DocumentView = "self.document.view";

        /// <summary>Creating and submitting one's own expense claims - viewing them is covered by
        /// the same permission, the same "own record" shape as <see cref="LeaveApply"/>.</summary>
        public const string ExpenseClaimCreate = "self.expense-claim.create";

        /// <summary>Creating and submitting one's own travel requests - the same "own record"
        /// shape as <see cref="ExpenseClaimCreate"/>. Requesting an advance or settling the trip is
        /// not here: those are always an admin/finance action - see
        /// <see cref="Spend.TravelAdvanceManage"/>.</summary>
        public const string TravelRequestCreate = "self.travel-request.create";

        /// <summary>Seeing which assets are currently assigned to oneself - read-only; registering,
        /// assigning or returning an asset is always an admin action (<see cref="Asset.Manage"/>).</summary>
        public const string AssetView = "self.asset.view";

        /// <summary>Reading the policies that apply to oneself and acknowledging them.</summary>
        public const string PolicyView = "self.policy.view";

        /// <summary>Asking to trade one's own shift with a colleague for a day.</summary>
        public const string ShiftSwapRequest = "self.shift-swap.request";

        /// <summary>Filling in and submitting one's own timesheet.</summary>
        public const string TimesheetSubmit = "self.timesheet.submit";

        /// <summary>Keeping one's own skills and certifications up to date (self-assessed).</summary>
        public const string SkillManage = "self.skill.manage";

        /// <summary>One's own income-tax regime and investment declaration (Form 12BB).</summary>
        public const string TaxDeclarationManage = "self.tax-declaration.manage";

        /// <summary>Enrolling oneself (and dependents) in benefit plans.</summary>
        public const string BenefitManage = "self.benefit.manage";

        /// <summary>One's own total rewards: salary, band position, revisions, variable pay and equity.</summary>
        public const string CompensationView = "self.compensation.view";

        /// <summary>The internal job board: applying for openings and referring candidates.</summary>
        public const string ReferralCreate = "self.referral.create";

        /// <summary>Interviews one is on the panel for, and the scorecard for each.</summary>
        public const string InterviewFeedback = "self.interview.feedback";

        /// <summary>One's own onboarding checklist, and the onboarding tasks assigned to one.</summary>
        public const string OnboardingView = "self.onboarding.view";

        /// <summary>Giving 360 feedback when a colleague asks for it, and asking for it on one's own review.</summary>
        public const string FeedbackGive = "self.feedback.give";

        /// <summary>One's own learning: the catalogue, enrolling, progress, certificates.</summary>
        public const string LearningView = "self.learning.view";

        /// <summary>One's own career: the path for one's role, the next step and the gaps to it.</summary>
        public const string CareerView = "self.career.view";

        /// <summary>One's points wallet, the recognition feed, and redeeming from the catalogue.</summary>
        public const string RewardView = "self.reward.view";

        /// <summary>Recognising a colleague from one's monthly allowance.</summary>
        public const string RecognitionGive = "self.recognition.give";

        /// <summary>Raising one's own helpdesk tickets and reading the knowledge base.</summary>
        public const string TicketCreate = "self.ticket.create";

        /// <summary>Answering the surveys one is invited to.</summary>
        public const string SurveyRespond = "self.survey.respond";

        /// <summary>Resigning, following one's own separation, and the exit interview.</summary>
        public const string ResignationSubmit = "self.resignation.submit";
    }

    /// <summary>Leave administration and approvals.</summary>
    public static class Leave
    {
        public const string TypeManage = "leave.type.manage";
        public const string PolicyManage = "leave.policy.manage";
        public const string BalanceView = "leave.balance.view";
        public const string BalanceAdjust = "leave.balance.adjust";
        public const string RequestView = "leave.request.view";
        public const string RequestCreate = "leave.request.create";
        public const string RequestCreateOnBehalf = "leave.request.create-on-behalf";
        public const string RequestCancel = "leave.request.cancel";
        public const string HolidayManage = "leave.holiday.manage";
    }

    /// <summary>Attendance administration.</summary>
    public static class Attendance
    {
        public const string View = "attendance.record.view";
        public const string Manage = "attendance.record.manage";
        public const string RegularizationView = "attendance.regularization.view";
        public const string RegularizationCreate = "attendance.regularization.create";
        public const string OvertimeView = "attendance.overtime.view";
        public const string OvertimeCreate = "attendance.overtime.create";
        public const string ShiftManage = "attendance.shift.manage";
        public const string RosterManage = "attendance.roster.manage";
        public const string PolicyManage = "attendance.policy.manage";
        public const string DeviceManage = "attendance.device.manage";
    }

    /// <summary>Workflow: acting on approval tasks and configuring the definitions.</summary>
    public static class Workflow
    {
        public const string TaskView = "workflow.task.view";
        public const string TaskAct = "workflow.task.act";
        public const string TaskReassign = "workflow.task.reassign";
        public const string DefinitionView = "workflow.definition.view";
        public const string DefinitionManage = "workflow.definition.manage";
        public const string DelegationManage = "workflow.delegation.manage";
        public const string InstanceViewAll = "workflow.instance.view-all";
    }

    /// <summary>Documents.</summary>
    public static class Document
    {
        public const string View = "document.file.view";
        public const string Upload = "document.file.upload";
        public const string Delete = "document.file.delete";
        public const string TypeManage = "document.type.manage";
        public const string TemplateManage = "document.template.manage";
    }

    /// <summary>
    /// Company policies (spec §110): writing and versioning them, publishing a version (which goes
    /// through Workflow when the tenant has defined an approval for it), and reading who has
    /// acknowledged what.
    /// </summary>
    public static class Policy
    {
        public const string View = "policy.policy.view";
        public const string Manage = "policy.policy.manage";
        public const string AcknowledgementView = "policy.acknowledgement.view";
    }

    /// <summary>Clients and their contacts (spec §37).</summary>
    public static class Client
    {
        public const string View = "client.client.view";
        public const string Manage = "client.client.manage";
        public const string ContactManage = "client.contact.manage";
    }

    /// <summary>Client contracts - MSA, SOW, PO, SLA, retainer, T&amp;M (spec §38).</summary>
    public static class Contract
    {
        public const string View = "contract.contract.view";
        public const string Manage = "contract.contract.manage";
    }

    /// <summary>
    /// Projects and everything inside them (spec §39-56, §78). A grant at Tenant scope reaches every
    /// project; at any narrower scope it reaches the projects the holder is on - as a team member or
    /// as the project, delivery or account manager.
    /// </summary>
    public static class Project
    {
        public const string View = "project.project.view";
        public const string Create = "project.project.create";

        /// <summary>The project itself: details, stage, team, phases, board columns, meetings.</summary>
        public const string Manage = "project.project.manage";

        /// <summary>Tasks, requirements and their dependencies.</summary>
        public const string TaskView = "project.task.view";
        public const string TaskManage = "project.task.manage";

        public const string MilestoneView = "project.milestone.view";
        public const string MilestoneManage = "project.milestone.manage";
        public const string SprintView = "project.sprint.view";
        public const string SprintManage = "project.sprint.manage";
        public const string ChangeRequestView = "project.change-request.view";
        public const string ChangeRequestManage = "project.change-request.manage";

        /// <summary>Risks, assumptions, issues and dependencies.</summary>
        public const string RaidView = "project.raid.view";
        public const string RaidManage = "project.raid.manage";

        /// <summary>Project roles and the health thresholds every project is judged by.</summary>
        public const string SettingsManage = "project.settings.manage";

        /// <summary>Budget, cost, revenue, invoices and margin (spec §74-77).</summary>
        public const string FinancialsView = "project.financials.view";

        /// <summary>Budget lines, rate cards, non-labour costs and invoices.</summary>
        public const string FinancialsManage = "project.financials.manage";
    }

    /// <summary>The skill catalog and who has which skill at what level (spec §29).</summary>
    public static class Skill
    {
        public const string View = "skill.skill.view";
        public const string Manage = "skill.skill.manage";

        /// <summary>Reading people's skills; scoped like employee records.</summary>
        public const string EmployeeSkillView = "skill.employee-skill.view";

        /// <summary>Assessing people's skills; scoped like employee records.</summary>
        public const string EmployeeSkillManage = "skill.employee-skill.manage";
    }

    /// <summary>
    /// Resource management (spec §57-69): profiles and rates, requests and matching, allocation,
    /// capacity, bench, forecast and utilization.
    /// </summary>
    public static class Resource
    {
        public const string ProfileView = "resource.profile.view";

        /// <summary>Cost and billing rates, notice and capacity - financial, so separate from viewing.</summary>
        public const string ProfileManage = "resource.profile.manage";
        public const string RequestView = "resource.request.view";
        public const string RequestManage = "resource.request.manage";
        public const string AllocationView = "resource.allocation.view";

        /// <summary>Allocating, changing and releasing people on any project, and transfers between projects.</summary>
        public const string AllocationManage = "resource.allocation.manage";
        public const string UtilizationView = "resource.utilization.view";
    }

    /// <summary>Timesheets (spec §70-71) beyond one's own: reading, approving, reopening, locking periods.</summary>
    public static class Timesheet
    {
        /// <summary>Reading other people's timesheets; scoped like employee records.</summary>
        public const string View = "timesheet.entry.view";

        /// <summary>Acting on timesheets outside the approval inbox: reopening an approved one for correction.</summary>
        public const string Approve = "timesheet.entry.approve";

        /// <summary>The rules (hours, weekends, deadlines) and locking periods so nothing in them changes.</summary>
        public const string SettingsManage = "timesheet.settings.manage";
    }

    /// <summary>Audit and compliance.</summary>
    public static class Audit
    {
        public const string LogView = "audit.log.view";
        public const string LogExport = "audit.log.export";
        public const string PrivacyRequestManage = "audit.privacy-request.manage";
    }

    /// <summary>Reports and dashboards.</summary>
    public static class Report
    {
        public const string View = "report.report.view";
        public const string Export = "report.report.export";
        public const string ScheduleManage = "report.schedule.manage";
    }

    /// <summary>A user's own notification inbox, and administering templates.</summary>
    public static class Notification
    {
        public const string InboxView = "notification.inbox.view";
        public const string InboxManage = "notification.inbox.manage";
        public const string TemplateView = "notification.template.view";
        public const string TemplateManage = "notification.template.manage";
    }

    /// <summary>Recruitment: requisitions, postings, candidates, applications, interviews and offers.</summary>
    public static class Recruitment
    {
        public const string RequisitionView = "recruitment.requisition.view";
        public const string RequisitionManage = "recruitment.requisition.manage";
        public const string PostingView = "recruitment.posting.view";
        public const string PostingManage = "recruitment.posting.manage";
        public const string ApplicationView = "recruitment.application.view";
        public const string ApplicationManage = "recruitment.application.manage";
        public const string OfferView = "recruitment.offer.view";
        public const string OfferManage = "recruitment.offer.manage";

        /// <summary>Recruitment agencies, referral bonus and the other recruitment settings.</summary>
        public const string SettingsManage = "recruitment.settings.manage";

        /// <summary>Time to hire, sources, offer acceptance, recruiter performance and cost per hire.</summary>
        public const string AnalyticsView = "recruitment.analytics.view";
    }

    /// <summary>Workforce planning (spec §11): headcount plans and manpower requests.</summary>
    public static class Workforce
    {
        public const string PlanView = "workforce.plan.view";
        public const string PlanManage = "workforce.plan.manage";

        /// <summary>Manpower requests, scoped by who raised them.</summary>
        public const string RequestView = "workforce.request.view";
        public const string RequestManage = "workforce.request.manage";
    }

    /// <summary>Onboarding: templates and cases for bringing a new hire on board.</summary>
    public static class Onboarding
    {
        public const string TemplateView = "onboarding.template.view";
        public const string TemplateManage = "onboarding.template.manage";
        public const string CaseView = "onboarding.case.view";
        public const string CaseManage = "onboarding.case.manage";

        /// <summary>Completing a task that falls to you - narrower than CaseManage, which can act
        /// on any case's tasks regardless of assignee role.</summary>
        public const string TaskComplete = "onboarding.task.complete";
    }

    /// <summary>Performance: cycles, goals, and self/manager reviews.</summary>
    public static class Performance
    {
        public const string CycleView = "performance.cycle.view";
        public const string CycleManage = "performance.cycle.manage";

        /// <summary>Setting and checking in on your own goals.</summary>
        public const string GoalManageOwn = "performance.goal.manage-own";

        /// <summary>Setting goals and reading progress for people other than yourself - HR/admin,
        /// not the everyday manager-of-a-team case, which stays scoped to ReviewManageOwn's
        /// reviewing-manager check instead of a separate permission.</summary>
        public const string GoalManage = "performance.goal.manage";

        public const string ReviewView = "performance.review.view";

        /// <summary>Creating a review and submitting the self-review half of it.</summary>
        public const string ReviewManageOwn = "performance.review.manage-own";

        /// <summary>Submitting the manager half and releasing a review - checked against the
        /// review's own <c>ManagerEmployeeId</c>, not a blanket grant, the same "own record" shape
        /// self-service permissions elsewhere in this catalog use.</summary>
        public const string ReviewManageAsManager = "performance.review.manage-as-manager";

        /// <summary>Calibrating a cycle's ratings and releasing its reviews.</summary>
        public const string CalibrationManage = "performance.calibration.manage";

        /// <summary>Performance improvement plans, scoped by the employee on the plan.</summary>
        public const string PipView = "performance.pip.view";
        public const string PipManage = "performance.pip.manage";
    }

    /// <summary>Courses and enrollments. No session scheduling or certification tracking in this v1
    /// slice - see <c>Course</c>'s own remarks.</summary>
    public static class Learning
    {
        public const string CourseView = "learning.course.view";
        public const string CourseManage = "learning.course.manage";

        public const string EnrollmentView = "learning.enrollment.view";

        /// <summary>Assigning and cancelling enrollments for people other than yourself - HR/admin.</summary>
        public const string EnrollmentManage = "learning.enrollment.manage";

        /// <summary>Starting and completing your own enrollment.</summary>
        public const string EnrollmentManageOwn = "learning.enrollment.manage-own";

        /// <summary>Certificates earned from courses and when they expire.</summary>
        public const string CertificationView = "learning.certification.view";
    }

    /// <summary>Career paths, promotions, succession plans and talent review (spec: career and succession).</summary>
    public static class Career
    {
        public const string PathView = "career.path.view";
        public const string PathManage = "career.path.manage";

        /// <summary>Promotion nominations, scoped by the employee nominated.</summary>
        public const string PromotionView = "career.promotion.view";
        public const string PromotionManage = "career.promotion.manage";

        public const string SuccessionView = "career.succession.view";
        public const string SuccessionManage = "career.succession.manage";

        /// <summary>Assessing potential and flight risk for the 9-box talent review.</summary>
        public const string TalentReviewManage = "career.talent-review.manage";
    }

    /// <summary>Salary components, structures, employee salary assignment, payroll runs and the
    /// payslips a finalized run publishes. No statutory compliance in this v1 slice - see
    /// <c>EmployeeSalary</c>'s own remarks. An employee reads their own payslip through
    /// <see cref="Self.PayslipView"/> rather than a permission in this class, the same split
    /// <see cref="Self.AttendanceView"/> already uses for "my own" versus "anyone's."</summary>
    public static class Payroll
    {
        public const string ComponentManage = "payroll.component.manage";
        public const string StructureManage = "payroll.structure.manage";

        /// <summary>Viewing an employee's assigned salary and its component breakdown. Sensitive:
        /// step-up authentication plus access auditing, the same treatment as
        /// <see cref="Employee.BankView"/>.</summary>
        public const string EmployeeSalaryView = "payroll.employee-salary.view";

        /// <summary>Assigning a structure and CTC to an employee. Sensitive for the same reason as
        /// <see cref="EmployeeSalaryView"/>.</summary>
        public const string EmployeeSalaryManage = "payroll.employee-salary.manage";

        /// <summary>Pay day, proration, loss-of-pay and overtime rules for the payroll as a whole.</summary>
        public const string SettingsManage = "payroll.settings.manage";

        /// <summary>One-off inputs for a period: overtime, bonus, incentive, arrears, reimbursements.</summary>
        public const string InputManage = "payroll.input.manage";

        /// <summary>Reading employees' tax declarations. Sensitive for the same reason as <see cref="EmployeeSalaryView"/>.</summary>
        public const string TaxDeclarationView = "payroll.tax-declaration.view";

        /// <summary>Verifying declarations against proofs. Sensitive for the same reason as <see cref="EmployeeSalaryView"/>.</summary>
        public const string TaxDeclarationVerify = "payroll.tax-declaration.verify";

        /// <summary>The payroll register, statutory returns (ECR, ESI, PT), the bank file and the journal. Sensitive.</summary>
        public const string ReportView = "payroll.report.view";

        /// <summary>Viewing a run and its per-employee results. Sensitive for the same reason as
        /// <see cref="EmployeeSalaryView"/> - a run's numbers are just as much compensation data.</summary>
        public const string RunView = "payroll.run.view";

        /// <summary>Creating and calculating a run. Sensitive for the same reason as
        /// <see cref="RunView"/>.</summary>
        public const string RunProcess = "payroll.run.process";

        /// <summary>Finalizing a run - separate from <see cref="RunProcess"/> so a smaller group can
        /// be trusted to calculate and review without also being able to lock the period for good.
        /// Sensitive for the same reason as <see cref="RunView"/>.</summary>
        public const string RunFinalize = "payroll.run.finalize";
    }

    /// <summary>The compliance centre (spec §22, §107): versioned statutory rules, filings and registrations.</summary>
    public static class Compliance
    {
        public const string RuleView = "compliance.rule.view";

        /// <summary>Drafting and publishing new versions of statutory rules.</summary>
        public const string RuleManage = "compliance.rule.manage";

        /// <summary>The filing calendar, licences, certificates and audits.</summary>
        public const string FilingView = "compliance.filing.view";
        public const string FilingManage = "compliance.filing.manage";
    }

    /// <summary>Compensation (spec §23): bands, revisions, variable pay and equity.</summary>
    public static class Compensation
    {
        public const string BandView = "compensation.band.view";
        public const string BandManage = "compensation.band.manage";

        /// <summary>Salary revisions (merit, promotion, correction) and review cycles. Sensitive.</summary>
        public const string RevisionView = "compensation.revision.view";

        /// <summary>Proposing revisions and running review cycles. Sensitive.</summary>
        public const string RevisionManage = "compensation.revision.manage";

        /// <summary>Bonus, commission and incentive awards.</summary>
        public const string VariablePayView = "compensation.variable-pay.view";
        public const string VariablePayManage = "compensation.variable-pay.manage";

        /// <summary>ESOP and other equity grants.</summary>
        public const string EquityView = "compensation.equity.view";
        public const string EquityManage = "compensation.equity.manage";
    }

    /// <summary>Benefits (spec §24): plans, contributions and enrollments.</summary>
    public static class Benefit
    {
        public const string PlanView = "benefit.plan.view";
        public const string PlanManage = "benefit.plan.manage";

        /// <summary>Anyone's enrollments, scoped like employee records.</summary>
        public const string EnrollmentView = "benefit.enrollment.view";
        public const string EnrollmentManage = "benefit.enrollment.manage";
    }

    /// <summary>Expense categories and claim review - submitting one's own claim is
    /// <see cref="Self.ExpenseClaimCreate"/> instead.</summary>
    public static class Spend
    {
        public const string CategoryManage = "spend.category.manage";

        /// <summary>Viewing any employee's claims, not just one's own. Approving/rejecting a
        /// submitted claim is not a Spend permission at all - it goes through Workflow's own task
        /// inbox (<c>workflow.task.act</c>), the same as every other approvable entity in this
        /// codebase (see <c>ExpenseClaim</c>'s own remarks).</summary>
        public const string ClaimView = "spend.claim.view";

        /// <summary>Viewing any employee's travel requests, not just one's own. Approval goes
        /// through Workflow, same as <see cref="ClaimView"/>'s own remark.</summary>
        public const string TravelView = "spend.travel.view";

        /// <summary>Requesting and disbursing a cash advance against an Approved travel request,
        /// and building/settling the trip's <c>Travel.TravelSettlement</c> once it is done -
        /// one permission for the whole post-approval money flow of a trip, since it is always an
        /// admin/finance action, never something the traveling employee does themselves.</summary>
        public const string TravelAdvanceManage = "spend.travel-advance.manage";

        /// <summary>Marking approved claims reimbursed, or sending them to payroll.</summary>
        public const string ClaimPay = "spend.claim.pay";
    }

    /// <summary>Rewards and recognition: the catalogue, programme rules and budgets, recognitions and redemptions.</summary>
    public static class Reward
    {
        public const string CatalogView = "reward.catalog.view";
        public const string CatalogManage = "reward.catalog.manage";

        /// <summary>Recognition types, automatic rules, budgets and the points settings.</summary>
        public const string ProgramManage = "reward.program.manage";

        /// <summary>Every recognition and wallet, not just one's own - reports and approvals.</summary>
        public const string RecognitionView = "reward.recognition.view";

        public const string RedemptionManage = "reward.redemption.manage";
    }

    /// <summary>Engagement surveys and eNPS.</summary>
    public static class Engagement
    {
        public const string SurveyView = "engagement.survey.view";
        public const string SurveyManage = "engagement.survey.manage";

        /// <summary>Results, always aggregated and never below the minimum group size.</summary>
        public const string ResultsView = "engagement.results.view";
    }

    /// <summary>The employee helpdesk: tickets, queues and SLAs, and the knowledge base.</summary>
    public static class Helpdesk
    {
        /// <summary>Seeing the queues an agent works.</summary>
        public const string TicketView = "helpdesk.ticket.view";

        /// <summary>Assigning, answering and resolving tickets.</summary>
        public const string TicketManage = "helpdesk.ticket.manage";

        public const string CategoryManage = "helpdesk.category.manage";
        public const string KnowledgeManage = "helpdesk.knowledge.manage";
    }

    /// <summary>Separation: resignations and exits, clearance, full and final settlement, exit interviews and alumni.</summary>
    public static class Separation
    {
        /// <summary>Separation cases, scoped by the employee leaving.</summary>
        public const string CaseView = "separation.case.view";
        public const string CaseManage = "separation.case.manage";

        /// <summary>Clearing the checklist items that fall to one's function (IT, admin, finance).</summary>
        public const string ClearanceManage = "separation.clearance.manage";

        public const string SettlementManage = "separation.settlement.manage";
        public const string AlumniView = "separation.alumni.view";
    }
}
