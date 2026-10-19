using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Performance.Domain;

/// <summary>The five-point rating scale and how a review's score is worked out.</summary>
public static class RatingScale
{
    public const int Min = 1;
    public const int Max = 5;

    public static IReadOnlyDictionary<int, string> Labels { get; } = new Dictionary<int, string>
    {
        [1] = "Unsatisfactory",
        [2] = "Needs improvement",
        [3] = "Meets expectations",
        [4] = "Exceeds expectations",
        [5] = "Outstanding",
    };

    /// <summary>The guidance for calibration: what share of people should land on each rating.</summary>
    public static IReadOnlyDictionary<int, int> DefaultDistribution { get; } = new Dictionary<int, int> { [1] = 5, [2] = 15, [3] = 50, [4] = 20, [5] = 10 };

    public static string Label(int rating) => Labels.TryGetValue(rating, out string? label) ? label : rating.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Goal achievement (0-100%) on the 1-5 scale: 0% is 1, 100% is 5.</summary>
    public static decimal FromProgress(decimal progress) => Math.Round(1 + (Math.Clamp(progress, 0, 100) * 4 / 100), 2);

    /// <summary>
    /// A review's score: goals (their weighted achievement on the scale) and competencies (the average
    /// rating) blended by the cycle's goal weight. Either half alone when the other has nothing.
    /// </summary>
    public static decimal? Score(decimal? goalRating, IReadOnlyDictionary<string, int> competencies, decimal goalWeight)
    {
        ArgumentNullException.ThrowIfNull(competencies);
        decimal? competency = competencies.Count == 0 ? null : (decimal)competencies.Values.Average();
        return (goalRating, competency) switch
        {
            (null, null) => null,
            ({ } goals, null) => Math.Round(goals, 2),
            (null, { } skills) => Math.Round(skills, 2),
            ({ } goals, { } skills) => Math.Round(((goals * goalWeight) + (skills * (100 - goalWeight))) / 100, 2),
        };
    }

    /// <summary>Goals' achievement weighted by their weights (equal weights when none are set).</summary>
    public static decimal? GoalAchievement(IReadOnlyCollection<(decimal Progress, decimal Weight)> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        if (goals.Count == 0)
        {
            return null;
        }

        decimal weights = goals.Sum(goal => goal.Weight);
        return weights == 0
            ? Math.Round(goals.Average(goal => goal.Progress), 1)
            : Math.Round(goals.Sum(goal => goal.Progress * goal.Weight) / weights, 1);
    }

    public static void EnsureRatings(IReadOnlyDictionary<string, int>? ratings)
    {
        if (ratings is not null && ratings.Values.Any(value => value is < Min or > Max))
        {
            throw new DomainRuleException("REVIEW_RATING", "Ratings are from 1 to 5.");
        }
    }
}

public enum CycleType
{
    Annual = 1,
    HalfYearly = 2,
    Quarterly = 3,
    Probation = 4,
}

public enum CycleStatus
{
    Draft = 1,
    Active = 2,
    Calibration = 3,
    Released = 4,
    Closed = 5,
}

public sealed record CycleDetails(
    string Name,
    CycleType Type,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly SelfReviewDue,
    DateOnly ManagerReviewDue,
    IReadOnlyList<string> Competencies,
    decimal GoalWeight,
    bool PeerFeedback,
    IReadOnlyList<Guid>? DepartmentIds);

/// <summary>
/// A review cycle (spec: performance): the period reviewed, the competencies rated, how goals and
/// competencies are weighed, and its stages - launch, self and manager reviews, calibration, release.
/// </summary>
public sealed class ReviewCycle : TenantAggregateRoot
{
    private ReviewCycle()
    {
        Name = null!;
        CompetenciesJson = "[]";
        DepartmentsJson = "[]";
    }

    public string Name { get; private set; }

    public CycleType Type { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public DateOnly SelfReviewDue { get; private set; }

    public DateOnly ManagerReviewDue { get; private set; }

    public string CompetenciesJson { get; private set; }

    /// <summary>The share of the score that comes from goals; the rest comes from competencies.</summary>
    public decimal GoalWeight { get; private set; }

    public bool PeerFeedback { get; private set; }

    /// <summary>Who takes part: these departments, or everyone when empty.</summary>
    public string DepartmentsJson { get; private set; }

    public CycleStatus Status { get; private set; }

    public DateOnly? LaunchedOn { get; private set; }

    public DateOnly? ReleasedOn { get; private set; }

    public IReadOnlyList<string> Competencies => JsonSerializer.Deserialize<List<string>>(CompetenciesJson) ?? [];

    public IReadOnlyList<Guid> DepartmentIds => JsonSerializer.Deserialize<List<Guid>>(DepartmentsJson) ?? [];

    public static ReviewCycle Draft(CycleDetails details)
    {
        var cycle = new ReviewCycle { Status = CycleStatus.Draft };
        cycle.Update(details);
        return cycle;
    }

    public void Update(CycleDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is CycleStatus.Released or CycleStatus.Closed)
        {
            throw new DomainRuleException("CYCLE_STATE", "A released cycle cannot change.");
        }

        if (details.PeriodEnd < details.PeriodStart || details.ManagerReviewDue < details.SelfReviewDue)
        {
            throw new DomainRuleException("CYCLE_DATES", "The period ends before it starts, or manager reviews are due before self-reviews.");
        }

        if (details.GoalWeight is < 0 or > 100)
        {
            throw new DomainRuleException("CYCLE_WEIGHT", "The goal weight is between 0 and 100.");
        }

        List<string> competencies = [.. (details.Competencies ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => Guard.MaxLength(item.Trim(), 80)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (competencies.Count == 0 && details.GoalWeight < 100)
        {
            throw new DomainRuleException("CYCLE_COMPETENCIES", "Name the competencies to rate, or weigh goals at 100%.");
        }

        if (Status != CycleStatus.Draft && (details.PeriodStart != PeriodStart || details.PeriodEnd != PeriodEnd))
        {
            throw new DomainRuleException("CYCLE_STATE", "A launched cycle keeps its period; only due dates and settings can move.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Type = details.Type;
        PeriodStart = details.PeriodStart;
        PeriodEnd = details.PeriodEnd;
        SelfReviewDue = details.SelfReviewDue;
        ManagerReviewDue = details.ManagerReviewDue;
        CompetenciesJson = JsonSerializer.Serialize(competencies);
        GoalWeight = details.GoalWeight;
        PeerFeedback = details.PeerFeedback;
        if (Status == CycleStatus.Draft)
        {
            DepartmentsJson = JsonSerializer.Serialize((details.DepartmentIds ?? []).Distinct().ToList());
        }
    }

    public void Launch(DateOnly today)
    {
        Ensure(CycleStatus.Draft, "Only a draft cycle can be launched.");
        Status = CycleStatus.Active;
        LaunchedOn = today;
    }

    public void StartCalibration()
    {
        Ensure(CycleStatus.Active, "Calibration follows the review stage.");
        Status = CycleStatus.Calibration;
    }

    public void MarkReleased(DateOnly today)
    {
        if (Status is not (CycleStatus.Active or CycleStatus.Calibration or CycleStatus.Released))
        {
            throw new DomainRuleException("CYCLE_STATE", "Reviews are released once the cycle is running.");
        }

        Status = CycleStatus.Released;
        ReleasedOn ??= today;
    }

    public void Close()
    {
        Ensure(CycleStatus.Released, "Only a released cycle can be closed.");
        Status = CycleStatus.Closed;
    }

    public void EnsureRunning()
    {
        if (Status is not (CycleStatus.Active or CycleStatus.Calibration))
        {
            throw new DomainRuleException("CYCLE_STATE", $"The cycle is {Status.ToString().ToLowerInvariant()}.");
        }
    }

    private void Ensure(CycleStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("CYCLE_STATE", message);
        }
    }
}

public enum ReviewStatus
{
    /// <summary>Waiting for the self-review.</summary>
    Pending = 1,
    SelfSubmitted = 2,
    ManagerSubmitted = 3,
    Released = 4,
    Acknowledged = 5,
}

/// <summary>One half of a review: competency ratings, comments and an overall rating.</summary>
public sealed record ReviewInput(IReadOnlyDictionary<string, int>? Ratings, string? Strengths, string? Improvements, string? Comments, int? Overall);

/// <summary>An employee's review in a cycle: their self-review, their manager's review, calibration and release.</summary>
public sealed class Review : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Review()
    {
        SelfJson = "{}";
        ManagerJson = "{}";
    }

    public Guid CycleId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public ReviewStatus Status { get; private set; }

    public string SelfJson { get; private set; }

    public DateTime? SelfSubmittedDate { get; private set; }

    public string ManagerJson { get; private set; }

    public DateTime? ManagerSubmittedDate { get; private set; }

    /// <summary>Goal achievement (0-100) when the manager submitted.</summary>
    public decimal? GoalAchievement { get; private set; }

    /// <summary>The blended score on the 1-5 scale when the manager submitted.</summary>
    public decimal? Score { get; private set; }

    public int? CalibratedRating { get; private set; }

    public string? CalibrationNote { get; private set; }

    public int? FinalRating { get; private set; }

    public DateTime? ReleasedDate { get; private set; }

    public DateTime? AcknowledgedDate { get; private set; }

    public string? EmployeeComment { get; private set; }

    public DateOnly? LastRemindedOn { get; private set; }

    public ReviewInput Self => JsonSerializer.Deserialize<ReviewInput>(SelfJson, Json) ?? new ReviewInput(null, null, null, null, null);

    public ReviewInput Manager => JsonSerializer.Deserialize<ReviewInput>(ManagerJson, Json) ?? new ReviewInput(null, null, null, null, null);

    /// <summary>The rating the manager gave, as calibration starts from it.</summary>
    public int? ManagerRating => Manager.Overall;

    public static Review Open(Guid cycleId, Guid employeeId, Guid? managerEmployeeId) => new()
    {
        CycleId = Guard.NotEmpty(cycleId),
        EmployeeId = Guard.NotEmpty(employeeId),
        ManagerEmployeeId = managerEmployeeId,
        Status = ReviewStatus.Pending,
    };

    public void SaveSelf(ReviewInput input, bool submit, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Status != ReviewStatus.Pending)
        {
            throw new DomainRuleException("REVIEW_STATE", "The self-review has already been submitted.");
        }

        RatingScale.EnsureRatings(input.Ratings);
        EnsureOverall(input.Overall);
        SelfJson = JsonSerializer.Serialize(Clean(input), Json);
        if (submit)
        {
            Status = ReviewStatus.SelfSubmitted;
            SelfSubmittedDate = now;
        }
    }

    /// <summary>The manager's half. A manager can review without waiting when the employee has not submitted.</summary>
    public void SaveManager(ReviewInput input, bool submit, decimal? goalAchievement, decimal? score, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Status is not (ReviewStatus.Pending or ReviewStatus.SelfSubmitted))
        {
            throw new DomainRuleException("REVIEW_STATE", "The manager review has already been submitted.");
        }

        RatingScale.EnsureRatings(input.Ratings);
        EnsureOverall(input.Overall);
        if (submit && input.Overall is null)
        {
            throw new DomainRuleException("REVIEW_RATING", "Give an overall rating before submitting.");
        }

        ManagerJson = JsonSerializer.Serialize(Clean(input), Json);
        GoalAchievement = goalAchievement;
        Score = score;
        if (submit)
        {
            Status = ReviewStatus.ManagerSubmitted;
            ManagerSubmittedDate = now;
        }
    }

    /// <summary>Sends a submitted manager review back for changes, before release.</summary>
    public void ReturnToManager()
    {
        if (Status != ReviewStatus.ManagerSubmitted)
        {
            throw new DomainRuleException("REVIEW_STATE", "Only a submitted manager review can be returned.");
        }

        Status = SelfSubmittedDate is null ? ReviewStatus.Pending : ReviewStatus.SelfSubmitted;
        ManagerSubmittedDate = null;
    }

    public void Calibrate(int rating, string? note)
    {
        if (Status != ReviewStatus.ManagerSubmitted)
        {
            throw new DomainRuleException("REVIEW_STATE", "Calibrate a review once the manager has submitted it.");
        }

        EnsureOverall(rating);
        CalibratedRating = rating;
        CalibrationNote = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 1000);
    }

    public void Release(DateTime now)
    {
        if (Status != ReviewStatus.ManagerSubmitted)
        {
            throw new DomainRuleException("REVIEW_STATE", "Only a manager-submitted review can be released.");
        }

        FinalRating = CalibratedRating ?? ManagerRating;
        Status = ReviewStatus.Released;
        ReleasedDate = now;
    }

    public void Acknowledge(string? comment, DateTime now)
    {
        if (Status != ReviewStatus.Released)
        {
            throw new DomainRuleException("REVIEW_STATE", "Only a released review can be acknowledged.");
        }

        Status = ReviewStatus.Acknowledged;
        AcknowledgedDate = now;
        EmployeeComment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 2000);
    }

    public void Reassign(Guid? managerEmployeeId) => ManagerEmployeeId = managerEmployeeId;

    public bool NeedsReminder(DateOnly today, DateOnly due) =>
        Status is ReviewStatus.Pending or ReviewStatus.SelfSubmitted && today >= due.AddDays(-3) && (LastRemindedOn is null || LastRemindedOn.Value.AddDays(3) <= today);

    public void Reminded(DateOnly today) => LastRemindedOn = today;

    private static void EnsureOverall(int? overall)
    {
        if (overall is < RatingScale.Min or > RatingScale.Max)
        {
            throw new DomainRuleException("REVIEW_RATING", "Ratings are from 1 to 5.");
        }
    }

    private static ReviewInput Clean(ReviewInput input) => new(
        input.Ratings?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)).ToDictionary(pair => pair.Key.Trim(), pair => pair.Value),
        Trim(input.Strengths),
        Trim(input.Improvements),
        Trim(input.Comments),
        input.Overall);

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : Guard.MaxLength(text.Trim(), 4000);
}

public enum FeedbackRelationship
{
    Peer = 1,
    DirectReport = 2,
    Manager = 3,
    Other = 4,
}

public enum FeedbackStatus
{
    Requested = 1,
    Submitted = 2,
    Declined = 3,
}

/// <summary>360 feedback asked of a colleague for someone's review.</summary>
public sealed class FeedbackRequest : TenantAggregateRoot
{
    private FeedbackRequest()
    {
        RatingsJson = "{}";
    }

    public Guid ReviewId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid ReviewerEmployeeId { get; private set; }

    public Guid? RequestedByEmployeeId { get; private set; }

    public FeedbackRelationship Relationship { get; private set; }

    public FeedbackStatus Status { get; private set; }

    public string RatingsJson { get; private set; }

    public string? Strengths { get; private set; }

    public string? Improvements { get; private set; }

    public DateTime? RespondedDate { get; private set; }

    public IReadOnlyDictionary<string, int> Ratings => JsonSerializer.Deserialize<Dictionary<string, int>>(RatingsJson) ?? [];

    public static FeedbackRequest Ask(Guid reviewId, Guid employeeId, Guid reviewerEmployeeId, Guid? requestedBy, FeedbackRelationship relationship)
    {
        if (reviewerEmployeeId == employeeId)
        {
            throw new DomainRuleException("FEEDBACK_SELF", "Ask someone other than the person being reviewed.");
        }

        return new FeedbackRequest
        {
            ReviewId = Guard.NotEmpty(reviewId),
            EmployeeId = employeeId,
            ReviewerEmployeeId = Guard.NotEmpty(reviewerEmployeeId),
            RequestedByEmployeeId = requestedBy,
            Relationship = relationship,
            Status = FeedbackStatus.Requested,
        };
    }

    public void Submit(IReadOnlyDictionary<string, int>? ratings, string? strengths, string? improvements, DateTime now)
    {
        EnsureOpen();
        RatingScale.EnsureRatings(ratings);
        if (string.IsNullOrWhiteSpace(strengths) && string.IsNullOrWhiteSpace(improvements) && (ratings is null || ratings.Count == 0))
        {
            throw new DomainRuleException("FEEDBACK_EMPTY", "Say something: a rating, a strength or something to work on.");
        }

        RatingsJson = JsonSerializer.Serialize(ratings ?? new Dictionary<string, int>());
        Strengths = string.IsNullOrWhiteSpace(strengths) ? null : Guard.MaxLength(strengths.Trim(), 4000);
        Improvements = string.IsNullOrWhiteSpace(improvements) ? null : Guard.MaxLength(improvements.Trim(), 4000);
        Status = FeedbackStatus.Submitted;
        RespondedDate = now;
    }

    public void Decline(DateTime now)
    {
        EnsureOpen();
        Status = FeedbackStatus.Declined;
        RespondedDate = now;
    }

    private void EnsureOpen()
    {
        if (Status != FeedbackStatus.Requested)
        {
            throw new DomainRuleException("FEEDBACK_STATE", "This feedback has already been given or declined.");
        }
    }
}

public enum PipStatus
{
    Draft = 1,
    Active = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

public sealed record PipObjective(string Title, string? Measure, bool? Met);

public sealed record PipNote(DateOnly On, Guid? ByEmployeeId, string Text);

public sealed record PipDetails(string Reason, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<PipObjective> Objectives, Guid? ReviewId);

/// <summary>A performance improvement plan: what must improve, by when, the check-ins along the way and the outcome.</summary>
public sealed class ImprovementPlan : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ImprovementPlan()
    {
        Reason = null!;
        ObjectivesJson = "[]";
        NotesJson = "[]";
    }

    public Guid EmployeeId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public Guid? ReviewId { get; private set; }

    public string Reason { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public string ObjectivesJson { get; private set; }

    public string NotesJson { get; private set; }

    public PipStatus Status { get; private set; }

    public string? Outcome { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public bool Extended { get; private set; }

    public IReadOnlyList<PipObjective> Objectives => JsonSerializer.Deserialize<List<PipObjective>>(ObjectivesJson, Json) ?? [];

    public IReadOnlyList<PipNote> Notes => JsonSerializer.Deserialize<List<PipNote>>(NotesJson, Json) ?? [];

    public static ImprovementPlan Draft(Guid employeeId, Guid? managerEmployeeId, PipDetails details)
    {
        var plan = new ImprovementPlan { EmployeeId = Guard.NotEmpty(employeeId), ManagerEmployeeId = managerEmployeeId, Status = PipStatus.Draft };
        plan.Update(details);
        return plan;
    }

    public void Update(PipDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is not (PipStatus.Draft or PipStatus.Active))
        {
            throw new DomainRuleException("PIP_STATE", "A closed plan cannot change.");
        }

        if (details.EndDate <= details.StartDate || details.EndDate > details.StartDate.AddDays(180))
        {
            throw new DomainRuleException("PIP_DATES", "A plan runs for up to 180 days and ends after it starts.");
        }

        List<PipObjective> objectives = [.. (details.Objectives ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new PipObjective(Guard.MaxLength(item.Title.Trim(), 300), string.IsNullOrWhiteSpace(item.Measure) ? null : Guard.MaxLength(item.Measure.Trim(), 500), item.Met))];
        if (objectives.Count == 0)
        {
            throw new DomainRuleException("PIP_OBJECTIVES", "Say what must improve: at least one objective.");
        }

        Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Reason).Trim(), 2000);
        StartDate = details.StartDate;
        EndDate = details.EndDate;
        ObjectivesJson = JsonSerializer.Serialize(objectives, Json);
        ReviewId = details.ReviewId;
    }

    public void Start()
    {
        if (Status != PipStatus.Draft)
        {
            throw new DomainRuleException("PIP_STATE", "Only a draft plan can start.");
        }

        Status = PipStatus.Active;
    }

    public void AddNote(DateOnly on, Guid? byEmployeeId, string text)
    {
        if (Status != PipStatus.Active)
        {
            throw new DomainRuleException("PIP_STATE", "Check-ins are recorded on an active plan.");
        }

        List<PipNote> notes = [.. Notes, new PipNote(on, byEmployeeId, Guard.MaxLength(Guard.NotNullOrWhiteSpace(text).Trim(), 2000))];
        NotesJson = JsonSerializer.Serialize(notes, Json);
    }

    public void Extend(DateOnly newEnd)
    {
        if (Status != PipStatus.Active || newEnd <= EndDate)
        {
            throw new DomainRuleException("PIP_STATE", "Extend an active plan to a later date.");
        }

        EndDate = newEnd;
        Extended = true;
    }

    public void Close(bool succeeded, string outcome, IReadOnlyList<PipObjective>? objectives, DateOnly today)
    {
        if (Status != PipStatus.Active)
        {
            throw new DomainRuleException("PIP_STATE", "Only an active plan can be closed.");
        }

        if (objectives is { Count: > 0 })
        {
            ObjectivesJson = JsonSerializer.Serialize(objectives, Json);
        }

        Status = succeeded ? PipStatus.Succeeded : PipStatus.Failed;
        Outcome = Guard.MaxLength(Guard.NotNullOrWhiteSpace(outcome).Trim(), 2000);
        ClosedOn = today;
    }

    public void Cancel(DateOnly today)
    {
        if (Status is not (PipStatus.Draft or PipStatus.Active))
        {
            throw new DomainRuleException("PIP_STATE", "The plan is already closed.");
        }

        Status = PipStatus.Cancelled;
        ClosedOn = today;
    }
}
