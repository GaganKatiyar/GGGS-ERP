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
    /// Computes each component's monthly amount from the structure's own lines: a Fixed line's
    /// value is the monthly amount directly; a PercentOfBasic line's value is a percentage applied
    /// to the structure's Basic monthly amount; a PercentOfBasicCapped line (PF) applies that same
    /// percentage to Basic capped at the line's own wage ceiling first. The Basic line is resolved
    /// first, in a separate pass, since every percentage line's amount depends on it - see
    /// <see cref="SalaryStructureService.AddComponentAsync"/>'s own remarks on why a structure is
    /// guaranteed to have exactly one Basic line before it can reach this calculation.
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

            decimal monthlyAmount = component.Code == BasicComponentCode
                ? basicMonthly
                : component.CalcType == SalaryComponentCalcType.PercentOfBasic
                    ? Math.Round(basicMonthly * line.Value / 100m, 2)
                    : component.CalcType == SalaryComponentCalcType.PercentOfBasicCapped
                        ? Math.Round(Math.Min(basicMonthly, line.WageCeiling ?? basicMonthly) * line.Value / 100m, 2)
                        : line.Value;

            salaryComponents.Add(EmployeeSalaryComponent.Create(salary.Id, component.Id, monthlyAmount, monthlyAmount * 12m));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return salary;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
