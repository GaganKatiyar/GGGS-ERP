using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Recruitment.Infrastructure;
using Gggs.Modules.Workforce.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Application;

public sealed record RequisitionItem(
    Guid Id,
    string Number,
    Guid? ManpowerRequestId,
    string Title,
    Guid DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    Guid? JobId,
    string? JobName,
    Guid? LocationId,
    string? LocationName,
    int Openings,
    int Hired,
    int Remaining,
    string EmploymentTypeCode,
    string WorkModeCode,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears,
    decimal? SalaryMin,
    decimal? SalaryMax,
    IReadOnlyList<string> Skills,
    string? Description,
    Guid? HiringManagerEmployeeId,
    string? HiringManagerName,
    Guid? RecruiterEmployeeId,
    string? RecruiterName,
    string? Priority,
    DateOnly? TargetDate,
    RequisitionStatus Status,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int Applications,
    IReadOnlyDictionary<string, int> Pipeline);

public sealed record RequisitionQuery
{
    public RequisitionStatus? Status { get; init; }

    public Guid? DepartmentId { get; init; }

    public string? Search { get; init; }
}

public sealed record PostingItem(
    Guid Id,
    Guid RequisitionId,
    string RequisitionNumber,
    string Title,
    string Description,
    bool Internal,
    bool External,
    PostingStatus Status,
    DateOnly? PublishedOn,
    DateOnly? ExpiresOn,
    string? DepartmentName,
    string? LocationName,
    string EmploymentTypeCode,
    string WorkModeCode,
    IReadOnlyList<string> Skills,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears);

public sealed record PostingBody(Guid RequisitionId, string Title, string Description, bool Internal, bool External, DateOnly? ExpiresOn);

public sealed record AgencyItem(Guid Id, string Name, string? ContactName, string? Email, string? Phone, decimal FeePercent, bool IsActive, int Candidates, int Hires);

public sealed record AgencyBody(string Name, string? ContactName, string? Email, string? Phone, decimal FeePercent, bool IsActive = true);

public sealed record CountRow(string Key, string Label, int Count);

public sealed record SourceRow(string Source, int Applications, int Interviewed, int Offered, int Hired, decimal ConversionPercent);

public sealed record RecruiterRow(Guid? RecruiterEmployeeId, string? RecruiterName, int Requisitions, int Applications, int Hires, decimal? AverageDaysToHire);

public sealed record RecruitmentAnalytics(
    DateOnly From,
    DateOnly To,
    int OpenRequisitions,
    int OpenPositions,
    int Applications,
    int Hires,
    decimal? AverageDaysToHire,
    decimal? CostPerHire,
    decimal TotalHiringCost,
    decimal? OfferAcceptancePercent,
    int OffersAccepted,
    int OffersDeclined,
    IReadOnlyList<CountRow> Pipeline,
    IReadOnlyList<SourceRow> Sources,
    IReadOnlyList<RecruiterRow> Recruiters);

/// <summary>
/// Requisitions, job postings, agencies, the tenant's recruitment rules and hiring analytics (spec §12).
/// A requisition is opened from an approved manpower request or by a recruiter directly.
/// </summary>
internal sealed class RecruitmentService(
    AppDbContext dbContext,
    RecruitmentAccess access,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    ITenantClock clock)
{
    // ----- Requisitions -------------------------------------------------------------------------

    public async Task<IReadOnlyList<RequisitionItem>> RequisitionsAsync(RequisitionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Requisition> rows = await access.RequisitionsAsync(Permissions.Recruitment.RequisitionView, cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.DepartmentId is { } department)
        {
            rows = rows.Where(row => row.DepartmentId == department);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            rows = rows.Where(row => row.Title.Contains(term) || row.Number.Contains(term));
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.OpenedOn).ThenByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<RequisitionItem> RequisitionAsync(Guid id, CancellationToken cancellationToken)
        => (await ItemsAsync([await access.RequisitionAsync(id, Permissions.Recruitment.RequisitionView, tracked: false, cancellationToken)], cancellationToken))[0];

    public async Task<RequisitionItem> CreateRequisitionAsync(RequisitionDetails details, CancellationToken cancellationToken)
    {
        await ValidateAsync(details, cancellationToken);
        var requisition = Requisition.Open(await NextRequisitionNumberAsync(cancellationToken), null, details, await clock.TodayAsync(cancellationToken));
        dbContext.Requisitions.Add(requisition);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await RequisitionAsync(requisition.Id, cancellationToken);
    }

    public async Task<RequisitionItem> UpdateRequisitionAsync(Guid id, RequisitionDetails details, CancellationToken cancellationToken)
    {
        Requisition requisition = await access.RequisitionAsync(id, Permissions.Recruitment.RequisitionManage, tracked: true, cancellationToken);
        await ValidateAsync(details, cancellationToken);
        requisition.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await RequisitionAsync(id, cancellationToken);
    }

    public async Task<RequisitionItem> ChangeRequisitionAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        Requisition requisition = await access.RequisitionAsync(id, Permissions.Recruitment.RequisitionManage, tracked: true, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        switch (action)
        {
            case "hold":
                requisition.Hold();
                break;
            case "resume":
                requisition.Resume();
                break;
            case "close":
            case "cancel":
                requisition.Close(today, cancelled: action == "cancel");
                foreach (JobPosting posting in await dbContext.JobPostings.Where(row => row.RequisitionId == id && row.Status != PostingStatus.Closed).ToListAsync(cancellationToken))
                {
                    posting.Close();
                }

                break;
            default:
                throw new DomainRuleException("REQUISITION_ACTION", $"'{action}' is not a requisition action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await RequisitionAsync(id, cancellationToken);
    }

    /// <summary>Opens a requisition for an approved manpower request (once per request).</summary>
    public async Task OpenFromRequestAsync(ManpowerRequestApprovedIntegrationEvent approved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approved);
        if (await dbContext.Requisitions.AnyAsync(row => row.ManpowerRequestId == approved.RequestId, cancellationToken))
        {
            return;
        }

        var details = new RequisitionDetails(
            approved.Title, approved.DepartmentId, approved.PositionId, approved.JobId, approved.LocationId, approved.Positions, approved.EmploymentTypeCode, approved.WorkModeCode,
            approved.MinExperienceYears, approved.MaxExperienceYears, approved.SalaryMin, approved.SalaryMax, approved.Skills,
            string.IsNullOrWhiteSpace(approved.Justification) ? null : approved.Justification, approved.RequesterEmployeeId, null, approved.Priority, approved.RequiredBy);
        dbContext.Requisitions.Add(Requisition.Open(await NextRequisitionNumberAsync(cancellationToken), approved.RequestId, details, await clock.TodayAsync(cancellationToken)));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Postings -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<PostingItem>> PostingsAsync(Guid? requisitionId, CancellationToken cancellationToken)
    {
        IQueryable<JobPosting> rows = dbContext.JobPostings.AsNoTracking();
        if (requisitionId is { } requisition)
        {
            rows = rows.Where(row => row.RequisitionId == requisition);
        }

        return await PostingItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>The internal job board: published internal postings that have not expired.</summary>
    public async Task<IReadOnlyList<PostingItem>> JobBoardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<JobPosting> live = await dbContext.JobPostings.AsNoTracking()
            .Where(row => row.Status == PostingStatus.Published && row.Internal && (row.ExpiresOn == null || row.ExpiresOn >= today))
            .OrderByDescending(row => row.PublishedOn)
            .ToListAsync(cancellationToken);
        List<Guid> open = await dbContext.Requisitions.AsNoTracking().Where(row => row.Status == RequisitionStatus.Open).Select(row => row.Id).ToListAsync(cancellationToken);
        return await PostingItemsAsync([.. live.Where(posting => open.Contains(posting.RequisitionId))], cancellationToken);
    }

    public async Task<PostingItem> SavePostingAsync(Guid? id, PostingBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.RequisitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Requisition), body.RequisitionId);
        JobPosting posting;
        if (id is { } existing)
        {
            posting = await dbContext.JobPostings.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(JobPosting), existing);
            posting.Update(body.Title, body.Description, body.Internal, body.External, body.ExpiresOn);
        }
        else
        {
            requisition.EnsureAccepting();
            posting = JobPosting.Draft(requisition.Id, body.Title, body.Description, body.Internal, body.External, body.ExpiresOn);
            dbContext.JobPostings.Add(posting);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await PostingItemsAsync([posting], cancellationToken))[0];
    }

    public async Task<PostingItem> ChangePostingAsync(Guid id, bool publish, CancellationToken cancellationToken)
    {
        JobPosting posting = await dbContext.JobPostings.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(JobPosting), id);
        if (publish)
        {
            Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == posting.RequisitionId, cancellationToken);
            requisition.EnsureAccepting();
            posting.Publish(await clock.TodayAsync(cancellationToken));
        }
        else
        {
            posting.Close();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await PostingItemsAsync([posting], cancellationToken))[0];
    }

    // ----- Agencies -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<AgencyItem>> AgenciesAsync(CancellationToken cancellationToken)
    {
        List<Agency> agencies = await dbContext.Agencies.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken);
        var candidates = await dbContext.Candidates.AsNoTracking().Where(row => row.AgencyId != null)
            .GroupBy(row => row.AgencyId!.Value)
            .Select(group => new { AgencyId = group.Key, Count = group.Count(), Hires = group.Count(row => row.EmployeeId != null) })
            .ToListAsync(cancellationToken);
        return [.. agencies.Select(agency =>
        {
            var stats = candidates.Find(row => row.AgencyId == agency.Id);
            return new AgencyItem(agency.Id, agency.Name, agency.ContactName, agency.Email, agency.Phone, agency.FeePercent, agency.IsActive, stats?.Count ?? 0, stats?.Hires ?? 0);
        })];
    }

    public async Task<IReadOnlyList<AgencyItem>> SaveAgencyAsync(Guid? id, AgencyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (id is { } existing)
        {
            Agency agency = await dbContext.Agencies.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(Agency), existing);
            agency.Update(body.Name, body.ContactName, body.Email, body.Phone, body.FeePercent, body.IsActive);
        }
        else
        {
            dbContext.Agencies.Add(Agency.Create(body.Name, body.ContactName, body.Email, body.Phone, body.FeePercent));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await AgenciesAsync(cancellationToken);
    }

    // ----- Settings -----------------------------------------------------------------------------

    public async Task<RecruitmentRules> RulesAsync(CancellationToken cancellationToken)
        => (await dbContext.RecruitmentSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToRules() ?? RecruitmentRules.Default;

    public async Task<RecruitmentRules> SaveRulesAsync(RecruitmentRules rules, CancellationToken cancellationToken)
    {
        RecruitmentSettings? settings = await dbContext.RecruitmentSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = RecruitmentSettings.Defaults();
            dbContext.RecruitmentSettings.Add(settings);
        }

        settings.Update(rules);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings.ToRules();
    }

    // ----- Analytics ----------------------------------------------------------------------------

    /// <summary>Time to hire, cost per hire, offer acceptance, source effectiveness, recruiter performance and the live pipeline (spec §12).</summary>
    public async Task<RecruitmentAnalytics> AnalyticsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly end = to ?? today;
        DateOnly start = from ?? end.AddMonths(-12).AddDays(1);
        if (start > end)
        {
            throw new DomainRuleException("ANALYTICS_RANGE", "The start date is after the end date.");
        }

        DateTime startAt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTime endAt = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        List<Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().ToListAsync(cancellationToken);
        List<JobApplication> applications = await dbContext.JobApplications.AsNoTracking()
            .Where(row => (row.AppliedDate >= startAt && row.AppliedDate < endAt) || row.Stage < ApplicationStage.Hired || (row.Stage == ApplicationStage.Hired && row.StageChangedDate >= startAt && row.StageChangedDate < endAt))
            .ToListAsync(cancellationToken);
        List<JobApplication> applied = [.. applications.Where(row => row.AppliedDate >= startAt && row.AppliedDate < endAt)];
        List<JobApplication> hired = [.. applications.Where(row => row.Stage == ApplicationStage.Hired && row.StageChangedDate >= startAt && row.StageChangedDate < endAt)];
        List<Guid> candidateIds = [.. applications.Select(row => row.CandidateId).Distinct()];
        Dictionary<Guid, Candidate> candidates = await dbContext.Candidates.AsNoTracking().Where(row => candidateIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, decimal> agencyFees = await dbContext.Agencies.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.FeePercent, cancellationToken);
        List<Offer> offers = await dbContext.Offers.AsNoTracking().Where(row => row.RespondedDate >= startAt && row.RespondedDate < endAt || row.Status == OfferStatus.Accepted).ToListAsync(cancellationToken);
        List<Guid> hiredIds = [.. hired.Select(row => row.Id)];
        Dictionary<Guid, Offer> acceptedByApplication = offers.Where(row => row.Status == OfferStatus.Accepted && hiredIds.Contains(row.ApplicationId))
            .GroupBy(row => row.ApplicationId).ToDictionary(group => group.Key, group => group.First());
        decimal referralBonuses = await dbContext.Referrals.AsNoTracking()
            .Where(row => row.HiredEmployeeId != null && row.BonusAmount != null && candidateIds.Contains(row.CandidateId))
            .Select(row => new { row.CandidateId, row.BonusAmount })
            .ToListAsync(cancellationToken) is var bonuses
            ? bonuses.Where(row => hired.Any(application => application.CandidateId == row.CandidateId)).Sum(row => row.BonusAmount ?? 0)
            : 0;

        decimal agencyCost = hired.Sum(application =>
            candidates.TryGetValue(application.CandidateId, out Candidate? candidate) && candidate.AgencyId is { } agency && agencyFees.TryGetValue(agency, out decimal fee)
            && acceptedByApplication.TryGetValue(application.Id, out Offer? offer)
                ? Math.Round(offer.AnnualCtc * fee / 100, 2)
                : 0);
        decimal totalCost = agencyCost + referralBonuses;
        int accepted = offers.Count(row => row.Status == OfferStatus.Accepted && row.RespondedDate >= startAt && row.RespondedDate < endAt);
        int declined = offers.Count(row => row.Status == OfferStatus.Declined);

        List<CountRow> pipeline = [.. Enum.GetValues<ApplicationStage>()
            .Where(stage => stage < ApplicationStage.Hired)
            .Select(stage => new CountRow(stage.ToString(), stage.ToString(), applications.Count(row => row.Stage == stage)))];

        List<SourceRow> sources = [.. applied
            .GroupBy(row => candidates.TryGetValue(row.CandidateId, out Candidate? candidate) ? candidate.Source : CandidateSource.Direct)
            .Select(group =>
            {
                int count = group.Count();
                int interviewed = group.Count(row => Reached(row, ApplicationStage.Interview));
                int offered = group.Count(row => Reached(row, ApplicationStage.Offer));
                int hires = group.Count(row => row.Stage == ApplicationStage.Hired);
                return new SourceRow(group.Key.ToString(), count, interviewed, offered, hires, count == 0 ? 0 : Math.Round(100m * hires / count, 1));
            })
            .OrderByDescending(row => row.Hired).ThenByDescending(row => row.Applications)];

        Dictionary<Guid, string> recruiterNames = (await employees.GetManyAsync(requisitions.Where(row => row.RecruiterEmployeeId != null).Select(row => row.RecruiterEmployeeId!.Value).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        List<RecruiterRow> recruiters = [.. requisitions
            .Where(row => row.Status == RequisitionStatus.Open || applied.Any(application => application.RequisitionId == row.Id) || hired.Any(application => application.RequisitionId == row.Id))
            .GroupBy(row => row.RecruiterEmployeeId)
            .Select(group =>
            {
                HashSet<Guid> ids = [.. group.Select(row => row.Id)];
                List<JobApplication> theirHires = [.. hired.Where(row => ids.Contains(row.RequisitionId))];
                return new RecruiterRow(
                    group.Key, group.Key is { } recruiter ? recruiterNames.GetValueOrDefault(recruiter) : null, ids.Count,
                    applied.Count(row => ids.Contains(row.RequisitionId)), theirHires.Count, AverageDays(theirHires));
            })
            .OrderByDescending(row => row.Hires)];

        return new RecruitmentAnalytics(
            start, end,
            requisitions.Count(row => row.Status == RequisitionStatus.Open),
            requisitions.Where(row => row.Status is RequisitionStatus.Open or RequisitionStatus.OnHold).Sum(row => row.Remaining),
            applied.Count, hired.Count, AverageDays(hired),
            hired.Count == 0 ? null : Math.Round(totalCost / hired.Count, 2), totalCost,
            accepted + declined == 0 ? null : Math.Round(100m * accepted / (accepted + declined), 1), accepted, declined,
            pipeline, sources, recruiters);

        static bool Reached(JobApplication application, ApplicationStage stage) => application.History.Any(change => change.Stage == stage) || (application.Stage >= stage && application.Stage <= ApplicationStage.Hired);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static decimal? AverageDays(List<JobApplication> hires)
        => hires.Count == 0 ? null : Math.Round((decimal)hires.Average(row => (row.StageChangedDate - row.AppliedDate).TotalDays), 1);

    private async Task ValidateAsync(RequisitionDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        List<OrgReference> references = [new(OrgUnitKind.Department, details.DepartmentId)];
        if (details.JobId is { } job)
        {
            references.Add(new OrgReference(OrgUnitKind.Job, job));
        }

        if (details.LocationId is { } location)
        {
            references.Add(new OrgReference(OrgUnitKind.Location, location));
        }

        if (details.PositionId is { } position)
        {
            references.Add(new OrgReference(OrgUnitKind.Position, position));
        }

        IReadOnlyList<OrgReference> invalid = await organization.FindInvalidAsync(references, cancellationToken);
        if (invalid.Count > 0)
        {
            throw new DomainRuleException("REQUISITION_ORG", $"The {invalid[0].Kind.ToString().ToLowerInvariant()} chosen is not active.");
        }
    }

    private async Task<string> NextRequisitionNumberAsync(CancellationToken cancellationToken)
    {
        int year = (await clock.TodayAsync(cancellationToken)).Year;
        return await RecruitmentNumbers.NextAsync(dbContext.Requisitions.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Number), $"REQ-{year}-", cancellationToken);
    }

    private async Task<List<RequisitionItem>> ItemsAsync(IReadOnlyList<Requisition> rows, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. rows.Select(row => row.Id)];
        var stages = await dbContext.JobApplications.AsNoTracking().Where(row => ids.Contains(row.RequisitionId))
            .GroupBy(row => new { row.RequisitionId, row.Stage })
            .Select(group => new { group.Key.RequisitionId, group.Key.Stage, Count = group.Count() })
            .ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, rows.Select(row => row.DepartmentId).Distinct(), cancellationToken);
        IReadOnlyDictionary<Guid, string> jobs = await organization.GetNamesAsync(OrgUnitKind.Job, rows.Where(row => row.JobId != null).Select(row => row.JobId!.Value).Distinct(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await organization.GetNamesAsync(OrgUnitKind.Location, rows.Where(row => row.LocationId != null).Select(row => row.LocationId!.Value).Distinct(), cancellationToken);
        Dictionary<Guid, string> people = (await employees.GetManyAsync(
                rows.SelectMany(row => new[] { row.HiringManagerEmployeeId, row.RecruiterEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. rows.Select(row => new RequisitionItem(
            row.Id, row.Number, row.ManpowerRequestId, row.Title, row.DepartmentId, departments.GetValueOrDefault(row.DepartmentId), row.PositionId,
            row.JobId, row.JobId is { } job ? jobs.GetValueOrDefault(job) : null, row.LocationId, row.LocationId is { } location ? locations.GetValueOrDefault(location) : null,
            row.Openings, row.Hired, row.Remaining, row.EmploymentTypeCode, row.WorkModeCode, row.MinExperienceYears, row.MaxExperienceYears, row.SalaryMin, row.SalaryMax,
            row.Skills, row.Description, row.HiringManagerEmployeeId, row.HiringManagerEmployeeId is { } manager ? people.GetValueOrDefault(manager) : null,
            row.RecruiterEmployeeId, row.RecruiterEmployeeId is { } recruiter ? people.GetValueOrDefault(recruiter) : null, row.Priority, row.TargetDate, row.Status,
            row.OpenedOn, row.ClosedOn, stages.Where(stage => stage.RequisitionId == row.Id).Sum(stage => stage.Count),
            stages.Where(stage => stage.RequisitionId == row.Id).ToDictionary(stage => stage.Stage.ToString(), stage => stage.Count)))];
    }

    private async Task<List<PostingItem>> PostingItemsAsync(IReadOnlyList<JobPosting> postings, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. postings.Select(row => row.RequisitionId).Distinct()];
        Dictionary<Guid, Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, requisitions.Values.Select(row => row.DepartmentId).Distinct(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await organization.GetNamesAsync(OrgUnitKind.Location, requisitions.Values.Where(row => row.LocationId != null).Select(row => row.LocationId!.Value).Distinct(), cancellationToken);
        return [.. postings.Select(posting =>
        {
            Requisition requisition = requisitions[posting.RequisitionId];
            return new PostingItem(
                posting.Id, posting.RequisitionId, requisition.Number, posting.Title, posting.Description, posting.Internal, posting.External, posting.Status, posting.PublishedOn,
                posting.ExpiresOn, departments.GetValueOrDefault(requisition.DepartmentId), requisition.LocationId is { } location ? locations.GetValueOrDefault(location) : null,
                requisition.EmploymentTypeCode, requisition.WorkModeCode, requisition.Skills, requisition.MinExperienceYears, requisition.MaxExperienceYears);
        })];
    }
}

/// <summary>Sequential, human-readable numbers per prefix: REQ-2026-0001, CAN-000123, OFF-2026-0007.</summary>
internal static class RecruitmentNumbers
{
    public static async Task<string> NextAsync(IQueryable<string> numbers, string prefix, CancellationToken cancellationToken, string format = "0000")
    {
        List<string> taken = await numbers.Where(number => number.StartsWith(prefix)).ToListAsync(cancellationToken);
        int next = taken.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return prefix + next.ToString(format, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Who sees which requisitions: everyone with tenant scope; otherwise the requisitions whose hiring
/// manager or recruiter is in the caller's scope, or is the caller.
/// </summary>
internal sealed class RecruitmentAccess(AppDbContext dbContext, IEmployeeDirectory employees, IPermissionProvider permissions, ICurrentUser currentUser)
{
    public Guid? Me => currentUser.EmployeeId;

    public async Task<bool> HasAsync(string permission, CancellationToken cancellationToken) => (await permissions.GetCurrentAsync(cancellationToken)).Has(permission);

    public async Task<IQueryable<Requisition>> RequisitionsAsync(string permission, CancellationToken cancellationToken)
    {
        IQueryable<Requisition> rows = dbContext.Requisitions.AsNoTracking();
        if (!await HasAsync(permission, cancellationToken))
        {
            return rows.Where(row => false);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(permission, cancellationToken);
        if (scope is null)
        {
            return rows;
        }

        Guid? me = Me;
        return rows.Where(row =>
            (row.HiringManagerEmployeeId != null && (scope.Contains(row.HiringManagerEmployeeId.Value) || row.HiringManagerEmployeeId == me))
            || (row.RecruiterEmployeeId != null && (scope.Contains(row.RecruiterEmployeeId.Value) || row.RecruiterEmployeeId == me)));
    }

    /// <summary>The ids of the requisitions the caller may see under the permission; null when all of them.</summary>
    public async Task<List<Guid>?> RequisitionIdsAsync(string permission, CancellationToken cancellationToken)
    {
        if (await HasAsync(permission, cancellationToken) && await employees.InScopeIdsAsync(permission, cancellationToken) is null)
        {
            return null;
        }

        return await (await RequisitionsAsync(permission, cancellationToken)).Select(row => row.Id).ToListAsync(cancellationToken);
    }

    public async Task<Requisition> RequisitionAsync(Guid id, string permission, bool tracked, CancellationToken cancellationToken)
    {
        bool visible = await (await RequisitionsAsync(permission, cancellationToken)).AnyAsync(row => row.Id == id, cancellationToken);
        if (!visible)
        {
            throw new NotFoundException(nameof(Requisition), id);
        }

        return await (tracked ? dbContext.Requisitions : dbContext.Requisitions.AsNoTracking()).FirstAsync(row => row.Id == id, cancellationToken);
    }

    public async Task EnsureRequisitionAsync(Guid requisitionId, string permission, CancellationToken cancellationToken)
    {
        if (!await (await RequisitionsAsync(permission, cancellationToken)).AnyAsync(row => row.Id == requisitionId, cancellationToken))
        {
            throw new NotFoundException(nameof(Requisition), requisitionId);
        }
    }
}
