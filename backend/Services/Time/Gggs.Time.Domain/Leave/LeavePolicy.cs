using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Leave;

public enum AccrualFrequency
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,

    /// <summary>The whole year's entitlement is posted once, on the employee's date of joining -
    /// no recurring accrual job touches it again for that period.</summary>
    OnJoining = 4,
}

/// <summary>
/// A named policy an employee can be assigned to - "Standard India Policy". The rules that decide
/// how each leave type behaves under it live in <see cref="LeavePolicyRule"/>; the policy itself is
/// just the grouping and the version number.
/// </summary>
public sealed class LeavePolicy : TenantAggregateRoot
{
    private LeavePolicy()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public int Version { get; private set; }

    public static LeavePolicy Create(string name) => new()
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150),
        Version = 1,
        IsActive = true,
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150);
}

/// <summary>
/// How one <see cref="LeaveType"/> behaves under one <see cref="LeavePolicy"/>: how much accrues,
/// how often, whether unused days carry forward, and the eligibility/request-shape limits
/// <see cref="LeaveRequest"/> is checked against.
/// </summary>
public sealed class LeavePolicyRule : TenantAggregateRoot
{
    private LeavePolicyRule()
    {
    }

    public Guid PolicyId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public AccrualFrequency AccrualFrequency { get; private set; }

    /// <summary>Days credited per accrual event - per month for <see cref="Leave.AccrualFrequency.Monthly"/>,
    /// the whole year's worth for <see cref="Leave.AccrualFrequency.OnJoining"/>.</summary>
    public decimal AccrualAmount { get; private set; }

    /// <summary>Pro-rate the first period's accrual by how much of it the employee actually
    /// worked, rather than crediting the full amount on a partial period.</summary>
    public bool ProRataOnJoining { get; private set; }

    public decimal? MaxBalance { get; private set; }

    public decimal? CarryForwardMax { get; private set; }

    public int? CarryForwardExpiryMonths { get; private set; }

    public bool EncashmentAllowed { get; private set; }

    /// <summary>Days of continuous employment before this leave type can be applied for at all -
    /// probation-gated leave types (most commonly, the first paid leave a new hire earns).</summary>
    public int EligibleAfterDays { get; private set; }

    public int MinNoticeDays { get; private set; }

    public int? MaxConsecutiveDays { get; private set; }

    public decimal MinDaysPerRequest { get; private set; } = 0.5m;

    public bool AllowHalfDay { get; private set; } = true;

    public bool AllowNegativeBalance { get; private set; }

    public decimal NegativeLimit { get; private set; }

    public static LeavePolicyRule Create(
        Guid policyId,
        Guid leaveTypeId,
        AccrualFrequency accrualFrequency,
        decimal accrualAmount,
        bool proRataOnJoining,
        decimal? maxBalance,
        decimal? carryForwardMax,
        int? carryForwardExpiryMonths,
        bool encashmentAllowed,
        int eligibleAfterDays,
        int minNoticeDays,
        int? maxConsecutiveDays,
        decimal minDaysPerRequest,
        bool allowHalfDay,
        bool allowNegativeBalance,
        decimal negativeLimit)
    {
        Guard.Against(accrualAmount < 0, "LEAVE_POLICY_RULE_NEGATIVE_ACCRUAL", "The accrual amount cannot be negative.");
        Guard.Against(minDaysPerRequest <= 0, "LEAVE_POLICY_RULE_INVALID_MIN_DAYS", "The minimum days per request must be greater than zero.");
        Guard.Against(
            carryForwardMax is { } cf && maxBalance is { } mb && cf > mb,
            "LEAVE_POLICY_RULE_CARRY_FORWARD_EXCEEDS_MAX",
            "The carry-forward maximum cannot exceed the balance cap.");

        return new LeavePolicyRule
        {
            PolicyId = Guard.NotEmpty(policyId),
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            AccrualFrequency = accrualFrequency,
            AccrualAmount = accrualAmount,
            ProRataOnJoining = proRataOnJoining,
            MaxBalance = maxBalance,
            CarryForwardMax = carryForwardMax,
            CarryForwardExpiryMonths = carryForwardExpiryMonths,
            EncashmentAllowed = encashmentAllowed,
            EligibleAfterDays = eligibleAfterDays,
            MinNoticeDays = minNoticeDays,
            MaxConsecutiveDays = maxConsecutiveDays,
            MinDaysPerRequest = minDaysPerRequest,
            AllowHalfDay = allowHalfDay,
            AllowNegativeBalance = allowNegativeBalance,
            NegativeLimit = negativeLimit,
            IsActive = true,
        };
    }
}
