using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Application;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;

namespace Gggs.Modules.Project.Endpoints;

/// <summary>
/// Projects under <c>/api/v1/projects</c>, with the cross-project registers at
/// <c>/api/v1/project-tasks</c>, <c>/api/v1/project-risks</c> and <c>/api/v1/project-issues</c>
/// (spec §128). Each endpoint names the broadest permission it needs; the services then narrow it
/// to the projects the caller is on.
/// </summary>
internal static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/projects").WithTags("Projects");
        MapProjects(group);
        MapTeamAndStructure(group);
        MapWork(group);
        MapPlanning(group);
        MapGovernance(group);
        MapSettings(group);

        endpoints.MapGet("/api/v1/project-tasks", async ([AsParameters] PortfolioTaskQuery query, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.PortfolioAsync(query, ct)))
            .WithTags("Projects").WithName("ListPortfolioTasks").RequirePermission(Permissions.Project.TaskView);
        endpoints.MapGet("/api/v1/project-risks", async (Guid? projectId, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.PortfolioRaidAsync(new RaidQuery { ProjectId = projectId, Kind = RaidKind.Risk }, ct)))
            .WithTags("Projects").WithName("ListPortfolioRisks").RequirePermission(Permissions.Project.RaidView);
        endpoints.MapGet("/api/v1/project-issues", async (Guid? projectId, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.PortfolioRaidAsync(new RaidQuery { ProjectId = projectId, Kind = RaidKind.Issue }, ct)))
            .WithTags("Projects").WithName("ListPortfolioIssues").RequirePermission(Permissions.Project.RaidView);

        endpoints.MapGet("/api/v1/me/projects", async (ProjectAccess access, IProjectDirectory directory, CancellationToken ct) =>
                TypedResults.Ok(await directory.MembershipsAsync(access.RequireEmployee(), ct)))
            .WithTags("Projects").WithName("ListMyProjects").RequirePermission(Permissions.Project.View);

        return endpoints;
    }

    private static void MapProjects(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] ProjectListQuery query, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.ListAsync(query, ct)))
            .WithName("ListProjects").RequirePermission(Permissions.Project.View);
        group.MapGet("/summary", async (ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SummaryAsync(ct)))
            .WithName("ProjectPortfolioSummary").RequirePermission(Permissions.Project.View);
        group.MapPost("/", async (ProjectRequest body, ProjectService projects, CancellationToken ct) =>
            {
                ProjectDetail created = await projects.CreateAsync(body, ct);
                return TypedResults.Created($"/api/v1/projects/{created.Project.Id}", created);
            })
            .WithName("CreateProject").RequirePermission(Permissions.Project.Create);
        group.MapGet("/{id:guid}", async (Guid id, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.GetAsync(id, ct)))
            .WithName("GetProject").RequirePermission(Permissions.Project.View);
        group.MapPut("/{id:guid}", async (Guid id, ProjectRequest body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.UpdateAsync(id, body, ct)))
            .WithName("UpdateProject").RequirePermission(Permissions.Project.Manage);
        group.MapPost("/{id:guid}/stage", async (Guid id, StageBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.MoveToStageAsync(id, body, ct)))
            .WithName("MoveProjectStage").RequirePermission(Permissions.Project.Manage);
        group.MapPost("/{id:guid}/status", async (Guid id, StatusBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SetStatusAsync(id, body, ct)))
            .WithName("SetProjectStatus").RequirePermission(Permissions.Project.Manage);
        group.MapPost("/{id:guid}/client-satisfaction", async (Guid id, SatisfactionBody body, ProjectService projects, CancellationToken ct) =>
            {
                await projects.RateClientSatisfactionAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("RateClientSatisfaction").RequirePermission(Permissions.Project.Manage);
        group.MapGet("/{id:guid}/history", async (Guid id, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.HistoryAsync(id, ct)))
            .WithName("ProjectHistory").RequirePermission(Permissions.Project.View);
        group.MapGet("/{id:guid}/dashboard", async (Guid id, InsightService insights, CancellationToken ct) => TypedResults.Ok(await insights.DashboardAsync(id, ct)))
            .WithName("ProjectDashboard").RequirePermission(Permissions.Project.View);
        group.MapGet("/{id:guid}/health", async (Guid id, InsightService insights, CancellationToken ct) => TypedResults.Ok(await insights.HealthAsync(id, ct)))
            .WithName("ProjectHealth").RequirePermission(Permissions.Project.View);
        group.MapPost("/{id:guid}/health/assess", async (Guid id, InsightService insights, CancellationToken ct) => TypedResults.Ok(await insights.AssessAsync(id, ct)))
            .WithName("AssessProjectHealth").RequirePermission(Permissions.Project.Manage);
        group.MapGet("/{id:guid}/calendar", async (Guid id, DateOnly from, DateOnly to, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.CalendarAsync(id, from, to, ct)))
            .WithName("ProjectCalendar").RequirePermission(Permissions.Project.View);

        // Employee 360's projects tab: anyone who may see the person may see where they have worked.
        group.MapGet("/by-employee/{employeeId:guid}", async (Guid employeeId, IEmployeeDirectory employees, IProjectDirectory directory, CancellationToken ct) =>
                await employees.IsInScopeAsync(employeeId, Permissions.Employee.View, ct)
                    ? Results.Ok(await directory.MembershipsAsync(employeeId, ct))
                    : Results.NotFound())
            .WithName("ProjectsByEmployee").RequirePermission(Permissions.Employee.View);
    }

    private static void MapTeamAndStructure(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/team", async (Guid id, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.TeamAsync(id, ct)))
            .WithName("ProjectTeam").RequirePermission(Permissions.Project.View);
        group.MapPost("/{id:guid}/team", async (Guid id, TeamMemberBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.AddMemberAsync(id, body, ct)))
            .WithName("AddProjectMember").RequirePermission(Permissions.Project.Manage);
        group.MapPut("/{id:guid}/team/{memberId:guid}", async (Guid id, Guid memberId, TeamMemberBody body, ProjectService projects, CancellationToken ct) =>
                TypedResults.Ok(await projects.UpdateMemberAsync(id, memberId, body, ct)))
            .WithName("UpdateProjectMember").RequirePermission(Permissions.Project.Manage);
        group.MapPost("/{id:guid}/team/{memberId:guid}/release", async (Guid id, Guid memberId, ReleaseBody body, ProjectService projects, CancellationToken ct) =>
            {
                await projects.ReleaseMemberAsync(id, memberId, body.On, ct);
                return TypedResults.NoContent();
            })
            .WithName("ReleaseProjectMember").RequirePermission(Permissions.Project.Manage);

        group.MapGet("/{id:guid}/phases", async (Guid id, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.PhasesAsync(id, ct)))
            .WithName("ProjectPhases").RequirePermission(Permissions.Project.View);
        group.MapPost("/{id:guid}/phases", async (Guid id, PhaseBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SavePhaseAsync(id, null, body, ct)))
            .WithName("AddProjectPhase").RequirePermission(Permissions.Project.Manage);
        group.MapPut("/{id:guid}/phases/{phaseId:guid}", async (Guid id, Guid phaseId, PhaseBody body, ProjectService projects, CancellationToken ct) =>
                TypedResults.Ok(await projects.SavePhaseAsync(id, phaseId, body, ct)))
            .WithName("UpdateProjectPhase").RequirePermission(Permissions.Project.Manage);
        group.MapDelete("/{id:guid}/phases/{phaseId:guid}", async (Guid id, Guid phaseId, ProjectService projects, CancellationToken ct) =>
            {
                await projects.DeletePhaseAsync(id, phaseId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteProjectPhase").RequirePermission(Permissions.Project.Manage);

        group.MapGet("/{id:guid}/board", async (Guid id, Guid? sprintId, Guid? assigneeEmployeeId, TaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.BoardAsync(id, sprintId, assigneeEmployeeId, ct)))
            .WithName("ProjectBoard").RequirePermission(Permissions.Project.TaskView);
        group.MapPut("/{id:guid}/board/columns", async (Guid id, IReadOnlyList<ColumnBody> body, ProjectService projects, CancellationToken ct) =>
                TypedResults.Ok(await projects.SaveColumnsAsync(id, body, ct)))
            .WithName("SaveBoardColumns").RequirePermission(Permissions.Project.Manage);
    }

    private static void MapWork(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/tasks", async (Guid id, [AsParameters] TaskListQuery query, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.ListAsync(id, query, ct)))
            .WithName("ListProjectTasks").RequirePermission(Permissions.Project.TaskView);
        group.MapPost("/{id:guid}/tasks", async (Guid id, TaskBody body, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.CreateAsync(id, body, ct)))
            .WithName("CreateProjectTask").RequirePermission(Permissions.Project.TaskManage);
        group.MapGet("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.GetAsync(id, taskId, ct)))
            .WithName("GetProjectTask").RequirePermission(Permissions.Project.TaskView);
        group.MapPut("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, TaskBody body, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.UpdateAsync(id, taskId, body, ct)))
            .WithName("UpdateProjectTask").RequirePermission(Permissions.Project.TaskManage);
        group.MapPost("/{id:guid}/tasks/{taskId:guid}/move", async (Guid id, Guid taskId, MoveBody body, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.MoveAsync(id, taskId, body, ct)))
            .WithName("MoveProjectTask").RequirePermission(Permissions.Project.TaskManage);
        group.MapDelete("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, TaskService tasks, CancellationToken ct) =>
            {
                await tasks.DeleteAsync(id, taskId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteProjectTask").RequirePermission(Permissions.Project.TaskManage);

        group.MapGet("/{id:guid}/dependencies", async (Guid id, Guid? taskId, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.DependenciesAsync(id, taskId, ct)))
            .WithName("ListTaskDependencies").RequirePermission(Permissions.Project.TaskView);
        group.MapPost("/{id:guid}/tasks/{taskId:guid}/dependencies", async (Guid id, Guid taskId, DependencyBody body, TaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.AddDependencyAsync(id, taskId, body, ct)))
            .WithName("AddTaskDependency").RequirePermission(Permissions.Project.TaskManage);
        group.MapDelete("/{id:guid}/dependencies/{dependencyId:guid}", async (Guid id, Guid dependencyId, TaskService tasks, CancellationToken ct) =>
            {
                await tasks.RemoveDependencyAsync(id, dependencyId, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveTaskDependency").RequirePermission(Permissions.Project.TaskManage);

        group.MapGet("/{id:guid}/requirements", async (Guid id, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.RequirementsAsync(id, ct)))
            .WithName("ListRequirements").RequirePermission(Permissions.Project.TaskView);
        group.MapPost("/{id:guid}/requirements", async (Guid id, RequirementBody body, TaskService tasks, CancellationToken ct) => TypedResults.Ok(await tasks.SaveRequirementAsync(id, null, body, ct)))
            .WithName("AddRequirement").RequirePermission(Permissions.Project.TaskManage);
        group.MapPut("/{id:guid}/requirements/{requirementId:guid}", async (Guid id, Guid requirementId, RequirementBody body, TaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.SaveRequirementAsync(id, requirementId, body, ct)))
            .WithName("UpdateRequirement").RequirePermission(Permissions.Project.TaskManage);
        group.MapDelete("/{id:guid}/requirements/{requirementId:guid}", async (Guid id, Guid requirementId, TaskService tasks, CancellationToken ct) =>
            {
                await tasks.DeleteRequirementAsync(id, requirementId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteRequirement").RequirePermission(Permissions.Project.TaskManage);
    }

    private static void MapPlanning(RouteGroupBuilder group)
    {
        group.MapGet("/milestones", async ([AsParameters] PortfolioMilestoneQuery query, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.PortfolioMilestonesAsync(query, ct)))
            .WithName("ListPortfolioMilestones").RequirePermission(Permissions.Project.MilestoneView);
        group.MapGet("/{id:guid}/milestones", async (Guid id, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.MilestonesAsync(id, ct)))
            .WithName("ListMilestones").RequirePermission(Permissions.Project.MilestoneView);
        group.MapPost("/{id:guid}/milestones", async (Guid id, MilestoneBody body, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.SaveMilestoneAsync(id, null, body, ct)))
            .WithName("AddMilestone").RequirePermission(Permissions.Project.MilestoneManage);
        group.MapPut("/{id:guid}/milestones/{milestoneId:guid}", async (Guid id, Guid milestoneId, MilestoneBody body, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.SaveMilestoneAsync(id, milestoneId, body, ct)))
            .WithName("UpdateMilestone").RequirePermission(Permissions.Project.MilestoneManage);
        group.MapPost("/{id:guid}/milestones/{milestoneId:guid}/achieve", async (Guid id, Guid milestoneId, AchieveBody body, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.AchieveMilestoneAsync(id, milestoneId, body.On, ct)))
            .WithName("AchieveMilestone").RequirePermission(Permissions.Project.MilestoneManage);
        group.MapDelete("/{id:guid}/milestones/{milestoneId:guid}", async (Guid id, Guid milestoneId, PlanningService planning, CancellationToken ct) =>
            {
                await planning.DeleteMilestoneAsync(id, milestoneId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteMilestone").RequirePermission(Permissions.Project.MilestoneManage);

        group.MapGet("/sprints", async (SprintStatus? status, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.PortfolioSprintsAsync(status, ct)))
            .WithName("ListPortfolioSprints").RequirePermission(Permissions.Project.SprintView);
        group.MapGet("/{id:guid}/sprints", async (Guid id, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.SprintsAsync(id, ct)))
            .WithName("ListSprints").RequirePermission(Permissions.Project.SprintView);
        group.MapGet("/{id:guid}/velocity", async (Guid id, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.VelocityAsync(id, ct)))
            .WithName("ProjectVelocity").RequirePermission(Permissions.Project.SprintView);
        group.MapPost("/{id:guid}/sprints", async (Guid id, SprintBody body, PlanningService planning, CancellationToken ct) => TypedResults.Ok(await planning.SaveSprintAsync(id, null, body, ct)))
            .WithName("PlanSprint").RequirePermission(Permissions.Project.SprintManage);
        group.MapPut("/{id:guid}/sprints/{sprintId:guid}", async (Guid id, Guid sprintId, SprintBody body, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.SaveSprintAsync(id, sprintId, body, ct)))
            .WithName("UpdateSprint").RequirePermission(Permissions.Project.SprintManage);
        group.MapPost("/{id:guid}/sprints/{sprintId:guid}/tasks", async (Guid id, Guid sprintId, SprintTasksBody body, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.PlanTasksAsync(id, sprintId, body.TaskIds, ct)))
            .WithName("PlanSprintTasks").RequirePermission(Permissions.Project.SprintManage);
        group.MapDelete("/{id:guid}/sprints/{sprintId:guid}/tasks/{taskId:guid}", async (Guid id, Guid sprintId, Guid taskId, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.UnplanTaskAsync(id, sprintId, taskId, ct)))
            .WithName("UnplanSprintTask").RequirePermission(Permissions.Project.SprintManage);
        group.MapPost("/{id:guid}/sprints/{sprintId:guid}/start", async (Guid id, Guid sprintId, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.StartSprintAsync(id, sprintId, ct)))
            .WithName("StartSprint").RequirePermission(Permissions.Project.SprintManage);
        group.MapPost("/{id:guid}/sprints/{sprintId:guid}/complete", async (Guid id, Guid sprintId, CompleteSprintBody body, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.CompleteSprintAsync(id, sprintId, body.MoveOpenTasksToSprintId, ct)))
            .WithName("CompleteSprint").RequirePermission(Permissions.Project.SprintManage);
        group.MapPost("/{id:guid}/sprints/{sprintId:guid}/cancel", async (Guid id, Guid sprintId, PlanningService planning, CancellationToken ct) =>
            {
                await planning.CancelSprintAsync(id, sprintId, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelSprint").RequirePermission(Permissions.Project.SprintManage);
        group.MapGet("/{id:guid}/sprints/{sprintId:guid}/burndown", async (Guid id, Guid sprintId, PlanningService planning, CancellationToken ct) =>
                TypedResults.Ok(await planning.BurndownAsync(id, sprintId, ct)))
            .WithName("SprintBurndown").RequirePermission(Permissions.Project.SprintView);
    }

    private static void MapGovernance(RouteGroupBuilder group)
    {
        group.MapGet("/change-requests", async (ChangeRequestStatus? status, Guid? projectId, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.PortfolioChangeRequestsAsync(status, projectId, ct)))
            .WithName("ListPortfolioChangeRequests").RequirePermission(Permissions.Project.ChangeRequestView);
        group.MapGet("/{id:guid}/change-requests", async (Guid id, GovernanceService governance, CancellationToken ct) => TypedResults.Ok(await governance.ChangeRequestsAsync(id, ct)))
            .WithName("ListChangeRequests").RequirePermission(Permissions.Project.ChangeRequestView);
        group.MapPost("/{id:guid}/change-requests", async (Guid id, ChangeRequestBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveChangeRequestAsync(id, null, body, ct)))
            .WithName("DraftChangeRequest").RequirePermission(Permissions.Project.ChangeRequestManage);
        group.MapPut("/{id:guid}/change-requests/{crId:guid}", async (Guid id, Guid crId, ChangeRequestBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveChangeRequestAsync(id, crId, body, ct)))
            .WithName("EditChangeRequest").RequirePermission(Permissions.Project.ChangeRequestManage);
        group.MapPost("/{id:guid}/change-requests/{crId:guid}/submit", async (Guid id, Guid crId, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SubmitChangeRequestAsync(id, crId, ct)))
            .WithName("SubmitChangeRequest").RequirePermission(Permissions.Project.ChangeRequestManage);
        group.MapPost("/{id:guid}/change-requests/{crId:guid}/client-approval", async (Guid id, Guid crId, ClientApprovalBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.RecordClientApprovalAsync(id, crId, body, ct)))
            .WithName("RecordClientApproval").RequirePermission(Permissions.Project.ChangeRequestManage);
        group.MapPost("/{id:guid}/change-requests/{crId:guid}/advance", async (Guid id, Guid crId, AdvanceBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.AdvanceChangeRequestAsync(id, crId, body.Status, ct)))
            .WithName("AdvanceChangeRequest").RequirePermission(Permissions.Project.ChangeRequestManage);
        group.MapPost("/{id:guid}/change-requests/{crId:guid}/withdraw", async (Guid id, Guid crId, GovernanceService governance, CancellationToken ct) =>
            {
                await governance.WithdrawChangeRequestAsync(id, crId, ct);
                return TypedResults.NoContent();
            })
            .WithName("WithdrawChangeRequest").RequirePermission(Permissions.Project.ChangeRequestManage);

        group.MapGet("/raid", async ([AsParameters] RaidQuery query, GovernanceService governance, CancellationToken ct) => TypedResults.Ok(await governance.PortfolioRaidAsync(query, ct)))
            .WithName("ListPortfolioRaid").RequirePermission(Permissions.Project.RaidView);
        group.MapGet("/{id:guid}/raid", async (Guid id, RaidKind? kind, RaidStatus? status, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.RaidAsync(id, kind, status, ct)))
            .WithName("ListRaid").RequirePermission(Permissions.Project.RaidView);
        group.MapPost("/{id:guid}/raid", async (Guid id, RaidBody body, GovernanceService governance, CancellationToken ct) => TypedResults.Ok(await governance.SaveRaidAsync(id, null, body, ct)))
            .WithName("LogRaidItem").RequirePermission(Permissions.Project.RaidManage);
        group.MapPut("/{id:guid}/raid/{itemId:guid}", async (Guid id, Guid itemId, RaidBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveRaidAsync(id, itemId, body, ct)))
            .WithName("UpdateRaidItem").RequirePermission(Permissions.Project.RaidManage);

        group.MapGet("/{id:guid}/meetings", async (Guid id, GovernanceService governance, CancellationToken ct) => TypedResults.Ok(await governance.MeetingsAsync(id, ct)))
            .WithName("ListMeetings").RequirePermission(Permissions.Project.View);
        group.MapPost("/{id:guid}/meetings", async (Guid id, MeetingBody body, GovernanceService governance, CancellationToken ct) => TypedResults.Ok(await governance.SaveMeetingAsync(id, null, body, ct)))
            .WithName("ScheduleMeeting").RequirePermission(Permissions.Project.Manage);
        group.MapPut("/{id:guid}/meetings/{meetingId:guid}", async (Guid id, Guid meetingId, MeetingBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveMeetingAsync(id, meetingId, body, ct)))
            .WithName("UpdateMeeting").RequirePermission(Permissions.Project.Manage);
        group.MapPost("/{id:guid}/meetings/{meetingId:guid}/actions", async (Guid id, Guid meetingId, ActionBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveActionAsync(id, meetingId, null, body, ct)))
            .WithName("AddMeetingAction").RequirePermission(Permissions.Project.Manage);
        group.MapPut("/{id:guid}/meetings/{meetingId:guid}/actions/{actionId:guid}", async (Guid id, Guid meetingId, Guid actionId, ActionBody body, GovernanceService governance, CancellationToken ct) =>
                TypedResults.Ok(await governance.SaveActionAsync(id, meetingId, actionId, body, ct)))
            .WithName("UpdateMeetingAction").RequirePermission(Permissions.Project.View);
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/roles", async (bool? includeInactive, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.RolesAsync(includeInactive == true, ct)))
            .WithName("ListProjectRoles").RequirePermission(Permissions.Project.View);
        group.MapPost("/roles", async (RoleBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SaveRoleAsync(null, body, ct)))
            .WithName("CreateProjectRole").RequirePermission(Permissions.Project.SettingsManage);
        group.MapPut("/roles/{roleId:guid}", async (Guid roleId, RoleBody body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SaveRoleAsync(roleId, body, ct)))
            .WithName("UpdateProjectRole").RequirePermission(Permissions.Project.SettingsManage);
        group.MapPost("/roles/{roleId:guid}/activate", async (Guid roleId, ProjectService projects, CancellationToken ct) =>
            {
                await projects.SetRoleActiveAsync(roleId, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateProjectRole").RequirePermission(Permissions.Project.SettingsManage);
        group.MapPost("/roles/{roleId:guid}/deactivate", async (Guid roleId, ProjectService projects, CancellationToken ct) =>
            {
                await projects.SetRoleActiveAsync(roleId, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateProjectRole").RequirePermission(Permissions.Project.SettingsManage);
        group.MapGet("/settings/health", async (ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.ThresholdsAsync(ct)))
            .WithName("GetProjectHealthThresholds").RequirePermission(Permissions.Project.View);
        group.MapPut("/settings/health", async (ProjectHealthThresholds body, ProjectService projects, CancellationToken ct) => TypedResults.Ok(await projects.SaveThresholdsAsync(body, ct)))
            .WithName("SaveProjectHealthThresholds").RequirePermission(Permissions.Project.SettingsManage);
    }
}
