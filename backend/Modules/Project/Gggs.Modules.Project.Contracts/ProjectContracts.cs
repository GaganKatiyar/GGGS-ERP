using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Project.Contracts;

/// <summary>A project as other modules see it.</summary>
public sealed record ProjectSummary(
    Guid Id,
    string Code,
    string Name,
    Guid? ClientId,
    string Status,
    string Stage,
    Guid? ProjectManagerEmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>One person's place on one project: the role, the share of their time and the dates.</summary>
public sealed record ProjectMembership(
    Guid MemberId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string ProjectStatus,
    Guid EmployeeId,
    string RoleName,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool Billable,
    bool IsActive);

/// <summary>Project lookups for resources, timesheets and Employee 360 (ADR-12).</summary>
public interface IProjectDirectory
{
    Task<IReadOnlyDictionary<Guid, ProjectSummary>> GetManyAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Every project the employee has been on, current first.</summary>
    Task<IReadOnlyList<ProjectMembership>> MembershipsAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocations overlapping the window, active or released; <paramref name="employeeIds"/> null means
    /// everyone. The project team is the allocation of record (spec §60): one row per person per role.
    /// </summary>
    Task<IReadOnlyList<ProjectAllocation>> AllocationsAsync(IEnumerable<Guid>? employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);

    /// <summary>Whether the employee may book time to the project on that day: on the team then, or one of its managers.</summary>
    Task<bool> IsOnTeamAsync(Guid projectId, Guid employeeId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>The projects (and their open tasks for this person) an employee can book time to in a window.</summary>
    Task<IReadOnlyList<TimesheetTarget>> TimesheetTargetsAsync(Guid employeeId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);

    /// <summary>The project each task belongs to.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> TaskProjectsAsync(IEnumerable<Guid> taskIds, CancellationToken cancellationToken = default);
}

public sealed record ProjectAllocation(
    Guid MemberId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string ProjectStatus,
    Guid EmployeeId,
    Guid ProjectRoleId,
    string RoleName,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    bool IsActive);

public sealed record TimesheetTask(Guid TaskId, string Code, string Title);

public sealed record TimesheetTarget(Guid ProjectId, string ProjectCode, string ProjectName, bool Billable, IReadOnlyList<TimesheetTask> Tasks);

/// <summary>Putting someone on a project, as resource management does when it fills a request.</summary>
public sealed record StaffingRequest(
    Guid ProjectId,
    Guid EmployeeId,
    Guid? ProjectRoleId,
    DateOnly StartDate,
    DateOnly? EndDate,
    int AllocationPercent,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    string? Notes);

/// <summary>
/// Allocation commands for resource management (spec §60, §68-69). The caller has already decided the
/// change is allowed; these write into the caller's unit of work and save nothing themselves.
/// </summary>
public interface IProjectStaffing
{
    Task<Guid> AllocateAsync(StaffingRequest request, CancellationToken cancellationToken = default);

    Task UpdateAllocationAsync(Guid memberId, int allocationPercent, DateOnly startDate, DateOnly? endDate, bool billable, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid memberId, DateOnly lastDay, CancellationToken cancellationToken = default);
}

/// <summary>Project access as the Project module decides it, for modules acting on project data (timesheets, requests).</summary>
public interface IProjectPermissions
{
    Task<bool> CanAsync(Guid projectId, string permission, CancellationToken cancellationToken = default);

    /// <summary>Projects visible under <paramref name="permission"/>; null for all of them.</summary>
    Task<IReadOnlyCollection<Guid>?> VisibleIdsAsync(string permission, CancellationToken cancellationToken = default);
}

/// <summary>Someone joined a project, their place on it changed, or they were released from it.</summary>
[MessageType("gggs.project.team-changed")]
public sealed record ProjectTeamChangedIntegrationEvent(Guid ProjectId, Guid MemberId, Guid EmployeeId, string Change) : IntegrationEvent;
