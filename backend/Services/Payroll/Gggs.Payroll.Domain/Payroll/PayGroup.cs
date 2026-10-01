using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

public enum PayFrequency
{
    Monthly = 1,
    SemiMonthly = 2,
    BiWeekly = 3,
    Weekly = 4,
}

/// <summary>
/// A group of employees paid together on the same cadence - "Monthly Payroll - India." No
/// <c>LegalEntityId</c>/<c>CompliancePackCode</c> targeting in this v1 slice (architecture.md's
/// <c>PayGroup</c>): one tenant-wide catalog of pay groups, matching <see cref="SalaryStructure"/>'s
/// own simplification. <see cref="PayPeriod"/>s are opened against a pay group; a
/// <see cref="PayrollRun"/> is always for one pay group's one pay period.
/// </summary>
public sealed class PayGroup : TenantAggregateRoot
{
    private PayGroup()
    {
        Code = null!;
        Name = null!;
        Currency = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public PayFrequency Frequency { get; private set; }

    public string Currency { get; private set; }

    public static PayGroup Create(string code, string name, PayFrequency frequency, string currency) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        Frequency = frequency,
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
        IsActive = true,
    };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
