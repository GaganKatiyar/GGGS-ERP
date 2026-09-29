using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Workflow.Api.Contracts;
using Gggs.Workflow.Application.Workflows;
using Gggs.Workflow.Domain.Workflows;
using Gggs.Workflow.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Workflow.Api.Endpoints;

/// <summary>
/// Workflow definitions/versions (admin), running instances (read + cancel), the approvals inbox,
/// and delegations - architecture.md §10.2's <c>/api/v1/workflows/*</c> catalog, minus
/// <c>/internal/workflows/start</c>: starting an instance is always the async
/// <c>StartWorkflowIntegrationEvent</c> command (§13.3 step 1), never a synchronous call, so there
/// is no HTTP route for it.
/// </summary>
public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapDefinitionEndpoints(endpoints);
        MapInstanceEndpoints(endpoints);
        MapTaskEndpoints(endpoints);
        MapDelegationEndpoints(endpoints);

        return endpoints;
    }

    private static void MapDefinitionEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workflows/definitions").WithTags("Workflow");

        group.MapGet("/", async (WorkflowDbContext database, CancellationToken cancellationToken) =>
        {
            List<WorkflowDefinitionResponse> items = await database.Definitions.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new WorkflowDefinitionResponse(x.Id, x.Code, x.Name, x.EntityType, x.IsSystem))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListWorkflowDefinitions").RequirePermission(Permissions.Workflow.DefinitionView);

        group.MapGet("/{id:guid}", async (Guid id, WorkflowDbContext database, CancellationToken cancellationToken) =>
        {
            WorkflowDefinition? entity = await database.Definitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionView);

        group.MapPost("/", async (CreateWorkflowDefinitionBody body, WorkflowDefinitionService service, CancellationToken cancellationToken) =>
        {
            WorkflowDefinition entity = await service.CreateAsync(body.Code, body.Name, body.EntityType, cancellationToken);
            return TypedResults.Created($"/api/v1/workflows/definitions/{entity.Id}", ToResponse(entity));
        }).WithName("CreateWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionManage);

        group.MapGet("/{id:guid}/versions", async (Guid id, WorkflowDbContext database, CancellationToken cancellationToken) =>
        {
            List<WorkflowDefinitionVersionResponse> items = await database.DefinitionVersions.AsNoTracking()
                .Where(x => x.DefinitionId == id)
                .OrderByDescending(x => x.Version)
                .Select(x => ToVersionResponse(x))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListWorkflowDefinitionVersions").RequirePermission(Permissions.Workflow.DefinitionView);

        group.MapPost("/{id:guid}/versions", async (
            Guid id, CreateWorkflowDefinitionVersionBody body, WorkflowDefinitionService service, CancellationToken cancellationToken) =>
        {
            WorkflowDefinitionVersion version = await service.CreateDraftVersionAsync(id, body.DefinitionJson, cancellationToken);
            return TypedResults.Created(
                $"/api/v1/workflows/definitions/{id}/versions/{version.Id}", ToVersionResponse(version));
        }).WithName("CreateWorkflowDefinitionVersion").RequirePermission(Permissions.Workflow.DefinitionManage);

        group.MapPut("/versions/{versionId:guid}", async (
            Guid versionId, UpdateWorkflowDefinitionVersionBody body, WorkflowDefinitionService service, CancellationToken cancellationToken) =>
        {
            WorkflowDefinitionVersion version = await service.UpdateDraftVersionAsync(versionId, body.DefinitionJson, cancellationToken);
            return TypedResults.Ok(ToVersionResponse(version));
        }).WithName("UpdateWorkflowDefinitionVersion").RequirePermission(Permissions.Workflow.DefinitionManage);

        group.MapPost("/versions/{versionId:guid}/publish", async (
            Guid versionId, WorkflowDefinitionService service, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            WorkflowDefinitionVersion version = await service.PublishAsync(versionId, clock.GetUtcNow().UtcDateTime, cancellationToken);
            return TypedResults.Ok(ToVersionResponse(version));
        }).WithName("PublishWorkflowDefinitionVersion").RequirePermission(Permissions.Workflow.DefinitionManage);
    }

    private static void MapInstanceEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workflows/instances").WithTags("Workflow");

        group.MapGet("/{id:guid}", async (Guid id, WorkflowDbContext database, CancellationToken cancellationToken) =>
        {
            WorkflowInstance? entity = await database.Instances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetWorkflowInstance").RequirePermission(Permissions.Workflow.TaskView);

        group.MapGet("/{id:guid}/timeline", async (Guid id, WorkflowDbContext database, CancellationToken cancellationToken) =>
        {
            List<WorkflowActionLogItem> items = await database.ActionLog.AsNoTracking()
                .Where(x => x.InstanceId == id)
                .OrderBy(x => x.OccurredDate)
                .Select(x => new WorkflowActionLogItem(x.Id, x.Action, x.ActorUserId, x.OnBehalfOfUserId, x.Comment, x.OccurredDate))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("GetWorkflowInstanceTimeline").RequirePermission(Permissions.Workflow.TaskView);

        group.MapPost("/{id:guid}/cancel", async (
            Guid id, WorkflowEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            await engine.CancelAsync(id, currentUser.UserId, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("CancelWorkflowInstance").RequirePermission(Permissions.Workflow.TaskAct);
    }

    private static void MapTaskEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workflows/tasks").WithTags("Workflow");

        // The approvals inbox: everything pending for me, across every module - architecture.md
        // §12.1's "Unified Approvals Inbox". No EntityType filter query param yet (v1 has one
        // consumer, Leave, which has not shipped); add it when a second module needs to
        // distinguish its own tasks in this list.
        group.MapGet("/", async (
            [AsParameters] PageQuery page,
            [FromQuery] WorkflowTaskStatus? status,
            WorkflowDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            PageRequest request = page.ToPageRequest();

            IQueryable<WorkflowTaskRow> query =
                from task in database.Tasks.AsNoTracking()
                where task.AssigneeUserId == currentUser.UserId
                join instance in database.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                where status == null || task.Status == status
                select new WorkflowTaskRow(task, instance);

            long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

            List<WorkflowTaskItem> items = await query
                .OrderBy(x => x.Task.DueDate == null)
                .ThenBy(x => x.Task.DueDate)
                .Skip(request.Skip)
                .Take(request.PageSize)
                .Select(x => new WorkflowTaskItem(
                    x.Task.Id, x.Task.InstanceId, x.Instance.EntityType, x.Instance.EntityId, x.Instance.SourceService,
                    x.Task.StepKey, x.Task.OriginalAssigneeUserId, x.Task.Status, x.Task.DueDate))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(PagedResult.From(items, request, total));
        }).WithName("ListMyWorkflowTasks").RequirePermission(Permissions.Workflow.TaskView);

        group.MapPost("/{id:guid}/approve", async (
            Guid id, ActOnWorkflowTaskBody body, WorkflowEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            await engine.ActAsync(id, currentUser.UserId, WorkflowTaskStatus.Approved, body.Comment, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("ApproveWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);

        group.MapPost("/{id:guid}/reject", async (
            Guid id, ActOnWorkflowTaskBody body, WorkflowEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            await engine.ActAsync(id, currentUser.UserId, WorkflowTaskStatus.Rejected, body.Comment, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("RejectWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);

        group.MapPost("/{id:guid}/send-back", async (
            Guid id, ActOnWorkflowTaskBody body, WorkflowEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            await engine.ActAsync(id, currentUser.UserId, WorkflowTaskStatus.SentBack, body.Comment, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("SendBackWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);

        group.MapPost("/{id:guid}/reassign", async (
            Guid id, Guid newAssigneeUserId, WorkflowDbContext database, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            WorkflowTask? task = await database.Tasks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (task is null)
            {
                return Results.NotFound();
            }

            task.Reassign(newAssigneeUserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).WithName("ReassignWorkflowTask").RequirePermission(Permissions.Workflow.TaskReassign);
    }

    private static void MapDelegationEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workflows/delegations").WithTags("Workflow");

        group.MapGet("/", async (WorkflowDbContext database, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            List<DelegationResponse> items = await database.Delegations.AsNoTracking()
                .Where(x => x.FromUserId == currentUser.UserId && x.IsActive)
                .OrderByDescending(x => x.StartDate)
                .Select(x => new DelegationResponse(x.Id, x.FromUserId, x.ToUserId, x.StartDate, x.EndDate, x.Reason))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListMyDelegations").RequirePermission(Permissions.Workflow.DelegationManage);

        group.MapPost("/", async (
            CreateDelegationBody body,
            WorkflowDbContext database,
            IUnitOfWork unitOfWork,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            Delegation delegation = Delegation.Create(currentUser.UserId, body.ToUserId, body.StartDate, body.EndDate, body.Reason);
            database.Delegations.Add(delegation);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return TypedResults.Created(
                $"/api/v1/workflows/delegations/{delegation.Id}",
                new DelegationResponse(delegation.Id, delegation.FromUserId, delegation.ToUserId, delegation.StartDate, delegation.EndDate, delegation.Reason));
        }).WithName("CreateDelegation").RequirePermission(Permissions.Workflow.DelegationManage);

        group.MapPost("/{id:guid}/revoke", async (
            Guid id, WorkflowDbContext database, ICurrentUser currentUser, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        {
            Delegation? delegation = await database.Delegations
                .FirstOrDefaultAsync(x => x.Id == id && x.FromUserId == currentUser.UserId, cancellationToken);

            if (delegation is null)
            {
                return Results.NotFound();
            }

            delegation.Revoke();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).WithName("RevokeDelegation").RequirePermission(Permissions.Workflow.DelegationManage);
    }

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition entity)
        => new(entity.Id, entity.Code, entity.Name, entity.EntityType, entity.IsSystem);

    private static WorkflowInstanceResponse ToResponse(WorkflowInstance entity) => new(
        entity.Id, entity.EntityType, entity.EntityId, entity.SourceService, entity.InitiatorUserId,
        entity.SubjectEmployeeId, entity.Status, entity.CurrentStepKey, entity.StartedDate, entity.CompletedDate);

    private static WorkflowDefinitionVersionResponse ToVersionResponse(WorkflowDefinitionVersion entity)
        => new(entity.Id, entity.DefinitionId, entity.Version, entity.Status, entity.DefinitionJson, entity.PublishedDate);

    private sealed record WorkflowTaskRow(WorkflowTask Task, WorkflowInstance Instance);
}
