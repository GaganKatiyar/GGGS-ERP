using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Performance.Contracts;

/// <summary>An employee's most recent released review: the final 1-5 rating and the cycle it came from.</summary>
public sealed record RatingInfo(Guid EmployeeId, Guid CycleId, string CycleName, int Rating, string Label, DateOnly ReleasedOn);

/// <summary>Released performance ratings, for promotions, succession and the 9-box (ADR-12).</summary>
public interface IPerformanceRatings
{
    /// <summary>Each employee's latest released rating, for those who have one.</summary>
    Task<IReadOnlyDictionary<Guid, RatingInfo>> LatestAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>A review was released to the employee with its final rating.</summary>
[MessageType("gggs.performance.review-released")]
public sealed record ReviewReleasedIntegrationEvent(Guid ReviewId, Guid CycleId, Guid EmployeeId, int FinalRating) : IntegrationEvent;
