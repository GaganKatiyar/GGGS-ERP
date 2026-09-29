using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Leave;

public interface ILeaveTypeRepository
{
    Task<LeaveType?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LeaveType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    void Add(LeaveType leaveType);
}

public interface ILeavePolicyRepository
{
    Task<LeavePolicy?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>v1 has no policy-assignment targeting (see <see cref="LeavePolicyResolver"/>'s own
    /// remarks) - a tenant is expected to have at most one policy, and this is it.</summary>
    Task<Guid?> GetTenantDefaultPolicyIdAsync(CancellationToken cancellationToken = default);

    void Add(LeavePolicy policy);

    Task<LeavePolicyRule?> GetRuleAsync(Guid policyId, Guid leaveTypeId, CancellationToken cancellationToken = default);

    void Add(LeavePolicyRule rule);
}

public interface ILeaveLedgerRepository
{
    Task<IReadOnlyList<LeaveLedgerEntry>> GetEntriesAsync(
        Guid employeeId, Guid leaveTypeId, int periodYear, CancellationToken cancellationToken = default);

    Task<bool> TransactionExistsAsync(
        string sourceType, Guid sourceId, LeaveLedgerTransactionType transactionType, CancellationToken cancellationToken = default);

    void Add(LeaveLedgerEntry entry);

    Task<LeaveBalance?> GetBalanceAsync(Guid employeeId, Guid leaveTypeId, int periodYear, CancellationToken cancellationToken = default);

    void Add(LeaveBalance balance);

    /// <summary>Every other request still awaiting a decision for this employee/leave type/period -
    /// what <see cref="LeaveBalance.PendingApproval"/> is rebuilt from.</summary>
    Task<decimal> GetPendingApprovalDaysAsync(
        Guid employeeId, Guid leaveTypeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default);
}

public interface ILeaveRequestRepository
{
    Task<LeaveRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> HasOverlapAsync(Guid employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    void Add(LeaveRequest request);

    void AddDay(LeaveRequestDay day);
}
