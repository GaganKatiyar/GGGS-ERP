using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

/// <summary>
/// A named template an employee's salary is assigned from - "Standard CTC Structure." No
/// <c>LegalEntityId</c>/<c>CTCBasis</c> targeting in this v1 slice: one tenant-wide catalog of
/// structures, matching Leave v1's single tenant-wide policy and every other v1 module's own
/// simplification ahead of a real multi-target configuration model nothing yet needs.
/// </summary>
public sealed class SalaryStructure : TenantAggregateRoot
{
    private SalaryStructure()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public static SalaryStructure Create(string code, string name) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        IsActive = true,
    };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

/// <summary>
/// One line of a <see cref="SalaryStructure"/>: which <see cref="SalaryComponent"/>, and how much of
/// it - a fixed monthly amount when the component's <see cref="SalaryComponent.CalcType"/> is
/// <see cref="SalaryComponentCalcType.Fixed"/>, or a percentage of the structure's own Basic
/// component when it is <see cref="SalaryComponentCalcType.PercentOfBasic"/>. Exactly one Fixed line
/// per structure must be the "BASIC" component - <see cref="SalaryStructureService"/> enforces this
/// at assignment time, not here, since checking it needs the component catalog alongside this row.
/// </summary>
public sealed class SalaryStructureComponent : TenantAggregateRoot
{
    private SalaryStructureComponent()
    {
    }

    public Guid StructureId { get; private set; }

    public Guid ComponentId { get; private set; }

    /// <summary>A fixed monthly amount when the component is Fixed, or a percentage (0-100) when
    /// the component is PercentOfBasic - which one is decided entirely by the component's own
    /// <see cref="SalaryComponent.CalcType"/>, so this row carries no type tag of its own.</summary>
    public decimal Value { get; private set; }

    public int DisplayOrder { get; private set; }

    public static SalaryStructureComponent Create(Guid structureId, Guid componentId, decimal value, int displayOrder)
    {
        Guard.Against(value < 0, "SALARY_STRUCTURE_COMPONENT_VALUE_NEGATIVE", "The value cannot be negative.");

        return new SalaryStructureComponent
        {
            StructureId = Guard.NotEmpty(structureId),
            ComponentId = Guard.NotEmpty(componentId),
            Value = value,
            DisplayOrder = displayOrder,
            IsActive = true,
        };
    }
}
