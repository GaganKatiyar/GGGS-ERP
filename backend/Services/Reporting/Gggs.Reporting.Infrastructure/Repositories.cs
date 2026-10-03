using Gggs.Reporting.Application.Headcount;
using Gggs.Reporting.Domain.Headcount;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Reporting.Infrastructure;

public sealed class EmployeeRosterFactRepository(ReportingDbContext dbContext) : IEmployeeRosterFactRepository
{
    public async Task<EmployeeRosterFact?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeRosterFacts.SingleOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeRosterFact fact) => dbContext.EmployeeRosterFacts.Add(fact);

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default)
        => await dbContext.EmployeeRosterFacts
            .IgnoreQueryFilters()
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeRosterFact>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeRosterFacts
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}

public sealed class HeadcountDailySnapshotRepository(ReportingDbContext dbContext)
    : IHeadcountDailySnapshotRepository
{
    public void AddRange(IEnumerable<HeadcountDailySnapshot> snapshots) => dbContext.HeadcountDailySnapshots.AddRange(snapshots);

    public async Task RemoveForDateAsync(Guid tenantId, DateOnly snapshotDate, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters + an explicit TenantId match: the snapshot job runs in platform scope
        // across every tenant in turn, so the usual single-tenant query filter would hide the very
        // rows this call needs to remove once the job has moved on to the next tenant.
        await dbContext.HeadcountDailySnapshots
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.SnapshotDate == snapshotDate)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HeadcountDailySnapshot>> ListAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
        => await dbContext.HeadcountDailySnapshots
            .Where(s => s.SnapshotDate >= fromDate && s.SnapshotDate <= toDate)
            .AsNoTracking()
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(cancellationToken);
}
