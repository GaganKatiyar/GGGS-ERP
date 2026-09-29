using Gggs.Time.Domain.Leave;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class LeaveBalanceTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LeaveTypeId = Guid.NewGuid();
    private const int PeriodYear = 2026;

    [Fact]
    public void Recalculate_WithOpeningAndAccrualOnly_AvailableEqualsTheirSum()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.Opening, 5m),
            Entry(LeaveLedgerTransactionType.Accrual, 1.5m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.Opening.ShouldBe(5m);
        balance.Accrued.ShouldBe(1.5m);
        balance.Taken.ShouldBe(0m);
        balance.Available.ShouldBe(6.5m);
    }

    [Fact]
    public void Recalculate_WithADebit_ReducesAvailableAndIncreasesTaken()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.Opening, 10m),
            Entry(LeaveLedgerTransactionType.Debit, -3m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.Taken.ShouldBe(3m);
        balance.Available.ShouldBe(7m);
    }

    [Fact]
    public void Recalculate_WithADebitThenItsReversal_NetsBackToZeroTaken()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.Opening, 10m),
            Entry(LeaveLedgerTransactionType.Debit, -3m),
            Entry(LeaveLedgerTransactionType.Reversal, 3m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.Taken.ShouldBe(0m);
        balance.Available.ShouldBe(10m);
    }

    [Fact]
    public void Recalculate_WithPendingApprovalDays_SubtractsThemFromAvailable()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry> { Entry(LeaveLedgerTransactionType.Opening, 10m) };

        balance.Recalculate(entries, pendingApprovalDays: 2m);

        balance.PendingApproval.ShouldBe(2m);
        balance.Available.ShouldBe(8m);
    }

    [Fact]
    public void Recalculate_WithCarryForwardAndAdjustment_AddsBothToAvailable()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.CarryForward, 4m),
            Entry(LeaveLedgerTransactionType.Adjustment, 1m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.CarriedForward.ShouldBe(4m);
        balance.Adjusted.ShouldBe(1m);
        balance.Available.ShouldBe(5m);
    }

    [Fact]
    public void Recalculate_WithLapseAndEncash_ReducesAvailable()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);

        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.Opening, 10m),
            Entry(LeaveLedgerTransactionType.Lapse, -2m),
            Entry(LeaveLedgerTransactionType.Encash, -3m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.Lapsed.ShouldBe(2m);
        balance.Encashed.ShouldBe(3m);
        balance.Available.ShouldBe(5m);
    }

    [Fact]
    public void Recalculate_IsIdempotent_CallingTwiceWithTheSameEntriesGivesTheSameResult()
    {
        LeaveBalance balance = LeaveBalance.CreateEmpty(EmployeeId, LeaveTypeId, PeriodYear);
        var entries = new List<LeaveLedgerEntry>
        {
            Entry(LeaveLedgerTransactionType.Opening, 10m),
            Entry(LeaveLedgerTransactionType.Debit, -3m),
        };

        balance.Recalculate(entries, pendingApprovalDays: 0m);
        decimal firstAvailable = balance.Available;

        balance.Recalculate(entries, pendingApprovalDays: 0m);

        balance.Available.ShouldBe(firstAvailable);
    }

    private static LeaveLedgerEntry Entry(LeaveLedgerTransactionType type, decimal days)
        => LeaveLedgerEntry.Post(
            EmployeeId, LeaveTypeId, PeriodYear, type, days, new DateOnly(PeriodYear, 1, 15),
            sourceType: "Test", sourceId: Guid.NewGuid(), remarks: null);
}
