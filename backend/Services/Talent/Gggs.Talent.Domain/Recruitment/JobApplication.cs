using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Recruitment;

/// <summary>
/// Where an application sits in the hiring pipeline. A fixed sequence rather than tenant-configurable
/// <c>rec.PipelineStage</c> rows (architecture.md §7.2): v1 has no admin screen to define stages, and
/// a fixed happy path (plus the two ways out) is enough to prove the application record itself before
/// building the configuration layer nothing yet needs.
/// </summary>
public enum ApplicationStage
{
    Applied = 1,
    Screening = 2,
    Interview = 3,
    Offer = 4,
    Hired = 5,
    Rejected = 6,
}

/// <summary>
/// A candidate's application to one posting. <see cref="Stage"/> only ever moves forward through
/// the happy path (<see cref="Advance"/>) or sideways into <see cref="ApplicationStage.Rejected"/>
/// (<see cref="Reject"/>), which is terminal: a rejected candidate re-applies as a new
/// <see cref="JobApplication"/> rather than being reinstated into this one, keeping the stage
/// history honest about what actually happened.
/// </summary>
public sealed class JobApplication : TenantAggregateRoot
{
    private JobApplication()
    {
    }

    public Guid CandidateId { get; private set; }

    public Guid PostingId { get; private set; }

    public ApplicationStage Stage { get; private set; }

    public string? RejectionReason { get; private set; }

    public static JobApplication Create(Guid candidateId, Guid postingId) => new()
    {
        CandidateId = Guard.NotEmpty(candidateId),
        PostingId = Guard.NotEmpty(postingId),
        Stage = ApplicationStage.Applied,
        IsActive = true,
    };

    /// <summary>
    /// Moves one step along the happy path: Applied -> Screening -> Interview -> Offer. Stops at
    /// Offer rather than continuing to Hired - <see cref="MarkHired"/> is the only way to reach it,
    /// so an application can never be marked Hired without a real <c>Offer</c> aggregate having
    /// actually been accepted first.
    /// </summary>
    public void Advance()
    {
        Guard.Against(
            Stage is ApplicationStage.Offer or ApplicationStage.Hired or ApplicationStage.Rejected,
            "APPLICATION_CANNOT_ADVANCE",
            Stage == ApplicationStage.Offer
                ? "This application is at the Offer stage; it moves to Hired only once an offer is accepted."
                : $"This application is already {Stage} and cannot move further.");

        Stage = Stage switch
        {
            ApplicationStage.Applied => ApplicationStage.Screening,
            ApplicationStage.Screening => ApplicationStage.Interview,
            ApplicationStage.Interview => ApplicationStage.Offer,
            _ => throw new InvalidOperationException($"Unreachable: {Stage} was already excluded above."),
        };
    }

    /// <summary>The only path to Hired - called once the candidate's <c>Offer</c> is accepted.</summary>
    public void MarkHired()
    {
        Guard.Against(
            Stage != ApplicationStage.Offer,
            "APPLICATION_NOT_AT_OFFER_STAGE",
            $"This application must be at the Offer stage to be marked Hired; it is {Stage}.");

        Stage = ApplicationStage.Hired;
    }

    public void Reject(string? reason)
    {
        Guard.Against(
            Stage is ApplicationStage.Hired or ApplicationStage.Rejected,
            "APPLICATION_ALREADY_TERMINAL",
            $"This application is already {Stage} and cannot be rejected.");

        Stage = ApplicationStage.Rejected;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500);
    }
}
