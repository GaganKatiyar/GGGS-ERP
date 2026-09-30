using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Recruitment;

public enum JobPostingStatus
{
    Draft = 1,
    Published = 2,
    Closed = 3,
}

/// <summary>
/// A posting makes a <see cref="JobRequisition"/> visible for applications - internally, publicly,
/// or both. v1 has no external channel fan-out (LinkedIn, Indeed - architecture.md's
/// <c>rec.PostingChannel</c>) and no public careers page to render it on: those need the Integration
/// Hub and the public careers portal, both later phases. This is the posting record alone.
/// <para>
/// <see cref="Slug"/> is reserved from creation (Draft included) rather than only once published,
/// so an editor can share/preview a draft's future URL and a later publish never discovers a
/// collision it did not expect.
/// </para>
/// </summary>
public sealed class JobPosting : TenantAggregateRoot
{
    private JobPosting()
    {
        Title = null!;
        DescriptionHtml = null!;
        Slug = null!;
    }

    public Guid RequisitionId { get; private set; }

    public string Title { get; private set; }

    public string DescriptionHtml { get; private set; }

    public string? Location { get; private set; }

    public bool IsInternal { get; private set; }

    public bool IsPublic { get; private set; }

    public string Slug { get; private set; }

    public DateTime? PublishedDate { get; private set; }

    public DateOnly? ClosesDate { get; private set; }

    public JobPostingStatus Status { get; private set; }

    public static JobPosting Create(
        Guid requisitionId,
        string title,
        string descriptionHtml,
        string? location,
        bool isInternal,
        bool isPublic,
        string slug,
        DateOnly? closesDate)
    {
        Guard.Against(
            !isInternal && !isPublic, "POSTING_NEEDS_A_VISIBILITY", "A posting must be internal, public, or both.");

        return new JobPosting
        {
            RequisitionId = Guard.NotEmpty(requisitionId),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            DescriptionHtml = Guard.NotNullOrWhiteSpace(descriptionHtml),
            Location = string.IsNullOrWhiteSpace(location) ? null : Guard.MaxLength(location.Trim(), 200),
            IsInternal = isInternal,
            IsPublic = isPublic,
            Slug = NormalizeSlug(slug),
            ClosesDate = closesDate,
            Status = JobPostingStatus.Draft,
            IsActive = true,
        };
    }

    /// <summary>Draft -> Published: visible wherever <see cref="IsInternal"/>/<see cref="IsPublic"/>
    /// say it should be, from now.</summary>
    public void Publish(DateTime publishedDate)
    {
        Guard.Against(
            Status != JobPostingStatus.Draft,
            "POSTING_NOT_DRAFT",
            $"Posting '{Title}' must be Draft to publish; it is {Status}.");

        Status = JobPostingStatus.Published;
        PublishedDate = publishedDate;
    }

    public void Close()
    {
        Guard.Against(
            Status != JobPostingStatus.Published,
            "POSTING_NOT_PUBLISHED",
            $"Posting '{Title}' must be Published to close; it is {Status}.");

        Status = JobPostingStatus.Closed;
    }

    private static string NormalizeSlug(string slug)
    {
        string trimmed = Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug).Trim().ToLowerInvariant(), 200);

        Guard.Against(
            !trimmed.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'),
            "POSTING_SLUG_INVALID",
            "A slug may only contain lowercase letters, digits and hyphens.");

        return trimmed;
    }
}
