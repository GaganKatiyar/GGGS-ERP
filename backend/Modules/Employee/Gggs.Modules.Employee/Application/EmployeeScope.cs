using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Application;

/// <summary>
/// Narrows employee queries to what the caller's grant reaches (spec §9, architecture §8.4).
/// <para>
/// The permission check at the endpoint says whether the caller may read employees at all; this
/// says which ones. A manager holding <c>employee.profile.view</c> at DirectReports scope gets their
/// team, not the company. Records outside the scope are simply absent, so the API answers 404 for
/// them - the same as for a record that does not exist.
/// </para>
/// </summary>
public sealed class EmployeeScope(AppDbContext dbContext, IPermissionProvider permissions, ICurrentUser currentUser)
{
    /// <summary>Whether the caller's grant of <paramref name="permission"/> is tenant-wide.</summary>
    public async Task<bool> ReachesEveryoneAsync(string permission, CancellationToken cancellationToken = default)
        => (await permissions.GetCurrentAsync(cancellationToken)).ScopeFor(permission) == DataScope.Tenant;

    /// <summary>The employees the caller may see under <paramref name="permission"/>.</summary>
    public async Task<IQueryable<EmployeeEntity>> ApplyAsync(
        IQueryable<EmployeeEntity> query,
        string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);
        DataScope? scope = held.ScopeFor(permission);

        if (scope is null)
        {
            return query.Where(_ => false);
        }

        if (scope == DataScope.Tenant)
        {
            return query;
        }

        Guid? self = currentUser.EmployeeId;
        if (self is null)
        {
            // A narrowed grant is anchored on the caller's own employee record; an account with no
            // employee record (an external administrator, say) has nothing to anchor it on.
            return query.Where(_ => false);
        }

        Guid me = self.Value;

        var anchor = await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == me)
            .Select(employee => new { employee.DepartmentId, employee.LocationId, employee.LegalEntityId, employee.BusinessUnitId })
            .FirstOrDefaultAsync(cancellationToken);

        return scope switch
        {
            DataScope.Own => query.Where(employee => employee.Id == me),
            DataScope.DirectReports => query.Where(employee => employee.Id == me || employee.ManagerEmployeeId == me),
            DataScope.AllReports => await AllReportsAsync(query, me, cancellationToken),
            DataScope.Department => query.Where(employee => employee.Id == me || (anchor != null && employee.DepartmentId == anchor.DepartmentId)),
            DataScope.Branch => query.Where(employee => employee.Id == me || (anchor != null && employee.LocationId == anchor.LocationId)),
            DataScope.LegalEntity => query.Where(employee => employee.Id == me || (anchor != null && employee.LegalEntityId == anchor.LegalEntityId)),
            DataScope.BusinessUnit => query.Where(employee => employee.Id == me || (anchor != null && employee.BusinessUnitId == anchor.BusinessUnitId)),
            _ => query.Where(_ => false),
        };
    }

    /// <summary>The caller and everyone below them in the reporting line, however deep.</summary>
    private async Task<IQueryable<EmployeeEntity>> AllReportsAsync(IQueryable<EmployeeEntity> query, Guid me, CancellationToken cancellationToken)
    {
        List<(Guid Id, Guid? ManagerId)> lines = [.. (await dbContext.Employees
                .AsNoTracking()
                .Where(employee => employee.ManagerEmployeeId != null)
                .Select(employee => new { employee.Id, employee.ManagerEmployeeId })
                .ToListAsync(cancellationToken))
            .Select(line => (line.Id, line.ManagerEmployeeId))];

        ILookup<Guid?, Guid> reportsOf = lines.ToLookup(line => line.ManagerId, line => line.Id);
        var reachable = new HashSet<Guid> { me };
        var queue = new Queue<Guid>([me]);

        while (queue.TryDequeue(out Guid manager))
        {
            foreach (Guid report in reportsOf[manager])
            {
                if (reachable.Add(report))
                {
                    queue.Enqueue(report);
                }
            }
        }

        List<Guid> ids = [.. reachable];
        return query.Where(employee => ids.Contains(employee.Id));
    }
}
