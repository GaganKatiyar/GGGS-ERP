using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Platform.Domain.Subscriptions;

/// <summary>Editions from the product overview.</summary>
public enum PlanEdition
{
    Core = 1,
    Professional = 2,
    Enterprise = 3,
}

/// <summary>
/// A sellable plan and the features it includes.
/// <para>
/// Platform-owned: plans are our catalogue, identical for every customer. What varies per tenant is
/// which plan they hold and any agreed exceptions, both of which live on the subscription.
/// </para>
/// </summary>
public sealed class Plan : AggregateRoot, IPlatformOwned, IAuditable, IActivatable
{
    private readonly List<string> _featureCodes = [];

    private Plan()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public PlanEdition Edition { get; private set; }

    /// <summary>
    /// Employee ceiling, or null for unlimited. Enforced as a warning rather than a hard block:
    /// refusing to onboard employee 501 on a Friday afternoon damages the customer, not us.
    /// </summary>
    public int? MaxEmployees { get; private set; }

    /// <summary>Whether a customer can self-serve onto this plan, or it is negotiated.</summary>
    public bool IsPubliclyAvailable { get; private set; }

    public bool IsActive { get; private set; } = true;

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    /// <summary>Feature codes this plan includes, e.g. <c>Payroll</c>, <c>Recruitment</c>.</summary>
    public IReadOnlyCollection<string> FeatureCodes => _featureCodes;

    public static Plan Create(
        string code,
        string name,
        PlanEdition edition,
        int? maxEmployees,
        bool isPubliclyAvailable,
        IEnumerable<string> featureCodes)
    {
        var plan = new Plan
        {
            Id = SequentialGuid.Create(),
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            Edition = edition,
            MaxEmployees = maxEmployees,
            IsPubliclyAvailable = isPubliclyAvailable,
        };

        foreach (string featureCode in featureCodes ?? [])
        {
            plan.AddFeature(featureCode);
        }

        return plan;
    }

    public void AddFeature(string featureCode)
    {
        string normalized = Guard.NotNullOrWhiteSpace(featureCode).Trim();

        if (!_featureCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _featureCodes.Add(normalized);
        }
    }

    public void RemoveFeature(string featureCode)
        => _featureCodes.RemoveAll(code => string.Equals(code, featureCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Retires a plan from sale. Existing subscriptions keep working: withdrawing a plan must never
    /// cut off the customers already on it.
    /// </summary>
    public void Retire() => IsActive = false;
}
