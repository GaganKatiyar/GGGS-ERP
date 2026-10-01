using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Travel;

namespace Gggs.Spend.Application.Travel;

/// <summary>
/// Building and settling a <see cref="TravelSettlement"/> for one trip - see
/// <see cref="TravelSettlement"/>'s own remarks on the reconciliation it performs.
/// </summary>
public sealed class TravelSettlementService(
    ITravelSettlementRepository settlements,
    ITravelRequestRepository requests,
    ITravelAdvanceRepository advances,
    IExpenseClaimRepository claims,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// Sums every Disbursed advance against the request and every Approved
    /// <see cref="ExpenseClaim"/> linked to it, and records the Draft settlement that follows from
    /// those two totals. Replaces any prior Draft settlement for the same request wholesale -
    /// building again (say, because a late expense claim was just approved) is expected to happen
    /// more than once before the trip is actually <see cref="SettleAsync"/>d.
    /// </summary>
    public async Task<TravelSettlement> BuildAsync(Guid travelRequestId, CancellationToken cancellationToken = default)
    {
        TravelRequest request = await requests.GetAsync(travelRequestId, cancellationToken)
            ?? throw new NotFoundException(nameof(TravelRequest), travelRequestId);

        TravelSettlement? existing = await settlements.GetForRequestAsync(travelRequestId, cancellationToken);
        Guard(
            existing is null || existing.Status == TravelSettlementStatus.Draft,
            "TRAVEL_SETTLEMENT_ALREADY_SETTLED",
            "This trip has already been settled.");

        if (existing is not null)
        {
            settlements.Remove(existing);
        }

        IReadOnlyList<TravelAdvance> requestAdvances = await advances.ListForRequestAsync(travelRequestId, cancellationToken);
        decimal advanceTotal = requestAdvances.Where(x => x.Status == TravelAdvanceStatus.Disbursed).Sum(x => x.Amount);

        IReadOnlyList<ExpenseClaim> approvedClaims = await claims.ListApprovedForTravelRequestAsync(travelRequestId, cancellationToken);
        decimal expenseTotal = approvedClaims.Sum(x => x.Total);

        TravelSettlement settlement = TravelSettlement.Create(travelRequestId, advanceTotal, expenseTotal);
        settlements.Add(settlement);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return settlement;
    }

    public async Task<TravelSettlement> SettleAsync(Guid settlementId, CancellationToken cancellationToken = default)
    {
        TravelSettlement settlement = await settlements.GetAsync(settlementId, cancellationToken)
            ?? throw new NotFoundException(nameof(TravelSettlement), settlementId);

        settlement.Settle(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return settlement;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
