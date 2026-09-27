using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Domain.Roles;

namespace Gggs.Identity.Application.Provisioning;

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
/// Payroll, expense and travel permissions are absent because those modules do not exist yet. The
/// roles that will need them (<c>PAYROLL_ADMIN</c>, <c>FINANCE_USER</c>) are seeded now with the
/// access they can have, and gain the rest when the catalog does.
/// </para>
/// </summary>
public static class RoleTemplates
{
    /// <summary>The role every new employee account is given, so self-service works immediately.</summary>
    public const string EmployeeTemplateCode = "EMPLOYEE";

    /// <summary>The role the first administrator is given, and the only one that can grant the rest.</summary>
    public const string TenantAdministratorTemplateCode = "TENANT_ADMIN";

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

                .. SelfService(),
            ],

            ["FINANCE_USER"] =
            [
                new(Permissions.Organization.View, DataScope.Tenant),
                new(Permissions.Employee.View, DataScope.LegalEntity),
                new(Permissions.Workflow.TaskView, DataScope.Own),
                new(Permissions.Workflow.TaskAct, DataScope.Own),
                new(Permissions.Report.View, DataScope.LegalEntity),

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
        new("PAYROLL_ADMIN", "Payroll Administrator", "Runs payroll and manages salary structures."),
        new("FINANCE_USER", "Finance User", "Approves expenses and reviews payroll cost reports."),
        new("DEPARTMENT_MANAGER", "Department Manager", "Manages their department: approvals, goals and reports."),
        new("TEAM_LEAD", "Team Lead", "Approves leave and attendance for their direct reports."),
        new(EmployeeTemplateCode, "Employee", "Self-service access to their own records.", IsDefaultForNewEmployees: true),
        new("AUDITOR", "Auditor", "Read-only access across the organization, including the audit log."),
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
