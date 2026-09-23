namespace Gggs.Platform.Application.Provisioning;

/// <summary>
/// What a brand-new tenant starts with.
/// <para>
/// This is a product decision as much as a technical one, so it is data in one reviewable place
/// rather than scattered through each service's seeding code. The Platform service does not apply
/// any of it: it publishes <c>TenantProvisioned</c>, and each service seeds its own defaults from
/// the list it owns.
/// </para>
/// <para>
/// The principle behind the choices: a new tenant should be able to run a real leave request on
/// day one without configuring anything, but should never be given a permission by default that
/// nobody asked for. Anything with commercial or privacy weight - payroll access, data export,
/// bank details - is left for the customer to grant deliberately.
/// </para>
/// </summary>
public static class TenantProvisioningDefaults
{
    /// <summary>
    /// Role templates every tenant is created with, matching §8.5 of the architecture.
    /// <para>
    /// Created as editable copies rather than links to a shared definition, so our changing a
    /// template later never silently widens access inside a live customer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RoleTemplate> Roles { get; } =
    [
        new("TENANT_ADMIN", "Tenant Administrator", "Full administrative control of this organization.", IsAssignedToFirstUser: true),
        new("HR_ADMIN", "HR Administrator", "Manages employees, organization structure and HR policies."),
        new("HR_MANAGER", "HR Manager", "Manages employees and approvals within their branch or entity."),
        new("PAYROLL_ADMIN", "Payroll Administrator", "Runs payroll and manages salary structures."),
        new("FINANCE_USER", "Finance User", "Approves expenses and reviews payroll cost reports."),
        new("DEPARTMENT_MANAGER", "Department Manager", "Manages their department: approvals, goals and reports."),
        new("TEAM_LEAD", "Team Lead", "Approves leave and attendance for their direct reports."),
        new("EMPLOYEE", "Employee", "Self-service access to their own records.", IsDefaultForNewEmployees: true),
        new("AUDITOR", "Auditor", "Read-only access across the organization, including the audit log."),
    ];

    /// <summary>
    /// Leave types most organizations need on day one. Deliberately short: a long list of types
    /// nobody uses is harder to clean up than a short one is to extend, and the right set is
    /// country- and policy-specific.
    /// </summary>
    public static IReadOnlyList<LeaveTypeTemplate> LeaveTypes { get; } =
    [
        new("CASUAL", "Casual Leave", IsPaid: true),
        new("SICK", "Sick Leave", IsPaid: true),
        new("EARNED", "Earned Leave", IsPaid: true),
        new("UNPAID", "Unpaid Leave", IsPaid: false),
    ];

    /// <summary>
    /// Baseline settings. Working days and the week start differ by country, so these are seeded
    /// from the tenant's country rather than assumed: a Sunday-to-Thursday week is normal in much
    /// of the Gulf, and getting it wrong makes every leave calculation wrong.
    /// </summary>
    public static IReadOnlyList<SettingTemplate> Settings { get; } =
    [
        new("core", "WeekStartsOn", "\"Monday\""),
        new("core", "DateFormat", "\"dd MMM yyyy\""),
        new("leave", "WorkingDays", "[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"]"),
        new("leave", "AllowHalfDay", "true"),
        new("leave", "MinNoticeDays", "0"),
        new("attendance", "GraceMinutes", "15"),
        new("attendance", "FullDayMinutes", "480"),

        // Off by default: geo-fencing and selfie capture are surveillance features, and a customer
        // should switch them on knowingly rather than discover them running.
        new("attendance", "RequireGeoFence", "false"),
        new("notification", "QuietHoursStart", "\"21:00\""),
        new("notification", "QuietHoursEnd", "\"07:00\""),
    ];

    /// <summary>
    /// Approval flows so the first leave request works without anyone configuring a workflow.
    /// A single step to the reporting manager is what almost every organization starts with.
    /// </summary>
    public static IReadOnlyList<WorkflowTemplate> Workflows { get; } =
    [
        new("LEAVE_REQUEST", "Leave request approval", "ReportingManager"),
        new("ATTENDANCE_REGULARIZATION", "Attendance correction approval", "ReportingManager"),
        new("EXPENSE_CLAIM", "Expense claim approval", "ReportingManager"),
    ];
}

/// <summary>A role seeded into a new tenant.</summary>
/// <param name="IsAssignedToFirstUser">Given to the administrator created during provisioning.</param>
/// <param name="IsDefaultForNewEmployees">Granted automatically when an employee gets a user account.</param>
public sealed record RoleTemplate(
    string Code,
    string Name,
    string Description,
    bool IsAssignedToFirstUser = false,
    bool IsDefaultForNewEmployees = false);

public sealed record LeaveTypeTemplate(string Code, string Name, bool IsPaid);

public sealed record SettingTemplate(string Module, string Key, string ValueJson);

/// <param name="ApproverRule">Approver resolution rule, as the workflow engine names them.</param>
public sealed record WorkflowTemplate(string DefinitionCode, string Name, string ApproverRule);
