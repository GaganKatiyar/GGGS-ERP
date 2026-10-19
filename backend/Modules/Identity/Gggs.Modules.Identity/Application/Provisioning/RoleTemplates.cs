using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Domain.Roles;

namespace Gggs.Modules.Identity.Application.Provisioning;

/// <summary>
/// What each seeded role can actually do.
/// <para>
/// The Platform service decides which roles a tenant starts with; this decides what they mean.
/// The split matters: permission codes are only meaningful next to the endpoints that check them,
/// and those live here. Platform naming permissions it cannot see the catalog for is how a role
/// ends up granting something no endpoint enforces.
/// </para>
/// <para>
/// Two rules shape every list below. First, least privilege at the point of creation: a role gets
/// what its job needs on day one and nothing held in reserve. Second, and the reason several
/// obvious-looking permissions are missing: <b>bank details, identity documents and bulk export go
/// to nobody, not even the Tenant Administrator</b>. §8.5 of the architecture lists sensitive
/// employee access against HR Admin, and this deliberately withholds it at seeding - a customer
/// should reach those by deciding to, which is one edit away for anyone holding
/// <c>admin.role.manage</c>, rather than discovering after a breach that every HR user could read
/// bank accounts because we shipped it that way.
/// </para>
/// <para>
/// Expense and travel permissions are absent because those modules do not exist yet. Payroll's
/// sensitive permissions (salaries, runs, declarations, reports) go to the Payroll Administrator,
/// scoped to their legal entity, and reading runs and reports to Finance.
/// </para>
/// </summary>
public static class RoleTemplates
{
    /// <summary>The role every new employee account is given, so self-service works immediately.</summary>
    public const string EmployeeTemplateCode = "EMPLOYEE";

    /// <summary>The role the first administrator is given, and the only one that can grant the rest.</summary>
    public const string TenantAdministratorTemplateCode = "TENANT_ADMIN";

    /// <summary>The people and structure a delivery role plans with.</summary>
    private static IReadOnlyList<PermissionAssignment> DeliveryBaseline(DataScope peopleScope) =>
    [
        new(Permissions.Organization.View, DataScope.Tenant),
        new(Permissions.Employee.View, peopleScope),
        new(Permissions.Leave.RequestView, peopleScope),
        new(Permissions.Workflow.TaskView, DataScope.Own),
        new(Permissions.Workflow.TaskAct, DataScope.Own),
        new(Permissions.Report.View, peopleScope),

        // Project documents follow the project's own access; these let the endpoints through.
        new(Permissions.Document.View, DataScope.Own),
        new(Permissions.Document.Upload, DataScope.Own),

        .. SelfService(),
    ];

    /// <summary>
    /// Working on the projects one is on (spec §41-52): every employee's share of delivery. Own
    /// scope means "projects I am on", so this reaches nothing until someone is added to a team.
    /// </summary>
    private static PermissionAssignment[] ProjectMember() =>
    [
        new(Permissions.Project.View, DataScope.Own),
        new(Permissions.Project.TaskView, DataScope.Own),
        new(Permissions.Project.TaskManage, DataScope.Own),
        new(Permissions.Project.MilestoneView, DataScope.Own),
        new(Permissions.Project.SprintView, DataScope.Own),
        new(Permissions.Project.ChangeRequestView, DataScope.Own),
        new(Permissions.Project.RaidView, DataScope.Own),
        new(Permissions.Project.RaidManage, DataScope.Own),
    ];

    /// <summary>Running projects, at the given reach: Own for a project manager's own projects, Tenant for a portfolio.</summary>
    private static PermissionAssignment[] ProjectLead(DataScope scope) =>
    [
        new(Permissions.Client.View, DataScope.Tenant),
        new(Permissions.Contract.View, DataScope.Tenant),
        new(Permissions.Project.View, scope),
        new(Permissions.Project.Create, DataScope.Tenant),
        new(Permissions.Project.Manage, scope),
        new(Permissions.Project.TaskView, scope),
        new(Permissions.Project.TaskManage, scope),
        new(Permissions.Project.MilestoneView, scope),
        new(Permissions.Project.MilestoneManage, scope),
        new(Permissions.Project.SprintView, scope),
        new(Permissions.Project.SprintManage, scope),
        new(Permissions.Project.ChangeRequestView, scope),
        new(Permissions.Project.ChangeRequestManage, scope),
        new(Permissions.Project.RaidView, scope),
        new(Permissions.Project.RaidManage, scope),
    ];

    /// <summary>Seeing every client, contract and project without changing any.</summary>
    private static PermissionAssignment[] DeliveryReadOnly() =>
    [
        new(Permissions.Client.View, DataScope.Tenant),
        new(Permissions.Contract.View, DataScope.Tenant),
        new(Permissions.Project.View, DataScope.Tenant),
        new(Permissions.Project.TaskView, DataScope.Tenant),
        new(Permissions.Project.MilestoneView, DataScope.Tenant),
        new(Permissions.Project.SprintView, DataScope.Tenant),
        new(Permissions.Project.ChangeRequestView, DataScope.Tenant),
        new(Permissions.Project.RaidView, DataScope.Tenant),
    ];

    /// <summary>Seeing skills, resourcing and timesheets across the company without changing them.</summary>
    private static PermissionAssignment[] ResourceReadOnly() =>
    [
        new(Permissions.Skill.View, DataScope.Tenant),
        new(Permissions.Skill.EmployeeSkillView, DataScope.Tenant),
        new(Permissions.Resource.ProfileView, DataScope.Tenant),
        new(Permissions.Resource.RequestView, DataScope.Tenant),
        new(Permissions.Resource.AllocationView, DataScope.Tenant),
        new(Permissions.Resource.UtilizationView, DataScope.Tenant),
        new(Permissions.Timesheet.View, DataScope.Tenant),
    ];

    /// <summary>Running resourcing (spec section 57-69): profiles and rates, requests, allocation, transfers.</summary>
    private static PermissionAssignment[] ResourceManagement() =>
    [
        new(Permissions.Skill.View, DataScope.Tenant),
        new(Permissions.Skill.Manage, DataScope.Tenant),
        new(Permissions.Skill.EmployeeSkillView, DataScope.Tenant),
        new(Permissions.Skill.EmployeeSkillManage, DataScope.Tenant),
        new(Permissions.Resource.ProfileView, DataScope.Tenant),
        new(Permissions.Resource.ProfileManage, DataScope.Tenant),
        new(Permissions.Resource.RequestView, DataScope.Tenant),
        new(Permissions.Resource.RequestManage, DataScope.Tenant),
        new(Permissions.Resource.AllocationView, DataScope.Tenant),
        new(Permissions.Resource.AllocationManage, DataScope.Tenant),
        new(Permissions.Resource.UtilizationView, DataScope.Tenant),
        new(Permissions.Timesheet.View, DataScope.Tenant),
    ];

    private static readonly Dictionary<string, IReadOnlyList<PermissionAssignment>> Templates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TenantAdministratorTemplateCode] =
            [
                // Administration: the one role that can hand out the others.
                new(Permissions.Admin.UserView, DataScope.Tenant),
                new(Permissions.Admin.UserManage, DataScope.Tenant),
                new(Permissions.Admin.UserInvite, DataScope.Tenant),
                new(Permissions.Admin.UserLock, DataScope.Tenant),
                new(Permissions.Admin.RoleView, DataScope.Tenant),
                new(Permissions.Admin.RoleManage, DataScope.Tenant),
                new(Permissions.Admin.SettingView, DataScope.Tenant),
                new(Permissions.Admin.SettingManage, DataScope.Tenant),
                new(Permissions.Admin.CustomFieldManage, DataScope.Tenant),
                new(Permissions.Admin.IdentityProviderManage, DataScope.Tenant),

                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Organization.Manage, DataScope.Tenant),
                new(Permissions.Organization.LegalEntityManage, DataScope.Tenant),
                new(Permissions.Organization.DepartmentManage, DataScope.Tenant),
                new(Permissions.MasterData.Manage, DataScope.Tenant),

                // Employee records but not the sensitive sub-resources, and no delete: employment
                // ends through the exit process, which keeps the history a soft delete would hide.
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Employee.Create, DataScope.Tenant),
                new(Permissions.Employee.Edit, DataScope.Tenant),
                new(Permissions.Employee.EmploymentManage, DataScope.Tenant),
                new(Permissions.Employee.HistoryView, DataScope.Tenant),
                new(Permissions.Employee.ExitManage, DataScope.Tenant),

                new(Permissions.Leave.TypeManage, DataScope.Tenant),
                new(Permissions.Leave.PolicyManage, DataScope.Tenant),
                new(Permissions.Leave.BalanceView, DataScope.Tenant),
                new(Permissions.Leave.BalanceAdjust, DataScope.Tenant),
                new(Permissions.Leave.RequestView, DataScope.Tenant),
                new(Permissions.Leave.RequestCreateOnBehalf, DataScope.Tenant),
                new(Permissions.Leave.RequestCancel, DataScope.Tenant),
                new(Permissions.Leave.HolidayManage, DataScope.Tenant),

                new(Permissions.Attendance.View, DataScope.Tenant),
                new(Permissions.Attendance.Manage, DataScope.Tenant),
                new(Permissions.Attendance.RegularizationView, DataScope.Tenant),
                new(Permissions.Attendance.ShiftManage, DataScope.Tenant),
                new(Permissions.Attendance.RosterManage, DataScope.Tenant),
                new(Permissions.Attendance.PolicyManage, DataScope.Tenant),
                new(Permissions.Attendance.DeviceManage, DataScope.Tenant),

                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Workflow.TaskReassign, DataScope.Tenant),
                new(Permissions.Workflow.DefinitionView, DataScope.Tenant),
                new(Permissions.Workflow.DefinitionManage, DataScope.Tenant),
                new(Permissions.Workflow.DelegationManage, DataScope.Tenant),
                new(Permissions.Workflow.InstanceViewAll, DataScope.Tenant),

                new(Permissions.Document.View, DataScope.Tenant),
                new(Permissions.Document.Upload, DataScope.Tenant),
                new(Permissions.Document.Delete, DataScope.Tenant),
                new(Permissions.Document.TypeManage, DataScope.Tenant),
                new(Permissions.Document.TemplateManage, DataScope.Tenant),

                new(Permissions.Policy.View, DataScope.Tenant),
                new(Permissions.Policy.Manage, DataScope.Tenant),
                new(Permissions.Policy.AcknowledgementView, DataScope.Tenant),

                new(Permissions.Client.Manage, DataScope.Tenant),
                new(Permissions.Client.ContactManage, DataScope.Tenant),
                new(Permissions.Contract.Manage, DataScope.Tenant),
                .. ProjectLead(DataScope.Tenant),
                new(Permissions.Project.SettingsManage, DataScope.Tenant),
                .. ResourceManagement(),
                new(Permissions.Timesheet.Approve, DataScope.Tenant),
                new(Permissions.Timesheet.SettingsManage, DataScope.Tenant),
                new(Permissions.Project.FinancialsView, DataScope.Tenant),
                new(Permissions.Project.FinancialsManage, DataScope.Tenant),

                // Money set-up, but none of the sensitive grants: no salaries, runs or declarations.
                .. PayrollSetup(),
                .. ComplianceManagement(),
                .. BenefitManagement(),
                new(Permissions.Compensation.BandView, DataScope.Tenant),
                new(Permissions.Compensation.BandManage, DataScope.Tenant),
                .. HiringManagement(),
                .. TalentManagement(),
                .. ExperienceManagement(),

                new(Permissions.Notification.TemplateView, DataScope.Tenant),
                new(Permissions.Notification.TemplateManage, DataScope.Tenant),

                new(Permissions.Audit.LogView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),
                new(Permissions.Report.ScheduleManage, DataScope.Tenant),

                .. SelfService(),
            ],

            ["HR_ADMIN"] =
            [
                // Creates accounts for new hires, but cannot decide what those accounts may do -
                // role management stays with the Tenant Administrator.
                new(Permissions.Admin.UserView, DataScope.Tenant),
                new(Permissions.Admin.UserInvite, DataScope.Tenant),

                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Organization.Manage, DataScope.Tenant),
                new(Permissions.Organization.DepartmentManage, DataScope.Tenant),
                new(Permissions.MasterData.Manage, DataScope.Tenant),

                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Employee.Create, DataScope.Tenant),
                new(Permissions.Employee.Edit, DataScope.Tenant),
                new(Permissions.Employee.EmploymentManage, DataScope.Tenant),
                new(Permissions.Employee.HistoryView, DataScope.Tenant),
                new(Permissions.Employee.ExitManage, DataScope.Tenant),

                new(Permissions.Leave.TypeManage, DataScope.Tenant),
                new(Permissions.Leave.PolicyManage, DataScope.Tenant),
                new(Permissions.Leave.BalanceView, DataScope.Tenant),
                new(Permissions.Leave.BalanceAdjust, DataScope.Tenant),
                new(Permissions.Leave.RequestView, DataScope.Tenant),
                new(Permissions.Leave.RequestCreateOnBehalf, DataScope.Tenant),
                new(Permissions.Leave.RequestCancel, DataScope.Tenant),
                new(Permissions.Leave.HolidayManage, DataScope.Tenant),

                new(Permissions.Attendance.View, DataScope.Tenant),
                new(Permissions.Attendance.Manage, DataScope.Tenant),
                new(Permissions.Attendance.RegularizationView, DataScope.Tenant),
                new(Permissions.Attendance.ShiftManage, DataScope.Tenant),
                new(Permissions.Attendance.RosterManage, DataScope.Tenant),
                new(Permissions.Attendance.PolicyManage, DataScope.Tenant),

                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Workflow.DefinitionView, DataScope.Tenant),
                new(Permissions.Workflow.InstanceViewAll, DataScope.Tenant),

                new(Permissions.Document.View, DataScope.Tenant),
                new(Permissions.Document.Upload, DataScope.Tenant),
                new(Permissions.Document.TypeManage, DataScope.Tenant),
                new(Permissions.Document.TemplateManage, DataScope.Tenant),

                new(Permissions.Policy.View, DataScope.Tenant),
                new(Permissions.Policy.Manage, DataScope.Tenant),
                new(Permissions.Policy.AcknowledgementView, DataScope.Tenant),

                new(Permissions.Skill.View, DataScope.Tenant),
                new(Permissions.Skill.Manage, DataScope.Tenant),
                new(Permissions.Skill.EmployeeSkillView, DataScope.Tenant),
                new(Permissions.Skill.EmployeeSkillManage, DataScope.Tenant),
                new(Permissions.Timesheet.View, DataScope.Tenant),
                new(Permissions.Timesheet.Approve, DataScope.Tenant),
                new(Permissions.Timesheet.SettingsManage, DataScope.Tenant),

                .. BenefitManagement(),
                new(Permissions.Compensation.BandView, DataScope.Tenant),
                new(Permissions.Compensation.BandManage, DataScope.Tenant),
                new(Permissions.Compensation.VariablePayView, DataScope.Tenant),
                new(Permissions.Compensation.VariablePayManage, DataScope.Tenant),
                new(Permissions.Compensation.EquityView, DataScope.Tenant),
                new(Permissions.Compensation.EquityManage, DataScope.Tenant),
                .. HiringManagement(),
                .. TalentManagement(),
                .. ExperienceManagement(),

                new(Permissions.Notification.TemplateView, DataScope.Tenant),

                new(Permissions.Report.View, DataScope.Tenant),

                .. SelfService(),
            ],

            // Branch-scoped on purpose: the same template serves every site's HR, each grant
            // anchored to their own branch through UserRole.ScopeId.
            ["HR_MANAGER"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),

                new(Permissions.Employee.View, DataScope.Branch),
                new(Permissions.Employee.Edit, DataScope.Branch),
                new(Permissions.Employee.HistoryView, DataScope.Branch),

                new(Permissions.Leave.BalanceView, DataScope.Branch),
                new(Permissions.Leave.RequestView, DataScope.Branch),
                new(Permissions.Leave.RequestCreateOnBehalf, DataScope.Branch),

                new(Permissions.Attendance.View, DataScope.Branch),
                new(Permissions.Attendance.Manage, DataScope.Branch),
                new(Permissions.Attendance.RegularizationView, DataScope.Branch),

                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Workflow.InstanceViewAll, DataScope.Branch),

                new(Permissions.Document.View, DataScope.Branch),
                new(Permissions.Document.Upload, DataScope.Branch),

                new(Permissions.Report.View, DataScope.Branch),

                .. SelfService(),
            ],

            // Legal entity, not tenant: the legal entity is the payroll boundary, so a payroll
            // administrator for one registered company has no reach into another.
            ["PAYROLL_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.LegalEntity),
                new(Permissions.Employee.HistoryView, DataScope.LegalEntity),
                new(Permissions.Leave.BalanceView, DataScope.LegalEntity),
                new(Permissions.Attendance.View, DataScope.LegalEntity),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Report.View, DataScope.LegalEntity),

                .. PayrollSetup(),
                new(Permissions.Payroll.EmployeeSalaryView, DataScope.LegalEntity),
                new(Permissions.Payroll.EmployeeSalaryManage, DataScope.LegalEntity),
                new(Permissions.Payroll.InputManage, DataScope.LegalEntity),
                new(Permissions.Payroll.TaxDeclarationView, DataScope.LegalEntity),
                new(Permissions.Payroll.TaxDeclarationVerify, DataScope.LegalEntity),
                new(Permissions.Payroll.RunView, DataScope.LegalEntity),
                new(Permissions.Payroll.RunProcess, DataScope.LegalEntity),
                new(Permissions.Payroll.RunFinalize, DataScope.LegalEntity),
                new(Permissions.Payroll.ReportView, DataScope.LegalEntity),
                new(Permissions.Compliance.RuleView, DataScope.Tenant),
                new(Permissions.Compliance.FilingView, DataScope.Tenant),
                new(Permissions.Compliance.FilingManage, DataScope.Tenant),
                new(Permissions.Benefit.PlanView, DataScope.Tenant),
                new(Permissions.Benefit.EnrollmentView, DataScope.LegalEntity),
                new(Permissions.Compensation.BandView, DataScope.Tenant),
                new(Permissions.Compensation.RevisionView, DataScope.LegalEntity),
                new(Permissions.Compensation.RevisionManage, DataScope.LegalEntity),
                new(Permissions.Compensation.VariablePayView, DataScope.LegalEntity),

                .. SelfService(),
            ],

            ["FINANCE_USER"] =
            [
                new(Permissions.Spend.ClaimView, DataScope.Tenant),
                new(Permissions.Spend.TravelView, DataScope.Tenant),
                new(Permissions.Spend.TravelAdvanceManage, DataScope.Tenant),
                new(Permissions.Spend.ClaimPay, DataScope.Tenant),
                new(Permissions.Spend.CategoryManage, DataScope.Tenant),
                new(Permissions.Separation.CaseView, DataScope.Tenant),
                new(Permissions.Separation.ClearanceManage, DataScope.Tenant),
                new(Permissions.Separation.SettlementManage, DataScope.Tenant),
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.LegalEntity),
                new(Permissions.Client.View, DataScope.Tenant),
                new(Permissions.Contract.View, DataScope.Tenant),
                new(Permissions.Project.View, DataScope.Tenant),
                new(Permissions.Resource.ProfileView, DataScope.Tenant),
                new(Permissions.Resource.UtilizationView, DataScope.Tenant),
                new(Permissions.Timesheet.View, DataScope.Tenant),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Report.View, DataScope.LegalEntity),

                new(Permissions.Project.FinancialsView, DataScope.Tenant),
                new(Permissions.Project.FinancialsManage, DataScope.Tenant),
                new(Permissions.Payroll.RunView, DataScope.LegalEntity),
                new(Permissions.Payroll.ReportView, DataScope.LegalEntity),
                new(Permissions.Compliance.RuleView, DataScope.Tenant),
                new(Permissions.Compliance.FilingView, DataScope.Tenant),
                new(Permissions.Benefit.PlanView, DataScope.Tenant),
                new(Permissions.Compensation.BandView, DataScope.Tenant),
                new(Permissions.Compensation.VariablePayView, DataScope.Tenant),

                .. SelfService(),
            ],

            ["DEPARTMENT_MANAGER"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),

                new(Permissions.Employee.View, DataScope.Department),

                new(Permissions.Leave.BalanceView, DataScope.Department),
                new(Permissions.Leave.RequestView, DataScope.Department),

                new(Permissions.Attendance.View, DataScope.Department),
                new(Permissions.Attendance.RegularizationView, DataScope.Department),

                new(Permissions.Timesheet.View, DataScope.Department),
                new(Permissions.Skill.EmployeeSkillView, DataScope.Department),

                new(Permissions.Workforce.PlanView, DataScope.Department),
                new(Permissions.Workforce.RequestView, DataScope.Department),
                new(Permissions.Workforce.RequestManage, DataScope.Department),
                new(Permissions.Recruitment.RequisitionView, DataScope.Department),
                new(Permissions.Recruitment.ApplicationView, DataScope.Department),
                new(Permissions.Onboarding.CaseView, DataScope.Department),
                .. TalentLeadership(DataScope.Department),

                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Workflow.DelegationManage, DataScope.Own),

                new(Permissions.Report.View, DataScope.Department),

                .. SelfService(),
            ],

            ["TEAM_LEAD"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),

                new(Permissions.Employee.View, DataScope.DirectReports),

                new(Permissions.Leave.BalanceView, DataScope.DirectReports),
                new(Permissions.Leave.RequestView, DataScope.DirectReports),

                new(Permissions.Attendance.View, DataScope.DirectReports),
                new(Permissions.Attendance.RegularizationView, DataScope.DirectReports),

                new(Permissions.Timesheet.View, DataScope.DirectReports),
                new(Permissions.Skill.EmployeeSkillView, DataScope.DirectReports),
                new(Permissions.Skill.EmployeeSkillManage, DataScope.DirectReports),
                new(Permissions.Workforce.RequestView, DataScope.Own),
                new(Permissions.Workforce.RequestManage, DataScope.Own),
                new(Permissions.Onboarding.CaseView, DataScope.DirectReports),
                .. TalentLeadership(DataScope.DirectReports),

                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Workflow.DelegationManage, DataScope.Own),

                .. SelfService(),
            ],

            [EmployeeTemplateCode] =
            [
                // Everything an employee does is Own. The org structure is the one exception:
                // people need to find a department or a colleague's manager to get anything done.
                new(Permissions.Organization.View, DataScope.Tenant),

                new(Permissions.Leave.RequestCreate, DataScope.Own),
                new(Permissions.Leave.RequestCancel, DataScope.Own),
                new(Permissions.Attendance.RegularizationCreate, DataScope.Own),

                // Their own approval tasks: someone with no reports has none, but a delegate
                // standing in for a manager on leave does.
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),

                new(Permissions.Document.View, DataScope.Own),
                new(Permissions.Document.Upload, DataScope.Own),

                .. ProjectMember(),

                .. SelfService(),
            ],

            // PeopleOS roles (spec section 9). Grants cover the modules that exist today; each later
            // milestone extends the role it concerns as its permissions are released. Templates are
            // seeded as editable copies, so a tenant created before that keeps what it was given.
            ["HR_EXECUTIVE"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Branch),
                new(Permissions.Employee.Create, DataScope.Branch),
                new(Permissions.Employee.Edit, DataScope.Branch),
                new(Permissions.Leave.BalanceView, DataScope.Branch),
                new(Permissions.Leave.RequestView, DataScope.Branch),
                new(Permissions.Attendance.View, DataScope.Branch),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),

                .. SelfService(),
            ],

            // An HR business partner looks after a legal entity's people without administering HR.
            ["HRBP"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.LegalEntity),
                new(Permissions.Employee.HistoryView, DataScope.LegalEntity),
                new(Permissions.Leave.BalanceView, DataScope.LegalEntity),
                new(Permissions.Leave.RequestView, DataScope.LegalEntity),
                new(Permissions.Attendance.View, DataScope.LegalEntity),
                new(Permissions.Performance.CycleView, DataScope.LegalEntity),
                new(Permissions.Performance.ReviewView, DataScope.LegalEntity),
                new(Permissions.Performance.PipView, DataScope.LegalEntity),
                new(Permissions.Learning.EnrollmentView, DataScope.LegalEntity),
                new(Permissions.Career.PathView, DataScope.Tenant),
                new(Permissions.Career.PromotionView, DataScope.LegalEntity),
                new(Permissions.Career.SuccessionView, DataScope.LegalEntity),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Report.View, DataScope.LegalEntity),

                .. SelfService(),
            ],

            ["RECRUITER"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Recruitment.RequisitionView, DataScope.Tenant),
                new(Permissions.Recruitment.PostingView, DataScope.Tenant),
                new(Permissions.Recruitment.PostingManage, DataScope.Tenant),
                new(Permissions.Recruitment.ApplicationView, DataScope.Tenant),
                new(Permissions.Recruitment.ApplicationManage, DataScope.Tenant),
                new(Permissions.Recruitment.OfferView, DataScope.Tenant),
                new(Permissions.Recruitment.OfferManage, DataScope.Tenant),
                new(Permissions.Recruitment.RequisitionManage, DataScope.Tenant),
                new(Permissions.Recruitment.AnalyticsView, DataScope.Tenant),
                new(Permissions.Workforce.RequestView, DataScope.Tenant),
                new(Permissions.Onboarding.CaseView, DataScope.Tenant),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),

                .. SelfService(),
            ],

            // Raises requisitions and judges candidates for their own department's openings.
            ["HIRING_MANAGER"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Recruitment.RequisitionView, DataScope.Department),
                new(Permissions.Recruitment.RequisitionManage, DataScope.Department),
                new(Permissions.Recruitment.ApplicationView, DataScope.Department),
                new(Permissions.Recruitment.OfferView, DataScope.Department),
                new(Permissions.Workforce.RequestView, DataScope.Department),
                new(Permissions.Workforce.RequestManage, DataScope.Department),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),

                .. SelfService(),
            ],

            // Delivery roles (M5): a project manager runs their own projects; delivery and program
            // managers the whole portfolio. Resource management arrives with M6.
            ["PROJECT_MANAGER"] =
            [
                .. DeliveryBaseline(DataScope.Department),
                .. ProjectLead(DataScope.Own),

                // Finding people for their projects: who has which skills, who is free, and asking for them.
                new(Permissions.Skill.View, DataScope.Tenant),
                new(Permissions.Skill.EmployeeSkillView, DataScope.Tenant),
                new(Permissions.Resource.ProfileView, DataScope.Tenant),
                new(Permissions.Resource.AllocationView, DataScope.Tenant),
                new(Permissions.Resource.RequestView, DataScope.Own),
                new(Permissions.Resource.RequestManage, DataScope.Own),
                new(Permissions.Project.FinancialsView, DataScope.Own),
            ],
            ["DELIVERY_MANAGER"] =
            [
                .. DeliveryBaseline(DataScope.Tenant),
                .. ProjectLead(DataScope.Tenant),
                new(Permissions.Client.Manage, DataScope.Tenant),
                new(Permissions.Client.ContactManage, DataScope.Tenant),
                new(Permissions.Contract.Manage, DataScope.Tenant),
                new(Permissions.Project.SettingsManage, DataScope.Tenant),
                .. ResourceReadOnly(),
                new(Permissions.Resource.RequestManage, DataScope.Tenant),
                new(Permissions.Project.FinancialsView, DataScope.Tenant),
                new(Permissions.Project.FinancialsManage, DataScope.Tenant),
            ],
            ["PROGRAM_MANAGER"] =
            [
                .. DeliveryBaseline(DataScope.Tenant),
                .. ProjectLead(DataScope.Tenant),
                .. ResourceReadOnly(),
                new(Permissions.Project.FinancialsView, DataScope.Tenant),
            ],
            ["RESOURCE_MANAGER"] = [.. DeliveryBaseline(DataScope.Tenant), .. DeliveryReadOnly(), .. ResourceManagement()],

            ["PERFORMANCE_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Performance.CycleView, DataScope.Tenant),
                new(Permissions.Performance.CycleManage, DataScope.Tenant),
                new(Permissions.Performance.GoalManage, DataScope.Tenant),
                new(Permissions.Performance.ReviewView, DataScope.Tenant),
                new(Permissions.Performance.CalibrationManage, DataScope.Tenant),
                new(Permissions.Performance.PipView, DataScope.Tenant),
                new(Permissions.Performance.PipManage, DataScope.Tenant),
                new(Permissions.Career.PathView, DataScope.Tenant),
                new(Permissions.Career.PathManage, DataScope.Tenant),
                new(Permissions.Career.PromotionView, DataScope.Tenant),
                new(Permissions.Career.SuccessionView, DataScope.Tenant),
                new(Permissions.Career.SuccessionManage, DataScope.Tenant),
                new(Permissions.Career.TalentReviewManage, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),

                .. SelfService(),
            ],

            ["LEARNING_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Learning.CourseView, DataScope.Tenant),
                new(Permissions.Learning.CourseManage, DataScope.Tenant),
                new(Permissions.Learning.EnrollmentView, DataScope.Tenant),
                new(Permissions.Learning.EnrollmentManage, DataScope.Tenant),
                new(Permissions.Learning.CertificationView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),

                .. SelfService(),
            ],

            // Reward permissions arrive with M10.
            ["REWARD_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Reward.CatalogView, DataScope.Tenant),
                new(Permissions.Reward.CatalogManage, DataScope.Tenant),
                new(Permissions.Reward.ProgramManage, DataScope.Tenant),
                new(Permissions.Reward.RecognitionView, DataScope.Tenant),
                new(Permissions.Reward.RedemptionManage, DataScope.Tenant),
                new(Permissions.Engagement.SurveyView, DataScope.Tenant),
                new(Permissions.Engagement.ResultsView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),

                .. SelfService(),
            ],

            ["COMPLIANCE_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Audit.LogView, DataScope.Tenant),
                new(Permissions.Document.View, DataScope.Tenant),
                new(Permissions.Policy.View, DataScope.Tenant),
                new(Permissions.Policy.Manage, DataScope.Tenant),
                new(Permissions.Policy.AcknowledgementView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),
                .. ComplianceManagement(),

                .. SelfService(),
            ],

            // Accounts and equipment: who can sign in, and what they have been issued.
            ["IT_ADMIN"] =
            [
                new(Permissions.Admin.UserView, DataScope.Tenant),
                new(Permissions.Admin.UserLock, DataScope.Tenant),
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Asset.View, DataScope.Tenant),
                new(Permissions.Asset.Manage, DataScope.Tenant),
                new(Permissions.Onboarding.CaseView, DataScope.Tenant),
                new(Permissions.Onboarding.TaskComplete, DataScope.Tenant),
                new(Permissions.Helpdesk.TicketView, DataScope.Tenant),
                new(Permissions.Helpdesk.TicketManage, DataScope.Tenant),
                new(Permissions.Helpdesk.KnowledgeManage, DataScope.Tenant),
                new(Permissions.Separation.CaseView, DataScope.Tenant),
                new(Permissions.Separation.ClearanceManage, DataScope.Tenant),

                .. SelfService(),
            ],

            ["ASSET_ADMIN"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Asset.CategoryManage, DataScope.Tenant),
                new(Permissions.Asset.View, DataScope.Tenant),
                new(Permissions.Asset.Manage, DataScope.Tenant),
                new(Permissions.Separation.CaseView, DataScope.Tenant),
                new(Permissions.Separation.ClearanceManage, DataScope.Tenant),
                new(Permissions.Helpdesk.TicketView, DataScope.Tenant),
                new(Permissions.Helpdesk.TicketManage, DataScope.Tenant),

                .. SelfService(),
            ],

            // Leadership: the whole picture, read-only.
            ["EXECUTIVE"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Leave.BalanceView, DataScope.Tenant),
                new(Permissions.Attendance.View, DataScope.Tenant),
                new(Permissions.Performance.CycleView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),
                .. DeliveryReadOnly(),
                .. ResourceReadOnly(),
                .. MoneyReadOnly(),

                .. SelfService(),
            ],

            // Read-only by construction: nothing here ends in manage, create, edit or delete.
            // Auditor grants are also time-boxed when handed out (UserRole.ValidTo).
            ["AUDITOR"] =
            [
                new(Permissions.Admin.UserView, DataScope.Tenant),
                new(Permissions.Admin.RoleView, DataScope.Tenant),
                new(Permissions.Admin.SettingView, DataScope.Tenant),

                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.Tenant),
                new(Permissions.Employee.HistoryView, DataScope.Tenant),

                new(Permissions.Leave.BalanceView, DataScope.Tenant),
                new(Permissions.Leave.RequestView, DataScope.Tenant),
                new(Permissions.Attendance.View, DataScope.Tenant),
                new(Permissions.Attendance.RegularizationView, DataScope.Tenant),

                new(Permissions.Workflow.DefinitionView, DataScope.Tenant),
                new(Permissions.Workflow.InstanceViewAll, DataScope.Tenant),

                new(Permissions.Document.View, DataScope.Tenant),
                new(Permissions.Audit.LogView, DataScope.Tenant),
                new(Permissions.Report.View, DataScope.Tenant),
                .. DeliveryReadOnly(),
                .. ResourceReadOnly(),
                .. MoneyReadOnly(),

                .. SelfService(),
            ],
        };

    /// <summary>
    /// The roles a new tenant is created with, in the order they are presented.
    /// <para>
    /// Names and descriptions live here rather than in the Platform service even though Platform
    /// drives provisioning: roles are Identity's data, and a list of role names in a service that
    /// cannot see the permission catalog is a list nobody can check.
    /// </para>
    /// <para>
    /// Seeded as editable copies, not links to a shared definition, so our changing a template
    /// later never silently widens access inside a live customer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RoleTemplate> Definitions { get; } =
    [
        new(TenantAdministratorTemplateCode, "Tenant Administrator", "Full administrative control of this organization.", IsAssignedToFirstAdministrator: true),
        new("HR_ADMIN", "HR Administrator", "Manages employees, organization structure and HR policies."),
        new("HR_MANAGER", "HR Manager", "Manages employees and approvals within their branch."),
        new("HR_EXECUTIVE", "HR Executive", "Maintains employee records and day-to-day HR operations for a branch."),
        new("HRBP", "HR Business Partner", "Partners with a legal entity's leadership on its people."),
        new("PAYROLL_ADMIN", "Payroll Administrator", "Runs payroll and manages salary structures."),
        new("FINANCE_USER", "Finance", "Approves expenses and reviews payroll and project cost reports."),
        new("RECRUITER", "Recruiter", "Runs job postings, candidates, interviews and offers."),
        new("HIRING_MANAGER", "Hiring Manager", "Raises requisitions and evaluates candidates for their department."),
        new("DEPARTMENT_MANAGER", "Department Head", "Manages their department: approvals, goals and reports."),
        new("TEAM_LEAD", "Manager", "Approves leave and attendance for their direct reports."),
        new(EmployeeTemplateCode, "Employee", "Self-service access to their own records.", IsDefaultForNewEmployees: true),
        new("PROJECT_MANAGER", "Project Manager", "Plans and runs projects: team, tasks, milestones, risks."),
        new("DELIVERY_MANAGER", "Delivery Manager", "Oversees delivery, health and margin across a portfolio of projects."),
        new("RESOURCE_MANAGER", "Resource Manager", "Decides who works on what, when, at what capacity and cost."),
        new("PROGRAM_MANAGER", "Program Manager", "Coordinates related projects towards a shared outcome."),
        new("PERFORMANCE_ADMIN", "Performance Administrator", "Runs performance cycles, goals and calibration."),
        new("LEARNING_ADMIN", "Learning Administrator", "Manages courses, learning paths and certifications."),
        new("REWARD_ADMIN", "Reward Administrator", "Manages the reward catalog, rules, budgets and redemptions."),
        new("COMPLIANCE_ADMIN", "Compliance Administrator", "Owns statutory rules, filings, policies and audits."),
        new("IT_ADMIN", "IT Administrator", "Manages user access and IT equipment."),
        new("ASSET_ADMIN", "Asset Administrator", "Manages the asset register and assignments."),
        new("AUDITOR", "Auditor", "Read-only access across the organization, including the audit log."),
        new("EXECUTIVE", "Executive", "Read-only view of the whole organization's people, delivery and cost."),
    ];

    /// <summary>Every template this service knows how to seed, keyed by the template code.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PermissionAssignment>> All => Templates;

    /// <summary>
    /// The permissions for a template code, or an empty list when Platform names one this service
    /// has no opinion about.
    /// <para>
    /// Empty rather than an exception on purpose. An unknown template means the two lists have
    /// drifted, and the right outcome is one role that grants nothing plus a warning - not a
    /// tenant whose provisioning fails outright and ends up with no roles at all.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PermissionAssignment> For(string templateCode)
        => Templates.GetValueOrDefault(templateCode) ?? [];

    /// <summary>Salary components, structures and payroll rules: set-up, not anyone's pay.</summary>
    private static PermissionAssignment[] PayrollSetup() =>
    [
        new(Permissions.Payroll.ComponentManage, DataScope.Tenant),
        new(Permissions.Payroll.StructureManage, DataScope.Tenant),
        new(Permissions.Payroll.SettingsManage, DataScope.Tenant),
    ];

    private static PermissionAssignment[] ComplianceManagement() =>
    [
        new(Permissions.Compliance.RuleView, DataScope.Tenant),
        new(Permissions.Compliance.RuleManage, DataScope.Tenant),
        new(Permissions.Compliance.FilingView, DataScope.Tenant),
        new(Permissions.Compliance.FilingManage, DataScope.Tenant),
    ];

    private static PermissionAssignment[] BenefitManagement() =>
    [
        new(Permissions.Benefit.PlanView, DataScope.Tenant),
        new(Permissions.Benefit.PlanManage, DataScope.Tenant),
        new(Permissions.Benefit.EnrollmentView, DataScope.Tenant),
        new(Permissions.Benefit.EnrollmentManage, DataScope.Tenant),
    ];

    /// <summary>Margins, rules and plans for those who oversee but do not run them - nothing personal or sensitive.</summary>
    private static PermissionAssignment[] MoneyReadOnly() =>
    [
        new(Permissions.Project.FinancialsView, DataScope.Tenant),
        new(Permissions.Compliance.RuleView, DataScope.Tenant),
        new(Permissions.Compliance.FilingView, DataScope.Tenant),
        new(Permissions.Benefit.PlanView, DataScope.Tenant),
        new(Permissions.Compensation.BandView, DataScope.Tenant),
        new(Permissions.Workforce.PlanView, DataScope.Tenant),
        new(Permissions.Workforce.RequestView, DataScope.Tenant),
        new(Permissions.Recruitment.AnalyticsView, DataScope.Tenant),
    ];

    /// <summary>Workforce plans, manpower requests, recruitment end to end, and onboarding.</summary>
    /// <summary>Rewards, engagement, helpdesk, assets, expenses and separation end to end.</summary>
    private static PermissionAssignment[] ExperienceManagement() =>
    [
        new(Permissions.Reward.CatalogView, DataScope.Tenant),
        new(Permissions.Reward.CatalogManage, DataScope.Tenant),
        new(Permissions.Reward.ProgramManage, DataScope.Tenant),
        new(Permissions.Reward.RecognitionView, DataScope.Tenant),
        new(Permissions.Reward.RedemptionManage, DataScope.Tenant),
        new(Permissions.Engagement.SurveyView, DataScope.Tenant),
        new(Permissions.Engagement.SurveyManage, DataScope.Tenant),
        new(Permissions.Engagement.ResultsView, DataScope.Tenant),
        new(Permissions.Helpdesk.TicketView, DataScope.Tenant),
        new(Permissions.Helpdesk.TicketManage, DataScope.Tenant),
        new(Permissions.Helpdesk.CategoryManage, DataScope.Tenant),
        new(Permissions.Helpdesk.KnowledgeManage, DataScope.Tenant),
        new(Permissions.Asset.CategoryManage, DataScope.Tenant),
        new(Permissions.Asset.View, DataScope.Tenant),
        new(Permissions.Asset.Manage, DataScope.Tenant),
        new(Permissions.Spend.CategoryManage, DataScope.Tenant),
        new(Permissions.Spend.ClaimView, DataScope.Tenant),
        new(Permissions.Spend.TravelView, DataScope.Tenant),
        new(Permissions.Spend.TravelAdvanceManage, DataScope.Tenant),
        new(Permissions.Spend.ClaimPay, DataScope.Tenant),
        new(Permissions.Separation.CaseView, DataScope.Tenant),
        new(Permissions.Separation.CaseManage, DataScope.Tenant),
        new(Permissions.Separation.ClearanceManage, DataScope.Tenant),
        new(Permissions.Separation.SettlementManage, DataScope.Tenant),
        new(Permissions.Separation.AlumniView, DataScope.Tenant),
    ];

    /// <summary>Goals, performance, learning and career end to end.</summary>
    private static PermissionAssignment[] TalentManagement() =>
    [
        new(Permissions.Performance.CycleView, DataScope.Tenant),
        new(Permissions.Performance.CycleManage, DataScope.Tenant),
        new(Permissions.Performance.GoalManage, DataScope.Tenant),
        new(Permissions.Performance.ReviewView, DataScope.Tenant),
        new(Permissions.Performance.ReviewManageAsManager, DataScope.Tenant),
        new(Permissions.Performance.CalibrationManage, DataScope.Tenant),
        new(Permissions.Performance.PipView, DataScope.Tenant),
        new(Permissions.Performance.PipManage, DataScope.Tenant),
        new(Permissions.Learning.CourseView, DataScope.Tenant),
        new(Permissions.Learning.CourseManage, DataScope.Tenant),
        new(Permissions.Learning.EnrollmentView, DataScope.Tenant),
        new(Permissions.Learning.EnrollmentManage, DataScope.Tenant),
        new(Permissions.Learning.CertificationView, DataScope.Tenant),
        new(Permissions.Career.PathView, DataScope.Tenant),
        new(Permissions.Career.PathManage, DataScope.Tenant),
        new(Permissions.Career.PromotionView, DataScope.Tenant),
        new(Permissions.Career.PromotionManage, DataScope.Tenant),
        new(Permissions.Career.SuccessionView, DataScope.Tenant),
        new(Permissions.Career.SuccessionManage, DataScope.Tenant),
        new(Permissions.Career.TalentReviewManage, DataScope.Tenant),
    ];

    /// <summary>What a people leader does for their own people: goals, reviews, plans, learning, promotions, potential.</summary>
    private static PermissionAssignment[] TalentLeadership(DataScope scope) =>
    [
        new(Permissions.Spend.ClaimView, scope),
        new(Permissions.Spend.TravelView, scope),
        new(Permissions.Separation.CaseView, scope),
        new(Permissions.Reward.RecognitionView, scope),
        new(Permissions.Performance.CycleView, DataScope.Tenant),
        new(Permissions.Performance.GoalManage, scope),
        new(Permissions.Performance.ReviewView, scope),
        new(Permissions.Performance.ReviewManageAsManager, scope),
        new(Permissions.Performance.PipView, scope),
        new(Permissions.Performance.PipManage, scope),
        new(Permissions.Learning.CourseView, DataScope.Tenant),
        new(Permissions.Learning.EnrollmentView, scope),
        new(Permissions.Learning.EnrollmentManage, scope),
        new(Permissions.Learning.CertificationView, scope),
        new(Permissions.Career.PathView, DataScope.Tenant),
        new(Permissions.Career.PromotionView, scope),
        new(Permissions.Career.PromotionManage, scope),
        new(Permissions.Career.SuccessionView, scope),
        new(Permissions.Career.TalentReviewManage, scope),
    ];

    private static PermissionAssignment[] HiringManagement() =>
    [
        new(Permissions.Workforce.PlanView, DataScope.Tenant),
        new(Permissions.Workforce.PlanManage, DataScope.Tenant),
        new(Permissions.Workforce.RequestView, DataScope.Tenant),
        new(Permissions.Workforce.RequestManage, DataScope.Tenant),
        new(Permissions.Recruitment.RequisitionView, DataScope.Tenant),
        new(Permissions.Recruitment.RequisitionManage, DataScope.Tenant),
        new(Permissions.Recruitment.PostingView, DataScope.Tenant),
        new(Permissions.Recruitment.PostingManage, DataScope.Tenant),
        new(Permissions.Recruitment.ApplicationView, DataScope.Tenant),
        new(Permissions.Recruitment.ApplicationManage, DataScope.Tenant),
        new(Permissions.Recruitment.OfferView, DataScope.Tenant),
        new(Permissions.Recruitment.OfferManage, DataScope.Tenant),
        new(Permissions.Recruitment.SettingsManage, DataScope.Tenant),
        new(Permissions.Recruitment.AnalyticsView, DataScope.Tenant),
        new(Permissions.Onboarding.TemplateView, DataScope.Tenant),
        new(Permissions.Onboarding.TemplateManage, DataScope.Tenant),
        new(Permissions.Onboarding.CaseView, DataScope.Tenant),
        new(Permissions.Onboarding.CaseManage, DataScope.Tenant),
        new(Permissions.Onboarding.TaskComplete, DataScope.Tenant),
    ];

    /// <summary>
    /// Self-service, which every role carries. A payroll administrator still has to apply for
    /// their own leave, and a role that took that away would leave the holder unable to use the
    /// product on themselves.
    /// </summary>
    private static PermissionAssignment[] SelfService() =>
    [
        new(Permissions.Self.ProfileView, DataScope.Own),
        new(Permissions.Self.ProfileEdit, DataScope.Own),
        new(Permissions.Self.AttendanceView, DataScope.Own),
        new(Permissions.Self.AttendancePunch, DataScope.Own),
        new(Permissions.Self.LeaveView, DataScope.Own),
        new(Permissions.Self.LeaveApply, DataScope.Own),
        new(Permissions.Self.PayslipView, DataScope.Own),
        new(Permissions.Self.DocumentView, DataScope.Own),
        new(Permissions.Self.PolicyView, DataScope.Own),
        new(Permissions.Self.ShiftSwapRequest, DataScope.Own),
        new(Permissions.Self.TimesheetSubmit, DataScope.Own),
        new(Permissions.Self.SkillManage, DataScope.Own),
        new(Permissions.Self.TaxDeclarationManage, DataScope.Own),
        new(Permissions.Self.BenefitManage, DataScope.Own),
        new(Permissions.Self.CompensationView, DataScope.Own),
        new(Permissions.Self.ReferralCreate, DataScope.Own),
        new(Permissions.Self.InterviewFeedback, DataScope.Own),
        new(Permissions.Self.OnboardingView, DataScope.Own),
        new(Permissions.Self.FeedbackGive, DataScope.Own),
        new(Permissions.Self.LearningView, DataScope.Own),
        new(Permissions.Self.CareerView, DataScope.Own),
        new(Permissions.Performance.GoalManageOwn, DataScope.Own),
        new(Permissions.Performance.ReviewManageOwn, DataScope.Own),
        new(Permissions.Learning.EnrollmentManageOwn, DataScope.Own),
        new(Permissions.Self.RewardView, DataScope.Own),
        new(Permissions.Self.RecognitionGive, DataScope.Own),
        new(Permissions.Self.TicketCreate, DataScope.Own),
        new(Permissions.Self.SurveyRespond, DataScope.Own),
        new(Permissions.Self.ResignationSubmit, DataScope.Own),
        new(Permissions.Self.AssetView, DataScope.Own),
        new(Permissions.Self.ExpenseClaimCreate, DataScope.Own),
        new(Permissions.Self.TravelRequestCreate, DataScope.Own),

        // Everyone receives notifications - approvals, policies to acknowledge, invitations.
        new(Permissions.Notification.InboxView, DataScope.Own),
        new(Permissions.Notification.InboxManage, DataScope.Own),

        // Every form's dropdowns read lookup lists, so reading them is part of using the product.
        new(Permissions.MasterData.View, DataScope.Tenant),
    ];
}

/// <summary>A role seeded into a new tenant.</summary>
/// <param name="IsAssignedToFirstAdministrator">Given to the administrator created during provisioning.</param>
/// <param name="IsDefaultForNewEmployees">Granted automatically when an employee gets a user account.</param>
public sealed record RoleTemplate(
    string Code,
    string Name,
    string Description,
    bool IsAssignedToFirstAdministrator = false,
    bool IsDefaultForNewEmployees = false)
{
    /// <summary>What this role may do, resolved from the catalog above.</summary>
    public IReadOnlyList<PermissionAssignment> Permissions => RoleTemplates.For(Code);
}
