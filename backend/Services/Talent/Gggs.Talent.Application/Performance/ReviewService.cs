using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Performance;

namespace Gggs.Talent.Application.Performance;

public sealed class ReviewService(IReviewRepository reviews, IPerformanceCycleRepository cycles, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Review> CreateAsync(
        Guid cycleId, Guid employeeId, Guid managerEmployeeId, CancellationToken cancellationToken = default)
    {
        PerformanceCycle cycle = await cycles.GetAsync(cycleId, cancellationToken)
            ?? throw new NotFoundException(nameof(PerformanceCycle), cycleId);

        Guard(
            cycle.Status == PerformanceCycleStatus.Open,
            "CYCLE_NOT_OPEN_FOR_REVIEWS",
            $"Cycle '{cycle.Name}' must be Open to raise a review against it; it is {cycle.Status}.");

        Guard(
            !await reviews.ExistsForEmployeeAndCycleAsync(employeeId, cycleId, cancellationToken),
            "REVIEW_ALREADY_EXISTS",
            "This employee already has a review for this cycle.");

        Review review = Review.Create(cycleId, employeeId, managerEmployeeId);
        reviews.Add(review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<Review> SubmitSelfReviewAsync(Guid id, int rating, string? comments, CancellationToken cancellationToken = default)
    {
        Review review = await GetOrThrowAsync(id, cancellationToken);
        review.SubmitSelfReview(rating, comments);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<Review> SubmitManagerReviewAsync(Guid id, int rating, string? comments, CancellationToken cancellationToken = default)
    {
        Review review = await GetOrThrowAsync(id, cancellationToken);
        review.SubmitManagerReview(rating, comments);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<Review> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Review review = await GetOrThrowAsync(id, cancellationToken);
        review.Release(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return review;
    }

    private async Task<Review> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await reviews.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Review), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
