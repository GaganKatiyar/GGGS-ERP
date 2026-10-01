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
    }

    /// <summary>Salary components, structures, employee salary assignment, and payroll runs. No
    /// payslips or statutory compliance in this v1 slice - see <c>EmployeeSalary</c>'s own remarks.</summary>
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

        public const string PayGroupManage = "payroll.pay-group.manage";

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
}
