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

    public void Add(EmployeeSalary salary) => dbContext.EmployeeSalaries.Add(salary);
}

public sealed class EmployeeSalaryComponentRepository(PayrollDbContext dbContext) : IEmployeeSalaryComponentRepository
{
    public async Task<IReadOnlyList<EmployeeSalaryComponent>> ListForSalaryAsync(
        Guid employeeSalaryId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSalaryComponents
            .Where(x => x.EmployeeSalaryId == employeeSalaryId)
            .ToListAsync(cancellationToken);

    public void Add(EmployeeSalaryComponent component) => dbContext.EmployeeSalaryComponents.Add(component);
}

public sealed class EmployeeSnapshotRepository(PayrollDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
