using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

/// <summary>
/// Postings are checked against their parent requisition on every write that matters
/// (<see cref="CreateAsync"/> requires it Open; nothing here re-checks on publish/close, since the
/// requisition's own later status change - Closed, Cancelled - does not retroactively invalidate a
/// posting already live).
/// </summary>
public sealed class JobPostingService(
    IJobPostingRepository postings, IJobRequisitionRepository requisitions, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<JobPosting> CreateAsync(
        Guid requisitionId,
        string title,
        string descriptionHtml,
        string? location,
        bool isInternal,
        bool isPublic,
        string slug,
        DateOnly? closesDate,
        CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await requisitions.GetAsync(requisitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobRequisition), requisitionId);

        Guard(
            requisition.Status == JobRequisitionStatus.Open,
            "REQUISITION_NOT_OPEN_FOR_POSTING",
            $"Requisition '{requisition.RequisitionNo}' must be Open to post against; it is {requisition.Status}.");

        string normalizedSlug = slug.Trim().ToLowerInvariant();

        Guard(
            !await postings.SlugExistsAsync(normalizedSlug, cancellationToken),
            "POSTING_SLUG_IN_USE",
            $"A posting with slug '{normalizedSlug}' already exists.");

        JobPosting posting = JobPosting.Create(
            requisitionId, title, descriptionHtml, location, isInternal, isPublic, normalizedSlug, closesDate);

        postings.Add(posting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return posting;
    }

    public async Task<JobPosting> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobPosting posting = await GetOrThrowAsync(id, cancellationToken);
        posting.Publish(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return posting;
    }

    public async Task<JobPosting> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobPosting posting = await GetOrThrowAsync(id, cancellationToken);
        posting.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return posting;
    }

    private async Task<JobPosting> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await postings.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(JobPosting), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
