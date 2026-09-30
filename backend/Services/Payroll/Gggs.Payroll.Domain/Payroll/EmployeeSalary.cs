using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

/// <summary>
/// One employee's current salary assignment: a structure plus an annual CTC, from which
/// <see cref="EmployeeSalaryComponent"/> rows are computed. No effective-dating/history in this v1
/// slice (architecture.md's <c>EmployeeSalary</c> is a system-versioned temporal table with
/// <c>EffectiveFrom</c>/<c>EffectiveTo</c>/<c>IsCurrent</c>) - one current record per employee,
/// replaced wholesale on a revision rather than superseded, matching Onboarding v1's own
/// simplification of dropping history it didn't yet need. A real revision history with
/// effective-dating is a later slice, once payroll runs exist to actually need "what was this
/// employee's pay as of a past period."
/// </summary>
public sealed class EmployeeSalary : TenantAggregateRoot
{
    private EmployeeSalary()
    {
        Currency = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid StructureId { get; private set; }

    public decimal AnnualCtc { get; private set; }

    public string Currency { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public static EmployeeSalary Create(Guid employeeId, Guid structureId, decimal annualCtc, string currency, DateOnly effectiveFrom)
    {
        Guard.Against(annualCtc <= 0, "EMPLOYEE_SALARY_CTC_MUST_BE_POSITIVE", "The annual CTC must be greater than zero.");

        return new EmployeeSalary
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            StructureId = Guard.NotEmpty(structureId),
            AnnualCtc = annualCtc,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            EffectiveFrom = effectiveFrom,
            IsActive = true,
        };
    }
}

/// <summary>
/// The computed monthly/annual amount for one component of an <see cref="EmployeeSalary"/> - the
/// breakdown a payslip would eventually read from, recomputed and replaced wholesale whenever the
/// employee's salary is reassigned rather than adjusted in place.
/// </summary>
public sealed class EmployeeSalaryComponent : TenantAggregateRoot
{
    private EmployeeSalaryComponent()
    {
    }

    public Guid EmployeeSalaryId { get; private set; }

    public Guid ComponentId { get; private set; }

    public decimal MonthlyAmount { get; private set; }

    public decimal AnnualAmount { get; private set; }

    public static EmployeeSalaryComponent Create(Guid employeeSalaryId, Guid componentId, decimal monthlyAmount, decimal annualAmount) => new()
    {
        EmployeeSalaryId = Guard.NotEmpty(employeeSalaryId),
        ComponentId = Guard.NotEmpty(componentId),
        MonthlyAmount = monthlyAmount,
        AnnualAmount = annualAmount,
        IsActive = true,
    };
}
