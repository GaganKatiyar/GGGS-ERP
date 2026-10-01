using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Travel;

public enum TravelAdvanceStatus
{
    Requested = 1,
    Disbursed = 2,
}

/// <summary>
/// A cash advance against an Approved <see cref="TravelRequest"/> - Requested -&gt; Disbursed
/// (terminal). No bank payment batch integration in this v1 slice (architecture.md's
/// <c>BankPaymentBatch</c>): disbursement here just records that the money went out, not how.
/// <see cref="TravelSettlement"/> reads this advance's <see cref="Amount"/> as the baseline the
/// trip's actual expenses are reconciled against.
/// </summary>
public sealed class TravelAdvance : TenantAggregateRoot
{
    private TravelAdvance()
    {
        Currency = null!;
    }

    public Guid TravelRequestId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public TravelAdvanceStatus Status { get; private set; }

    public DateTime? DisbursedDate { get; private set; }

    public static TravelAdvance Create(Guid travelRequestId, decimal amount, string currency)
    {
        Guard.Against(amount <= 0, "TRAVEL_ADVANCE_AMOUNT_NOT_POSITIVE", "The advance amount must be positive.");

        return new TravelAdvance
        {
            TravelRequestId = Guard.NotEmpty(travelRequestId),
            Amount = amount,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            Status = TravelAdvanceStatus.Requested,
            IsActive = true,
        };
    }

    public void Disburse(DateTime disbursedAtUtc)
    {
        Guard.Against(Status != TravelAdvanceStatus.Requested, "TRAVEL_ADVANCE_NOT_REQUESTED", $"The advance must be Requested to disburse; it is {Status}.");
        Status = TravelAdvanceStatus.Disbursed;
        DisbursedDate = disbursedAtUtc;
    }
}
