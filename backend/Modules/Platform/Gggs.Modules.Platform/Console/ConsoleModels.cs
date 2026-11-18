using Gggs.BuildingBlocks.Domain.Abstractions;

namespace Gggs.Modules.Platform.Console;

/*
 * The platform console's request and response shapes. Served only by Gggs.PlatformApi; none of them
 * carries anything from inside an organisation beyond the counts measured by the usage job.
 */

/// <summary>An organisation's measured figures; null until the usage job has run once.</summary>
public sealed record UsageView(
    int Employees,
    int Users,
    int SignedInToday,
    DateTime? LastSignIn,
    int EmailsSentToday,
    int EmailsFailedToday,
    int EmailsLoggedToday,
    string? EmailChannel,
    string? SignIn,
    DateTime MeasuredDate);

/// <summary>One row of the Organisations list.</summary>
public sealed record OrganisationRow(
    Guid Id,
    string Code,
    string Name,
    TenantStatus Status,
    string? PlanCode,
    string? PlanName,
    string? SubscriptionStatus,
    int? SeatLimit,
    DateOnly? TrialEndsOn,
    DateOnly? RenewsOn,
    DateTime? PurgeEligibleDate,
    string? SuspensionReason,
    string DataRegion,
    UsageView? Usage,
    DateTime CreatedDate);

/// <summary>A module (plan feature) as an organisation has it.</summary>
public sealed record ModuleState(string Code, bool InPlan, bool Enabled, bool Overridden);

public sealed record SubscriptionView(
    Guid Id,
    string PlanCode,
    string PlanName,
    int? PlanMaxEmployees,
    string Status,
    DateOnly StartDate,
    DateOnly? TrialEndsOn,
    DateOnly? RenewsOn,
    int? SeatLimit,
    string? BillingReference);

public sealed record OrganisationDetail(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    TenantStatus Status,
    TenantIsolationTier IsolationTier,
    string CountryCode,
    string DataRegion,
    string DefaultTimeZone,
    string DefaultCurrency,
    string DefaultLocale,
    DateTime? ActivatedDate,
    DateTime? SuspendedDate,
    string? SuspensionReason,
    DateTime? PurgeEligibleDate,
    SubscriptionView? Subscription,
    IReadOnlyList<ModuleState> Modules,
    UsageView? Usage,
    DateTime CreatedDate);

/// <summary>Creating an organisation from the console: the provisioning request plus its first administrator's name.</summary>
public sealed record CreateOrganisationBody
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? LegalName { get; init; }

    public string CountryCode { get; init; } = "IN";

    public string DataRegion { get; init; } = "IN";

    public string DefaultTimeZone { get; init; } = "Asia/Kolkata";

    public string DefaultCurrency { get; init; } = "INR";

    public string DefaultLocale { get; init; } = "en-IN";

    public required string PlanCode { get; init; }

    public TenantIsolationTier IsolationTier { get; init; } = TenantIsolationTier.Pooled;

    /// <summary>Null for a paid subscription from today.</summary>
    public int? TrialDays { get; init; }

    public int? SeatLimit { get; init; }

    public string? BillingReference { get; init; }

    public required string AdministratorEmail { get; init; }

    /// <summary>Modules outside the plan to add on.</summary>
    public IReadOnlyList<string> AddOns { get; init; } = [];
}

/// <summary>The subscription as the console's Subscription tab saves it.</summary>
public sealed record UpdateSubscriptionBody(
    string PlanCode,
    int? SeatLimit,
    bool Paid,
    DateOnly? TrialEndsOn,
    DateOnly? RenewsOn,
    string? BillingReference);

/// <summary>Every module the organisation should have on; the difference from the plan becomes overrides.</summary>
public sealed record UpdateModulesBody(IReadOnlyList<string> Enabled);

public sealed record SuspendBody(string Reason);

public sealed record OffboardBody(int GraceDays = 30);

public sealed record AuditItem(Guid Id, DateTime OccurredDate, string StaffName, Guid? TenantId, string? TenantCode, string Action, string Summary, string? Reason);

public sealed record PlanView(string Code, string Name, string Edition, int? MaxEmployees, bool IsActive, IReadOnlyList<string> Features, int Organisations);

/// <summary>Every module the product has, and how many live organisations have each on.</summary>
public sealed record ModuleUsage(string Code, int Organisations);

public sealed record PlansView(IReadOnlyList<PlanView> Plans, IReadOnlyList<ModuleUsage> Modules);

public sealed record CountByKey(string Key, int Count);

public sealed record MonthCount(string Month, int Count);

public sealed record AttentionItem(string Kind, Guid? TenantId, string Text);

public sealed record UpcomingItem(Guid TenantId, string Name, string What, DateOnly When, string? PlanName);

public sealed record DashboardView(
    IReadOnlyList<CountByKey> ByStatus,
    IReadOnlyList<CountByKey> ByPlan,
    int Employees,
    int SignedInToday,
    int EmailsSentToday,
    int EmailsFailedToday,
    IReadOnlyList<MonthCount> NewPerMonth,
    IReadOnlyList<UpcomingItem> Upcoming,
    IReadOnlyList<AttentionItem> Attention,
    IReadOnlyList<AuditItem> Recent,
    int FailedJobs);

public sealed record ServiceStatus(string Name, string State, bool Healthy, string Detail);

public sealed record RecurringJobView(string Id, string Cron, DateTime? LastExecution, DateTime? NextExecution, string? LastState, string? Error);

public sealed record FailedJobView(string Id, string Job, DateTime? FailedAt, string? Reason, Guid? TenantId, string? TenantName);

public sealed record EmailHealthRow(Guid TenantId, string Name, string? Channel, int Sent, int Failed, int Logged);

public sealed record HealthView(
    IReadOnlyList<ServiceStatus> Services,
    IReadOnlyList<FailedJobView> Failed,
    IReadOnlyList<RecurringJobView> Jobs,
    IReadOnlyList<EmailHealthRow> Email);
