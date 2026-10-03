using Gggs.BuildingBlocks.Application;
using Gggs.Reporting.Domain.Headcount;

namespace Gggs.Reporting.Application.Headcount;

/// <summary>
/// Builds one tenant's <see cref="HeadcountDailySnapshot"/> rows for a given date from the current
/// <see cref="EmployeeRosterFact"/> roster.
/// <para>
/// Headcount per org-unit combination counts every non-exited employee whose <c>DateOfJoining</c>
/// is on or before the snapshot date. Joiners/leavers are the employees whose join/exit date is
/// exactly the snapshot date. Recomputed from scratch each run (old rows for the date are removed
/// first), so a late-arriving event, a correction, or a re-run after a bug fix is reflected the
/// next time the job runs rather than needing a separate backfill path.
/// </para>
/// </summary>
public sealed class HeadcountSnapshotBuilder(
    IEmployeeRosterFactRepository roster,
    IHeadcountDailySnapshotRepository snapshots,
    IUnitOfWork unitOfWork)
{
    public async Task BuildForTenantAsync(Guid tenantId, DateOnly snapshotDate, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EmployeeRosterFact> employees = await roster.ListForTenantAsync(tenantId, cancellationToken);

        await snapshots.RemoveForDateAsync(tenantId, snapshotDate, cancellationToken);

        var rows = employees
            .Where(e => e.DateOfJoining <= snapshotDate)
            .GroupBy(e => (e.LegalEntityId, e.BranchId, e.DepartmentId))
            .Select(group => HeadcountDailySnapshot.Create(
                snapshotDate,
                group.Key.LegalEntityId,
                group.Key.BranchId,
                group.Key.DepartmentId,
                headcount: group.Count(e => e.ExitDate is null || e.ExitDate > snapshotDate),
                joiners: group.Count(e => e.DateOfJoining == snapshotDate),
                leavers: group.Count(e => e.ExitDate == snapshotDate)))
            .ToList();

        snapshots.AddRange(rows);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
