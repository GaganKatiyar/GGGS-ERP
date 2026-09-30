using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Performance;

public enum ReviewStatus
{
    Draft = 1,
    SelfSubmitted = 2,
    ManagerSubmitted = 3,
    Released = 4,
}

/// <summary>
/// One employee's review for one cycle - a fixed self-then-manager structure with a 1-5 rating
/// each, not the tenant-configurable <c>ReviewTemplate</c>/<c>RatingScale</c>/360 feedback
/// architecture.md describes (§7.2). Released once both ratings are in, which is the only thing
/// that makes the review visible to the employee in a later UI slice - <see cref="Status"/> alone
/// is the gate, not a separate visibility flag.
/// </summary>
public sealed class Review : TenantAggregateRoot
{
    private Review()
    {
    }

    public Guid CycleId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid ManagerEmployeeId { get; private set; }

    public int? SelfRating { get; private set; }

    public string? SelfComments { get; private set; }

    public int? ManagerRating { get; private set; }

    public string? ManagerComments { get; private set; }

    public ReviewStatus Status { get; private set; }

    public DateTime? ReleasedDate { get; private set; }

    public static Review Create(Guid cycleId, Guid employeeId, Guid managerEmployeeId)
    {
        Guard.Against(
            employeeId == managerEmployeeId, "REVIEW_CANNOT_SELF_MANAGE", "An employee cannot be their own reviewing manager.");

        return new Review
        {
            CycleId = Guard.NotEmpty(cycleId),
            EmployeeId = Guard.NotEmpty(employeeId),
            ManagerEmployeeId = Guard.NotEmpty(managerEmployeeId),
            Status = ReviewStatus.Draft,
            IsActive = true,
        };
    }

    public void SubmitSelfReview(int rating, string? comments)
    {
        Guard.Against(
            Status != ReviewStatus.Draft, "REVIEW_NOT_DRAFT", $"The review must be Draft to submit a self-review; it is {Status}.");

        SelfRating = ValidateRating(rating);
        SelfComments = string.IsNullOrWhiteSpace(comments) ? null : Guard.MaxLength(comments.Trim(), 2000);
        Status = ReviewStatus.SelfSubmitted;
    }

    public void SubmitManagerReview(int rating, string? comments)
    {
        Guard.Against(
            Status != ReviewStatus.SelfSubmitted,
            "REVIEW_SELF_NOT_SUBMITTED",
            $"The self-review must be submitted before the manager review; the review is {Status}.");

        ManagerRating = ValidateRating(rating);
        ManagerComments = string.IsNullOrWhiteSpace(comments) ? null : Guard.MaxLength(comments.Trim(), 2000);
        Status = ReviewStatus.ManagerSubmitted;
    }

    public void Release(DateTime releasedDate)
    {
        Guard.Against(
            Status != ReviewStatus.ManagerSubmitted,
            "REVIEW_MANAGER_NOT_SUBMITTED",
            $"The manager review must be submitted before release; the review is {Status}.");

        Status = ReviewStatus.Released;
        ReleasedDate = releasedDate;
    }

    private static int ValidateRating(int rating)
    {
        Guard.Against(rating is < 1 or > 5, "REVIEW_RATING_OUT_OF_RANGE", "Rating must be between 1 and 5.");
        return rating;
    }
}
