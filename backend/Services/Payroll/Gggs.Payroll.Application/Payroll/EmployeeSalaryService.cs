using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Application.Snapshots;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Domain.Snapshots;

namespace Gggs.Payroll.Application.Payroll;

/// <summary>
/// Assigning a structure and an annual CTC to an employee, and computing each component's monthly
/// and annual amount from it. No revision history/effective-dating in this v1 slice - see
/// <see cref="EmployeeSalary"/>'s own remarks; assigning again simply adds a new current record.
/// </summary>
public sealed class EmployeeSalaryService(
    IEmployeeSalaryRepository salaries,
    IEmployeeSalaryComponentRepository salaryComponents,
    ISalaryStructureRepository structures,
    ISalaryStructureComponentRepository structureComponents,
    ISalaryComponentRepository components,
    IEmployeeSnapshotRepository snapshots,
    IUnitOfWork unitOfWork)
{
    private const string BasicComponentCode = "BASIC";

    /// <summary>
    /// Computes each component's monthly amount from the structure's own lines - see
    /// <see cref="ComputeMonthlyAmount"/> for how each <see cref="SalaryComponentCalcType"/> reads
    /// its line. The Basic line is resolved first, in a separate pass, since every percentage line's
    /// amount depends on it - see <see cref="SalaryStructureService.AddComponentAsync"/>'s own
    /// remarks on why a structure is guaranteed to have exactly one Basic line before it can reach
    /// this calculation.
    /// </summary>
    public async Task<EmployeeSalary> AssignAsync(
        Guid employeeId, Guid structureId, decimal annualCtc, string currency, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        EmployeeSnapshot? employee = await snapshots.GetAsync(employeeId, cancellationToken);
        Guard(employee is not null, "PAYROLL_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "PAYROLL_EMPLOYEE_EXITED", "This employee has already exited.");

        SalaryStructure structure = await structures.GetAsync(structureId, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryStructure), structureId);

        IReadOnlyList<SalaryStructureComponent> lines = await structureComponents.ListForStructureAsync(structureId, cancellationToken);
        Guard(lines.Count > 0, "SALARY_STRUCTURE_HAS_NO_COMPONENTS", $"Structure '{structure.Name}' has no components to assign against.");

        IReadOnlyList<SalaryComponent> lineComponents = await components.GetManyAsync(
            lines.Select(x => x.ComponentId), cancellationToken);
        Dictionary<Guid, SalaryComponent> componentsById = lineComponents.ToDictionary(x => x.Id);

        SalaryStructureComponent basicLine = lines.FirstOrDefault(x => componentsById[x.ComponentId].Code == BasicComponentCode)
            ?? throw new DomainRuleException(
                "SALARY_STRUCTURE_HAS_NO_BASIC", $"Structure '{structure.Name}' has no Basic component configured.");

        decimal basicMonthly = basicLine.Value;

        EmployeeSalary salary = EmployeeSalary.Create(employeeId, structureId, annualCtc, currency, effectiveFrom);
        salaries.Add(salary);

        foreach (SalaryStructureComponent line in lines)
        {
            SalaryComponent component = componentsById[line.ComponentId];
            decimal monthlyAmount = component.Code == BasicComponentCode ? basicMonthly : ComputeMonthlyAmount(component, line, basicMonthly);
            salaryComponents.Add(EmployeeSalaryComponent.Create(salary.Id, component.Id, monthlyAmount, monthlyAmount * 12m));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return salary;
    }

    /// <summary>
    /// A non-Basic line's monthly amount, by <see cref="SalaryComponentCalcType"/>:
    /// <list type="bullet">
    /// <item>Fixed - the line's value directly.</item>
    /// <item>PercentOfBasic - the value as a percentage of the actual Basic monthly amount.</item>
    /// <item>PercentOfBasicCapped (PF) - that same percentage, but against Basic capped at the
    /// line's wage ceiling, so the contribution still applies even above the ceiling, just computed
    /// on a smaller base.</item>
    /// <item>PercentOfBasicWithEligibilityCap (ESI) - the percentage against the actual Basic, but
    /// only when Basic is at or under the wage ceiling; above it, the component is not deducted at
    /// all, matching ESI's real eligibility rule (<see cref="SalaryComponentCalcType"/>'s own
    /// remarks on why this cannot share PF's capped-base math).</item>
    /// </list>
    /// </summary>
    private static decimal ComputeMonthlyAmount(SalaryComponent component, SalaryStructureComponent line, decimal basicMonthly) =>
        component.CalcType switch
        {
            SalaryComponentCalcType.PercentOfBasic => Math.Round(basicMonthly * line.Value / 100m, 2),
            SalaryComponentCalcType.PercentOfBasicCapped =>
                Math.Round(Math.Min(basicMonthly, line.WageCeiling ?? basicMonthly) * line.Value / 100m, 2),
            SalaryComponentCalcType.PercentOfBasicWithEligibilityCap =>
                line.WageCeiling is { } ceiling && basicMonthly > ceiling ? 0m : Math.Round(basicMonthly * line.Value / 100m, 2),
            _ => line.Value,
        };

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
