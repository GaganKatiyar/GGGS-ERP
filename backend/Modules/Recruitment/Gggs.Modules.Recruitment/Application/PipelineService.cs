using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Recruitment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Application;

public sealed record CandidateItem(
    Guid Id,
    string Number,
    string FullName,
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
    IReadOnlyList<string> Skills,
    string? LinkedInUrl,
    string? Notes,
    CandidateSource Source,
    Guid? AgencyId,
    string? AgencyName,
    Guid? ReferrerEmployeeId,
    string? ReferrerName,
    Guid? EmployeeId,
    DateTime CreatedDate,
    int Applications,
    FormerEmployee? FormerEmployee = null);

/// <summary>A candidate who worked here before ("boomerang"), from the alumni network.</summary>
public sealed record FormerEmployee(Guid EmployeeId, string Name, string? LastDesignation, DateOnly ExitDate, string SeparationType, bool EligibleForRehire);

public sealed record CandidateQuery
{
    public string? Search { get; init; }

    public CandidateSource? Source { get; init; }

    public string? Skill { get; init; }
}

public sealed record CandidateBody(CandidateDetails Details, CandidateSource Source, Guid? AgencyId, Guid? ReferrerEmployeeId, Guid? RequisitionId);

public sealed record ApplicationItem(
    Guid Id,
    Guid CandidateId,
    string CandidateNumber,
    string CandidateName,
    string CandidateEmail,
    string? CurrentTitle,
    decimal? ExperienceYears,
    CandidateSource Source,
    Guid RequisitionId,
    string RequisitionNumber,
    string RequisitionTitle,
    ApplicationStage Stage,
    int MatchScore,
    DateTime AppliedDate,
    DateTime StageChangedDate,
    string? RejectionReason,
    decimal? AverageRating,
    int Interviews,
    OfferStatus? OfferStatus);

public sealed record InterviewItem(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string RequisitionTitle,
    string Round,
    DateTime ScheduledAt,
    int DurationMinutes,
    InterviewMode Mode,
    string? Location,
    IReadOnlyList<Guid> Panel,
    IReadOnlyList<string> PanelNames,
    InterviewStatus Status,
    string? Notes,
    int Scorecards,
    bool MyScorecardSubmitted);

public sealed record ScorecardItem(
    Guid Id,
    Guid InterviewId,
    string Round,
    Guid InterviewerEmployeeId,
    string? InterviewerName,
    IReadOnlyDictionary<string, int> Ratings,
    decimal OverallRating,
    Recommendation Recommendation,
    string? Comments,
    DateTime SubmittedDate);

public sealed record BackgroundCheckItem(
    Guid Id,
    Guid CandidateId,
    Guid? ApplicationId,
    string? Vendor,
    IReadOnlyList<string> Checks,
    BackgroundCheckStatus Status,
    DateOnly InitiatedOn,
    DateOnly? CompletedOn,
    string? Remarks);

public sealed record MatchView(int Score, int SkillsScore, int ExperienceScore, int SalaryScore, IReadOnlyList<string> MatchedSkills, IReadOnlyList<string> MissingSkills);

public sealed record ApplicationView(
    ApplicationItem Application,
    CandidateItem Candidate,
    MatchView Match,
    IReadOnlyList<StageChange> History,
    IReadOnlyList<InterviewItem> Interviews,
    IReadOnlyList<ScorecardItem> Scorecards,
    IReadOnlyList<OfferItem> Offers,
    IReadOnlyList<BackgroundCheckItem> BackgroundChecks,
    bool CanManage);

public sealed record CandidateView(CandidateItem Candidate, IReadOnlyList<ApplicationItem> Applications, IReadOnlyList<BackgroundCheckItem> BackgroundChecks, bool CanManage);

public sealed record MoveBody(ApplicationStage Stage, string? Note);

public sealed record ReasonBody(string? Reason);

public sealed record InterviewBody(string Round, DateTime ScheduledAt, int DurationMinutes, InterviewMode Mode, string? Location, IReadOnlyList<Guid> Panel, string? Notes);

public sealed record ScorecardBody(IReadOnlyDictionary<string, int> Ratings, Recommendation Recommendation, string? Comments);

public sealed record MyInterviewView(InterviewItem Interview, CandidateItem Candidate, IReadOnlyList<string> Criteria, ScorecardItem? MyScorecard, string? RequisitionDescription);

public sealed record BackgroundCheckBody(Guid CandidateId, Guid? ApplicationId, string? Vendor, IReadOnlyList<string> Checks);

public sealed record BackgroundResultBody(BackgroundCheckStatus Status, string? Remarks);

public sealed record ReferralBody(CandidateDetails Candidate, Guid? RequisitionId, string? Note);

public sealed record ReferralItem(
    Guid Id,
    Guid ReferrerEmployeeId,
    string? ReferrerName,
    Guid CandidateId,
    string CandidateName,
    Guid? RequisitionId,
    string? RequisitionTitle,
    ReferralStatus Status,
    string? Stage,
    decimal? BonusAmount,
    DateOnly? BonusDueOn,
    DateTime CreatedDate);

/// <summary>
/// The hiring pipeline (spec §12): candidates (one profile per person), their applications moving
/// through the stages, interviews and panel scorecards, referrals and background verification.
/// </summary>
internal sealed class PipelineService(
    AppDbContext dbContext,
    RecruitmentAccess access,
    IEmployeeDirectory employees,
    INotifier notifier,
    TimeProvider timeProvider,
    Gggs.BuildingBlocks.MultiTenancy.ITenantClock clock,
    Gggs.Modules.Separation.Contracts.IAlumniDirectory alumni)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Candidates ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<CandidateItem>> CandidatesAsync(CandidateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Candidate> rows = await VisibleCandidatesAsync(Permissions.Recruitment.ApplicationView, cancellationToken);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            string digits = new([.. term.Where(char.IsAsciiDigit)]);
            rows = rows.Where(row => row.FirstName.Contains(term) || (row.LastName != null && row.LastName.Contains(term)) || row.Email.Contains(term) || row.Number.Contains(term)
                || (row.CurrentCompany != null && row.CurrentCompany.Contains(term)) || (digits.Length >= 4 && row.Mobile != null && row.Mobile.Contains(digits)));
        }

        if (query.Source is { } source)
        {
            rows = rows.Where(row => row.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Skill))
        {
            string skill = query.Skill.Trim();
            rows = rows.Where(row => row.SkillsJson.Contains(skill));
        }

        return await CandidateItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<CandidateView> CandidateAsync(Guid id, CancellationToken cancellationToken)
    {
        Candidate candidate = await VisibleCandidateAsync(id, Permissions.Recruitment.ApplicationView, cancellationToken);
        List<JobApplication> applications = await dbContext.JobApplications.AsNoTracking().Where(row => row.CandidateId == id).OrderByDescending(row => row.AppliedDate).ToListAsync(cancellationToken);
        List<BackgroundCheck> checks = await dbContext.BackgroundChecks.AsNoTracking().Where(row => row.CandidateId == id).OrderByDescending(row => row.InitiatedOn).ToListAsync(cancellationToken);
        return new CandidateView(
            (await CandidateItemsAsync([candidate], cancellationToken))[0],
            await ApplicationItemsAsync(applications, cancellationToken),
            [.. checks.Select(CheckItem)],
            await access.HasAsync(Permissions.Recruitment.ApplicationManage, cancellationToken));
    }

    /// <summary>Adds a candidate, or finds the one with the same email or mobile; applies them to the requisition when one is given.</summary>
    public async Task<CandidateView> AddCandidateAsync(CandidateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.AgencyId is { } agency && !await dbContext.Agencies.AnyAsync(row => row.Id == agency && row.IsActive, cancellationToken))
        {
            throw new DomainRuleException("CANDIDATE_AGENCY", "The agency is not active.");
        }

        if (body.ReferrerEmployeeId is { } referrer && !await employees.IsActiveAsync(referrer, cancellationToken))
        {
            throw new DomainRuleException("CANDIDATE_REFERRER", "The referrer is not an active employee.");
        }

        Candidate candidate = await FindOrCreateAsync(body.Details, body.Source, body.AgencyId, body.ReferrerEmployeeId, cancellationToken);
        if (body.Source == CandidateSource.Referral && body.ReferrerEmployeeId is { } referredBy
            && !await dbContext.Referrals.AnyAsync(row => row.CandidateId == candidate.Id && row.ReferrerEmployeeId == referredBy, cancellationToken))
        {
            dbContext.Referrals.Add(Referral.Submit(referredBy, candidate.Id, body.RequisitionId, null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.RequisitionId is { } requisition)
        {
            await ApplyAsync(candidate.Id, requisition, cancellationToken);
        }

        return await CandidateAsync(candidate.Id, cancellationToken);
    }

    public async Task<CandidateView> UpdateCandidateAsync(Guid id, CandidateDetails details, CancellationToken cancellationToken)
    {
        Candidate visible = await VisibleCandidateAsync(id, Permissions.Recruitment.ApplicationManage, cancellationToken);
        Candidate candidate = await dbContext.Candidates.FirstAsync(row => row.Id == visible.Id, cancellationToken);
        candidate.Update(details);
        await EnsureUniqueAsync(candidate.Email, candidate.Mobile, candidate.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RescoreAsync(candidate, cancellationToken);
        return await CandidateAsync(id, cancellationToken);
    }

    // ----- Applications -------------------------------------------------------------------------

    /// <summary>The pipeline of a requisition (or of every visible one), best match first within each stage.</summary>
    public async Task<IReadOnlyList<ApplicationItem>> ApplicationsAsync(Guid? requisitionId, ApplicationStage? stage, CancellationToken cancellationToken)
    {
        List<Guid>? visible = await access.RequisitionIdsAsync(Permissions.Recruitment.ApplicationView, cancellationToken);
        IQueryable<JobApplication> rows = dbContext.JobApplications.AsNoTracking();
        if (visible is not null)
        {
            rows = rows.Where(row => visible.Contains(row.RequisitionId));
        }

        if (requisitionId is { } requisition)
        {
            rows = rows.Where(row => row.RequisitionId == requisition);
        }

        if (stage is { } wanted)
        {
            rows = rows.Where(row => row.Stage == wanted);
        }

        return await ApplicationItemsAsync(await rows.OrderBy(row => row.Stage).ThenByDescending(row => row.MatchScore).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<ApplicationView> ApplicationAsync(Guid id, CancellationToken cancellationToken)
    {
        JobApplication application = await VisibleApplicationAsync(id, Permissions.Recruitment.ApplicationView, tracked: false, cancellationToken);
        Candidate candidate = await dbContext.Candidates.AsNoTracking().FirstAsync(row => row.Id == application.CandidateId, cancellationToken);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == application.RequisitionId, cancellationToken);
        List<Interview> interviews = await dbContext.Interviews.AsNoTracking().Where(row => row.ApplicationId == id).OrderBy(row => row.ScheduledAt).ToListAsync(cancellationToken);
        List<Scorecard> scorecards = await dbContext.Scorecards.AsNoTracking().Where(row => row.ApplicationId == id).OrderBy(row => row.SubmittedDate).ToListAsync(cancellationToken);
        List<Offer> offers = await access.HasAsync(Permissions.Recruitment.OfferView, cancellationToken)
            ? await dbContext.Offers.AsNoTracking().Where(row => row.ApplicationId == id).OrderByDescending(row => row.CreatedDate).ToListAsync(cancellationToken)
            : [];
        List<BackgroundCheck> checks = await dbContext.BackgroundChecks.AsNoTracking().Where(row => row.CandidateId == candidate.Id).OrderByDescending(row => row.InitiatedOn).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(scorecards.Select(row => row.InterviewerEmployeeId).Concat(interviews.SelectMany(row => row.Panel)), cancellationToken);
        Dictionary<Guid, string> rounds = interviews.ToDictionary(row => row.Id, row => row.Round);
        MatchBreakdown match = Score(requisition, candidate);

        return new ApplicationView(
            (await ApplicationItemsAsync([application], cancellationToken))[0],
            (await CandidateItemsAsync([candidate], cancellationToken))[0],
            new MatchView(match.Score, match.SkillsScore, match.ExperienceScore, match.SalaryScore, match.MatchedSkills, match.MissingSkills),
            application.History,
            [.. interviews.Select(row => InterviewItem(row, candidate, requisition, names, scorecards))],
            [.. scorecards.Select(row => ScorecardItem(row, rounds.GetValueOrDefault(row.InterviewId) ?? string.Empty, names))],
            [.. offers.Select(row => OfferService.Item(row, candidate.FullName, requisition))],
            [.. checks.Select(CheckItem)],
            await access.HasAsync(Permissions.Recruitment.ApplicationManage, cancellationToken));
    }

    public async Task<ApplicationView> ApplyAsync(Guid candidateId, Guid requisitionId, CancellationToken cancellationToken)
    {
        await access.EnsureRequisitionAsync(requisitionId, Permissions.Recruitment.ApplicationManage, cancellationToken);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == requisitionId, cancellationToken);
        requisition.EnsureAccepting();
        Candidate candidate = await dbContext.Candidates.AsNoTracking().FirstOrDefaultAsync(row => row.Id == candidateId, cancellationToken) ?? throw new NotFoundException(nameof(Candidate), candidateId);
        if (candidate.EmployeeId is not null)
        {
            throw new DomainRuleException("CANDIDATE_HIRED", $"{candidate.FullName} has already been hired.");
        }

        JobApplication application = await NewApplicationAsync(candidate, requisition, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ApplicationAsync(application.Id, cancellationToken);
    }

    public async Task<ApplicationView> MoveAsync(Guid id, MoveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        JobApplication application = await VisibleApplicationAsync(id, Permissions.Recruitment.ApplicationManage, tracked: true, cancellationToken);
        if (body.Stage == ApplicationStage.Offer && application.Stage != ApplicationStage.Offer)
        {
            throw new DomainRuleException("APPLICATION_STAGE", "An application reaches the offer stage when an offer is drafted for it.");
        }

        application.MoveTo(body.Stage, access.Me, Now, body.Note);
        await TouchReferralAsync(application.CandidateId, application.Stage, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ApplicationAsync(id, cancellationToken);
    }

    public async Task<ApplicationView> RejectAsync(Guid id, string? reason, bool withdrawn, CancellationToken cancellationToken)
    {
        JobApplication application = await VisibleApplicationAsync(id, Permissions.Recruitment.ApplicationManage, tracked: true, cancellationToken);
        if (withdrawn)
        {
            application.Withdraw(reason, access.Me, Now);
        }
        else
        {
            application.Reject(reason ?? string.Empty, access.Me, Now);
        }

        foreach (Interview interview in await dbContext.Interviews.Where(row => row.ApplicationId == id && row.Status == InterviewStatus.Scheduled).ToListAsync(cancellationToken))
        {
            interview.Cancel(noShow: false);
        }

        await TouchReferralAsync(application.CandidateId, application.Stage, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ApplicationAsync(id, cancellationToken);
    }

    // ----- Interviews ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<InterviewItem>> InterviewsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateTime start = (from ?? today.AddDays(-7)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime end = (to ?? today.AddDays(30)).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        List<Guid>? visible = await access.RequisitionIdsAsync(Permissions.Recruitment.ApplicationView, cancellationToken);
        var rows = await (
                from interview in dbContext.Interviews.AsNoTracking()
                join application in dbContext.JobApplications.AsNoTracking() on interview.ApplicationId equals application.Id
                where interview.ScheduledAt >= start && interview.ScheduledAt < end && (visible == null || visible.Contains(application.RequisitionId))
                orderby interview.ScheduledAt
                select new { interview, application.CandidateId, application.RequisitionId })
            .Take(500)
            .ToListAsync(cancellationToken);
        return await InterviewItemsAsync([.. rows.Select(row => (row.interview, row.CandidateId, row.RequisitionId))], cancellationToken);
    }

    public async Task<ApplicationView> ScheduleAsync(Guid applicationId, InterviewBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        JobApplication application = await VisibleApplicationAsync(applicationId, Permissions.Recruitment.ApplicationManage, tracked: true, cancellationToken);
        if (application.IsClosed)
        {
            throw new DomainRuleException("APPLICATION_CLOSED", $"The application is {application.Stage.ToString().ToLowerInvariant()}.");
        }

        await EnsurePanelAsync(body.Panel, cancellationToken);
        var interview = Interview.Schedule(applicationId, body.Round, body.ScheduledAt, body.DurationMinutes, body.Mode, body.Location, body.Panel, body.Notes);
        dbContext.Interviews.Add(interview);
        if (application.Stage < ApplicationStage.Interview)
        {
            application.MoveTo(ApplicationStage.Interview, access.Me, Now, $"Interview scheduled: {interview.Round}");
            await TouchReferralAsync(application.CandidateId, application.Stage, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyPanelAsync(interview, application, interview.Panel, cancellationToken);
        return await ApplicationAsync(applicationId, cancellationToken);
    }

    public async Task<ApplicationView> RescheduleAsync(Guid interviewId, InterviewBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Interview interview = await dbContext.Interviews.FirstOrDefaultAsync(row => row.Id == interviewId, cancellationToken) ?? throw new NotFoundException(nameof(Interview), interviewId);
        JobApplication application = await VisibleApplicationAsync(interview.ApplicationId, Permissions.Recruitment.ApplicationManage, tracked: false, cancellationToken);
        await EnsurePanelAsync(body.Panel, cancellationToken);
        interview.Reschedule(body.Round, body.ScheduledAt, body.DurationMinutes, body.Mode, body.Location, body.Panel, body.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyPanelAsync(interview, application, interview.Panel, cancellationToken);
        return await ApplicationAsync(application.Id, cancellationToken);
    }

    public async Task<ApplicationView> CloseInterviewAsync(Guid interviewId, string action, CancellationToken cancellationToken)
    {
        Interview interview = await dbContext.Interviews.FirstOrDefaultAsync(row => row.Id == interviewId, cancellationToken) ?? throw new NotFoundException(nameof(Interview), interviewId);
        JobApplication application = await VisibleApplicationAsync(interview.ApplicationId, Permissions.Recruitment.ApplicationManage, tracked: false, cancellationToken);
        switch (action)
        {
            case "complete":
                interview.Complete();
                break;
            case "cancel":
                interview.Cancel(noShow: false);
                break;
            case "no-show":
                interview.Cancel(noShow: true);
                break;
            default:
                throw new DomainRuleException("INTERVIEW_ACTION", $"'{action}' is not an interview action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ApplicationAsync(application.Id, cancellationToken);
    }

    // ----- My interviews (panel members) ---------------------------------------------------------

    public async Task<IReadOnlyList<InterviewItem>> MyInterviewsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        string token = me.ToString();
        var rows = await (
                from interview in dbContext.Interviews.AsNoTracking()
                join application in dbContext.JobApplications.AsNoTracking() on interview.ApplicationId equals application.Id
                where interview.PanelJson.Contains(token) && interview.Status != InterviewStatus.Cancelled
                orderby interview.ScheduledAt descending
                select new { interview, application.CandidateId, application.RequisitionId })
            .Take(200)
            .ToListAsync(cancellationToken);
        return await InterviewItemsAsync([.. rows.Where(row => row.interview.Panel.Contains(me)).Select(row => (row.interview, row.CandidateId, row.RequisitionId))], cancellationToken);
    }

    public async Task<MyInterviewView> MyInterviewAsync(Guid interviewId, CancellationToken cancellationToken)
    {
        Guid me = Me();
        Interview interview = await PanelInterviewAsync(interviewId, me, cancellationToken);
        JobApplication application = await dbContext.JobApplications.AsNoTracking().FirstAsync(row => row.Id == interview.ApplicationId, cancellationToken);
        Candidate candidate = await dbContext.Candidates.AsNoTracking().FirstAsync(row => row.Id == application.CandidateId, cancellationToken);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == application.RequisitionId, cancellationToken);
        List<Scorecard> scorecards = await dbContext.Scorecards.AsNoTracking().Where(row => row.InterviewId == interviewId).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(interview.Panel, cancellationToken);
        RecruitmentRules rules = (await dbContext.RecruitmentSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToRules() ?? RecruitmentRules.Default;
        CandidateItem profile = (await CandidateItemsAsync([candidate], cancellationToken))[0] with { CurrentCtc = null, ExpectedCtc = null, Notes = null };
        return new MyInterviewView(
            InterviewItem(interview, candidate, requisition, names, scorecards),
            profile,
            rules.ScorecardCriteria,
            scorecards.Find(row => row.InterviewerEmployeeId == me) is { } mine ? ScorecardItem(mine, interview.Round, names) : null,
            requisition.Description);
    }

    public async Task<MyInterviewView> SubmitScorecardAsync(Guid interviewId, ScorecardBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        Interview interview = await PanelInterviewAsync(interviewId, me, cancellationToken);
        if (interview.Status is InterviewStatus.Cancelled or InterviewStatus.NoShow)
        {
            throw new DomainRuleException("INTERVIEW_STATE", "There is no feedback to give on an interview that did not happen.");
        }

        Scorecard? existing = await dbContext.Scorecards.FirstOrDefaultAsync(row => row.InterviewId == interviewId && row.InterviewerEmployeeId == me, cancellationToken);
        if (existing is null)
        {
            dbContext.Scorecards.Add(Scorecard.Submit(interviewId, interview.ApplicationId, me, body.Ratings, body.Recommendation, body.Comments, Now));
        }
        else
        {
            existing.Revise(body.Ratings, body.Recommendation, body.Comments, Now);
        }

        Interview tracked = await dbContext.Interviews.FirstAsync(row => row.Id == interviewId, cancellationToken);
        tracked.Complete();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MyInterviewAsync(interviewId, cancellationToken);
    }

    // ----- Referrals ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<ReferralItem>> MyReferralsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await ReferralItemsAsync(await dbContext.Referrals.AsNoTracking().Where(row => row.ReferrerEmployeeId == me).OrderByDescending(row => row.CreatedDate).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ReferralItem>> ReferralsAsync(CancellationToken cancellationToken)
        => await ReferralItemsAsync(await dbContext.Referrals.AsNoTracking().OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);

    /// <summary>An employee refers someone, optionally for an open requisition on the job board.</summary>
    public async Task<IReadOnlyList<ReferralItem>> ReferAsync(ReferralBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        Requisition? requisition = null;
        if (body.RequisitionId is { } requisitionId)
        {
            requisition = await dbContext.Requisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == requisitionId, cancellationToken) ?? throw new NotFoundException(nameof(Requisition), requisitionId);
            requisition.EnsureAccepting();
        }

        Candidate candidate = await FindOrCreateAsync(body.Candidate, CandidateSource.Referral, null, me, cancellationToken);
        if (candidate.EmployeeId is not null)
        {
            throw new DomainRuleException("CANDIDATE_HIRED", "That person already works here.");
        }

        if (await dbContext.Referrals.AnyAsync(row => row.CandidateId == candidate.Id, cancellationToken))
        {
            throw new DomainRuleException("REFERRAL_EXISTS", "Someone has already referred this person.");
        }

        var referral = Referral.Submit(me, candidate.Id, body.RequisitionId, body.Note);
        dbContext.Referrals.Add(referral);
        if (requisition is not null && !await dbContext.JobApplications.AnyAsync(row => row.CandidateId == candidate.Id && row.RequisitionId == requisition.Id, cancellationToken))
        {
            await NewApplicationAsync(candidate, requisition, cancellationToken);
            referral.InProcess();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await MyReferralsAsync(cancellationToken);
    }

    // ----- Background verification ---------------------------------------------------------------

    public async Task<CandidateView> InitiateCheckAsync(BackgroundCheckBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await VisibleCandidateAsync(body.CandidateId, Permissions.Recruitment.ApplicationManage, cancellationToken);
        if (await dbContext.BackgroundChecks.AnyAsync(row => row.CandidateId == body.CandidateId && row.Status <= BackgroundCheckStatus.InProgress, cancellationToken))
        {
            throw new DomainRuleException("BGV_OPEN", "A verification is already under way for this candidate.");
        }

        dbContext.BackgroundChecks.Add(BackgroundCheck.Initiate(body.CandidateId, body.ApplicationId, body.Vendor, body.Checks, await clock.TodayAsync(cancellationToken)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await CandidateAsync(body.CandidateId, cancellationToken);
    }

    public async Task<CandidateView> RecordCheckAsync(Guid id, BackgroundResultBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        BackgroundCheck check = await dbContext.BackgroundChecks.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(BackgroundCheck), id);
        await VisibleCandidateAsync(check.CandidateId, Permissions.Recruitment.ApplicationManage, cancellationToken);
        check.Record(body.Status, body.Remarks, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await CandidateAsync(check.CandidateId, cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    internal static MatchBreakdown Score(Requisition requisition, Candidate candidate)
        => CandidateMatcher.Match(requisition.Skills, requisition.MinExperienceYears, requisition.MaxExperienceYears, requisition.SalaryMax, candidate.Skills, candidate.ExperienceYears, candidate.ExpectedCtc);

    private Guid Me() => access.Me ?? throw new DomainRuleException("RECRUITMENT_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<JobApplication> NewApplicationAsync(Candidate candidate, Requisition requisition, CancellationToken cancellationToken)
    {
        if (await dbContext.JobApplications.AnyAsync(row => row.CandidateId == candidate.Id && row.RequisitionId == requisition.Id, cancellationToken))
        {
            throw new DomainRuleException("APPLICATION_EXISTS", $"{candidate.FullName} has already applied to {requisition.Number}.");
        }

        var application = JobApplication.Apply(candidate.Id, requisition.Id, Score(requisition, candidate).Score, access.Me, Now);
        dbContext.JobApplications.Add(application);
        return application;
    }

    private async Task<Candidate> FindOrCreateAsync(CandidateDetails details, CandidateSource source, Guid? agencyId, Guid? referrerEmployeeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        string email = (details.Email ?? string.Empty).Trim().ToLowerInvariant();
        string? mobile = Candidate.NormalizeMobile(details.Mobile);
        Candidate? existing = await dbContext.Candidates.FirstOrDefaultAsync(row => row.Email == email || (mobile != null && row.Mobile == mobile), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        string number = await RecruitmentNumbers.NextAsync(
            dbContext.Candidates.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Number), "CAN-", cancellationToken, "000000");
        var candidate = Candidate.Create(number, details, source, agencyId, referrerEmployeeId);
        dbContext.Candidates.Add(candidate);
        return candidate;
    }

    private async Task EnsureUniqueAsync(string email, string? mobile, Guid candidateId, CancellationToken cancellationToken)
    {
        if (await dbContext.Candidates.AnyAsync(row => row.Id != candidateId && (row.Email == email || (mobile != null && row.Mobile == mobile)), cancellationToken))
        {
            throw new DomainRuleException("CANDIDATE_DUPLICATE", "Another candidate already has this email or mobile number.");
        }
    }

    private async Task RescoreAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        List<JobApplication> open = await dbContext.JobApplications.Where(row => row.CandidateId == candidate.Id && row.Stage < ApplicationStage.Hired).ToListAsync(cancellationToken);
        List<Guid> ids = [.. open.Select(row => row.RequisitionId)];
        Dictionary<Guid, Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (JobApplication application in open)
        {
            application.Rescore(Score(requisitions[application.RequisitionId], candidate).Score);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TouchReferralAsync(Guid candidateId, ApplicationStage stage, CancellationToken cancellationToken)
    {
        Referral? referral = await dbContext.Referrals.FirstOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        if (referral is null)
        {
            return;
        }

        if (stage is ApplicationStage.Rejected or ApplicationStage.Withdrawn)
        {
            bool stillOpen = await dbContext.JobApplications.AnyAsync(row => row.CandidateId == candidateId && row.Stage < ApplicationStage.Hired, cancellationToken);
            if (!stillOpen)
            {
                referral.NotSelected();
            }
        }
        else
        {
            referral.InProcess();
        }
    }

    private async Task EnsurePanelAsync(IReadOnlyList<Guid>? panel, CancellationToken cancellationToken)
    {
        foreach (Guid member in (panel ?? []).Distinct())
        {
            if (!await employees.IsActiveAsync(member, cancellationToken))
            {
                throw new DomainRuleException("INTERVIEW_PANEL", "Every panel member must be an active employee.");
            }
        }
    }

    private async Task NotifyPanelAsync(Interview interview, JobApplication application, IReadOnlyList<Guid> panel, CancellationToken cancellationToken)
    {
        Candidate candidate = await dbContext.Candidates.AsNoTracking().FirstAsync(row => row.Id == application.CandidateId, cancellationToken);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == application.RequisitionId, cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.InterviewScheduled,
                [.. panel.Select(id => new NotificationRecipient(id))],
                new Dictionary<string, string>
                {
                    ["candidateName"] = candidate.FullName,
                    ["jobTitle"] = requisition.Title,
                    ["round"] = interview.Round,
                    ["when"] = interview.ScheduledAt.ToString("ddd d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture),
                },
                $"/me/interviews/{interview.Id}"),
            cancellationToken);
    }

    private async Task<Interview> PanelInterviewAsync(Guid interviewId, Guid me, CancellationToken cancellationToken)
    {
        Interview interview = await dbContext.Interviews.AsNoTracking().FirstOrDefaultAsync(row => row.Id == interviewId, cancellationToken) ?? throw new NotFoundException(nameof(Interview), interviewId);
        return interview.Panel.Contains(me) ? interview : throw new NotFoundException(nameof(Interview), interviewId);
    }

    private async Task<IQueryable<Candidate>> VisibleCandidatesAsync(string permission, CancellationToken cancellationToken)
    {
        List<Guid>? visible = await access.RequisitionIdsAsync(permission, cancellationToken);
        IQueryable<Candidate> rows = dbContext.Candidates.AsNoTracking();
        IQueryable<JobApplication> applications = dbContext.JobApplications;
        return visible is null
            ? rows
            : rows.Where(candidate => applications.Any(application => application.CandidateId == candidate.Id && visible.Contains(application.RequisitionId)));
    }

    private async Task<Candidate> VisibleCandidateAsync(Guid id, string permission, CancellationToken cancellationToken)
        => await (await VisibleCandidatesAsync(permission, cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Candidate), id);

    private async Task<JobApplication> VisibleApplicationAsync(Guid id, string permission, bool tracked, CancellationToken cancellationToken)
    {
        JobApplication application = await (tracked ? dbContext.JobApplications : dbContext.JobApplications.AsNoTracking()).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(JobApplication), id);
        List<Guid>? visible = await access.RequisitionIdsAsync(permission, cancellationToken);
        return visible is null || visible.Contains(application.RequisitionId) ? application : throw new NotFoundException(nameof(JobApplication), id);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private async Task<List<CandidateItem>> CandidateItemsAsync(IReadOnlyList<Candidate> candidates, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. candidates.Select(row => row.Id)];
        Dictionary<Guid, int> applications = await dbContext.JobApplications.AsNoTracking().Where(row => ids.Contains(row.CandidateId))
            .GroupBy(row => row.CandidateId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        List<Guid> agencyIds = [.. candidates.Where(row => row.AgencyId != null).Select(row => row.AgencyId!.Value).Distinct()];
        Dictionary<Guid, string> agencies = await dbContext.Agencies.AsNoTracking().Where(row => agencyIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, string> referrers = await NamesAsync(candidates.Where(row => row.ReferrerEmployeeId != null).Select(row => row.ReferrerEmployeeId!.Value), cancellationToken);

        // Former employees who apply again, recognised by email or mobile in the alumni network.
        IReadOnlyList<Gggs.Modules.Separation.Contracts.AlumniMatch> former = await alumni.MatchAsync(
            [.. candidates.Select(row => row.Email)], [.. candidates.Where(row => row.Mobile != null).Select(row => row.Mobile!)], cancellationToken);
        FormerEmployee? Former(Candidate row) => former
            .Where(match => string.Equals(match.Email, row.Email, StringComparison.OrdinalIgnoreCase) || (match.Mobile != null && match.Mobile == row.Mobile))
            .OrderByDescending(match => match.ExitDate)
            .Select(match => new FormerEmployee(match.EmployeeId, match.Name, match.LastDesignation, match.ExitDate, match.SeparationType, match.EligibleForRehire))
            .FirstOrDefault();
        return [.. candidates.Select(row => new CandidateItem(
            row.Id, row.Number, row.FullName, row.FirstName, row.LastName, row.Email, row.Mobile, row.CurrentCompany, row.CurrentTitle, row.ExperienceYears, row.CurrentCtc,
            row.ExpectedCtc, row.NoticePeriodDays, row.Location, row.Skills, row.LinkedInUrl, row.Notes, row.Source, row.AgencyId,
            row.AgencyId is { } agency ? agencies.GetValueOrDefault(agency) : null, row.ReferrerEmployeeId,
            row.ReferrerEmployeeId is { } referrer ? referrers.GetValueOrDefault(referrer) : null, row.EmployeeId, row.CreatedDate, applications.GetValueOrDefault(row.Id), Former(row)))];
    }

    private async Task<List<ApplicationItem>> ApplicationItemsAsync(IReadOnlyList<JobApplication> applications, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. applications.Select(row => row.Id)];
        List<Guid> candidateIds = [.. applications.Select(row => row.CandidateId).Distinct()];
        List<Guid> requisitionIds = [.. applications.Select(row => row.RequisitionId).Distinct()];
        Dictionary<Guid, Candidate> candidates = await dbContext.Candidates.AsNoTracking().Where(row => candidateIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => requisitionIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        var ratings = await dbContext.Scorecards.AsNoTracking().Where(row => ids.Contains(row.ApplicationId))
            .GroupBy(row => row.ApplicationId).Select(group => new { group.Key, Average = group.Average(row => row.OverallRating) }).ToDictionaryAsync(row => row.Key, row => row.Average, cancellationToken);
        var interviews = await dbContext.Interviews.AsNoTracking().Where(row => ids.Contains(row.ApplicationId) && row.Status != InterviewStatus.Cancelled)
            .GroupBy(row => row.ApplicationId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        var offers = (await dbContext.Offers.AsNoTracking().Where(row => ids.Contains(row.ApplicationId)).Select(row => new { row.ApplicationId, row.Status, row.CreatedDate }).ToListAsync(cancellationToken))
            .GroupBy(row => row.ApplicationId).ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.CreatedDate).First().Status);
        return [.. applications.Select(row =>
        {
            Candidate candidate = candidates[row.CandidateId];
            Requisition requisition = requisitions[row.RequisitionId];
            return new ApplicationItem(
                row.Id, candidate.Id, candidate.Number, candidate.FullName, candidate.Email, candidate.CurrentTitle, candidate.ExperienceYears, candidate.Source,
                requisition.Id, requisition.Number, requisition.Title, row.Stage, row.MatchScore, row.AppliedDate, row.StageChangedDate, row.RejectionReason,
                ratings.TryGetValue(row.Id, out decimal average) ? Math.Round(average, 2) : null, interviews.GetValueOrDefault(row.Id),
                offers.TryGetValue(row.Id, out OfferStatus status) ? status : null);
        })];
    }

    private async Task<List<InterviewItem>> InterviewItemsAsync(IReadOnlyList<(Interview Interview, Guid CandidateId, Guid RequisitionId)> rows, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. rows.Select(row => row.Interview.Id)];
        List<Guid> candidateIds = [.. rows.Select(row => row.CandidateId).Distinct()];
        List<Guid> requisitionIds = [.. rows.Select(row => row.RequisitionId).Distinct()];
        Dictionary<Guid, Candidate> candidates = await dbContext.Candidates.AsNoTracking().Where(row => candidateIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => requisitionIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        List<Scorecard> scorecards = await dbContext.Scorecards.AsNoTracking().Where(row => ids.Contains(row.InterviewId)).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(rows.SelectMany(row => row.Interview.Panel), cancellationToken);
        return [.. rows.Select(row => InterviewItem(row.Interview, candidates[row.CandidateId], requisitions[row.RequisitionId], names, scorecards))];
    }

    private InterviewItem InterviewItem(Interview interview, Candidate candidate, Requisition requisition, Dictionary<Guid, string> names, List<Scorecard> scorecards)
    {
        List<Scorecard> mine = [.. scorecards.Where(row => row.InterviewId == interview.Id)];
        return new InterviewItem(
            interview.Id, interview.ApplicationId, candidate.Id, candidate.FullName, requisition.Title, interview.Round, interview.ScheduledAt, interview.DurationMinutes,
            interview.Mode, interview.Location, interview.Panel, [.. interview.Panel.Select(id => names.GetValueOrDefault(id) ?? "—")], interview.Status, interview.Notes,
            mine.Count, access.Me is { } me && mine.Exists(row => row.InterviewerEmployeeId == me));
    }

    private static ScorecardItem ScorecardItem(Scorecard scorecard, string round, Dictionary<Guid, string> names) => new(
        scorecard.Id, scorecard.InterviewId, round, scorecard.InterviewerEmployeeId, names.GetValueOrDefault(scorecard.InterviewerEmployeeId), scorecard.Ratings,
        scorecard.OverallRating, scorecard.Recommendation, scorecard.Comments, scorecard.SubmittedDate);

    private static BackgroundCheckItem CheckItem(BackgroundCheck check) => new(
        check.Id, check.CandidateId, check.ApplicationId, check.Vendor, check.Checks, check.Status, check.InitiatedOn, check.CompletedOn, check.Remarks);

    private async Task<List<ReferralItem>> ReferralItemsAsync(IReadOnlyList<Referral> referrals, CancellationToken cancellationToken)
    {
        List<Guid> candidateIds = [.. referrals.Select(row => row.CandidateId).Distinct()];
        Dictionary<Guid, string> candidates = await dbContext.Candidates.AsNoTracking().Where(row => candidateIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.LastName == null ? row.FirstName : row.FirstName + " " + row.LastName, cancellationToken);
        List<Guid> requisitionIds = [.. referrals.Where(row => row.RequisitionId != null).Select(row => row.RequisitionId!.Value).Distinct()];
        Dictionary<Guid, string> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => requisitionIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Title, cancellationToken);
        var stages = (await dbContext.JobApplications.AsNoTracking().Where(row => candidateIds.Contains(row.CandidateId)).Select(row => new { row.CandidateId, row.Stage, row.StageChangedDate }).ToListAsync(cancellationToken))
            .GroupBy(row => row.CandidateId).ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.StageChangedDate).First().Stage);
        Dictionary<Guid, string> referrers = await NamesAsync(referrals.Select(row => row.ReferrerEmployeeId), cancellationToken);
        return [.. referrals.Select(row => new ReferralItem(
            row.Id, row.ReferrerEmployeeId, referrers.GetValueOrDefault(row.ReferrerEmployeeId), row.CandidateId, candidates.GetValueOrDefault(row.CandidateId) ?? string.Empty,
            row.RequisitionId, row.RequisitionId is { } requisition ? requisitions.GetValueOrDefault(requisition) : null, row.Status,
            stages.TryGetValue(row.CandidateId, out ApplicationStage stage) ? stage.ToString() : null, row.BonusAmount, row.BonusDueOn, row.CreatedDate))];
    }
}
