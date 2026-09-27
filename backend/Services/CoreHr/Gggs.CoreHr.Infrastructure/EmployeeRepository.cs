using Gggs.CoreHr.Application.Employees;
using Gggs.CoreHr.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Infrastructure;

/// <summary>Employee and employment history persistence, scoped to the current tenant by the query filter.</summary>
public sealed class EmployeeRepository(CoreHrDbContext dbContext) : IEmployeeRepository
{
    public async Task<Employee?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> EmployeeCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => await dbContext.Employees.AnyAsync(x => x.EmployeeCode == normalizedCode, cancellationToken);

    public void Add(Employee employee) => dbContext.Employees.Add(employee);

    public async Task<EmploymentRecord?> GetCurrentEmploymentRecordAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmploymentRecords.FirstOrDefaultAsync(
            x => x.EmployeeId == employeeId && x.IsCurrent,
            cancellationToken);

    public async Task<IReadOnlyList<EmploymentRecord>> GetEmploymentHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmploymentRecords
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public void Add(EmploymentRecord record) => dbContext.EmploymentRecords.Add(record);
}
