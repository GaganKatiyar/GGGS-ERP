using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Leave;

public enum LeaveUnit
{
    Day = 1,
    HalfDay = 2,
    Hour = 3,
}

/// <summary>
/// A category of leave a tenant offers - "Casual", "Sick", "Maternity". The policy that says how
/// much of it accrues, and by when it has to be used, is a separate aggregate
/// (<see cref="LeavePolicyRule"/>): a leave type can exist (and appear in the leave-apply form)
/// before anyone has configured accrual for it, the same way <c>cfg.LookupType</c>/<c>LookupValue</c>
/// separates a picklist's existence from what populates it.
/// </summary>
public sealed class LeaveType : TenantAggregateRoot
{
    private LeaveType()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsPaid { get; private set; }

    public LeaveUnit Unit { get; private set; }

    /// <summary>Restricted to display for one gender, or unrestricted - "Maternity" is the
    /// motivating case. Not an enforcement mechanism on its own: the apply form filters by it, and
    /// nothing in this service reads Core HR's own gender field to double-check, since Core HR's
    /// gender field is itself a free-text tenant lookup, not a fixed enum this service could match
    /// against.</summary>
    public string? GenderRestrictionCode { get; private set; }

    /// <summary>"Comp Off" - earned by working a day off, not accrued on a schedule. Changes how
    /// <c>LeaveLedgerService</c> posts credits for this type: manually, per grant, rather than by
    /// <see cref="LeavePolicyRule.AccrualFrequency"/>.</summary>
    public bool IsCompOff { get; private set; }

    /// <summary>Loss of Pay - not really "leave" in the balance sense (no balance to track), but
    /// modelled as a <see cref="LeaveType"/> so it can still go through a <see cref="LeaveRequest"/>
    /// and the same approval workflow.</summary>
    public bool IsLop { get; private set; }

    public string ColorHex { get; private set; } = "#6b7280";

    public static LeaveType Create(
        string code, string name, bool isPaid, LeaveUnit unit, string? genderRestrictionCode, bool isCompOff, bool isLop, string colorHex)
        => new()
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            IsPaid = isPaid,
            Unit = unit,
            GenderRestrictionCode = string.IsNullOrWhiteSpace(genderRestrictionCode) ? null : genderRestrictionCode.Trim(),
            IsCompOff = isCompOff,
            IsLop = isLop,
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#6b7280" : Guard.MaxLength(colorHex.Trim(), 7),
            IsActive = true,
        };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
}
