using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Platform.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,

    /// <summary>Payment is late. Access continues during the dunning window.</summary>
    PastDue = 3,

    Cancelled = 4,
    Expired = 5,
}

/// <summary>
/// What one tenant is entitled to: their plan, its dates, and any agreed exceptions.
/// <para>
/// Platform-owned, even though it belongs to a tenant. Commercial terms are ours, not the
/// customer's: a tenant admin must never be able to grant themselves a module through the same
/// settings screens they use to configure leave types.
/// </para>
/// </summary>
public sealed class Subscription : AggregateRoot, IPlatformOwned, IAuditable, IConcurrencyAware
{
    private readonly List<FeatureOverride> _featureOverrides = [];

    private Subscription()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Null for an open-ended subscription.</summary>
    public DateOnly? EndDate { get; private set; }

    public DateOnly? TrialEndsOn { get; private set; }

    /// <summary>Licensed user seats, or null for unlimited.</summary>
    public int? SeatLimit { get; private set; }

    /// <summary>Reference in the billing system; we do not hold payment details.</summary>
    public string? BillingReference { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public byte[]? RowVersion { get; private set; }

    /// <summary>Features switched on or off for this tenant regardless of their plan.</summary>
    public IReadOnlyCollection<FeatureOverride> FeatureOverrides => _featureOverrides;

    /// <summary>
    /// Whether the tenant may use the product right now.
    /// <para>
    /// PastDue deliberately still counts. Cutting off payroll because an invoice is late punishes
    /// employees who are owed wages, not the finance department that missed it; collections handle
    /// that, and Suspended is the deliberate switch when they escalate.
    /// </para>
    /// </summary>
    public bool GrantsAccess => Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    public static Subscription Start(
        Guid tenantId,
        Guid planId,
        DateOnly startDate,
        DateOnly? trialEndsOn,
        int? seatLimit,
        string? billingReference)
    {
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(planId);

        return new Subscription
        {
            Id = SequentialGuid.Create(),
            TenantId = tenantId,
            PlanId = planId,
            StartDate = startDate,
            TrialEndsOn = trialEndsOn,
            SeatLimit = seatLimit,
            BillingReference = billingReference,
            Status = trialEndsOn is null ? SubscriptionStatus.Active : SubscriptionStatus.Trial,
        };
    }

    /// <summary>Moves to a different plan, keeping the same subscription and billing history.</summary>
    public void ChangePlan(Guid planId, int? seatLimit)
    {
        Guard.NotEmpty(planId);
        Guard.Against(
            Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired,
            "SUBSCRIPTION_CLOSED",
            $"A {Status} subscription cannot change plan; start a new one.");

        PlanId = planId;
        SeatLimit = seatLimit;
    }

    public void ConvertTrialToPaid(string? billingReference)
    {
        Guard.Against(
            Status != SubscriptionStatus.Trial,
            "SUBSCRIPTION_NOT_TRIAL",
            $"Only a trial can be converted; this subscription is {Status}.");

        Status = SubscriptionStatus.Active;
        TrialEndsOn = null;
        BillingReference = billingReference ?? BillingReference;
    }

    public void MarkPastDue()
    {
        Guard.Against(
            Status != SubscriptionStatus.Active,
            "SUBSCRIPTION_NOT_ACTIVE",
            $"Only an active subscription can fall past due; this one is {Status}.");

        Status = SubscriptionStatus.PastDue;
    }

    public void MarkPaid()
    {
        Guard.Against(
            Status != SubscriptionStatus.PastDue,
            "SUBSCRIPTION_NOT_PAST_DUE",
            $"Only a past-due subscription can be marked paid; this one is {Status}.");

        Status = SubscriptionStatus.Active;
    }

    public void Cancel(DateOnly effectiveDate)
    {
        Guard.Against(
            Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired,
            "SUBSCRIPTION_CLOSED",
            "The subscription is already closed.");

        Status = SubscriptionStatus.Cancelled;
        EndDate = effectiveDate;
    }

    /// <summary>
    /// Switches a feature on or off for this tenant regardless of plan: a pilot of an unreleased
    /// module, or a module withdrawn after an incident. Expiring overrides keep pilots from
    /// quietly becoming permanent entitlements nobody is billing for.
    /// </summary>
    public void SetFeatureOverride(string featureCode, bool isEnabled, DateOnly? expiresOn, string reason)
    {
        string normalized = Guard.NotNullOrWhiteSpace(featureCode).Trim();
        Guard.NotNullOrWhiteSpace(reason);

        _featureOverrides.RemoveAll(existing =>
            string.Equals(existing.FeatureCode, normalized, StringComparison.OrdinalIgnoreCase));

        _featureOverrides.Add(new FeatureOverride(normalized, isEnabled, expiresOn, Guard.MaxLength(reason.Trim(), 500)));
    }

    public void ClearFeatureOverride(string featureCode)
        => _featureOverrides.RemoveAll(existing =>
            string.Equals(existing.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The features the tenant may use: the plan's list, with overrides applied and expired ones
    /// ignored.
    /// </summary>
    public IReadOnlySet<string> ResolveFeatures(Plan plan, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var features = new HashSet<string>(plan.FeatureCodes, StringComparer.OrdinalIgnoreCase);

        foreach (FeatureOverride featureOverride in _featureOverrides)
        {
            if (featureOverride.ExpiresOn is not null && featureOverride.ExpiresOn < today)
            {
                continue;
            }

            if (featureOverride.IsEnabled)
            {
                features.Add(featureOverride.FeatureCode);
            }
            else
            {
                features.Remove(featureOverride.FeatureCode);
            }
        }

        return features;
    }
}

/// <summary>A per-tenant exception to what the plan includes.</summary>
public sealed record FeatureOverride(string FeatureCode, bool IsEnabled, DateOnly? ExpiresOn, string Reason);
