using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Spend.Domain.Travel;

namespace Gggs.Spend.Application.Travel;

/// <summary>Requesting and disbursing a cash advance against an Approved
/// <see cref="TravelRequest"/> - see <see cref="TravelAdvance"/>'s own remarks.</summary>
public sealed class TravelAdvanceService(
    ITravelAdvanceRepository advances, ITravelRequestRepository requests, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<TravelAdvance> RequestAsync(Guid travelRequestId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        TravelRequest request = await requests.GetAsync(travelRequestId, cancellationToken)
            ?? throw new NotFoundException(nameof(TravelRequest), travelRequestId);

        Guard(
            request.Status == TravelRequestStatus.Approved,
            "TRAVEL_REQUEST_NOT_APPROVED",
            $"Request '{request.RequestNo}' must be Approved before an advance can be requested against it; it is {request.Status}.");

        TravelAdvance advance = TravelAdvance.Create(travelRequestId, amount, currency);
        advances.Add(advance);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return advance;
    }

    public async Task<TravelAdvance> DisburseAsync(Guid advanceId, CancellationToken cancellationToken = default)
    {
        TravelAdvance advance = await advances.GetAsync(advanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(TravelAdvance), advanceId);

        advance.Disburse(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return advance;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
