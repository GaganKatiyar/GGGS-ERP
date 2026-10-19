using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workforce.Contracts;
using Gggs.Modules.Workforce.Domain;
using Gggs.Modules.Workforce.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workforce.Application;

public sealed record PlanItem(
    Guid Id,
    string Name,
    int Year,
    PlanStatus Status,
    DateOnly EffectiveDate,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    string? Notes,
    int CurrentHeadcount,
    int PlannedHeadcount,
    decimal SalaryBudget,
    decimal HiringBudget,
    int Lines);

public sealed record PlanLineItem(
    Guid Id,
    Guid DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? JobId,
    string? JobName,
    int CurrentHeadcount,
    int LiveHeadcount,
    int PlannedHeadcount,
    int HeadcountGap,
    int OpenPositions,
    decimal SalaryBudget,
    decimal HiringBudget,
    string? Notes);

public sealed record PlanView(PlanItem Plan, IReadOnlyList<PlanLineItem> Lines, bool CanManage);

public sealed record PlanBody(string Name, int Year, DateOnly EffectiveDate, Guid? OwnerEmployeeId, string? Notes);

public sealed record PlanLinesBody(IReadOnlyList<PlanLineDetails> Lines);

/// <summary>Whether a request fits the approved plan: what the plan allows, what is there and already asked for.</summary>
public sealed record PlanFit(string PlanName, int Planned, int Current, int Committed, int Remaining, bool Within);

public sealed record RequestItem(
    Guid Id,
    string Number,
    string Title,
    Guid DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    Guid? JobId,
    string? JobName,
    Guid? LocationId,
    string? LocationName,
    int Positions,
    IReadOnlyList<string> Skills,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears,
    string EmploymentTypeCode,
    string WorkModeCode,
    decimal? SalaryMin,
    decimal? SalaryMax,
    decimal? Budget,
    RequestPriority Priority,
    DateOnly? RequiredBy,
    RequestReason Reason,
    Guid? ReplacementForEmployeeId,
    string? ReplacementForName,
    string? Justification,
    Guid RequesterEmployeeId,
    string? RequesterName,
    ManpowerRequestStatus Status,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate,
    PlanFit? PlanFit,
    bool CanEdit);

public sealed record RequestQuery
{
    public ManpowerRequestStatus? Status { get; init; }

    public Guid? DepartmentId { get; init; }

    public bool? Mine { get; init; }
}

public sealed record RequestBody(RequestDetails Details, bool Submit);

public sealed record DepartmentDemand(Guid DepartmentId, string? DepartmentName, int Planned, int Live, int Gap, int OpenPositions, int PendingPositions);

public sealed record WorkforceDashboard(
    PlanItem? Plan,
    int Planned,
    int Live,
    int Gap,
    int OpenPositions,
    int PendingRequests,
    decimal SalaryBudget,
    decimal HiringBudget,
    IReadOnlyList<DepartmentDemand> Departments,
    IReadOnlyDictionary<string, int> RequestsByStatus);

/// <summary>
/// Workforce planning (spec §11): yearly headcount plans by department, location and job against the
/// live headcount, and manpower requests - checked against the plan - approved through the workflow
/// on their way to recruitment.
/// </summary>
internal sealed class WorkforceService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    IWorkflowStarter workflow,
    IIntegrationEventPublisher publisher,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string PlanSubjectType = "workforce.plan";
    public const string RequestSubjectType = "workforce.manpower-request";

    public static WorkflowSubject PlanSubject { get; } = new(
        PlanSubjectType,
        "Approve a workforce plan",
        "workforce",
        [
            new WorkflowSubjectField("plannedHeadcount", "Planned headcount", WorkflowFieldType.Number),
            new WorkflowSubjectField("hiringBudget", "Hiring budget", WorkflowFieldType.Number),
            new WorkflowSubjectField("salaryBudget", "Salary budget", WorkflowFieldType.Number),
        ]);

    public static WorkflowSubject RequestSubject { get; } = new(
        RequestSubjectType,
        "Approve a manpower request",
        "workforce",
        [
            new WorkflowSubjectField("positions", "Positions", WorkflowFieldType.Number),
            new WorkflowSubjectField("budget", "Budget", WorkflowFieldType.Number),
            new WorkflowSubjectField("withinPlan", "Within the approved plan (true/false)", WorkflowFieldType.Text),
            new WorkflowSubjectField("reason", "Reason", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Plans --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PlanItem>> PlansAsync(int? year, CancellationToken cancellationToken)
    {
        IQueryable<WorkforcePlan> plans = dbContext.WorkforcePlans.AsNoTracking();
        if (year is { } wanted)
        {
            plans = plans.Where(plan => plan.Year == wanted);
        }

        List<WorkforcePlan> list = await plans.OrderByDescending(plan => plan.Year).ThenByDescending(plan => plan.CreatedDate).ToListAsync(cancellationToken);
        List<Guid> ids = [.. list.Select(plan => plan.Id)];
        ILookup<Guid, WorkforcePlanLine> lines = (await dbContext.WorkforcePlanLines.AsNoTracking().Where(line => ids.Contains(line.PlanId)).ToListAsync(cancellationToken)).ToLookup(line => line.PlanId);
        Dictionary<Guid, string> owners = await NamesAsync(list.Select(plan => plan.OwnerEmployeeId), cancellationToken);
        return [.. list.Select(plan => Item(plan, [.. lines[plan.Id]], owners))];
    }

    public async Task<PlanView> PlanAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkforcePlan plan = await dbContext.WorkforcePlans.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(WorkforcePlan), id);
        List<WorkforcePlanLine> lines = await dbContext.WorkforcePlanLines.AsNoTracking().Where(line => line.PlanId == id).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, int> live = await employees.CountByDepartmentAsync(lines.Select(line => line.DepartmentId).Distinct(), cancellationToken);
        List<ManpowerRequest> open = await OpenRequestsAsync(lines.Select(line => line.DepartmentId), cancellationToken);
        Names names = await OrgNamesAsync(lines.Select(line => line.DepartmentId), lines.Select(line => line.LocationId), lines.Select(line => line.JobId), cancellationToken);
        Dictionary<Guid, string> owners = await NamesAsync([plan.OwnerEmployeeId], cancellationToken);

        return new PlanView(
            Item(plan, lines, owners),
            [.. lines
                .Select(line => new PlanLineItem(
                    line.Id, line.DepartmentId, names.Department(line.DepartmentId), line.LocationId, names.Location(line.LocationId), line.JobId, names.Job(line.JobId),
                    line.CurrentHeadcount, live.GetValueOrDefault(line.DepartmentId), line.PlannedHeadcount, line.HeadcountGap,
                    open.Where(request => request.DepartmentId == line.DepartmentId && (line.JobId is null || request.JobId == line.JobId) && request.Status == ManpowerRequestStatus.Approved).Sum(request => request.Positions),
                    line.SalaryBudget, line.HiringBudget, line.Notes))
                .OrderBy(line => line.DepartmentName, StringComparer.CurrentCulture)],
            await HasAsync(Permissions.Workforce.PlanManage, cancellationToken) && plan.Status is PlanStatus.Draft or PlanStatus.Rejected);
    }

    public async Task<PlanView> SavePlanAsync(Guid? id, PlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        WorkforcePlan plan;
        if (id is { } existing)
        {
            plan = await dbContext.WorkforcePlans.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(WorkforcePlan), existing);
            plan.Update(body.Name, body.Year, body.EffectiveDate, body.OwnerEmployeeId, body.Notes);
        }
        else
        {
            plan = WorkforcePlan.Draft(body.Name, body.Year, body.EffectiveDate, body.OwnerEmployeeId ?? currentUser.EmployeeId, body.Notes);
            dbContext.WorkforcePlans.Add(plan);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(plan.Id, cancellationToken);
    }

    /// <summary>Replaces the plan's lines; each line's current headcount is taken as it stands today.</summary>
    public async Task<PlanView> SaveLinesAsync(Guid id, PlanLinesBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        WorkforcePlan plan = await dbContext.WorkforcePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(WorkforcePlan), id);
        plan.EnsureEditable();
        List<PlanLineDetails> lines = [.. body.Lines ?? []];
        if (lines.GroupBy(line => (line.DepartmentId, line.LocationId, line.JobId)).Any(group => group.Count() > 1))
        {
            throw new DomainRuleException("PLAN_LINE", "Each department, location and job appears once in a plan.");
        }

        IReadOnlyList<OrgReference> invalid = await organization.FindInvalidAsync(
            [.. lines.Select(line => new OrgReference(OrgUnitKind.Department, line.DepartmentId)),
             .. lines.Where(line => line.LocationId is not null).Select(line => new OrgReference(OrgUnitKind.Location, line.LocationId!.Value)),
             .. lines.Where(line => line.JobId is not null).Select(line => new OrgReference(OrgUnitKind.Job, line.JobId!.Value))],
            cancellationToken);
        if (invalid.Count > 0)
        {
            throw new DomainRuleException("PLAN_LINE", $"A line names a {invalid[0].Kind.ToString().ToLowerInvariant()} that is not active.");
        }

        IReadOnlyDictionary<Guid, int> live = await employees.CountByDepartmentAsync(lines.Select(line => line.DepartmentId).Distinct(), cancellationToken);
        dbContext.WorkforcePlanLines.RemoveRange(await dbContext.WorkforcePlanLines.Where(line => line.PlanId == id).ToListAsync(cancellationToken));
        foreach (PlanLineDetails line in lines)
        {
            dbContext.WorkforcePlanLines.Add(WorkforcePlanLine.For(id, line, live.GetValueOrDefault(line.DepartmentId)));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PlanView> SubmitPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkforcePlan plan = await dbContext.WorkforcePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(WorkforcePlan), id);
        List<WorkforcePlanLine> lines = await dbContext.WorkforcePlanLines.AsNoTracking().Where(line => line.PlanId == id).ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            throw new DomainRuleException("PLAN_EMPTY", "Add the departments the plan covers before submitting it.");
        }

        plan.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = PlanSubjectType,
                SubjectId = plan.Id,
                Title = $"Workforce plan {plan.Year}: {plan.Name}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = $"/workforce/plans/{plan.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["plannedHeadcount"] = lines.Sum(line => line.PlannedHeadcount).ToString(CultureInfo.InvariantCulture),
                    ["hiringBudget"] = lines.Sum(line => line.HiringBudget).ToString(CultureInfo.InvariantCulture),
                    ["salaryBudget"] = lines.Sum(line => line.SalaryBudget).ToString(CultureInfo.InvariantCulture),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            plan.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            plan.Decide(approved: true, Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(plan.Id, cancellationToken);
    }

    public async Task<PlanView> ClosePlanAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkforcePlan plan = await dbContext.WorkforcePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(WorkforcePlan), id);
        plan.Close();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task DecidePlanAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        WorkforcePlan? plan = await dbContext.WorkforcePlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (plan is not null)
        {
            plan.Decide(approved, Now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    // ----- Manpower requests --------------------------------------------------------------------

    public async Task<IReadOnlyList<RequestItem>> RequestsAsync(RequestQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<ManpowerRequest> rows = dbContext.ManpowerRequests.AsNoTracking();
        Guid? me = currentUser.EmployeeId;
        IReadOnlyCollection<Guid>? scope = await HasAsync(Permissions.Workforce.RequestView, cancellationToken)
            ? await employees.InScopeIdsAsync(Permissions.Workforce.RequestView, cancellationToken)
            : [];
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.RequesterEmployeeId) || row.RequesterEmployeeId == me);
        }

        if (query.Mine == true)
        {
            rows = rows.Where(row => row.RequesterEmployeeId == me);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.DepartmentId is { } department)
        {
            rows = rows.Where(row => row.DepartmentId == department);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), withFit: false, cancellationToken);
    }

    public async Task<RequestItem> RequestAsync(Guid id, CancellationToken cancellationToken)
        => (await ItemsAsync([await VisibleRequestAsync(id, tracked: false, cancellationToken)], withFit: true, cancellationToken))[0];

    public async Task<RequestItem> RaiseAsync(RequestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("WORKFORCE_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        await ValidateAsync(body.Details, cancellationToken);
        var request = ManpowerRequest.Raise(await NextNumberAsync(cancellationToken), me, body.Details);
        dbContext.ManpowerRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit)
        {
            await SubmitAsync(request, cancellationToken);
        }

        return (await ItemsAsync([request], withFit: true, cancellationToken))[0];
    }

    public async Task<RequestItem> UpdateRequestAsync(Guid id, RequestDetails details, CancellationToken cancellationToken)
    {
        ManpowerRequest request = await EditableRequestAsync(id, cancellationToken);
        await ValidateAsync(details, cancellationToken);
        request.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([request], withFit: true, cancellationToken))[0];
    }

    public async Task<RequestItem> SubmitRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        ManpowerRequest request = await EditableRequestAsync(id, cancellationToken);
        await SubmitAsync(request, cancellationToken);
        return (await ItemsAsync([request], withFit: true, cancellationToken))[0];
    }

    public async Task<RequestItem> CancelRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        ManpowerRequest request = await EditableRequestAsync(id, cancellationToken, allowApproved: true);
        request.Cancel();
        await workflow.WithdrawAsync(RequestSubjectType, request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([request], withFit: false, cancellationToken))[0];
    }

    public async Task DecideRequestAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        ManpowerRequest? request = await dbContext.ManpowerRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (request?.Status != ManpowerRequestStatus.PendingApproval)
        {
            return;
        }

        request.Decide(approved, Now);
        if (request.Status == ManpowerRequestStatus.Approved)
        {
            PublishApproved(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FulfilAsync(Guid requestId, CancellationToken cancellationToken)
    {
        ManpowerRequest? request = await dbContext.ManpowerRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        if (request is not null)
        {
            request.Fulfil();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    // ----- Dashboard ----------------------------------------------------------------------------

    public async Task<WorkforceDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        WorkforcePlan? plan = await dbContext.WorkforcePlans.AsNoTracking()
            .Where(row => row.Status == PlanStatus.Approved && row.Year <= today.Year)
            .OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.DecidedDate)
            .FirstOrDefaultAsync(cancellationToken);
        List<WorkforcePlanLine> lines = plan is null ? [] : await dbContext.WorkforcePlanLines.AsNoTracking().Where(line => line.PlanId == plan.Id).ToListAsync(cancellationToken);
        List<ManpowerRequest> requests = await dbContext.ManpowerRequests.AsNoTracking().ToListAsync(cancellationToken);
        List<Guid> departments = [.. lines.Select(line => line.DepartmentId).Concat(requests.Where(request => request.Status is ManpowerRequestStatus.Approved or ManpowerRequestStatus.PendingApproval).Select(request => request.DepartmentId)).Distinct()];
        IReadOnlyDictionary<Guid, int> live = await employees.CountByDepartmentAsync(departments, cancellationToken);
        IReadOnlyDictionary<Guid, string> names = await organization.GetNamesAsync(OrgUnitKind.Department, departments, cancellationToken);
        Dictionary<Guid, string> owners = plan is null ? [] : await NamesAsync([plan.OwnerEmployeeId], cancellationToken);

        List<DepartmentDemand> demand = [.. departments.Select(department =>
        {
            int planned = lines.Where(line => line.DepartmentId == department).Sum(line => line.PlannedHeadcount);
            int current = live.GetValueOrDefault(department);
            return new DepartmentDemand(
                department, names.GetValueOrDefault(department), planned, current, planned - current,
                requests.Where(request => request.DepartmentId == department && request.Status == ManpowerRequestStatus.Approved).Sum(request => request.Positions),
                requests.Where(request => request.DepartmentId == department && request.Status == ManpowerRequestStatus.PendingApproval).Sum(request => request.Positions));
        }).OrderByDescending(row => row.Gap)];

        return new WorkforceDashboard(
            plan is null ? null : Item(plan, lines, owners),
            demand.Sum(row => row.Planned), demand.Sum(row => row.Live), demand.Sum(row => row.Gap), demand.Sum(row => row.OpenPositions),
            requests.Count(request => request.Status == ManpowerRequestStatus.PendingApproval),
            lines.Sum(line => line.SalaryBudget), lines.Sum(line => line.HiringBudget), demand,
            requests.GroupBy(request => request.Status.ToString()).ToDictionary(group => group.Key, group => group.Count()));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task SubmitAsync(ManpowerRequest request, CancellationToken cancellationToken)
    {
        request.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        PlanFit? fit = await FitAsync(request, cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = RequestSubjectType,
                SubjectId = request.Id,
                Title = $"{request.Number}: {request.Positions} × {request.Title}",
                RequesterEmployeeId = request.RequesterEmployeeId,
                Link = $"/workforce/requests/{request.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["positions"] = request.Positions.ToString(CultureInfo.InvariantCulture),
                    ["budget"] = (request.Budget ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["withinPlan"] = fit is null ? "false" : fit.Within ? "true" : "false",
                    ["reason"] = request.Reason.ToString(),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            request.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            request.Decide(approved: true, Now);
            PublishApproved(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void PublishApproved(ManpowerRequest request)
        => publisher.Publish(new ManpowerRequestApprovedIntegrationEvent(
            request.Id, request.Number, request.Title, request.DepartmentId, request.PositionId, request.JobId, request.LocationId, request.Positions,
            request.EmploymentTypeCode, request.WorkModeCode, request.MinExperienceYears, request.MaxExperienceYears, request.SalaryMin, request.SalaryMax,
            request.Skills, request.RequiredBy, request.RequesterEmployeeId, request.Priority.ToString(), request.Justification));

    /// <summary>The approved plan's room for this request: planned less today's headcount less what other approved requests already ask for.</summary>
    private async Task<PlanFit?> FitAsync(ManpowerRequest request, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        int year = (request.RequiredBy ?? today).Year;
        WorkforcePlan? plan = await dbContext.WorkforcePlans.AsNoTracking()
            .Where(row => row.Status == PlanStatus.Approved && row.Year == year)
            .OrderByDescending(row => row.DecidedDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (plan is null)
        {
            return null;
        }

        List<WorkforcePlanLine> lines = await dbContext.WorkforcePlanLines.AsNoTracking()
            .Where(line => line.PlanId == plan.Id && line.DepartmentId == request.DepartmentId && (line.JobId == null || line.JobId == request.JobId))
            .ToListAsync(cancellationToken);
        int planned = lines.Sum(line => line.PlannedHeadcount);
        int current = (await employees.CountByDepartmentAsync([request.DepartmentId], cancellationToken)).GetValueOrDefault(request.DepartmentId);
        int committed = await dbContext.ManpowerRequests.AsNoTracking()
            .Where(other => other.Id != request.Id && other.DepartmentId == request.DepartmentId && other.Status == ManpowerRequestStatus.Approved)
            .SumAsync(other => other.Positions, cancellationToken);
        int remaining = planned - current - committed;
        return new PlanFit(plan.Name, planned, current, committed, remaining, request.Positions <= remaining);
    }

    private async Task ValidateAsync(RequestDetails details, CancellationToken cancellationToken)
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
            throw new DomainRuleException("REQUEST_ORG", $"The {invalid[0].Kind.ToString().ToLowerInvariant()} chosen is not active.");
        }
    }

    private async Task<ManpowerRequest> VisibleRequestAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<ManpowerRequest> rows = tracked ? dbContext.ManpowerRequests : dbContext.ManpowerRequests.AsNoTracking();
        ManpowerRequest request = await rows.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ManpowerRequest), id);
        return request.RequesterEmployeeId == currentUser.EmployeeId || await employees.IsInScopeAsync(request.RequesterEmployeeId, Permissions.Workforce.RequestView, cancellationToken)
            ? request
            : throw new NotFoundException(nameof(ManpowerRequest), id);
    }

    private async Task<ManpowerRequest> EditableRequestAsync(Guid id, CancellationToken cancellationToken, bool allowApproved = false)
    {
        ManpowerRequest request = await VisibleRequestAsync(id, tracked: true, cancellationToken);
        bool mine = request.RequesterEmployeeId == currentUser.EmployeeId;
        if (!mine && !await employees.IsInScopeAsync(request.RequesterEmployeeId, Permissions.Workforce.RequestManage, cancellationToken))
        {
            throw new ForbiddenException("REQUEST_FORBIDDEN", "Only the requester or someone who manages requests can change this request.");
        }

        if (!allowApproved && request.Status == ManpowerRequestStatus.Approved)
        {
            throw new DomainRuleException("REQUEST_STATE", "An approved request has gone to recruitment and cannot change.");
        }

        return request;
    }

    private async Task<string> NextNumberAsync(CancellationToken cancellationToken)
    {
        int year = (await clock.TodayAsync(cancellationToken)).Year;
        string prefix = $"MPR-{year}-";
        List<string> numbers = await dbContext.ManpowerRequests.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Where(row => row.Number.StartsWith(prefix)).Select(row => row.Number).ToListAsync(cancellationToken);
        int next = numbers.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    private async Task<List<ManpowerRequest>> OpenRequestsAsync(IEnumerable<Guid> departments, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. departments.Distinct()];
        return await dbContext.ManpowerRequests.AsNoTracking()
            .Where(request => ids.Contains(request.DepartmentId) && (request.Status == ManpowerRequestStatus.Approved || request.Status == ManpowerRequestStatus.PendingApproval))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<RequestItem>> ItemsAsync(IReadOnlyList<ManpowerRequest> requests, bool withFit, CancellationToken cancellationToken)
    {
        Names names = await OrgNamesAsync(requests.Select(row => row.DepartmentId), requests.Select(row => row.LocationId), requests.Select(row => row.JobId), cancellationToken);
        Dictionary<Guid, string> people = await NamesAsync(requests.Select(row => (Guid?)row.RequesterEmployeeId).Concat(requests.Select(row => row.ReplacementForEmployeeId)), cancellationToken);
        bool canManage = await HasAsync(Permissions.Workforce.RequestManage, cancellationToken);
        var items = new List<RequestItem>();
        foreach (ManpowerRequest row in requests)
        {
            PlanFit? fit = withFit ? await FitAsync(row, cancellationToken) : null;
            items.Add(new RequestItem(
                row.Id, row.Number, row.Title, row.DepartmentId, names.Department(row.DepartmentId), row.PositionId, row.JobId, names.Job(row.JobId), row.LocationId,
                names.Location(row.LocationId), row.Positions, row.Skills, row.MinExperienceYears, row.MaxExperienceYears, row.EmploymentTypeCode, row.WorkModeCode,
                row.SalaryMin, row.SalaryMax, row.Budget, row.Priority, row.RequiredBy, row.Reason, row.ReplacementForEmployeeId,
                row.ReplacementForEmployeeId is { } replaced ? people.GetValueOrDefault(replaced) : null, row.Justification, row.RequesterEmployeeId,
                people.GetValueOrDefault(row.RequesterEmployeeId), row.Status, row.WorkflowInstanceId, row.CreatedDate, fit,
                row.Status is ManpowerRequestStatus.Draft or ManpowerRequestStatus.Rejected && (row.RequesterEmployeeId == currentUser.EmployeeId || canManage)));
        }

        return items;
    }

    private async Task<bool> HasAsync(string permission, CancellationToken cancellationToken) => (await permissions.GetCurrentAsync(cancellationToken)).Has(permission);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private async Task<Names> OrgNamesAsync(IEnumerable<Guid> departments, IEnumerable<Guid?> locations, IEnumerable<Guid?> jobs, CancellationToken cancellationToken)
        => new(
            await organization.GetNamesAsync(OrgUnitKind.Department, departments.Distinct(), cancellationToken),
            await organization.GetNamesAsync(OrgUnitKind.Location, locations.OfType<Guid>().Distinct(), cancellationToken),
            await organization.GetNamesAsync(OrgUnitKind.Job, jobs.OfType<Guid>().Distinct(), cancellationToken));

    private static PlanItem Item(WorkforcePlan plan, List<WorkforcePlanLine> lines, Dictionary<Guid, string> owners) => new(
        plan.Id, plan.Name, plan.Year, plan.Status, plan.EffectiveDate, plan.OwnerEmployeeId, plan.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null,
        plan.Notes, lines.Sum(line => line.CurrentHeadcount), lines.Sum(line => line.PlannedHeadcount), lines.Sum(line => line.SalaryBudget),
        lines.Sum(line => line.HiringBudget), lines.Count);

    private sealed record Names(IReadOnlyDictionary<Guid, string> Departments, IReadOnlyDictionary<Guid, string> Locations, IReadOnlyDictionary<Guid, string> Jobs)
    {
        public string? Department(Guid id) => Departments.GetValueOrDefault(id);

        public string? Location(Guid? id) => id is { } value ? Locations.GetValueOrDefault(value) : null;

        public string? Job(Guid? id) => id is { } value ? Jobs.GetValueOrDefault(value) : null;
    }
}
