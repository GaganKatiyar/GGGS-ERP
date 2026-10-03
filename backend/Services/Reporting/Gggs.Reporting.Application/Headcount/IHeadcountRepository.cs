using Gggs.Reporting.Domain.Headcount;

namespace Gggs.Reporting.Application.Headcount;

public interface IEmployeeRosterFactRepository
{
    Task<EmployeeRosterFact?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);

    void Add(EmployeeRosterFact fact);

    /// <summary>Every tenant with at least one roster row - what the nightly snapshot job fans out
    /// over, since the roster itself is the only record of which tenants Reporting has data for.</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>The current roster for one tenant, read directly (bypassing the tenant query
    /// filter) because the snapshot job runs in platform scope and must read every tenant in
    /// turn rather than the one tenant a request-scoped context would resolve to.</summary>
    Task<IReadOnlyList<EmployeeRosterFact>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IHeadcountDailySnapshotRepository
{
    void AddRange(IEnumerable<HeadcountDailySnapshot> snapshots);

    /// <summary>Removes any existing rows for the date so a re-run of the job (replay, backfill)
    /// is idempotent rather than duplicating rows.</summary>
    Task RemoveForDateAsync(Guid tenantId, DateOnly snapshotDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HeadcountDailySnapshot>> ListAsync(
        Guid tenantId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}
