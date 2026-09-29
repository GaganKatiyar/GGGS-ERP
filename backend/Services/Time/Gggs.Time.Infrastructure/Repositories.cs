using Gggs.Time.Application.Holidays;
using Gggs.Time.Application.Leave;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Domain.Holidays;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Infrastructure;

public sealed class LeaveTypeRepository(TimeDbContext dbContext) : ILeaveTypeRepository
{
    public async Task<LeaveType?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.LeaveTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<LeaveType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalizedCode = code.Trim().ToUpperInvariant();
        return await dbContext.LeaveTypes.FirstOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);
    }

    public void Add(LeaveType leaveType) => dbContext.LeaveTypes.Add(leaveType);
}

public sealed class LeavePolicyRepository(TimeDbContext dbContext) : ILeavePolicyRepository
{
    public async Task<LeavePolicy?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.LeavePolicies.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Guid?> GetTenantDefaultPolicyIdAsync(CancellationToken cancellationToken = default)
        => await dbContext.LeavePolicies.OrderBy(x => x.CreatedDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

    public void Add(LeavePolicy policy) => dbContext.LeavePolicies.Add(policy);

    public async Task<LeavePolicyRule?> GetRuleAsync(Guid policyId, Guid leaveTypeId, CancellationToken cancellationToken = default)
        => await dbContext.LeavePolicyRules
            .FirstOrDefaultAsync(x => x.PolicyId == policyId && x.LeaveTypeId == leaveTypeId, cancellationToken);

    public void Add(LeavePolicyRule rule) => dbContext.LeavePolicyRules.Add(rule);
}

public sealed class LeaveLedgerRepository(TimeDbContext dbContext) : ILeaveLedgerRepository
{
    /// <summary>
    /// Includes entries added earlier in the same unit of work but not yet saved: a caller that
    /// posts a ledger entry and immediately rebuilds the balance from this method (every
    /// <see cref="LeaveLedgerService.PostAsync"/> call) must see that entry, and a plain
    /// <c>DbSet</c> query would not - it always hits the database, never the change tracker's
    /// pending adds. Read via <see cref="DbContext.Set{TEntity}"/>.Local first for anything not yet
    /// saved, then fall back to the database for everything else, keyed by <see cref="LeaveLedgerEntry.Id"/>
    /// so a row is never counted twice if it happens to appear in both.
    /// </summary>
    public async Task<IReadOnlyList<LeaveLedgerEntry>> GetEntriesAsync(
        Guid employeeId, Guid leaveTypeId, int periodYear, CancellationToken cancellationToken = default)
    {
        List<LeaveLedgerEntry> persisted = await dbContext.LeaveLedgerEntries
            .Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId && x.PeriodYear == periodYear)
            .ToListAsync(cancellationToken);

        IEnumerable<LeaveLedgerEntry> pending = dbContext.ChangeTracker.Entries<LeaveLedgerEntry>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId && x.PeriodYear == periodYear);

        HashSet<Guid> persistedIds = persisted.Select(x => x.Id).ToHashSet();
        return persisted.Concat(pending.Where(x => !persistedIds.Contains(x.Id))).ToList();
    }

    public async Task<bool> TransactionExistsAsync(
        string sourceType, Guid sourceId, LeaveLedgerTransactionType transactionType, CancellationToken cancellationToken = default)
    {
        bool pending = dbContext.ChangeTracker.Entries<LeaveLedgerEntry>()
            .Any(e => e.State == EntityState.Added
                && e.Entity.SourceType == sourceType && e.Entity.SourceId == sourceId && e.Entity.TransactionType == transactionType);

        return pending || await dbContext.LeaveLedgerEntries.AnyAsync(
            x => x.SourceType == sourceType && x.SourceId == sourceId && x.TransactionType == transactionType,
            cancellationToken);
    }

    public void Add(LeaveLedgerEntry entry) => dbContext.LeaveLedgerEntries.Add(entry);

    public async Task<LeaveBalance?> GetBalanceAsync(
        Guid employeeId, Guid leaveTypeId, int periodYear, CancellationToken cancellationToken = default)
        => await dbContext.LeaveBalances.FirstOrDefaultAsync(
            x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId && x.PeriodYear == periodYear, cancellationToken);

    public void Add(LeaveBalance balance) => dbContext.LeaveBalances.Add(balance);

    /// <summary>
    /// Same not-yet-saved concern as <see cref="GetEntriesAsync"/>: a request's own status change
    /// (submission adding a new Pending row, or approval/rejection/cancellation flipping an
    /// existing one away from Pending) happens earlier in the same unit of work as the rebuild that
    /// must reflect it, and a plain query would still see the pre-change status.
    /// </summary>
    public async Task<decimal> GetPendingApprovalDaysAsync(
        Guid employeeId, Guid leaveTypeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        List<LeaveRequest> persisted = await dbContext.LeaveRequests
            .Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId
                && x.FromDate >= periodStart && x.FromDate <= periodEnd)
            .ToListAsync(cancellationToken);

        HashSet<Guid> persistedIds = persisted.Select(x => x.Id).ToHashSet();

        IEnumerable<LeaveRequest> pendingAdds = dbContext.ChangeTracker.Entries<LeaveRequest>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId
                && x.FromDate >= periodStart && x.FromDate <= periodEnd
                && !persistedIds.Contains(x.Id));

        return persisted.Concat(pendingAdds)
            .Where(x => x.Status == LeaveRequestStatus.Pending)
            .Sum(x => x.TotalDays);
    }
}

public sealed class LeaveRequestRepository(TimeDbContext dbContext) : ILeaveRequestRepository
{
    public async Task<LeaveRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> HasOverlapAsync(Guid employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
        => await dbContext.LeaveRequests.AnyAsync(
            x => x.EmployeeId == employeeId
                && x.Status != LeaveRequestStatus.Cancelled && x.Status != LeaveRequestStatus.Rejected && x.Status != LeaveRequestStatus.Withdrawn
                && x.FromDate <= toDate && x.ToDate >= fromDate,
            cancellationToken);

    public void Add(LeaveRequest request) => dbContext.LeaveRequests.Add(request);

    public void AddDay(LeaveRequestDay day) => dbContext.LeaveRequestDays.Add(day);
}

public sealed class HolidayRepository(TimeDbContext dbContext) : IHolidayRepository
{
    public async Task<HolidayCalendar?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.HolidayCalendars.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<HolidayCalendar?> GetForYearAsync(int year, CancellationToken cancellationToken = default)
        => await dbContext.HolidayCalendars.FirstOrDefaultAsync(x => x.Year == year, cancellationToken);

    public void Add(HolidayCalendar calendar) => dbContext.HolidayCalendars.Add(calendar);

    public async Task<bool> HolidayExistsAsync(Guid holidayCalendarId, DateOnly holidayDate, CancellationToken cancellationToken = default)
        => await dbContext.Holidays.AnyAsync(x => x.HolidayCalendarId == holidayCalendarId && x.HolidayDate == holidayDate, cancellationToken);

    public void Add(Holiday holiday) => dbContext.Holidays.Add(holiday);

    public async Task<IReadOnlyList<Holiday>> GetHolidaysAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default)
        => await dbContext.Holidays.Where(x => x.HolidayCalendarId == holidayCalendarId).ToListAsync(cancellationToken);
}

public sealed class EmployeeSnapshotRepository(TimeDbContext dbContext) : IEmployeeSnapshotRepository
{
    public async Task<EmployeeSnapshot?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.EmployeeSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeSnapshot snapshot) => dbContext.EmployeeSnapshots.Add(snapshot);
}
