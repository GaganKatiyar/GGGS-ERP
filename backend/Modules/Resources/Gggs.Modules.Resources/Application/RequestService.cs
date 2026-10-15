using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Resources.Domain;
using Gggs.Modules.Resources.Infrastructure;
using Gggs.Modules.Skills.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Resources.Application;

public sealed record RequirementItem(Guid SkillId, string? SkillName, int MinLevel);

public sealed record RequestItem(
    Guid Id,
    Guid ProjectId,
    string? ProjectCode,
    string? ProjectName,
    Guid? ProjectRoleId,
    string Title,
    IReadOnlyList<RequirementItem> Skills,
    decimal? MinYearsExperience,
    int Quantity,
    int FulfilledCount,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Billable,
    Guid? LocationId,
    string? WorkModeCode,
    decimal? MaxCostRate,
    RequestPriority Priority,
    Guid? RequestedByEmployeeId,
    string? RequestedByName,
    RequestStatus Status,
    string? Notes,
    DateTime CreatedDate,
    bool CanManage);

public sealed record RequestBody(
    Guid ProjectId,
    Guid? ProjectRoleId,
    string Title,
    IReadOnlyList<SkillRequirement>? Skills,
    decimal? MinYearsExperience,
    int Quantity,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Billable,
    Guid? LocationId,
    string? WorkModeCode,
    decimal? MaxCostRate,
    RequestPriority Priority,
    string? Notes);

public sealed record MatchItem(
    Guid EmployeeId,
    string EmployeeName,
    string? Designation,
    IReadOnlyList<SkillTag> Skills,
    int PeakAllocation,
    decimal? CostRate,
    DateOnly? ExitDate,
    IReadOnlyList<string> Certifications,
    MatchScore Score,
    CandidateStatus? CandidateStatus);

public sealed record CandidateItem(Guid Id, Guid EmployeeId, string? EmployeeName, decimal Score, MatchScore? Breakdown, CandidateStatus Status, Guid? AllocationMemberId, string? Note);

public sealed record RequestDetail(RequestItem Request, IReadOnlyList<CandidateItem> Candidates);

public sealed record ShortlistBody(Guid EmployeeId, string? Note);

public sealed record RejectCandidateBody(string? Note);

public sealed record AllocateCandidateBody(DateOnly? StartDate, DateOnly? EndDate, int? AllocationPercent);

/// <summary>Resource requests and matching (spec §61-62): what a project needs, who fits, and filling the seat.</summary>
internal sealed class RequestService(
    AppDbContext dbContext,
    ResourceService resources,
    IEmployeeDirectory employees,
    IProjectDirectory projects,
    IProjectStaffing staffing,
    IProjectPermissions projectPermissions,
    ISkillDirectory skills,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    public async Task<IReadOnlyList<RequestItem>> ListAsync(RequestStatus? status, Guid? projectId, CancellationToken cancellationToken)
    {
        IQueryable<ResourceRequest> rows = dbContext.ResourceRequests.AsNoTracking();
        if (!await IsTenantWideAsync(Permissions.Resource.RequestView, cancellationToken))
        {
            IReadOnlyCollection<Guid> visible = await projectPermissions.VisibleIdsAsync(Permissions.Project.Manage, cancellationToken) ?? [];
            rows = rows.Where(request => visible.Contains(request.ProjectId));
        }

        rows = status is { } wanted
            ? rows.Where(request => request.Status == wanted)
            : rows.Where(request => request.Status == RequestStatus.Open || request.Status == RequestStatus.PartiallyFulfilled);
        if (projectId is { } id)
        {
            rows = rows.Where(request => request.ProjectId == id);
        }

        List<ResourceRequest> list = await rows.OrderByDescending(request => request.Priority).ThenBy(request => request.StartDate).Take(500).ToListAsync(cancellationToken);
        return await ToItemsAsync(list, cancellationToken);
    }

    public async Task<RequestDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ResourceRequest request = await FindVisibleAsync(id, cancellationToken);
        List<ResourceCandidate> candidates = await dbContext.ResourceCandidates.AsNoTracking().Where(row => row.RequestId == id).OrderByDescending(row => row.Score).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(candidates.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        return new RequestDetail(
            (await ToItemsAsync([request], cancellationToken))[0],
            [.. candidates.Select(row => new CandidateItem(row.Id, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.Score, row.Breakdown, row.Status, row.AllocationMemberId, row.Note))]);
    }

    public async Task<RequestDetail> SaveAsync(Guid? id, RequestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureCanManageAsync(body.ProjectId, cancellationToken);

        ProjectSummary project = (await projects.GetManyAsync([body.ProjectId], cancellationToken)).GetValueOrDefault(body.ProjectId)
            ?? throw new DomainRuleException("RESOURCE_REQUEST_PROJECT", "The project does not exist.");
        if (project.Status is "Completed" or "Cancelled")
        {
            throw new DomainRuleException("RESOURCE_REQUEST_PROJECT", $"{project.Code} is {project.Status.ToLowerInvariant()} and needs no more people.");
        }

        IReadOnlyList<SkillRequirement> requirements = body.Skills ?? [];
        IReadOnlyDictionary<Guid, SkillInfo> known = await skills.GetSkillsAsync(requirements.Select(skill => skill.SkillId), cancellationToken);
        if (known.Count != requirements.Select(skill => skill.SkillId).Distinct().Count())
        {
            throw new DomainRuleException("RESOURCE_REQUEST_SKILLS", "A required skill is not in the catalog.");
        }

        var details = new RequestDetails(
            body.ProjectRoleId, body.Title, requirements, body.MinYearsExperience, body.Quantity, body.AllocationPercent, body.StartDate, body.EndDate,
            body.Billable, body.LocationId, body.WorkModeCode, body.MaxCostRate, body.Priority, body.Notes);

        ResourceRequest request;
        if (id is { } requestId)
        {
            request = await dbContext.ResourceRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken) ?? throw new NotFoundException(nameof(ResourceRequest), requestId);
            if (request.ProjectId != body.ProjectId)
            {
                throw new DomainRuleException("RESOURCE_REQUEST_PROJECT", "A request stays with the project it was raised for.");
            }

            request.Update(details);
        }
        else
        {
            request = ResourceRequest.Raise(body.ProjectId, currentUser.EmployeeId, details);
            dbContext.ResourceRequests.Add(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(request.Id, cancellationToken);
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        ResourceRequest request = await dbContext.ResourceRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ResourceRequest), id);
        await EnsureCanManageAsync(request.ProjectId, cancellationToken);
        request.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Everyone in reach scored against the request (spec §62), best first.</summary>
    public async Task<IReadOnlyList<MatchItem>> MatchAsync(Guid id, int take, CancellationToken cancellationToken)
    {
        ResourceRequest request = await FindVisibleAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EmployeeSummary> people = await resources.PeopleAsync(null, null, null, cancellationToken);
        List<Guid> ids = [.. people.Select(person => person.Id)];

        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync(ids, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> certifications = await skills.CertificationsAsync(ids, cancellationToken);
        ILookup<Guid, ProjectAllocation> allocations = (await projects.AllocationsAsync(ids, request.StartDate, request.EndDate, cancellationToken)).Where(row => row.IsActive).ToLookup(row => row.EmployeeId);
        Dictionary<Guid, ResourceProfile> profiles = await resources.ProfilesAsync(ids, cancellationToken);
        Dictionary<Guid, CandidateStatus> shortlisted = await dbContext.ResourceCandidates.AsNoTracking()
            .Where(row => row.RequestId == id)
            .ToDictionaryAsync(row => row.EmployeeId, row => row.Status, cancellationToken);
        IReadOnlyDictionary<Guid, SkillInfo> skillInfo = await skills.GetSkillsAsync(request.Skills.Select(skill => skill.SkillId), cancellationToken);
        Dictionary<Guid, string> skillNames = skillInfo.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        ILookup<Guid, EmployeeSkillLevel> byPerson = levels.ToLookup(level => level.EmployeeId);

        return [.. people
            .Select(person =>
            {
                List<EmployeeSkillLevel> mine = [.. byPerson[person.Id]];
                ResourceProfile? profile = profiles.GetValueOrDefault(person.Id);
                int peak = Capacity.PeakAllocation(request.StartDate, request.EndDate, allocations[person.Id].Select(ResourceService.Span));
                var facts = new CandidateFacts(
                    person.Id,
                    mine.ToDictionary(level => level.SkillId, level => level.Level),
                    mine.ToDictionary(level => level.SkillId, level => level.YearsExperience ?? 0),
                    ResourceService.YearsSince(person.JoiningDate, today),
                    peak,
                    person.LocationId,
                    person.WorkModeCode,
                    profile?.CostRate,
                    profile?.IsBillable ?? true,
                    person.ExitDate);
                MatchScore score = Matching.Score(request, facts, skillNames);
                return new MatchItem(
                    person.Id, person.DisplayName, person.Designation, ResourceService.Tags(mine, 4), peak, profile?.CostRate, person.ExitDate,
                    certifications.GetValueOrDefault(person.Id) ?? [], score, shortlisted.TryGetValue(person.Id, out CandidateStatus status) ? status : null);
            })
            .OrderByDescending(match => match.Score.Overall)
            .Take(Math.Clamp(take, 1, 100))];
    }

    public async Task<RequestDetail> ShortlistAsync(Guid id, ShortlistBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ResourceRequest request = await FindVisibleAsync(id, cancellationToken);
        await EnsureCanManageAsync(request.ProjectId, cancellationToken);
        if (await dbContext.ResourceCandidates.AnyAsync(row => row.RequestId == id && row.EmployeeId == body.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("RESOURCE_CANDIDATE_DUPLICATE", "This person is already on the shortlist.");
        }

        MatchItem match = (await MatchAsync(id, 100, cancellationToken)).FirstOrDefault(row => row.EmployeeId == body.EmployeeId)
            ?? throw new DomainRuleException("RESOURCE_CANDIDATE_UNKNOWN", "This person cannot be matched against the request.");
        dbContext.ResourceCandidates.Add(ResourceCandidate.Shortlist(id, body.EmployeeId, match.Score, body.Note));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<RequestDetail> RejectCandidateAsync(Guid id, Guid candidateId, string? note, CancellationToken cancellationToken)
    {
        ResourceRequest request = await FindVisibleAsync(id, cancellationToken);
        await EnsureCanManageAsync(request.ProjectId, cancellationToken);
        ResourceCandidate candidate = await dbContext.ResourceCandidates.FirstOrDefaultAsync(row => row.Id == candidateId && row.RequestId == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceCandidate), candidateId);
        candidate.Reject(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Puts a shortlisted candidate on the project at the request's terms (or adjusted ones), and counts the seat filled.</summary>
    public async Task<RequestDetail> AllocateCandidateAsync(Guid id, Guid candidateId, AllocateCandidateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ResourceRequest request = await dbContext.ResourceRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ResourceRequest), id);
        await EnsureCanAllocateAsync(request.ProjectId, cancellationToken);
        ResourceCandidate candidate = await dbContext.ResourceCandidates.FirstOrDefaultAsync(row => row.Id == candidateId && row.RequestId == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceCandidate), candidateId);

        ResourceProfile? profile = await dbContext.ResourceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == candidate.EmployeeId, cancellationToken);
        Guid memberId = await staffing.AllocateAsync(
            new StaffingRequest(
                request.ProjectId, candidate.EmployeeId, request.ProjectRoleId, body.StartDate ?? request.StartDate, body.EndDate ?? request.EndDate,
                body.AllocationPercent ?? request.AllocationPercent, request.Billable, profile?.BillingRate, profile?.CostRate, $"Filled request: {request.Title}"),
            cancellationToken);
        candidate.Allocate(memberId);
        request.Fulfil();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<bool> IsTenantWideAsync(string permission, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).ScopeFor(permission) == DataScope.Tenant;

    private async Task<bool> CanManageAsync(Guid projectId, CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);
        DataScope? scope = held.ScopeFor(Permissions.Resource.RequestManage);
        return scope == DataScope.Tenant || (scope is not null && await projectPermissions.CanAsync(projectId, Permissions.Project.Manage, cancellationToken));
    }

    /// <summary>Raising and shortlisting: resource managers anywhere, project managers on their own projects.</summary>
    private async Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(projectId, cancellationToken))
        {
            throw new ForbiddenException("RESOURCE_REQUEST_FORBIDDEN", "You can only raise and work on requests for projects you run.");
        }
    }

    /// <summary>Filling a seat: whoever may allocate people, or the manager of the project asking.</summary>
    private async Task EnsureCanAllocateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);
        if (held.ScopeFor(Permissions.Resource.AllocationManage) == DataScope.Tenant || await projectPermissions.CanAsync(projectId, Permissions.Project.Manage, cancellationToken))
        {
            return;
        }

        throw new ForbiddenException("RESOURCE_ALLOCATION_FORBIDDEN", "Allocating people takes resource management rights or running the project.");
    }

    private async Task<ResourceRequest> FindVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        ResourceRequest request = await dbContext.ResourceRequests.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceRequest), id);
        if (!await IsTenantWideAsync(Permissions.Resource.RequestView, cancellationToken) && !await projectPermissions.CanAsync(request.ProjectId, Permissions.Project.Manage, cancellationToken))
        {
            throw new NotFoundException(nameof(ResourceRequest), id);
        }

        return request;
    }

    private async Task<List<RequestItem>> ToItemsAsync(IReadOnlyList<ResourceRequest> requests, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, ProjectSummary> projectNames = await projects.GetManyAsync(requests.Select(request => request.ProjectId), cancellationToken);
        IReadOnlyDictionary<Guid, SkillInfo> skillNames = await skills.GetSkillsAsync(requests.SelectMany(request => request.Skills.Select(skill => skill.SkillId)), cancellationToken);
        Dictionary<Guid, string> people = (await employees.GetManyAsync(requests.Select(request => request.RequestedByEmployeeId).OfType<Guid>(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        List<RequestItem> items = [];
        foreach (ResourceRequest request in requests)
        {
            ProjectSummary? project = projectNames.GetValueOrDefault(request.ProjectId);
            items.Add(new RequestItem(
                request.Id, request.ProjectId, project?.Code, project?.Name, request.ProjectRoleId, request.Title,
                [.. request.Skills.Select(skill => new RequirementItem(skill.SkillId, skillNames.GetValueOrDefault(skill.SkillId)?.Name, skill.MinLevel))],
                request.MinYearsExperience, request.Quantity, request.FulfilledCount, request.AllocationPercent, request.StartDate, request.EndDate, request.Billable,
                request.LocationId, request.WorkModeCode, request.MaxCostRate, request.Priority, request.RequestedByEmployeeId,
                request.RequestedByEmployeeId is { } by ? people.GetValueOrDefault(by) : null, request.Status, request.Notes, request.CreatedDate,
                request.IsOpen && await CanManageAsync(request.ProjectId, cancellationToken)));
        }

        return items;
    }
}
