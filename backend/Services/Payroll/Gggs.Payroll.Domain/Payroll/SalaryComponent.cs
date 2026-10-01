using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

public enum SalaryComponentKind
{
    Earning = 1,
    Deduction = 2,
}

/// <summary>
/// How a component's amount is worked out. No formula DSL or GL account code in this v1 slice
/// (architecture.md's <c>SalaryComponent.Formula</c>) - a component is a fixed monthly amount, a
/// plain percentage of the structure's Basic component, one of two statutory percentage shapes that
/// both read a wage ceiling but differ in what happens once Basic exceeds it, or a flat slab lookup:
/// <list type="bullet">
/// <item><see cref="PercentOfBasicCapped"/> (Provident Fund) - the <em>base</em> is capped, so the
/// contribution still applies, just computed on the ceiling instead of the full Basic.</item>
/// <item><see cref="PercentOfBasicWithEligibilityCap"/> (ESI) - the ceiling is an
/// <em>eligibility</em> threshold: at or under it the percentage applies to the actual Basic in
/// full, and above it the component is not deducted at all (not even on the ceiling amount).</item>
/// <item><see cref="FlatSlab"/> (Professional Tax) - not a percentage at all: the component's own
/// <see cref="SalaryComponentSlab"/> rows say a flat amount for every wage threshold Basic meets or
/// exceeds, and the highest matching threshold wins. PT is genuinely a per-state slab table in the
/// real world (architecture.md's compliance-pack model), and this does not attempt that - a tenant
/// with employees in several states defines one "PT - Karnataka" component and one "PT -
/// Maharashtra" component, each with its own slabs, and assigns the right one per structure. There
/// is no state field, no half-yearly timing and no LegalEntity targeting here.</item>
/// </list>
/// All three statutory shapes are deliberately narrow: none hardcodes a real country's numbers in
/// code - the percentage/ceiling/slabs are configured per component or line, the same way a plain
/// <see cref="PercentOfBasic"/> line's percentage already is. A full compliance-pack model
/// (versioned, effective-dated, per-country rule tables) is still a later slice; this only buys the
/// shapes PF, ESI and PT need, matching every other v1 module's established simplification
/// (Performance v1's flat cycle instead of a rating-scale/template model).
/// </summary>
public enum SalaryComponentCalcType
{
    Fixed = 1,
    PercentOfBasic = 2,
    PercentOfBasicCapped = 3,
    PercentOfBasicWithEligibilityCap = 4,
    FlatSlab = 5,
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

/// <summary>
/// One threshold of a <see cref="SalaryComponentCalcType.FlatSlab"/> component's slab table - "at or
/// above Rs 15,000 of Basic, this component is Rs 200/month." <see cref="SalaryStructureService"/>
/// resolves the amount by taking the highest-threshold slab the employee's Basic meets or exceeds; a
/// component with no slab at or below Basic (every threshold is higher) resolves to zero, matching
/// how real PT tables have a zero-rate band below their first paid threshold. Belongs to the
/// component itself, not a structure's line, because the slab table is what makes "PT - Karnataka"
/// and "PT - Maharashtra" two different catalog entries in the first place - see
/// <see cref="SalaryComponentCalcType"/>'s own remarks.
/// </summary>
public sealed class SalaryComponentSlab : TenantAggregateRoot
{
    private SalaryComponentSlab()
    {
    }

    public Guid ComponentId { get; private set; }

    public decimal ThresholdAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public static SalaryComponentSlab Create(Guid componentId, decimal thresholdAmount, decimal taxAmount)
    {
        Guard.Against(thresholdAmount < 0, "SALARY_COMPONENT_SLAB_THRESHOLD_NEGATIVE", "The threshold cannot be negative.");
        Guard.Against(taxAmount < 0, "SALARY_COMPONENT_SLAB_AMOUNT_NEGATIVE", "The slab amount cannot be negative.");

        return new SalaryComponentSlab
        {
            ComponentId = Guard.NotEmpty(componentId),
            ThresholdAmount = thresholdAmount,
            TaxAmount = taxAmount,
            IsActive = true,
        };
    }
}
