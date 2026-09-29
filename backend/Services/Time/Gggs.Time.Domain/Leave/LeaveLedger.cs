using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Time.Domain.Leave;

public enum LeaveLedgerTransactionType
{
    Opening = 1,
    Accrual = 2,
    Debit = 3,
    Credit = 4,
    Reversal = 5,
    Lapse = 6,
    Encash = 7,
    CarryForward = 8,
    Adjustment = 9,
}

/// <summary>
/// One posting against one employee's balance for one leave type - the source of truth
/// (architecture.md §7.2). Never updated or deleted once written; a mistake is corrected by
/// posting a <see cref="LeaveLedgerTransactionType.Reversal"/> that cancels it out, so the balance
/// at any past date can always be reconstructed by summing rows, not by trusting a
/// separately-maintained running total.
/// <para>
/// <see cref="SourceType"/> + <see cref="SourceId"/> is how a poster stays idempotent - see
/// <c>LeaveLedgerRepository.TransactionExistsAsync</c> and every caller's own remarks on why it
/// checks before posting.
/// </para>
/// </summary>
public sealed class LeaveLedgerEntry : TenantAggregateRoot
{
    private LeaveLedgerEntry()
    {
        SourceType = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public int PeriodYear { get; private set; }

    public LeaveLedgerTransactionType TransactionType { get; private set; }

    /// <summary>Positive for a credit (accrual, opening balance, a reversed debit), negative for a
    /// debit (leave taken, a reversed credit) - the balance projection is a plain sum of this
    /// column, never a signed-by-type calculation, so the sign has to be right at posting time.</summary>
    public decimal Days { get; private set; }

    public DateOnly TransactionDate { get; private set; }

    /// <summary>What caused this posting - "LeaveRequest", "AccrualJob", "Manual". Free text by
    /// design: this ledger outlives whatever list of source types exists today.</summary>
    public string SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public string? Remarks { get; private set; }

    public static LeaveLedgerEntry Post(
        Guid employeeId,
        Guid leaveTypeId,
        int periodYear,
        LeaveLedgerTransactionType transactionType,
        decimal days,
        DateOnly transactionDate,
        string sourceType,
        Guid? sourceId,
        string? remarks) => new()
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            PeriodYear = periodYear,
            TransactionType = transactionType,
            Days = days,
            TransactionDate = transactionDate,
            SourceType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sourceType).Trim(), 50),
            SourceId = sourceId,
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : Guard.MaxLength(remarks.Trim(), 500),
            IsActive = true,
        };
}

/// <summary>
/// A rebuildable projection of <see cref="LeaveLedgerEntry"/> for one employee, one leave type, one
/// period - what "my leave balance" reads. Every field here is recomputed from the ledger by
/// <see cref="Recalculate"/>, never incremented in place: a per-transaction-type sign convention
/// (does a "Reversal" add or subtract from <see cref="Taken"/>? it depends what it is reversing)
/// is exactly the kind of thing that quietly drifts from the ledger over time, so this row is
/// always rebuilt from every entry that applies to it rather than trusted as a running total.
/// <see cref="LeaveLedgerService"/> is the only writer, and it always rebuilds this row in the same
/// transaction as the ledger posting that changed it.
/// </summary>
public sealed class LeaveBalance : TenantAggregateRoot
{
    private LeaveBalance()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public int PeriodYear { get; private set; }

    public decimal Opening { get; private set; }

    public decimal Accrued { get; private set; }

    public decimal Taken { get; private set; }

    public decimal PendingApproval { get; private set; }

    public decimal Adjusted { get; private set; }

    public decimal Encashed { get; private set; }

    public decimal Lapsed { get; private set; }

    public decimal CarriedForward { get; private set; }

    /// <summary>Opening + Accrued + Adjusted + CarriedForward - Taken - Encashed - Lapsed -
    /// PendingApproval - <see cref="PendingApproval"/> is subtracted here (unlike a plain ledger
    /// sum) so "how much can I still apply for" already accounts for what is sitting in someone's
    /// approval queue, per architecture.md §7.2's own field list.</summary>
    public decimal Available { get; private set; }

    public static LeaveBalance CreateEmpty(Guid employeeId, Guid leaveTypeId, int periodYear) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        LeaveTypeId = Guard.NotEmpty(leaveTypeId),
        PeriodYear = periodYear,
        IsActive = true,
    };

    /// <summary>
    /// Rebuilds every field from scratch against <paramref name="entries"/> (every
    /// <see cref="LeaveLedgerEntry"/> for this employee/leave type/period) and
    /// <paramref name="pendingApprovalDays"/> (summed separately by the caller from open
    /// <see cref="LeaveRequest"/> rows, since a pending request has no ledger entry yet - see
    /// <see cref="LeaveRequest.Submit"/>'s own remarks on why the debit posts only on approval).
    /// A <see cref="LeaveLedgerTransactionType.Reversal"/> entry's own <see cref="LeaveLedgerEntry.Days"/>
    /// sign already encodes which side it corrects, so it is summed into the same bucket as the
    /// transaction type would be for a positive amount - "Taken" for a reversed debit/credit - with
    /// no separate case needed.
    /// </summary>
    public void Recalculate(IReadOnlyList<LeaveLedgerEntry> entries, decimal pendingApprovalDays)
    {
        Opening = SumFor(entries, LeaveLedgerTransactionType.Opening);
        Accrued = SumFor(entries, LeaveLedgerTransactionType.Accrual);
        Adjusted = SumFor(entries, LeaveLedgerTransactionType.Adjustment);
        CarriedForward = SumFor(entries, LeaveLedgerTransactionType.CarryForward);

        // Debit/Credit/Reversal all move the same bucket: a debit (leave taken) is a negative
        // posting, a credit or a reversed debit is a positive one that brings Taken back down.
        Taken = -entries
            .Where(e => e.TransactionType is LeaveLedgerTransactionType.Debit
                or LeaveLedgerTransactionType.Credit or LeaveLedgerTransactionType.Reversal)
            .Sum(e => e.Days);

        Lapsed = -SumFor(entries, LeaveLedgerTransactionType.Lapse);
        Encashed = -SumFor(entries, LeaveLedgerTransactionType.Encash);
        PendingApproval = pendingApprovalDays;

        Available = Opening + Accrued + Adjusted + CarriedForward - Taken - Encashed - Lapsed - PendingApproval;
    }

    private static decimal SumFor(IReadOnlyList<LeaveLedgerEntry> entries, LeaveLedgerTransactionType type)
        => entries.Where(e => e.TransactionType == type).Sum(e => e.Days);
}
