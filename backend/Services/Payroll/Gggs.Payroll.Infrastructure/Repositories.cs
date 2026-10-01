using Gggs.Payroll.Application.Payroll;
using Gggs.Payroll.Application.Snapshots;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Payroll.Infrastructure;

public sealed class SalaryComponentRepository(PayrollDbContext dbContext) : ISalaryComponentRepository
{
    public async Task<SalaryComponent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.SalaryComponents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalaryComponent>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => await dbContext.SalaryComponents.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.SalaryComponents.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(SalaryComponent component) => dbContext.SalaryComponents.Add(component);
}

public sealed class SalaryStructureRepository(PayrollDbContext dbContext) : ISalaryStructureRepository
{
    public async Task<SalaryStructure?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.SalaryStructures.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.SalaryStructures.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(SalaryStructure salaryStructure) => dbContext.SalaryStructures.Add(salaryStructure);
}

public sealed class SalaryStructureComponentRepository(PayrollDbContext dbContext) : ISalaryStructureComponentRepository
{
    public async Task<IReadOnlyList<SalaryStructureComponent>> ListForStructureAsync(
        Guid structureId, CancellationToken cancellationToken = default)
        => await dbContext.SalaryStructureComponents.Where(x => x.StructureId == structureId).ToListAsync(cancellationToken);

    public void Add(SalaryStructureComponent structureComponent) => dbContext.SalaryStructureComponents.Add(structureComponent);
}

public sealed class EmployeeSalaryRepository(PayrollDbContext dbContext) : IEmployeeSalaryRepository
{
    public async Task<EmployeeSalary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<EmployeeSalary?> GetCurrentForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaries
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeSalary>> ListAllCurrentAsync(CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaries
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedDate).First())
            .ToListAsync(cancellationToken);

    public void Add(EmployeeSalary salary) => dbContext.EmployeeSalaries.Add(salary);
}

public sealed class EmployeeSalaryComponentRepository(PayrollDbContext dbContext) : IEmployeeSalaryComponentRepository
{
    public async Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalaryAsync(
        Guid employeeSalaryId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaryComponents
            .Where(x => x.EmployeeSalaryId == employeeSalaryId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalariesAsync(
        IEnumerable<Guid> employeeSalaryIds, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaryComponents
            .Where(x => employeeSalaryIds.Contains(x.EmployeeSalaryId))
            .ToListAsync(cancellationToken);

    public void Add(EmployeeSalaryComponent component) => dbContext.EmployeeSalaryComponents.Add(component);
}

public sealed class PayGroupRepository(PayrollDbContext dbContext) : IPayGroupRepository
{
    public async Task<PayGroup?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.PayGroups.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await dbContext.PayGroups.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public void Add(PayGroup payGroup) => dbContext.PayGroups.Add(payGroup);
}

public sealed class PayPeriodRepository(PayrollDbContext dbContext) : IPayPeriodRepository
{
    public async Task<PayPeriod?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.PayPeriods.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> PeriodCodeExistsAsync(Guid payGroupId, string periodCode, CancellationToken cancellationToken = default)
    {
        string normalized = periodCode.Trim();
        return await dbContext.PayPeriods.AnyAsync(
            x => x.PayGroupId == payGroupId && x.PeriodCode == normalized, cancellationToken);
    }

    public void Add(PayPeriod period) => dbContext.PayPeriods.Add(period);
}

public sealed class PayrollRunRepository(PayrollDbContext dbContext) : IPayrollRunRepository
{
    public async Task<PayrollRun?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> RunNoExistsAsync(string runNo, CancellationToken cancellationToken = default)
    {
        string normalized = runNo.Trim();
        return await dbContext.PayrollRuns.AnyAsync(x => x.RunNo == normalized, cancellationToken);
    }

    public void Add(PayrollRun run) => dbContext.PayrollRuns.Add(run);
}

public sealed class PayrollRunEmployeeRepository(PayrollDbContext dbContext) : IPayrollRunEmployeeRepository
{
    public async Task<IReadOnlyList<PayrollRunEmployee>> ListForRunAsync(Guid payrollRunId, CancellationToken cancellationToken = default)
        => await dbContext.PayrollRunEmployees.Where(x => x.PayrollRunId == payrollRunId).ToListAsync(cancellationToken);

    public void Add(PayrollRunEmployee runEmployee) => dbContext.PayrollRunEmployees.Add(runEmployee);

    public void RemoveAllForRun(IEnumerable<PayrollRunEmployee> runEmployees) => dbContext.PayrollRunEmployees.RemoveRange(runEmployees);
}

public sealed class PayrollRunLineRepository(PayrollDbContext dbContext) : IPayrollRunLineRepository
{
    public async Task<IReadOnlyList<PayrollRunLine>> ListForRunEmployeesAsync(
        IEnumerable<Guid> payrollRunEmployeeIds, CancellationToken cancellationToken = default)
        => await dbContext.PayrollRunLines
            .Where(x => payrollRunEmployeeIds.Contains(x.PayrollRunEmployeeId))
            .ToListAsync(cancellationToken);

    public void Add(PayrollRunLine line) => dbContext.PayrollRunLines.Add(line);

    public void RemoveAllForRunEmployees(IEnumerable<PayrollRunLine> lines) => dbContext.PayrollRunLines.RemoveRange(lines);
}

public sealed class EmployeeSnapshotRepository(PayrollDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
