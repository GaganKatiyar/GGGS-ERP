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
}

/// <summary>Someone joined a project, their place on it changed, or they were released from it.</summary>
[MessageType("gggs.project.team-changed")]
public sealed record ProjectTeamChangedIntegrationEvent(Guid ProjectId, Guid MemberId, Guid EmployeeId, string Change) : IntegrationEvent;
