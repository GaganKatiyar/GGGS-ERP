using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

/// <summary>
/// Recording that a candidate applied to a posting - the internal recruiter-entry path this v1
/// slice covers (see <see cref="Domain.Recruitment.Candidate"/>'s own remarks on what a public
/// apply flow would still need). Finds-or-creates the candidate by email rather than requiring the
/// caller to look one up first, the same "the write endpoint owns idempotency" shape
/// <c>LeaveLedgerService.PostAsync</c> uses for its own natural-key check.
/// </summary>
public sealed class JobApplicationService(
    ICandidateRepository candidates,
    IJobApplicationRepository applications,
    IJobPostingRepository postings,
    IUnitOfWork unitOfWork)
{
    public async Task<JobApplication> ApplyAsync(
        Guid postingId,
        string firstName,
        string? lastName,
        string email,
        string? phone,
        string? currentEmployer,
        int? totalExperienceMonths,
        string? source,
        CancellationToken cancellationToken = default)
    {
        JobPosting posting = await postings.GetAsync(postingId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobPosting), postingId);

        Guard(
            posting.Status == JobPostingStatus.Published,
            "POSTING_NOT_PUBLISHED_FOR_APPLICATION",
            $"Posting '{posting.Title}' must be Published to accept applications; it is {posting.Status}.");

        string normalizedEmail = Candidate.NormalizeEmail(email);
        Candidate? candidate = await candidates.GetByEmailAsync(normalizedEmail, cancellationToken);

        if (candidate is null)
        {
            candidate = Candidate.Create(firstName, lastName, email, phone, currentEmployer, totalExperienceMonths, source);
            candidates.Add(candidate);
        }

        Guard(
            !await applications.ExistsForCandidateAndPostingAsync(candidate.Id, postingId, cancellationToken),
            "APPLICATION_ALREADY_EXISTS",
            $"'{candidate.DisplayName}' has already applied to this posting.");

        JobApplication application = JobApplication.Create(candidate.Id, postingId);
        applications.Add(application);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return application;
    }

    public async Task<JobApplication> AdvanceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobApplication application = await GetOrThrowAsync(id, cancellationToken);
        application.Advance();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return application;
    }

    public async Task<JobApplication> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        JobApplication application = await GetOrThrowAsync(id, cancellationToken);
        application.Reject(reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return application;
    }

    private async Task<JobApplication> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await applications.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(JobApplication), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
