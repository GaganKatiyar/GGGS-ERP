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
/// amount, a plain percentage of the structure's Basic component, or one of two statutory shapes
/// that both apply a percentage against a wage ceiling but differ in what happens once Basic
/// exceeds it:
/// <list type="bullet">
/// <item><see cref="PercentOfBasicCapped"/> (Provident Fund) - the <em>base</em> is capped, so the
/// contribution still applies, just computed on the ceiling instead of the full Basic.</item>
/// <item><see cref="PercentOfBasicWithEligibilityCap"/> (ESI) - the ceiling is an
/// <em>eligibility</em> threshold: at or under it the percentage applies to the actual Basic in
/// full, and above it the component is not deducted at all (not even on the ceiling amount).</item>
/// </list>
/// Both are deliberately narrow: neither hardcodes PF's or ESI's actual numbers (12%/Rs 15,000,
/// 0.75%/Rs 21,000) in code - the percentage and the ceiling are configured per
/// <c>SalaryStructureComponent</c> line, the same way a plain <see cref="PercentOfBasic"/> line's
/// percentage already is. A full compliance-pack model (versioned, effective-dated, per-country
/// rule tables) is still a later slice; this only buys the two shapes PF and ESI need, matching
/// every other v1 module's established simplification (Performance v1's flat cycle instead of a
/// rating-scale/template model).
/// </summary>
public enum SalaryComponentCalcType
{
    Fixed = 1,
    PercentOfBasic = 2,
    PercentOfBasicCapped = 3,
    PercentOfBasicWithEligibilityCap = 4,
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
