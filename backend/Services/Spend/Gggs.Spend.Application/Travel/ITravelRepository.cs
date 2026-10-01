using Gggs.Spend.Domain.Travel;

namespace Gggs.Spend.Application.Travel;

public interface ITravelRequestRepository
{
    Task<TravelRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> RequestNoExistsAsync(string requestNo, CancellationToken cancellationToken = default);

    void Add(TravelRequest request);
}

public interface ITravelAdvanceRepository
{
    Task<TravelAdvance?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TravelAdvance>> ListForRequestAsync(Guid travelRequestId, CancellationToken cancellationToken = default);

    void Add(TravelAdvance advance);
}

public interface ITravelSettlementRepository
{
    Task<TravelSettlement?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TravelSettlement?> GetForRequestAsync(Guid travelRequestId, CancellationToken cancellationToken = default);

    void Add(TravelSettlement settlement);

    /// <summary>Removes a prior Draft settlement for the request - what
    /// <see cref="Gggs.Spend.Application.Travel.TravelSettlementService.BuildAsync"/> calls before
    /// adding a freshly computed one, since a settlement is replaced wholesale, never adjusted in
    /// place.</summary>
    void Remove(TravelSettlement settlement);
}
