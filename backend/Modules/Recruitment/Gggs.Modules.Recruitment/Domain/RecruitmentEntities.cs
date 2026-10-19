using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Recruitment.Domain;

public enum RequisitionStatus
{
    Open = 1,
    OnHold = 2,
    Filled = 3,
    Closed = 4,
    Cancelled = 5,
}

/// <summary>What a requisition is hiring for - shared by a manual requisition and one raised from a manpower request.</summary>
public sealed record RequisitionDetails(
    string Title,
    Guid DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? LocationId,
    int Openings,
    string EmploymentTypeCode,
    string WorkModeCode,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears,
    decimal? SalaryMin,
    decimal? SalaryMax,
    IReadOnlyList<string>? Skills,
    string? Description,
    Guid? HiringManagerEmployeeId,
    Guid? RecruiterEmployeeId,
    string? Priority,
    DateOnly? TargetDate);

/// <summary>
/// A requisition (spec §12): openings recruitment is working to fill, usually raised by an approved
/// manpower request. Each hire counts against its openings; when they are all filled it closes.
/// </summary>
public sealed class Requisition : TenantAggregateRoot
{
    private Requisition()
    {
        Number = null!;
        Title = null!;
        EmploymentTypeCode = null!;
        WorkModeCode = null!;
        SkillsJson = "[]";
    }

    public string Number { get; private set; }

    public Guid? ManpowerRequestId { get; private set; }

    public string Title { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? PositionId { get; private set; }

    public Guid? JobId { get; private set; }

    public Guid? LocationId { get; private set; }

    public int Openings { get; private set; }

    public int Hired { get; private set; }

    public string EmploymentTypeCode { get; private set; }

    public string WorkModeCode { get; private set; }

    public decimal? MinExperienceYears { get; private set; }

    public decimal? MaxExperienceYears { get; private set; }

    public decimal? SalaryMin { get; private set; }

    public decimal? SalaryMax { get; private set; }

    public string SkillsJson { get; private set; }

    public string? Description { get; private set; }

    public Guid? HiringManagerEmployeeId { get; private set; }

    public Guid? RecruiterEmployeeId { get; private set; }

    public string? Priority { get; private set; }

    public DateOnly? TargetDate { get; private set; }

    public RequisitionStatus Status { get; private set; }

    public DateOnly OpenedOn { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public IReadOnlyList<string> Skills => JsonSerializer.Deserialize<List<string>>(SkillsJson) ?? [];

    public int Remaining => Math.Max(0, Openings - Hired);

    public static Requisition Open(string number, Guid? manpowerRequestId, RequisitionDetails details, DateOnly today)
    {
        var requisition = new Requisition
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30),
            ManpowerRequestId = manpowerRequestId,
            Status = RequisitionStatus.Open,
            OpenedOn = today,
        };
        requisition.Apply(details);
        return requisition;
    }

    public void Update(RequisitionDetails details)
    {
        if (Status is RequisitionStatus.Filled or RequisitionStatus.Closed or RequisitionStatus.Cancelled)
        {
            throw new DomainRuleException("REQUISITION_STATE", $"A {Status.ToString().ToLowerInvariant()} requisition cannot change.");
        }

        Apply(details);
        if (Openings < Hired)
        {
            throw new DomainRuleException("REQUISITION_OPENINGS", $"{Hired} people have already been hired against this requisition.");
        }
    }

    public void Hold()
    {
        EnsureStatus(RequisitionStatus.Open);
        Status = RequisitionStatus.OnHold;
    }

    public void Resume()
    {
        EnsureStatus(RequisitionStatus.OnHold);
        Status = RequisitionStatus.Open;
    }

    public void Close(DateOnly today, bool cancelled)
    {
        if (Status is RequisitionStatus.Filled or RequisitionStatus.Closed or RequisitionStatus.Cancelled)
        {
            throw new DomainRuleException("REQUISITION_STATE", "The requisition is already closed.");
        }

        Status = cancelled ? RequisitionStatus.Cancelled : RequisitionStatus.Closed;
        ClosedOn = today;
    }

    public void EnsureAccepting()
    {
        if (Status != RequisitionStatus.Open)
        {
            throw new DomainRuleException("REQUISITION_STATE", $"The requisition is {Status.ToString().ToLowerInvariant()} and takes no applications.");
        }
    }

    /// <summary>Counts a hire. Returns whether that filled the last opening.</summary>
    public bool RecordHire(DateOnly today)
    {
        if (Remaining == 0)
        {
            throw new DomainRuleException("REQUISITION_FILLED", "Every opening of this requisition is already filled.");
        }

        Hired++;
        if (Remaining > 0)
        {
            return false;
        }

        Status = RequisitionStatus.Filled;
        ClosedOn = today;
        return true;
    }

    private void Apply(RequisitionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Openings is < 1 or > 500)
        {
            throw new DomainRuleException("REQUISITION_OPENINGS", "A requisition has between 1 and 500 openings.");
        }

        if (details.MinExperienceYears > details.MaxExperienceYears || details.SalaryMin > details.SalaryMax)
        {
            throw new DomainRuleException("REQUISITION_RANGE", "The minimum experience and salary must not exceed the maximum.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Title).Trim(), 200);
        DepartmentId = Guard.NotEmpty(details.DepartmentId);
        PositionId = details.PositionId;
        JobId = details.JobId;
        LocationId = details.LocationId;
        Openings = details.Openings;
        EmploymentTypeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.EmploymentTypeCode).Trim().ToUpperInvariant(), 64);
        WorkModeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.WorkModeCode).Trim().ToUpperInvariant(), 64);
        MinExperienceYears = details.MinExperienceYears;
        MaxExperienceYears = details.MaxExperienceYears;
        SalaryMin = details.SalaryMin;
        SalaryMax = details.SalaryMax;
        SkillsJson = RecruitmentJson.Skills(details.Skills);
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 8000);
        HiringManagerEmployeeId = details.HiringManagerEmployeeId;
        RecruiterEmployeeId = details.RecruiterEmployeeId;
        Priority = string.IsNullOrWhiteSpace(details.Priority) ? null : Guard.MaxLength(details.Priority.Trim(), 20);
        TargetDate = details.TargetDate;
    }

    private void EnsureStatus(RequisitionStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainRuleException("REQUISITION_STATE", $"The requisition is {Status.ToString().ToLowerInvariant()}.");
        }
    }
}

public enum PostingStatus
{
    Draft = 1,
    Published = 2,
    Closed = 3,
}

/// <summary>A job posting (spec §12): a requisition advertised on the internal job board, the careers page or both.</summary>
public sealed class JobPosting : TenantAggregateRoot
{
    private JobPosting()
    {
        Title = null!;
        Description = null!;
    }

    public Guid RequisitionId { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public bool Internal { get; private set; }

    public bool External { get; private set; }

    public PostingStatus Status { get; private set; }

    public DateOnly? PublishedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public static JobPosting Draft(Guid requisitionId, string title, string description, bool isInternal, bool isExternal, DateOnly? expiresOn)
    {
        var posting = new JobPosting { RequisitionId = Guard.NotEmpty(requisitionId), Status = PostingStatus.Draft };
        posting.Update(title, description, isInternal, isExternal, expiresOn);
        return posting;
    }

    public void Update(string title, string description, bool isInternal, bool isExternal, DateOnly? expiresOn)
    {
        if (!isInternal && !isExternal)
        {
            throw new DomainRuleException("POSTING_CHANNEL", "Post the job internally, externally or both.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(description).Trim(), 8000);
        Internal = isInternal;
        External = isExternal;
        ExpiresOn = expiresOn;
    }

    public void Publish(DateOnly today)
    {
        if (ExpiresOn is { } expires && expires < today)
        {
            throw new DomainRuleException("POSTING_EXPIRED", "The posting's expiry date has passed; move it before publishing.");
        }

        Status = PostingStatus.Published;
        PublishedOn ??= today;
    }

    public void Close() => Status = PostingStatus.Closed;

    public bool IsLive(DateOnly today) => Status == PostingStatus.Published && (ExpiresOn is null || ExpiresOn >= today);
}

public enum CandidateSource
{
    Direct = 1,
    Careers = 2,
    JobBoard = 3,
    Referral = 4,
    Agency = 5,
    Internal = 6,
    Campus = 7,
    Social = 8,
}

/// <summary>The candidate's profile as entered by a recruiter, a referrer or the candidate.</summary>
public sealed record CandidateDetails(
    string FirstName,
    string? LastName,
    string Email,
    string? Mobile,
    string? CurrentCompany,
    string? CurrentTitle,
    decimal? ExperienceYears,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticePeriodDays,
    string? Location,
    IReadOnlyList<string>? Skills,
    string? LinkedInUrl,
    string? Notes);

/// <summary>A candidate (spec §12). One profile per person, found again by email or mobile.</summary>
public sealed class Candidate : TenantAggregateRoot
{
    private Candidate()
    {
        Number = null!;
        FirstName = null!;
        Email = null!;
        SkillsJson = "[]";
    }

    public string Number { get; private set; }

    public string FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string Email { get; private set; }

    public string? Mobile { get; private set; }

    public string? CurrentCompany { get; private set; }

    public string? CurrentTitle { get; private set; }

    public decimal? ExperienceYears { get; private set; }

    public decimal? CurrentCtc { get; private set; }

    public decimal? ExpectedCtc { get; private set; }

    public int? NoticePeriodDays { get; private set; }

    public string? Location { get; private set; }

    public string SkillsJson { get; private set; }

    public string? LinkedInUrl { get; private set; }

    public string? Notes { get; private set; }

    public CandidateSource Source { get; private set; }

    public Guid? AgencyId { get; private set; }

    public Guid? ReferrerEmployeeId { get; private set; }

    /// <summary>The employee record the candidate became, once hired.</summary>
    public Guid? EmployeeId { get; private set; }

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";

    public IReadOnlyList<string> Skills => JsonSerializer.Deserialize<List<string>>(SkillsJson) ?? [];

    public static Candidate Create(string number, CandidateDetails details, CandidateSource source, Guid? agencyId, Guid? referrerEmployeeId)
    {
        if (source == CandidateSource.Agency && agencyId is null)
        {
            throw new DomainRuleException("CANDIDATE_AGENCY", "Say which agency sourced the candidate.");
        }

        if (source == CandidateSource.Referral && referrerEmployeeId is null)
        {
            throw new DomainRuleException("CANDIDATE_REFERRER", "Say who referred the candidate.");
        }

        var candidate = new Candidate
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30),
            Source = source,
            AgencyId = source == CandidateSource.Agency ? agencyId : null,
            ReferrerEmployeeId = source == CandidateSource.Referral ? referrerEmployeeId : null,
        };
        candidate.Update(details);
        return candidate;
    }

    public void Update(CandidateDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        string email = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Email).Trim().ToLowerInvariant(), 256);
        if (!email.Contains('@', StringComparison.Ordinal) || email.StartsWith('@') || email.EndsWith('@'))
        {
            throw new DomainRuleException("CANDIDATE_EMAIL", "Give the candidate a valid email address.");
        }

        if (details.ExperienceYears is < 0 or > 60 || details.NoticePeriodDays is < 0 or > 365 || details.CurrentCtc < 0 || details.ExpectedCtc < 0)
        {
            throw new DomainRuleException("CANDIDATE_DETAILS", "Experience, notice period and CTC must be sensible, non-negative values.");
        }

        FirstName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.FirstName).Trim(), 100);
        LastName = Optional(details.LastName, 100);
        Email = email;
        Mobile = NormalizeMobile(details.Mobile);
        CurrentCompany = Optional(details.CurrentCompany, 200);
        CurrentTitle = Optional(details.CurrentTitle, 200);
        ExperienceYears = details.ExperienceYears;
        CurrentCtc = details.CurrentCtc;
        ExpectedCtc = details.ExpectedCtc;
        NoticePeriodDays = details.NoticePeriodDays;
        Location = Optional(details.Location, 200);
        SkillsJson = RecruitmentJson.Skills(details.Skills);
        LinkedInUrl = Optional(details.LinkedInUrl, 500);
        Notes = Optional(details.Notes, 4000);
    }

    public void BecameEmployee(Guid employeeId) => EmployeeId = employeeId;

    /// <summary>Digits only, so "+91 98450-12345" and "919845012345" are the same person.</summary>
    public static string? NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        string digits = new([.. mobile.Where(char.IsAsciiDigit)]);
        return digits.Length is >= 7 and <= 15
            ? digits
            : throw new DomainRuleException("CANDIDATE_MOBILE", "The mobile number should have 7 to 15 digits.");
    }

    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

public enum ApplicationStage
{
    Applied = 1,
    Screening = 2,
    Shortlisted = 3,
    Interview = 4,
    Offer = 5,
    Hired = 6,
    Rejected = 7,
    Withdrawn = 8,
}

/// <summary>One move along the pipeline.</summary>
public sealed record StageChange(ApplicationStage Stage, DateTime At, Guid? ByEmployeeId, string? Note);

/// <summary>
/// A candidate's application to a requisition (spec §12), moving through the pipeline. The match score is
/// a rule-based aid for recruiters; people make every decision (spec: AI must not decide hiring alone).
/// </summary>
public sealed class JobApplication : TenantAggregateRoot
{
    private JobApplication()
    {
        HistoryJson = "[]";
    }

    public Guid CandidateId { get; private set; }

    public Guid RequisitionId { get; private set; }

    public ApplicationStage Stage { get; private set; }

    public int MatchScore { get; private set; }

    public DateTime AppliedDate { get; private set; }

    public DateTime StageChangedDate { get; private set; }

    public string? RejectionReason { get; private set; }

    public string HistoryJson { get; private set; }

    public IReadOnlyList<StageChange> History => JsonSerializer.Deserialize<List<StageChange>>(HistoryJson, RecruitmentJson.Options) ?? [];

    public bool IsClosed => Stage is ApplicationStage.Hired or ApplicationStage.Rejected or ApplicationStage.Withdrawn;

    public static JobApplication Apply(Guid candidateId, Guid requisitionId, int matchScore, Guid? byEmployeeId, DateTime now)
    {
        var application = new JobApplication
        {
            CandidateId = Guard.NotEmpty(candidateId),
            RequisitionId = Guard.NotEmpty(requisitionId),
            Stage = ApplicationStage.Applied,
            MatchScore = Math.Clamp(matchScore, 0, 100),
            AppliedDate = now,
            StageChangedDate = now,
        };
        application.Record(ApplicationStage.Applied, byEmployeeId, now, null);
        return application;
    }

    /// <summary>Moves forward or back among the open stages. Hiring and rejection have their own actions.</summary>
    public void MoveTo(ApplicationStage stage, Guid? byEmployeeId, DateTime now, string? note)
    {
        EnsureOpen();
        if (stage is ApplicationStage.Hired or ApplicationStage.Rejected or ApplicationStage.Withdrawn)
        {
            throw new DomainRuleException("APPLICATION_STAGE", $"Use the {stage.ToString().ToLowerInvariant()} action for that.");
        }

        if (stage == Stage)
        {
            return;
        }

        Stage = stage;
        StageChangedDate = now;
        Record(stage, byEmployeeId, now, note);
    }

    public void Reject(string reason, Guid? byEmployeeId, DateTime now)
    {
        EnsureOpen();
        RejectionReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500);
        Close(ApplicationStage.Rejected, byEmployeeId, now, RejectionReason);
    }

    public void Withdraw(string? note, Guid? byEmployeeId, DateTime now)
    {
        EnsureOpen();
        Close(ApplicationStage.Withdrawn, byEmployeeId, now, note);
    }

    public void Hire(Guid? byEmployeeId, DateTime now)
    {
        if (Stage != ApplicationStage.Offer)
        {
            throw new DomainRuleException("APPLICATION_STAGE", "Only an application at the offer stage can be hired.");
        }

        Close(ApplicationStage.Hired, byEmployeeId, now, null);
    }

    public void Rescore(int matchScore) => MatchScore = Math.Clamp(matchScore, 0, 100);

    private void Close(ApplicationStage stage, Guid? byEmployeeId, DateTime now, string? note)
    {
        Stage = stage;
        StageChangedDate = now;
        Record(stage, byEmployeeId, now, note);
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainRuleException("APPLICATION_CLOSED", $"The application is {Stage.ToString().ToLowerInvariant()}.");
        }
    }

    private void Record(ApplicationStage stage, Guid? byEmployeeId, DateTime now, string? note)
    {
        List<StageChange> history = [.. History, new StageChange(stage, now, byEmployeeId, string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)])];
        HistoryJson = JsonSerializer.Serialize(history, RecruitmentJson.Options);
    }
}

public enum InterviewMode
{
    InPerson = 1,
    Video = 2,
    Phone = 3,
}

public enum InterviewStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4,
}

/// <summary>An interview round (spec §12) with its panel.</summary>
public sealed class Interview : TenantAggregateRoot
{
    private Interview()
    {
        Round = null!;
        PanelJson = "[]";
    }

    public Guid ApplicationId { get; private set; }

    public string Round { get; private set; }

    public DateTime ScheduledAt { get; private set; }

    public int DurationMinutes { get; private set; }

    public InterviewMode Mode { get; private set; }

    /// <summary>The room or the meeting link.</summary>
    public string? Location { get; private set; }

    public string PanelJson { get; private set; }

    public InterviewStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<Guid> Panel => JsonSerializer.Deserialize<List<Guid>>(PanelJson) ?? [];

    public static Interview Schedule(Guid applicationId, string round, DateTime scheduledAt, int durationMinutes, InterviewMode mode, string? location, IReadOnlyList<Guid> panel, string? notes)
    {
        var interview = new Interview { ApplicationId = Guard.NotEmpty(applicationId), Status = InterviewStatus.Scheduled };
        interview.Reschedule(round, scheduledAt, durationMinutes, mode, location, panel, notes);
        return interview;
    }

    public void Reschedule(string round, DateTime scheduledAt, int durationMinutes, InterviewMode mode, string? location, IReadOnlyList<Guid> panel, string? notes)
    {
        if (Status != InterviewStatus.Scheduled)
        {
            throw new DomainRuleException("INTERVIEW_STATE", $"The interview is {Status.ToString().ToLowerInvariant()}.");
        }

        List<Guid> members = [.. (panel ?? []).Where(id => id != Guid.Empty).Distinct()];
        if (members.Count is 0 or > 10)
        {
            throw new DomainRuleException("INTERVIEW_PANEL", "An interview has between 1 and 10 panel members.");
        }

        if (durationMinutes is < 10 or > 480)
        {
            throw new DomainRuleException("INTERVIEW_DURATION", "An interview lasts between 10 minutes and 8 hours.");
        }

        Round = Guard.MaxLength(Guard.NotNullOrWhiteSpace(round).Trim(), 100);
        ScheduledAt = DateTime.SpecifyKind(scheduledAt, DateTimeKind.Utc);
        DurationMinutes = durationMinutes;
        Mode = mode;
        Location = string.IsNullOrWhiteSpace(location) ? null : Guard.MaxLength(location.Trim(), 500);
        PanelJson = JsonSerializer.Serialize(members);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 2000);
    }

    public void Complete() => Status = Status == InterviewStatus.Scheduled ? InterviewStatus.Completed : Status;

    public void Cancel(bool noShow)
    {
        if (Status != InterviewStatus.Scheduled)
        {
            throw new DomainRuleException("INTERVIEW_STATE", $"The interview is {Status.ToString().ToLowerInvariant()}.");
        }

        Status = noShow ? InterviewStatus.NoShow : InterviewStatus.Cancelled;
    }
}

public enum Recommendation
{
    StrongNo = 1,
    No = 2,
    Yes = 3,
    StrongYes = 4,
}

/// <summary>A panel member's feedback on an interview (spec §12): a 1-5 rating per criterion and a recommendation.</summary>
public sealed class Scorecard : TenantAggregateRoot
{
    private Scorecard()
    {
        RatingsJson = "{}";
    }

    public Guid InterviewId { get; private set; }

    public Guid ApplicationId { get; private set; }

    public Guid InterviewerEmployeeId { get; private set; }

    public string RatingsJson { get; private set; }

    public decimal OverallRating { get; private set; }

    public Recommendation Recommendation { get; private set; }

    public string? Comments { get; private set; }

    public DateTime SubmittedDate { get; private set; }

    public IReadOnlyDictionary<string, int> Ratings => JsonSerializer.Deserialize<Dictionary<string, int>>(RatingsJson) ?? [];

    public static Scorecard Submit(Guid interviewId, Guid applicationId, Guid interviewerEmployeeId, IReadOnlyDictionary<string, int> ratings, Recommendation recommendation, string? comments, DateTime now)
    {
        var scorecard = new Scorecard { InterviewId = Guard.NotEmpty(interviewId), ApplicationId = Guard.NotEmpty(applicationId), InterviewerEmployeeId = Guard.NotEmpty(interviewerEmployeeId) };
        scorecard.Revise(ratings, recommendation, comments, now);
        return scorecard;
    }

    public void Revise(IReadOnlyDictionary<string, int> ratings, Recommendation recommendation, string? comments, DateTime now)
    {
        Dictionary<string, int> cleaned = (ratings ?? new Dictionary<string, int>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => Guard.MaxLength(pair.Key.Trim(), 60), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (cleaned.Count == 0 || cleaned.Values.Any(value => value is < 1 or > 5))
        {
            throw new DomainRuleException("SCORECARD_RATINGS", "Rate at least one criterion, each from 1 to 5.");
        }

        if (!Enum.IsDefined(recommendation))
        {
            throw new DomainRuleException("SCORECARD_RECOMMENDATION", "Choose a recommendation.");
        }

        RatingsJson = JsonSerializer.Serialize(cleaned);
        OverallRating = Math.Round((decimal)cleaned.Values.Average(), 2);
        Recommendation = recommendation;
        Comments = string.IsNullOrWhiteSpace(comments) ? null : Guard.MaxLength(comments.Trim(), 4000);
        SubmittedDate = now;
    }
}

public enum OfferStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Sent = 5,
    Accepted = 6,
    Declined = 7,
    Withdrawn = 8,
}

public sealed record OfferTerms(decimal AnnualCtc, string Currency, DateOnly JoiningDate, string Designation, Guid? GradeId, DateOnly? ExpiresOn, string? Notes);

/// <summary>An offer (spec §12): approved through the workflow, sent, then accepted or declined by the candidate.</summary>
public sealed class Offer : TenantAggregateRoot
{
    private Offer()
    {
        Number = null!;
        Currency = null!;
        Designation = null!;
    }

    public string Number { get; private set; }

    public Guid ApplicationId { get; private set; }

    public Guid CandidateId { get; private set; }

    public Guid RequisitionId { get; private set; }

    public decimal AnnualCtc { get; private set; }

    public string Currency { get; private set; }

    public DateOnly JoiningDate { get; private set; }

    public string Designation { get; private set; }

    public Guid? GradeId { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public string? Notes { get; private set; }

    public OfferStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? SentDate { get; private set; }

    public DateTime? RespondedDate { get; private set; }

    public string? DeclineReason { get; private set; }

    public static Offer Draft(string number, Guid applicationId, Guid candidateId, Guid requisitionId, OfferTerms terms)
    {
        var offer = new Offer
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30),
            ApplicationId = Guard.NotEmpty(applicationId),
            CandidateId = Guard.NotEmpty(candidateId),
            RequisitionId = Guard.NotEmpty(requisitionId),
            Status = OfferStatus.Draft,
        };
        offer.Update(terms);
        return offer;
    }

    public void Update(OfferTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (Status is not (OfferStatus.Draft or OfferStatus.Rejected))
        {
            throw new DomainRuleException("OFFER_STATE", "Only a draft or rejected offer can change; withdraw it and make another.");
        }

        if (terms.AnnualCtc <= 0)
        {
            throw new DomainRuleException("OFFER_CTC", "The offer needs a CTC above zero.");
        }

        if (terms.ExpiresOn is { } expires && expires > terms.JoiningDate)
        {
            throw new DomainRuleException("OFFER_EXPIRY", "The offer should expire before the joining date.");
        }

        AnnualCtc = terms.AnnualCtc;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(terms.Currency).Trim().ToUpperInvariant(), 3);
        JoiningDate = terms.JoiningDate;
        Designation = Guard.MaxLength(Guard.NotNullOrWhiteSpace(terms.Designation).Trim(), 200);
        GradeId = terms.GradeId;
        ExpiresOn = terms.ExpiresOn;
        Notes = string.IsNullOrWhiteSpace(terms.Notes) ? null : Guard.MaxLength(terms.Notes.Trim(), 2000);
        Status = OfferStatus.Draft;
    }

    public void Submit()
    {
        if (Status is not (OfferStatus.Draft or OfferStatus.Rejected))
        {
            throw new DomainRuleException("OFFER_STATE", "Only a draft offer can go for approval.");
        }

        Status = OfferStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    /// <summary>The workflow's decision; null (sent back) returns the offer to draft.</summary>
    public void Decide(bool? approved)
    {
        if (Status != OfferStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => OfferStatus.Approved,
            false => OfferStatus.Rejected,
            null => OfferStatus.Draft,
        };
    }

    public void Send(DateTime now)
    {
        if (Status != OfferStatus.Approved)
        {
            throw new DomainRuleException("OFFER_STATE", "Only an approved offer can be sent.");
        }

        Status = OfferStatus.Sent;
        SentDate = now;
    }

    public void Respond(bool accepted, string? reason, DateTime now, DateOnly today)
    {
        if (Status != OfferStatus.Sent)
        {
            throw new DomainRuleException("OFFER_STATE", "Only a sent offer can be accepted or declined.");
        }

        if (accepted && ExpiresOn is { } expires && expires < today)
        {
            throw new DomainRuleException("OFFER_EXPIRED", $"The offer expired on {expires:d MMM yyyy}.");
        }

        Status = accepted ? OfferStatus.Accepted : OfferStatus.Declined;
        DeclineReason = accepted || string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500);
        RespondedDate = now;
    }

    public void Withdraw()
    {
        if (Status is OfferStatus.Accepted or OfferStatus.Declined or OfferStatus.Withdrawn)
        {
            throw new DomainRuleException("OFFER_STATE", $"The offer is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = OfferStatus.Withdrawn;
    }

    public bool IsLive => Status is not (OfferStatus.Declined or OfferStatus.Withdrawn or OfferStatus.Rejected);
}

/// <summary>A recruitment agency (spec §12) and the fee it charges on a hire's CTC.</summary>
public sealed class Agency : TenantAggregateRoot
{
    private Agency()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public string? ContactName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public decimal FeePercent { get; private set; }

    public static Agency Create(string name, string? contactName, string? email, string? phone, decimal feePercent)
    {
        var agency = new Agency();
        agency.Update(name, contactName, email, phone, feePercent, active: true);
        return agency;
    }

    public void Update(string name, string? contactName, string? email, string? phone, decimal feePercent, bool active)
    {
        if (feePercent is < 0 or > 50)
        {
            throw new DomainRuleException("AGENCY_FEE", "An agency fee is between 0% and 50% of CTC.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        ContactName = string.IsNullOrWhiteSpace(contactName) ? null : Guard.MaxLength(contactName.Trim(), 200);
        Email = string.IsNullOrWhiteSpace(email) ? null : Guard.MaxLength(email.Trim(), 256);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : Guard.MaxLength(phone.Trim(), 30);
        FeePercent = feePercent;
        IsActive = active;
    }
}

public enum BackgroundCheckStatus
{
    Initiated = 1,
    InProgress = 2,
    Clear = 3,
    Discrepancy = 4,
    Failed = 5,
}

/// <summary>Background verification of a candidate (spec §12): which checks, by whom, and the result.</summary>
public sealed class BackgroundCheck : TenantAggregateRoot
{
    private BackgroundCheck()
    {
        ChecksJson = "[]";
    }

    public Guid CandidateId { get; private set; }

    public Guid? ApplicationId { get; private set; }

    public string? Vendor { get; private set; }

    public string ChecksJson { get; private set; }

    public BackgroundCheckStatus Status { get; private set; }

    public DateOnly InitiatedOn { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public string? Remarks { get; private set; }

    public IReadOnlyList<string> Checks => JsonSerializer.Deserialize<List<string>>(ChecksJson) ?? [];

    public bool IsComplete => Status is BackgroundCheckStatus.Clear or BackgroundCheckStatus.Discrepancy or BackgroundCheckStatus.Failed;

    public static BackgroundCheck Initiate(Guid candidateId, Guid? applicationId, string? vendor, IReadOnlyList<string> checks, DateOnly today)
    {
        List<string> list = [.. (checks ?? []).Where(check => !string.IsNullOrWhiteSpace(check)).Select(check => Guard.MaxLength(check.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (list.Count == 0)
        {
            throw new DomainRuleException("BGV_CHECKS", "Choose at least one check: identity, address, education, employment, criminal.");
        }

        return new BackgroundCheck
        {
            CandidateId = Guard.NotEmpty(candidateId),
            ApplicationId = applicationId,
            Vendor = string.IsNullOrWhiteSpace(vendor) ? null : Guard.MaxLength(vendor.Trim(), 200),
            ChecksJson = JsonSerializer.Serialize(list),
            Status = BackgroundCheckStatus.Initiated,
            InitiatedOn = today,
        };
    }

    public void Record(BackgroundCheckStatus status, string? remarks, DateOnly today)
    {
        if (IsComplete)
        {
            throw new DomainRuleException("BGV_STATE", "The verification is already complete.");
        }

        if (status is BackgroundCheckStatus.Discrepancy or BackgroundCheckStatus.Failed && string.IsNullOrWhiteSpace(remarks))
        {
            throw new DomainRuleException("BGV_REMARKS", "Say what the verification found.");
        }

        Status = status;
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : Guard.MaxLength(remarks.Trim(), 2000);
        CompletedOn = IsComplete ? today : null;
    }
}

public enum ReferralStatus
{
    Submitted = 1,
    InProcess = 2,
    Hired = 3,
    NotSelected = 4,
    BonusAwarded = 5,
}

/// <summary>An employee referral (spec §12), and the bonus it earns once the hire has stayed long enough.</summary>
public sealed class Referral : TenantAggregateRoot
{
    private Referral()
    {
    }

    public Guid ReferrerEmployeeId { get; private set; }

    public Guid CandidateId { get; private set; }

    public Guid? RequisitionId { get; private set; }

    public string? Note { get; private set; }

    public ReferralStatus Status { get; private set; }

    public Guid? HiredEmployeeId { get; private set; }

    public decimal? BonusAmount { get; private set; }

    public DateOnly? BonusDueOn { get; private set; }

    public static Referral Submit(Guid referrerEmployeeId, Guid candidateId, Guid? requisitionId, string? note) => new()
    {
        ReferrerEmployeeId = Guard.NotEmpty(referrerEmployeeId),
        CandidateId = Guard.NotEmpty(candidateId),
        RequisitionId = requisitionId,
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 1000),
        Status = ReferralStatus.Submitted,
    };

    public void InProcess() => Status = Status == ReferralStatus.Submitted ? ReferralStatus.InProcess : Status;

    public void NotSelected() => Status = Status is ReferralStatus.Submitted or ReferralStatus.InProcess ? ReferralStatus.NotSelected : Status;

    public void Hired(Guid employeeId, decimal bonusAmount, DateOnly bonusDueOn)
    {
        Status = ReferralStatus.Hired;
        HiredEmployeeId = employeeId;
        BonusAmount = bonusAmount > 0 ? bonusAmount : null;
        BonusDueOn = bonusAmount > 0 ? bonusDueOn : null;
    }

    public void BonusAwarded() => Status = ReferralStatus.BonusAwarded;
}

/// <summary>The tenant's recruitment rules. One row per tenant, created with the defaults the first time it is saved.</summary>
public sealed class RecruitmentSettings : TenantAggregateRoot
{
    private RecruitmentSettings()
    {
        Currency = "INR";
        CriteriaJson = JsonSerializer.Serialize(RecruitmentRules.Default.ScorecardCriteria);
    }

    public decimal ReferralBonus { get; private set; } = RecruitmentRules.Default.ReferralBonus;

    /// <summary>Months the referred hire must stay before the referrer's bonus is paid.</summary>
    public int ReferralBonusAfterMonths { get; private set; } = RecruitmentRules.Default.ReferralBonusAfterMonths;

    public int OfferValidDays { get; private set; } = RecruitmentRules.Default.OfferValidDays;

    public string Currency { get; private set; }

    public string CriteriaJson { get; private set; }

    public static RecruitmentSettings Defaults() => new();

    public void Update(RecruitmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        List<string> criteria = [.. (rules.ScorecardCriteria ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => Guard.MaxLength(item.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (rules.ReferralBonus < 0 || rules.ReferralBonusAfterMonths is < 0 or > 24 || rules.OfferValidDays is < 1 or > 90 || criteria.Count == 0)
        {
            throw new DomainRuleException("RECRUITMENT_RULES", "The bonus is not negative, paid within 24 months, offers are valid 1-90 days, and the scorecard has criteria.");
        }

        ReferralBonus = rules.ReferralBonus;
        ReferralBonusAfterMonths = rules.ReferralBonusAfterMonths;
        OfferValidDays = rules.OfferValidDays;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(rules.Currency).Trim().ToUpperInvariant(), 3);
        CriteriaJson = JsonSerializer.Serialize(criteria);
    }

    public RecruitmentRules ToRules() => new(ReferralBonus, ReferralBonusAfterMonths, OfferValidDays, Currency, JsonSerializer.Deserialize<List<string>>(CriteriaJson) ?? []);
}

public sealed record RecruitmentRules(decimal ReferralBonus, int ReferralBonusAfterMonths, int OfferValidDays, string Currency, IReadOnlyList<string> ScorecardCriteria)
{
    public static RecruitmentRules Default { get; } = new(25000, 3, 7, "INR", ["Technical skills", "Problem solving", "Communication", "Culture fit"]);
}

internal static class RecruitmentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static string Skills(IReadOnlyList<string>? skills)
        => JsonSerializer.Serialize((skills ?? []).Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => Guard.MaxLength(skill.Trim(), 60)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
}
