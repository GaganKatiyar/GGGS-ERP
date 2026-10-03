using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Reporting.Domain.Headcount;

/// <summary>
/// One fact row per tenant/date/org-unit combination - architecture.md §7.2's
/// <c>HeadcountDailySnapshot</c>, narrowed for v1 to the dimensions the current Core HR events
/// actually carry (LegalEntity/Branch/Department). Gender and EmploymentType are not in
/// <c>EmployeeAssignmentChangedIntegrationEvent</c> today, so they are left for a later slice
/// rather than modelled against data that does not exist yet.
/// <para>
/// Built nightly from <see cref="EmployeeRosterFact"/> by <c>HeadcountSnapshotJob</c>, not
/// maintained incrementally - a true "as of this date" count needs the day's roster state, and
/// recomputing it is simple and self-correcting (a late-arriving event or a backfill just changes
/// the next run's output), where an incremental counter would drift silently.
/// </para>
/// </summary>
public sealed class HeadcountDailySnapshot : TenantAggregateRoot
{
    private HeadcountDailySnapshot()
    {
    }

    public DateOnly SnapshotDate { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public int Headcount { get; private set; }

    /// <summary>Employees whose <c>DateOfJoining</c> falls on <see cref="SnapshotDate"/>.</summary>
    public int Joiners { get; private set; }

    /// <summary>Employees whose <c>ExitDate</c> falls on <see cref="SnapshotDate"/>.</summary>
    public int Leavers { get; private set; }

    public static HeadcountDailySnapshot Create(
        DateOnly snapshotDate,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        int headcount,
        int joiners,
        int leavers)
    {
        Guard.Against(headcount < 0, "HEADCOUNT_NEGATIVE", "Headcount cannot be negative.");
        Guard.Against(joiners < 0, "JOINERS_NEGATIVE", "Joiners cannot be negative.");
        Guard.Against(leavers < 0, "LEAVERS_NEGATIVE", "Leavers cannot be negative.");

        return new()
        {
            SnapshotDate = snapshotDate,
            LegalEntityId = Guard.NotEmpty(legalEntityId),
            BranchId = Guard.NotEmpty(branchId),
            DepartmentId = Guard.NotEmpty(departmentId),
            Headcount = headcount,
            Joiners = joiners,
            Leavers = leavers,
            IsActive = true,
        };
    }
}
