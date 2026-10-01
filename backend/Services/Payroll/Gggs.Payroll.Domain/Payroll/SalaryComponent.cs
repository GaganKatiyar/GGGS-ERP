using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

public enum SalaryComponentKind
{
    Earning = 1,
    Deduction = 2,
}

/// <summary>
/// How a component's amount is worked out. No formula DSL, slab lookup or GL account code in this
/// v1 slice (architecture.md's <c>SalaryComponent.Formula</c>) - a component is a fixed monthly
/// amount, a plain percentage of the structure's Basic component, or (for the first statutory rule,
/// Provident Fund) a percentage of Basic capped at a wage ceiling, matching every other v1 module's
/// established simplification (Performance v1's flat cycle instead of a rating-scale/template
/// model). <see cref="PercentOfBasicCapped"/> is deliberately narrow: it hardcodes none of PF's
/// actual numbers (12%, Rs 15,000) in code - both the percentage and the ceiling are configured per
/// <c>SalaryStructureComponent</c> line, the same way a plain <see cref="PercentOfBasic"/> line's
/// percentage already is. A full compliance-pack model (versioned, effective-dated, per-country
/// rule tables) is still a later slice; this only buys the one shape PF/ESI both need.
/// </summary>
public enum SalaryComponentCalcType
{
    Fixed = 1,
    PercentOfBasic = 2,
    PercentOfBasicCapped = 3,
}

/// <summary>
/// A named line a salary structure can be built from - "Basic," "HRA," "Provident Fund." Reused
/// across structures rather than defined inline per structure, so renaming or retiring one component
/// is a single change instead of an edit to every structure that uses it.
/// </summary>
public sealed class SalaryComponent : TenantAggregateRoot
{
    private SalaryComponent()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public SalaryComponentKind Kind { get; private set; }

    public SalaryComponentCalcType CalcType { get; private set; }

    public bool IsTaxable { get; private set; }

    public int DisplayOrder { get; private set; }

    public static SalaryComponent Create(
        string code, string name, SalaryComponentKind kind, SalaryComponentCalcType calcType, bool isTaxable, int displayOrder) => new()
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            Kind = kind,
            CalcType = calcType,
            IsTaxable = isTaxable,
            DisplayOrder = displayOrder,
            IsActive = true,
        };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
