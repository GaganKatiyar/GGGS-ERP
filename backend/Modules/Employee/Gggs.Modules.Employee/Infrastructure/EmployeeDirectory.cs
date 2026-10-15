using Gggs.Data;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>The employee directory other modules read through <see cref="IEmployeeDirectory"/>.</summary>
internal sealed class EmployeeDirectory(AppDbContext dbContext, EmployeeScope scope) : IEmployeeDirectory
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
                employee.DepartmentId, employee.PositionId, employee.JobId, employee.ManagerEmployeeId, employee.LocationId, employee.LegalEntityId,
                employee.HrbpEmployeeId, employee.Designation, employee.JoiningDate,
                employee.EmploymentTypeCode, employee.ProbationEndDate, employee.ConfirmationDate))
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

    public async Task<IReadOnlyList<Guid>> ListActiveIdsAsync(
        IReadOnlyCollection<Guid>? departmentIds,
        IReadOnlyCollection<Guid>? locationIds,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.Employee> query = dbContext.Employees.AsNoTracking().Where(employee => employee.Status != EmployeeStatus.Exited);

        bool byDepartment = departmentIds is { Count: > 0 };
        bool byLocation = locationIds is { Count: > 0 };

        if (byDepartment || byLocation)
        {
            List<Guid?> departments = byDepartment ? [.. departmentIds!.Select(id => (Guid?)id)] : [];
            List<Guid?> locations = byLocation ? [.. locationIds!.Select(id => (Guid?)id)] : [];
            query = query.Where(employee => departments.Contains(employee.DepartmentId) || locations.Contains(employee.LocationId));
        }

        return await query.Select(employee => employee.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> IsInScopeAsync(Guid employeeId, string permission, CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.Employee> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), permission, cancellationToken);
        return await visible.AnyAsync(employee => employee.Id == employeeId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>?> InScopeIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (await scope.ReachesEveryoneAsync(permission, cancellationToken))
        {
            return null;
        }

        IQueryable<Domain.Employee> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), permission, cancellationToken);
        return await visible.Select(employee => employee.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveCodesAsync(IEnumerable<string> employeeCodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeCodes);
        List<string> codes = [.. employeeCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToUpperInvariant()).Distinct()];
        if (codes.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        var found = await dbContext.Employees.AsNoTracking()
            .Where(employee => codes.Contains(employee.EmployeeCode))
            .Select(employee => new { employee.EmployeeCode, employee.Id })
            .ToListAsync(cancellationToken);

        return found.ToDictionary(row => row.EmployeeCode, row => row.Id, StringComparer.OrdinalIgnoreCase);
    }
}
