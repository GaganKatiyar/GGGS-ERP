using Gggs.Modules.Leave.Contracts;

namespace Gggs.Modules.Leave.Application;

/// <summary>Leave balances for other modules, from the same ledger the balance screens read.</summary>
internal sealed class LeaveBalances(LeaveService leave) : ILeaveBalances
{
    public async Task<IReadOnlyList<LeaveBalanceInfo>> BalancesAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => [.. (await leave.BalancesAsync(employeeId, cancellationToken)).Select(balance => new LeaveBalanceInfo(balance.Code, balance.Name, balance.IsPaid, balance.Available))];
}
