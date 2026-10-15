using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record ChangeRequestItem(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Code,
    string Title,
    string Description,
    string? Reason,
    string? Impact,
    decimal? EstimatedHours,
    decimal? EstimatedCost,
    int? ScheduleImpactDays,
    WorkPriority Priority,
    Guid? RequestedByEmployeeId,
    string? RequestedByName,
    ChangeRequestStatus Status,
    Guid? WorkflowInstanceId,
    DateTime? DecidedDate,
    DateOnly? ClientApprovedOn,
    string? ClientApprovalReference,
    DateOnly? ImplementationDate,
    DateOnly? ClosedOn,
    DateTime CreatedDate);

public sealed record ChangeRequestBody(
    string Title,
    string Description,
    string? Reason,
    string? Impact,
    decimal? EstimatedHours,
    decimal? EstimatedCost,
    int? ScheduleImpactDays,
    WorkPriority Priority,
    Guid? RequestedByEmployeeId,
    string? RequestedByName);

public sealed record ClientApprovalBody(DateOnly On, string? Reference);

public sealed record AdvanceBody(ChangeRequestStatus Status);

public sealed record SubmitResult(ChangeRequestStatus Status, Guid? WorkflowInstanceId);

public sealed record RaidItemView(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Code,
    RaidKind Kind,
    string Title,
    string? Description,
    string? Category,
    int? Probability,
    int? Impact,
    int? Score,
    Severity? Severity,
    WorkPriority Priority,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    DateOnly? DueDate,
    string? Mitigation,
    string? ContingencyPlan,
    string? RootCause,
    string? Resolution,
    RaidStatus Status,
    DateOnly? ResolvedOn,
    bool IsOverdue);

public sealed record RaidBody(
    RaidKind Kind,
    string Title,
    string? Description,
    string? Category,
    int? Probability,
    int? Impact,
    Severity? Severity,
    WorkPriority Priority,
    Guid? OwnerEmployeeId,
    Guid? AssigneeEmployeeId,
    DateOnly? DueDate,
    string? Mitigation,
    string? ContingencyPlan,
    string? RootCause,
    string? Resolution,
    RaidStatus? Status);

public sealed record RaidQuery
{
    public Guid? ProjectId { get; init; }

    public RaidKind? Kind { get; init; }

    public RaidStatus? Status { get; init; }

    /// <summary>Only open entries (the default for portfolio views).</summary>
    public bool? OpenOnly { get; init; }
}

public sealed record MeetingItem(
    Guid Id,
    string Title,
    MeetingType Type,
    DateOnly Date,
    TimeOnly? StartTime,
    int? DurationMinutes,
    string? Location,
    IReadOnlyList<Guid> ParticipantEmployeeIds,
    IReadOnlyList<string> ParticipantNames,
    string? ExternalParticipants,
    string? Agenda,
    string? Minutes,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    MeetingStatus Status,
    IReadOnlyList<ActionItem> Actions);

public sealed record ActionItem(Guid Id, Guid MeetingId, string Description, Guid? OwnerEmployeeId, string? OwnerName, DateOnly? DueDate, ActionStatus Status, DateOnly? CompletedOn, bool IsOverdue);

public sealed record MeetingBody(
    string Title,
    MeetingType Type,
    DateOnly Date,
    TimeOnly? StartTime,
    int? DurationMinutes,
    string? Location,
    IReadOnlyList<Guid>? ParticipantEmployeeIds,
    string? ExternalParticipants,
    string? Agenda,
    string? Minutes,
    Guid? OwnerEmployeeId,
    MeetingStatus? Status);

public sealed record ActionBody(string Description, Guid? OwnerEmployeeId, DateOnly? DueDate, ActionStatus? Status);

/// <summary>Change requests, the RAID log and meetings (spec §50-54).</summary>
internal sealed class GovernanceService(
    AppDbContext dbContext,
    ProjectAccess access,
    ProjectService projects,
    IWorkflowStarter workflow,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string ChangeRequestSubjectType = "project.change-request";

    public static WorkflowSubject ChangeRequestSubject { get; } = new(
        ChangeRequestSubjectType,
        "Approve a project change request",
        "projects",
        [
            new WorkflowSubjectField("estimatedCost", "Estimated cost", WorkflowFieldType.Number),
            new WorkflowSubjectField("estimatedHours", "Estimated hours", WorkflowFieldType.Number),
            new WorkflowSubjectField("scheduleImpactDays", "Schedule impact (days)", WorkflowFieldType.Number),
            new WorkflowSubjectField("priority", "Priority", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Change requests ----------------------------------------------------------------------

    public async Task<IReadOnlyList<ChangeRequestItem>> ChangeRequestsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestView, cancellationToken);
        List<ChangeRequest> rows = await dbContext.ChangeRequests.AsNoTracking().Where(row => row.ProjectId == projectId).OrderByDescending(row => row.Number).ToListAsync(cancellationToken);
        return await ToChangeItemsAsync(rows, withProject: false, cancellationToken);
    }

    public async Task<IReadOnlyList<ChangeRequestItem>> PortfolioChangeRequestsAsync(ChangeRequestStatus? status, Guid? projectId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.ChangeRequestView, cancellationToken);
        IQueryable<ChangeRequest> rows = dbContext.ChangeRequests.AsNoTracking();
        if (visible is not null)
        {
            rows = rows.Where(row => visible.Contains(row.ProjectId));
        }

        if (projectId is { } id)
        {
            rows = rows.Where(row => row.ProjectId == id);
        }

        rows = status is { } wanted
            ? rows.Where(row => row.Status == wanted)
            : rows.Where(row => row.Status != ChangeRequestStatus.Closed && row.Status != ChangeRequestStatus.Withdrawn && row.Status != ChangeRequestStatus.Rejected);

        List<ChangeRequest> list = await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken);
        return await ToChangeItemsAsync(list, withProject: true, cancellationToken);
    }

    public async Task<ChangeRequestItem> SaveChangeRequestAsync(Guid projectId, Guid? changeRequestId, ChangeRequestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken);
        await projects.EnsureEmployeesAsync([body.RequestedByEmployeeId], cancellationToken);
        var details = new ChangeRequestDetails(
            body.Title, body.Description, body.Reason, body.Impact, body.EstimatedHours, body.EstimatedCost, body.ScheduleImpactDays, body.Priority,
            body.RequestedByEmployeeId ?? (body.RequestedByName is null ? access.Me : null), body.RequestedByName);

        ChangeRequest request;
        if (changeRequestId is { } id)
        {
            request = await FindChangeAsync(projectId, id, cancellationToken);
            request.Edit(details);
        }
        else
        {
            Domain.Project project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);
            request = ChangeRequest.Draft(projectId, project.NextChangeRequestNumber(), details);
            dbContext.ChangeRequests.Add(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToChangeItemsAsync([request], withProject: false, cancellationToken))[0];
    }

    /// <summary>Sends a draft for approval through the tenant's workflow, or approves it at once when there is none.</summary>
    public async Task<SubmitResult> SubmitChangeRequestAsync(Guid projectId, Guid changeRequestId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken);
        ChangeRequest request = await FindChangeAsync(projectId, changeRequestId, cancellationToken);
        Domain.Project project = await dbContext.Projects.AsNoTracking().FirstAsync(row => row.Id == projectId, cancellationToken);

        if (request.Status != ChangeRequestStatus.Draft)
        {
            throw new DomainRuleException("PROJECT_CR_STATE", $"{request.Code} is {request.Status}, not a draft.");
        }

        await workflow.WithdrawAsync(ChangeRequestSubjectType, request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = ChangeRequestSubjectType,
                SubjectId = request.Id,
                Title = $"{project.Code} {request.Code}: {request.Title}",
                RequesterEmployeeId = access.Me,
                Link = $"/projects/{projectId}?tab=changes",
                Fields = new Dictionary<string, string>
                {
                    ["estimatedCost"] = (request.EstimatedCost ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["estimatedHours"] = (request.EstimatedHours ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["scheduleImpactDays"] = (request.ScheduleImpactDays ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["priority"] = request.Priority.ToString(),
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            request.SubmitForApproval(started.InstanceId!.Value);
        }
        else
        {
            request.Approve(Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SubmitResult(request.Status, started.InstanceId);
    }

    public async Task<ChangeRequestItem> RecordClientApprovalAsync(Guid projectId, Guid changeRequestId, ClientApprovalBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken);
        ChangeRequest request = await FindChangeAsync(projectId, changeRequestId, cancellationToken);
        request.RecordClientApproval(body.On, body.Reference);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToChangeItemsAsync([request], withProject: false, cancellationToken))[0];
    }

    public async Task<ChangeRequestItem> AdvanceChangeRequestAsync(Guid projectId, Guid changeRequestId, ChangeRequestStatus to, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken);
        ChangeRequest request = await FindChangeAsync(projectId, changeRequestId, cancellationToken);
        request.Advance(to, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToChangeItemsAsync([request], withProject: false, cancellationToken))[0];
    }

    public async Task WithdrawChangeRequestAsync(Guid projectId, Guid changeRequestId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.ChangeRequestManage, cancellationToken);
        ChangeRequest request = await FindChangeAsync(projectId, changeRequestId, cancellationToken);
        bool pending = request.Status == ChangeRequestStatus.PendingApproval;
        request.Withdraw();
        if (pending)
        {
            await workflow.WithdrawAsync(ChangeRequestSubjectType, request.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Workflow's verdict, applied by the worker.</summary>
    public async Task ApplyDecisionAsync(Guid changeRequestId, bool? approved, CancellationToken cancellationToken)
    {
        ChangeRequest? request = await dbContext.ChangeRequests.FirstOrDefaultAsync(row => row.Id == changeRequestId, cancellationToken);
        if (request?.Status != ChangeRequestStatus.PendingApproval)
        {
            return;
        }

        switch (approved)
        {
            case true:
                request.Approve(Now);
                break;
            case false:
                request.Reject(Now);
                break;
            default:
                request.ReturnToDraft();
                break;
        }
    }

    // ----- RAID ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RaidItemView>> RaidAsync(Guid projectId, RaidKind? kind, RaidStatus? status, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.RaidView, cancellationToken);
        IQueryable<RaidItem> rows = dbContext.RaidItems.AsNoTracking().Where(item => item.ProjectId == projectId);
        if (kind is { } wantedKind)
        {
            rows = rows.Where(item => item.Kind == wantedKind);
        }

        if (status is { } wantedStatus)
        {
            rows = rows.Where(item => item.Status == wantedStatus);
        }

        List<RaidItem> items = await rows.OrderBy(item => item.Kind).ThenByDescending(item => item.Score).ThenBy(item => item.Number).ToListAsync(cancellationToken);
        return await ToRaidViewsAsync(items, withProject: false, cancellationToken);
    }

    public async Task<IReadOnlyList<RaidItemView>> PortfolioRaidAsync(RaidQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyCollection<Guid>? visible = await access.VisibleIdsAsync(Permissions.Project.RaidView, cancellationToken);
        IQueryable<RaidItem> rows = dbContext.RaidItems.AsNoTracking();
        if (visible is not null)
        {
            rows = rows.Where(item => visible.Contains(item.ProjectId));
        }

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(item => item.ProjectId == projectId);
        }

        if (query.Kind is { } kind)
        {
            rows = rows.Where(item => item.Kind == kind);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(item => item.Status == status);
        }
        else if (query.OpenOnly != false)
        {
            rows = rows.Where(item => item.Status == RaidStatus.Open || item.Status == RaidStatus.InProgress);
        }

        List<RaidItem> items = await rows.OrderByDescending(item => item.Score).ThenByDescending(item => item.Severity).ThenBy(item => item.DueDate).Take(500).ToListAsync(cancellationToken);
        return await ToRaidViewsAsync(items, withProject: true, cancellationToken);
    }

    public async Task<RaidItemView> SaveRaidAsync(Guid projectId, Guid? itemId, RaidBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.RaidManage, cancellationToken);
        await projects.EnsureEmployeesAsync([body.OwnerEmployeeId, body.AssigneeEmployeeId], cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);

        RaidItem item;
        if (itemId is { } id)
        {
            item = await dbContext.RaidItems.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken)
                ?? throw new NotFoundException(nameof(RaidItem), id);
            if (body.Kind != item.Kind)
            {
                throw new DomainRuleException("PROJECT_RAID_KIND_FIXED", "An entry stays in the register it was logged in.");
            }

            item.Update(ToDetails(body, body.Status ?? item.Status), today);
        }
        else
        {
            Domain.Project project = await dbContext.Projects.FirstAsync(row => row.Id == projectId, cancellationToken);
            item = RaidItem.Create(projectId, project.NextRaidNumber(), body.Kind, ToDetails(body, body.Status ?? RaidStatus.Open), today);
            dbContext.RaidItems.Add(item);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToRaidViewsAsync([item], withProject: false, cancellationToken))[0];
    }

    // ----- Meetings -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<MeetingItem>> MeetingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.View, cancellationToken);
        List<ProjectMeeting> meetings = await dbContext.ProjectMeetings.AsNoTracking().Where(row => row.ProjectId == projectId).OrderByDescending(row => row.Date).ThenByDescending(row => row.StartTime).ToListAsync(cancellationToken);
        return await ToMeetingItemsAsync(meetings, cancellationToken);
    }

    public async Task<MeetingItem> SaveMeetingAsync(Guid projectId, Guid? meetingId, MeetingBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        IReadOnlyList<Guid> participants = body.ParticipantEmployeeIds ?? [];
        await projects.EnsureEmployeesAsync([body.OwnerEmployeeId, .. participants.Select(id => (Guid?)id)], cancellationToken);
        var details = new MeetingDetails(
            body.Title, body.Type, body.Date, body.StartTime, body.DurationMinutes, body.Location, participants, body.ExternalParticipants,
            body.Agenda, body.Minutes, body.OwnerEmployeeId ?? access.Me, body.Status ?? MeetingStatus.Scheduled);

        ProjectMeeting meeting;
        if (meetingId is { } id)
        {
            meeting = await FindMeetingAsync(projectId, id, cancellationToken);
            meeting.Update(details);
        }
        else
        {
            meeting = ProjectMeeting.Schedule(projectId, details);
            dbContext.ProjectMeetings.Add(meeting);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToMeetingItemsAsync([meeting], cancellationToken))[0];
    }

    public async Task<MeetingItem> SaveActionAsync(Guid projectId, Guid meetingId, Guid? actionId, ActionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectMeeting meeting = await FindMeetingAsync(projectId, meetingId, cancellationToken, track: false);

        if (actionId is { } id)
        {
            MeetingAction action = await dbContext.MeetingActions.FirstOrDefaultAsync(row => row.Id == id && row.MeetingId == meetingId, cancellationToken)
                ?? throw new NotFoundException(nameof(MeetingAction), id);

            // Whoever owns an action may tick it off; anything more takes the right to run the project.
            bool owner = action.OwnerEmployeeId is { } ownerId && ownerId == access.Me;
            await access.EnsureAsync(projectId, owner ? Permissions.Project.View : Permissions.Project.Manage, cancellationToken);
            await projects.EnsureEmployeesAsync([body.OwnerEmployeeId], cancellationToken);
            action.Update(body.Description, body.OwnerEmployeeId, body.DueDate, body.Status ?? action.Status, await clock.TodayAsync(cancellationToken));
        }
        else
        {
            await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
            await projects.EnsureEmployeesAsync([body.OwnerEmployeeId], cancellationToken);
            dbContext.MeetingActions.Add(MeetingAction.Create(projectId, meetingId, body.Description, body.OwnerEmployeeId, body.DueDate));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToMeetingItemsAsync([meeting], cancellationToken))[0];
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static RaidDetails ToDetails(RaidBody body, RaidStatus status)
        => new(body.Title, body.Description, body.Category, body.Probability, body.Impact, body.Severity, body.Priority, body.OwnerEmployeeId,
            body.AssigneeEmployeeId, body.DueDate, body.Mitigation, body.ContingencyPlan, body.RootCause, body.Resolution, status);

    private async Task<ChangeRequest> FindChangeAsync(Guid projectId, Guid id, CancellationToken cancellationToken)
        => await dbContext.ChangeRequests.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ChangeRequest), id);

    private async Task<ProjectMeeting> FindMeetingAsync(Guid projectId, Guid id, CancellationToken cancellationToken, bool track = true)
    {
        IQueryable<ProjectMeeting> source = track ? dbContext.ProjectMeetings : dbContext.ProjectMeetings.AsNoTracking();
        return await source.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectMeeting), id);
    }

    private async Task<Dictionary<Guid, (string Code, string Name)>> ProjectNamesAsync(IEnumerable<Guid> ids, bool wanted, CancellationToken cancellationToken)
    {
        if (!wanted)
        {
            return [];
        }

        List<Guid> list = [.. ids.Distinct()];
        return await dbContext.Projects.AsNoTracking().Where(project => list.Contains(project.Id)).ToDictionaryAsync(project => project.Id, project => (project.Code, project.Name), cancellationToken);
    }

    private async Task<List<ChangeRequestItem>> ToChangeItemsAsync(IReadOnlyList<ChangeRequest> rows, bool withProject, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> people = await projects.NamesAsync(rows.Select(row => row.RequestedByEmployeeId), cancellationToken);
        Dictionary<Guid, (string Code, string Name)> names = await ProjectNamesAsync(rows.Select(row => row.ProjectId), withProject, cancellationToken);
        return [.. rows.Select(row =>
        {
            (string Code, string Name)? project = names.TryGetValue(row.ProjectId, out (string Code, string Name) found) ? found : null;
            return new ChangeRequestItem(
                row.Id, row.ProjectId, project?.Code, project?.Name, row.Code, row.Title, row.Description, row.Reason, row.Impact, row.EstimatedHours,
                row.EstimatedCost, row.ScheduleImpactDays, row.Priority, row.RequestedByEmployeeId,
                row.RequestedByName ?? (row.RequestedByEmployeeId is { } by ? people.GetValueOrDefault(by) : null), row.Status, row.WorkflowInstanceId,
                row.DecidedDate, row.ClientApprovedOn, row.ClientApprovalReference, row.ImplementationDate, row.ClosedOn, row.CreatedDate);
        })];
    }

    private async Task<List<RaidItemView>> ToRaidViewsAsync(IReadOnlyList<RaidItem> items, bool withProject, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, string> people = await projects.NamesAsync(items.SelectMany(item => new[] { item.OwnerEmployeeId, item.AssigneeEmployeeId }), cancellationToken);
        Dictionary<Guid, (string Code, string Name)> names = await ProjectNamesAsync(items.Select(item => item.ProjectId), withProject, cancellationToken);
        string? Name(Guid? id) => id is { } value ? people.GetValueOrDefault(value) : null;

        return [.. items.Select(item =>
        {
            (string Code, string Name)? project = names.TryGetValue(item.ProjectId, out (string Code, string Name) found) ? found : null;
            return new RaidItemView(
                item.Id, item.ProjectId, project?.Code, project?.Name, item.Code, item.Kind, item.Title, item.Description, item.Category,
                item.Probability, item.Impact, item.Score, item.Severity, item.Priority, item.OwnerEmployeeId, Name(item.OwnerEmployeeId),
                item.AssigneeEmployeeId, Name(item.AssigneeEmployeeId), item.DueDate, item.Mitigation, item.ContingencyPlan, item.RootCause,
                item.Resolution, item.Status, item.ResolvedOn, item.IsOverdue(today));
        })];
    }

    private async Task<List<MeetingItem>> ToMeetingItemsAsync(IReadOnlyList<ProjectMeeting> meetings, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Guid> ids = [.. meetings.Select(meeting => meeting.Id)];
        List<MeetingAction> actions = await dbContext.MeetingActions.AsNoTracking().Where(action => ids.Contains(action.MeetingId)).OrderBy(action => action.CreatedDate).ToListAsync(cancellationToken);
        Dictionary<Guid, string> people = await projects.NamesAsync(
            meetings.SelectMany(meeting => meeting.ParticipantEmployeeIds.Select(id => (Guid?)id).Append(meeting.OwnerEmployeeId))
                .Concat(actions.Select(action => action.OwnerEmployeeId)),
            cancellationToken);
        string? Name(Guid? id) => id is { } value ? people.GetValueOrDefault(value) : null;

        return [.. meetings.Select(meeting => new MeetingItem(
            meeting.Id, meeting.Title, meeting.Type, meeting.Date, meeting.StartTime, meeting.DurationMinutes, meeting.Location,
            meeting.ParticipantEmployeeIds, [.. meeting.ParticipantEmployeeIds.Select(id => people.GetValueOrDefault(id) ?? "?")],
            meeting.ExternalParticipants, meeting.Agenda, meeting.Minutes, meeting.OwnerEmployeeId, Name(meeting.OwnerEmployeeId), meeting.Status,
            [.. actions.Where(action => action.MeetingId == meeting.Id).Select(action => new ActionItem(
                action.Id, action.MeetingId, action.Description, action.OwnerEmployeeId, Name(action.OwnerEmployeeId), action.DueDate, action.Status,
                action.CompletedOn, action.IsOverdue(today)))]))];
    }
}
