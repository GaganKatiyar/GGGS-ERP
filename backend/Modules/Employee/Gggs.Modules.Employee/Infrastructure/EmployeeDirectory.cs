using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>The employee directory other modules read through <see cref="IEmployeeDirectory"/>.</summary>
internal sealed class EmployeeDirectory(AppDbContext dbContext) : IEmployeeDirectory
{
    public async Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EmployeeSummary> found = await GetManyAsync([employeeId], cancellationToken);
        return found.Count > 0 ? found[0] : null;
    }

    public async Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];

        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => ids.Contains(employee.Id))
            .Select(employee => new EmployeeSummary(
                employee.Id, employee.EmployeeCode, employee.DisplayName, employee.OfficialEmail, employee.Status.ToString(),
                employee.DepartmentId, employee.PositionId, employee.JobId, employee.ManagerEmployeeId, employee.LocationId, employee.LegalEntityId))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.Employees.AnyAsync(employee => employee.Id == employeeId && employee.Status != EmployeeStatus.Exited, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByPositionAsync(IEnumerable<Guid> positionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionIds);
        List<Guid?> ids = [.. positionIds.Distinct().Select(id => (Guid?)id)];

        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => ids.Contains(employee.PositionId) && employee.Status != EmployeeStatus.Exited)
            .GroupBy(employee => employee.PositionId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByDepartmentAsync(IEnumerable<Guid> departmentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(departmentIds);
        List<Guid?> ids = [.. departmentIds.Distinct().Select(id => (Guid?)id)];

        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => ids.Contains(employee.DepartmentId) && employee.Status != EmployeeStatus.Exited)
            .GroupBy(employee => employee.DepartmentId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
    }
}
