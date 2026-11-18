using Gggs.Modules.Recruitment.Domain;

namespace Gggs.Modules.Recruitment.Application;

/// <summary>What has to happen next on an application, and by when.</summary>
public sealed record NextStep(string Text, DateOnly Due);

/// <summary>An interview as the next-step rule needs it.</summary>
public sealed record InterviewFact(string Round, DateTime ScheduledAt, InterviewStatus Status);

/// <summary>The application's latest offer as the next-step rule needs it.</summary>
public sealed record OfferFact(OfferStatus Status, DateTime CreatedDate, DateTime? SentDate, DateOnly? ExpiresOn, DateOnly JoiningDate);

/// <summary>
/// Works out an application's next step from where it stands, so recruiters see one list of what is
/// due without anyone keeping a to-do field up to date. Nothing is stored: the answer follows from
/// the stage, the interviews and the latest offer. A closed application (hired, rejected,
/// withdrawn) has no next step.
/// </summary>
public static class NextSteps
{
    /// <summary>Days a recruiter has for each step before it counts as late.</summary>
    public const int ReviewDays = 3, ScreeningDays = 5, ScheduleDays = 3, DecideDays = 2, FeedbackDays = 1, OfferDays = 2, ReplyDays = 7;

    public static NextStep? For(ApplicationStage stage, DateTime appliedDate, DateTime stageChangedDate, IReadOnlyCollection<InterviewFact> interviews, OfferFact? offer)
    {
        ArgumentNullException.ThrowIfNull(interviews);
        DateOnly changed = DateOnly.FromDateTime(stageChangedDate);

        return stage switch
        {
            ApplicationStage.Applied => new("Review the application", DateOnly.FromDateTime(appliedDate).AddDays(ReviewDays)),
            ApplicationStage.Screening => new("Finish screening", changed.AddDays(ScreeningDays)),
            ApplicationStage.Shortlisted => new("Schedule an interview", changed.AddDays(ScheduleDays)),
            ApplicationStage.Interview => ForInterview(changed, interviews),
            ApplicationStage.Offer => ForOffer(changed, offer),
            _ => null,
        };
    }

    private static NextStep ForInterview(DateOnly changed, IReadOnlyCollection<InterviewFact> interviews)
    {
        // The earliest round still on the calendar: its feedback once it has happened, otherwise the round itself.
        InterviewFact? scheduled = interviews.Where(row => row.Status == InterviewStatus.Scheduled).MinBy(row => row.ScheduledAt);
        if (scheduled is not null)
        {
            DateOnly on = DateOnly.FromDateTime(scheduled.ScheduledAt);
            return scheduled.ScheduledAt <= DateTime.UtcNow
                ? new($"Feedback for {scheduled.Round}", on.AddDays(FeedbackDays))
                : new($"{scheduled.Round} interview", on);
        }

        InterviewFact? last = interviews.Where(row => row.Status == InterviewStatus.Completed).MaxBy(row => row.ScheduledAt);
        return last is null
            ? new("Schedule the first round", changed.AddDays(ScheduleDays))
            : new($"Decide after {last.Round}", DateOnly.FromDateTime(last.ScheduledAt).AddDays(DecideDays));
    }

    private static NextStep ForOffer(DateOnly changed, OfferFact? offer) => offer?.Status switch
    {
        null => new("Draft the offer", changed.AddDays(OfferDays)),
        OfferStatus.Draft => new("Submit the offer for approval", DateOnly.FromDateTime(offer.CreatedDate).AddDays(OfferDays)),
        OfferStatus.PendingApproval => new("Offer awaiting approval", DateOnly.FromDateTime(offer.CreatedDate).AddDays(OfferDays)),
        OfferStatus.Approved => new("Send the offer", DateOnly.FromDateTime(offer.CreatedDate).AddDays(OfferDays)),
        OfferStatus.Sent => new("Offer sent, awaiting reply", offer.ExpiresOn ?? DateOnly.FromDateTime(offer.SentDate ?? offer.CreatedDate).AddDays(ReplyDays)),
        OfferStatus.Accepted => new("Hire and start onboarding", offer.JoiningDate),
        _ => new("Make a new offer or close the application", changed.AddDays(OfferDays)),
    };
}
