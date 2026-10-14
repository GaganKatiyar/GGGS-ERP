namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Licensable features: what a tenant's plan switches on (spec §114).
/// <para>
/// One code per module group. A feature decides whether a module is <em>available</em> to a tenant
/// at all; a permission then decides who inside the tenant may use it. The navigation hides what a
/// tenant has not licensed, and the module's endpoints refuse it - the same pairing as permissions,
/// for the same reason.
/// </para>
/// <para>
/// Like permission codes, these are a released contract: stored in plans and subscriptions, and
/// checked by the frontend. Add freely; never rename.
/// </para>
/// </summary>
public static class Features
{
    // Always on: the platform itself and the HR core everything else hangs off.
    public const string CoreHr = "CoreHr";

    public const string Workforce = "Workforce";
    public const string Recruitment = "Recruitment";
    public const string Onboarding = "Onboarding";
    public const string Time = "Time";
    public const string Payroll = "Payroll";
    public const string Compensation = "Compensation";
    public const string Benefits = "Benefits";
    public const string Expenses = "Expenses";
    public const string Performance = "Performance";
    public const string Learning = "Learning";
    public const string Career = "Career";
    public const string Rewards = "Rewards";
    public const string Engagement = "Engagement";
    public const string Helpdesk = "Helpdesk";
    public const string Assets = "Assets";
    public const string Documents = "Documents";
    public const string Compliance = "Compliance";
    public const string Separation = "Separation";

    // Delivery: the IT-services half of the product.
    public const string Projects = "Projects";
    public const string Resources = "Resources";
    public const string Timesheets = "Timesheets";
    public const string ProjectFinancials = "ProjectFinancials";

    public const string Analytics = "Analytics";
    public const string AI = "AI";

    /// <summary>Every feature, for plans that include everything and for validation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CoreHr, Workforce, Recruitment, Onboarding, Time, Payroll, Compensation, Benefits, Expenses,
        Performance, Learning, Career, Rewards, Engagement, Helpdesk, Assets, Documents, Compliance,
        Separation, Projects, Resources, Timesheets, ProjectFinancials, Analytics, AI,
    ];
}
