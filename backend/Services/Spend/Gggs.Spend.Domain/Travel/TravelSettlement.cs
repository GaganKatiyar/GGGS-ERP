using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Travel;

public enum TravelSettlementStatus
{
    Draft = 1,
    Settled = 2,
}

/// <summary>
/// Reconciles a <see cref="TravelRequest"/>'s disbursed advance against the actual spend on it -
/// the sum of every Approved <c>Expenses.ExpenseClaim</c> linked to the same request by
/// <c>ExpenseClaim.TravelRequestId</c>. Exactly one of <see cref="NetPayable"/> (expenses exceeded
/// the advance - pay the employee the difference) or <see cref="NetRecoverable"/> (the advance
/// exceeded expenses - recover the difference from the employee) is non-zero; never both. Draft
/// -&gt; Settled (terminal) - <see cref="Settle"/> is the one action, there is no reopening a
/// settled trip in this v1 slice. No payroll deduction or bank payment wiring yet
/// (architecture.md's <c>trv.TravelSettlement</c>) - marking Settled only records the outcome, it
/// does not move any money itself.
/// </summary>
public sealed class TravelSettlement : TenantAggregateRoot
{
    private TravelSettlement()
    {
    }

    public Guid TravelRequestId { get; private set; }

    public decimal AdvanceTotal { get; private set; }

    public decimal ExpenseTotal { get; private set; }

    public decimal NetPayable { get; private set; }

    public decimal NetRecoverable { get; private set; }

    public TravelSettlementStatus Status { get; private set; }

    public DateTime? SettledDate { get; private set; }

    public static TravelSettlement Create(Guid travelRequestId, decimal advanceTotal, decimal expenseTotal)
    {
        decimal difference = expenseTotal - advanceTotal;

        return new TravelSettlement
        {
            TravelRequestId = Guard.NotEmpty(travelRequestId),
            AdvanceTotal = advanceTotal,
            ExpenseTotal = expenseTotal,
            NetPayable = difference > 0 ? difference : 0,
            NetRecoverable = difference < 0 ? -difference : 0,
            Status = TravelSettlementStatus.Draft,
            IsActive = true,
        };
    }

    public void Settle(DateTime settledAtUtc)
    {
        Guard.Against(Status != TravelSettlementStatus.Draft, "TRAVEL_SETTLEMENT_NOT_DRAFT", $"The settlement must be Draft to settle; it is {Status}.");
        Status = TravelSettlementStatus.Settled;
        SettledDate = settledAtUtc;
    }
}
