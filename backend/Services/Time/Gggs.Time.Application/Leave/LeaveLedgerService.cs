using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Leave;

/// <summary>
/// The one path anything in this service uses to change a balance - posts a
/// <see cref="LeaveLedgerEntry"/> and rebuilds the <see cref="LeaveBalance"/> projection for that
/// employee/leave type/period in the same call, so the two can never observe a moment where one
/// has moved and the other has not.
/// </summary>
public sealed class LeaveLedgerService(ILeaveLedgerRepository ledger)
{
    /// <summary>
    /// Idempotent on <paramref name="sourceType"/> + <paramref name="sourceId"/> +
    /// <paramref name="transactionType"/>: a caller that posts the same debit twice (a retried
    /// request, a redelivered event) gets the existing posting back rather than a doubled balance.
    /// A different <paramref name="transactionType"/> for the same source is a different posting
    /// (a request's Debit on submission and its Reversal on cancellation both key on the request's
    /// own id, and must not collide).
    /// </summary>
    public async Task<LeaveLedgerEntry?> PostAsync(
        Guid employeeId,
        Guid leaveTypeId,
        int periodYear,
        LeaveLedgerTransactionType transactionType,
        decimal days,
        DateOnly transactionDate,
        string sourceType,
        Guid sourceId,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        if (await ledger.TransactionExistsAsync(sourceType, sourceId, transactionType, cancellationToken))
        {
            return null;
        }

        LeaveLedgerEntry entry = LeaveLedgerEntry.Post(
            employeeId, leaveTypeId, periodYear, transactionType, days, transactionDate, sourceType, sourceId, remarks);
        ledger.Add(entry);

        await RebuildBalanceAsync(employeeId, leaveTypeId, periodYear, cancellationToken);
        return entry;
    }

    /// <summary>Rebuilds the balance projection from every ledger entry for the period, plus the
    /// pending-approval total from open requests - see <see cref="LeaveBalance.Recalculate"/>'s own
    /// remarks on why this is a full rebuild rather than an incremental update.</summary>
    public async Task RebuildBalanceAsync(Guid employeeId, Guid leaveTypeId, int periodYear, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LeaveLedgerEntry> entries = await ledger.GetEntriesAsync(employeeId, leaveTypeId, periodYear, cancellationToken);

        decimal pendingDays = await ledger.GetPendingApprovalDaysAsync(
            employeeId, leaveTypeId,
            new DateOnly(periodYear, 1, 1), new DateOnly(periodYear, 12, 31),
            cancellationToken);

        LeaveBalance? balance = await ledger.GetBalanceAsync(employeeId, leaveTypeId, periodYear, cancellationToken);

        if (balance is null)
        {
            balance = LeaveBalance.CreateEmpty(employeeId, leaveTypeId, periodYear);
            ledger.Add(balance);
        }

        balance.Recalculate(entries, pendingDays);
    }
}
