using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Workflow.Application;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Domain;
using Gggs.Modules.Workflow.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workflow.Endpoints;

public sealed record WorkflowDefinitionRequest(
    string Code,
    string Name,
    string? Description,
    string SubjectType,
    IReadOnlyList<WorkflowStage> Stages,
    Guid? FallbackApproverEmployeeId);

public sealed record WorkflowDecisionRequest(string? Comment);

public sealed record WorkflowReassignRequest(Guid EmployeeId, string? Comment);

public sealed record DelegationRequest(
    Guid? DelegatorEmployeeId,
    Guid DelegateEmployeeId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string? SubjectType,
    string? Reason);

public sealed record PendingCount(int Pending);

/// <summary>
/// Approvals (spec §109): the inbox every approver works from, the requester's view of their own
/// requests, delegations, and the definitions administrators configure.
/// </summary>
internal static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workflows").WithTags("Workflows");

        group.MapGet("/subjects", (WorkflowQueries queries) => TypedResults.Ok(queries.Subjects()))
            .WithName("ListWorkflowSubjects").RequirePermission(Permissions.Workflow.DefinitionView);

        group.MapGet("/definitions", async (WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.DefinitionsAsync(ct)))
            .WithName("ListWorkflowDefinitions").RequirePermission(Permissions.Workflow.DefinitionView);
        group.MapGet("/definitions/{id:guid}", async (Guid id, WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.DefinitionAsync(id, ct)))
            .WithName("GetWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionView);
        group.MapPost("/definitions", CreateDefinitionAsync).WithName("CreateWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionManage);
        group.MapPut("/definitions/{id:guid}", UpdateDefinitionAsync).WithName("UpdateWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionManage);
        group.MapPost("/definitions/{id:guid}/activate", ActivateDefinitionAsync).WithName("ActivateWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionManage);
        group.MapPost("/definitions/{id:guid}/deactivate", DeactivateDefinitionAsync).WithName("DeactivateWorkflowDefinition").RequirePermission(Permissions.Workflow.DefinitionManage);

        group.MapGet("/tasks", async ([AsParameters] TaskListQuery query, WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.MyTasksAsync(query, ct)))
            .WithName("ListMyWorkflowTasks").RequirePermission(Permissions.Workflow.TaskView);
        group.MapGet("/tasks/count", async (WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(new PendingCount(await queries.PendingCountAsync(ct))))
            .WithName("CountMyWorkflowTasks").RequirePermission(Permissions.Workflow.TaskView);
        group.MapPost("/tasks/{id:guid}/approve", (Guid id, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct)
                => DecideAsync(id, WorkflowTaskStatus.Approved, body, engine, ct))
            .WithName("ApproveWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);
        group.MapPost("/tasks/{id:guid}/reject", (Guid id, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct)
                => DecideAsync(id, WorkflowTaskStatus.Rejected, body, engine, ct))
            .WithName("RejectWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);
        group.MapPost("/tasks/{id:guid}/send-back", (Guid id, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct)
                => DecideAsync(id, WorkflowTaskStatus.SentBack, body, engine, ct))
            .WithName("SendBackWorkflowTask").RequirePermission(Permissions.Workflow.TaskAct);
        group.MapPost("/tasks/{id:guid}/reassign", ReassignAsync).WithName("ReassignWorkflowTask").RequirePermission(Permissions.Workflow.TaskReassign);

        group.MapGet("/instances", async ([AsParameters] InstanceListQuery query, WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.InstancesAsync(query, ct)))
            .WithName("ListWorkflowInstances").RequirePermission(Permissions.Workflow.TaskView, Permissions.Workflow.InstanceViewAll);
        group.MapGet("/instances/{id:guid}", async (Guid id, WorkflowQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.InstanceAsync(id, ct)))
            .WithName("GetWorkflowInstance").RequirePermission(Permissions.Workflow.TaskView, Permissions.Workflow.InstanceViewAll);
        group.MapPost("/instances/{id:guid}/withdraw", async (Guid id, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct) =>
            {
                await engine.WithdrawOwnAsync(id, body?.Comment, ct);
                return TypedResults.NoContent();
            })
            .WithName("WithdrawWorkflowInstance").RequirePermission(Permissions.Workflow.TaskView);
        group.MapPost("/instances/{id:guid}/resubmit", async (Guid id, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct) =>
            {
                await engine.ResubmitAsync(id, body?.Comment, ct);
                return TypedResults.NoContent();
            })
            .WithName("ResubmitWorkflowInstance").RequirePermission(Permissions.Workflow.TaskView);

        group.MapGet("/delegations", ListDelegationsAsync).WithName("ListDelegations").RequirePermission(Permissions.Workflow.DelegationManage);
        group.MapPost("/delegations", CreateDelegationAsync).WithName("CreateDelegation").RequirePermission(Permissions.Workflow.DelegationManage);
        group.MapPost("/delegations/{id:guid}/revoke", RevokeDelegationAsync).WithName("RevokeDelegation").RequirePermission(Permissions.Workflow.DelegationManage);

        return endpoints;
    }

    private static async Task<NoContent> DecideAsync(Guid id, WorkflowTaskStatus decision, WorkflowDecisionRequest? body, WorkflowEngine engine, CancellationToken ct)
    {
        await engine.DecideAsync(id, decision, body?.Comment, ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ReassignAsync(Guid id, WorkflowReassignRequest body, WorkflowEngine engine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await engine.ReassignAsync(id, body.EmployeeId, body.Comment, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Created<DefinitionItem>> CreateDefinitionAsync(
        WorkflowDefinitionRequest body,
        AppDbContext database,
        IEmployeeDirectory employees,
        IEnumerable<WorkflowSubject> subjects,
        WorkflowQueries queries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await ValidateAsync(body, employees, subjects, ct);

        string code = body.Code.Trim().ToUpperInvariant();
        if (await database.WorkflowDefinitions.AnyAsync(definition => definition.Code == code, ct))
        {
            throw new DomainRuleException("WORKFLOW_CODE_IN_USE", $"A workflow with code '{code}' already exists.");
        }

        var definition = WorkflowDefinition.Create(code, body.Name, body.Description, body.SubjectType, body.Stages, body.FallbackApproverEmployeeId);
        database.WorkflowDefinitions.Add(definition);
        await database.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/workflows/definitions/{definition.Id}", await queries.DefinitionAsync(definition.Id, ct));
    }

    private static async Task<Ok<DefinitionItem>> UpdateDefinitionAsync(
        Guid id,
        WorkflowDefinitionRequest body,
        AppDbContext database,
        IEmployeeDirectory employees,
        IEnumerable<WorkflowSubject> subjects,
        WorkflowQueries queries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        WorkflowDefinition definition = await FindDefinitionAsync(database, id, ct);

        if (!string.Equals(definition.SubjectType, body.SubjectType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleException("WORKFLOW_SUBJECT_FIXED", "A workflow's subject cannot change; create a new workflow instead.");
        }

        await ValidateAsync(body, employees, subjects, ct);
        definition.Update(body.Name, body.Description, body.Stages, body.FallbackApproverEmployeeId);
        await database.SaveChangesAsync(ct);

        return TypedResults.Ok(await queries.DefinitionAsync(id, ct));
    }

    private static async Task<Ok<DefinitionItem>> ActivateDefinitionAsync(Guid id, AppDbContext database, WorkflowQueries queries, CancellationToken ct)
    {
        WorkflowDefinition definition = await FindDefinitionAsync(database, id, ct);

        if (await database.WorkflowDefinitions.AnyAsync(
                other => other.Id != id && other.SubjectType == definition.SubjectType && other.IsActive, ct))
        {
            throw new DomainRuleException("WORKFLOW_SUBJECT_TAKEN", "Another workflow is already active for this kind of request. Deactivate it first.");
        }

        definition.Activate();
        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(await queries.DefinitionAsync(id, ct));
    }

    /// <summary>Requests already under way finish under the stages they started with.</summary>
    private static async Task<Ok<DefinitionItem>> DeactivateDefinitionAsync(Guid id, AppDbContext database, WorkflowQueries queries, CancellationToken ct)
    {
        WorkflowDefinition definition = await FindDefinitionAsync(database, id, ct);
        definition.Deactivate();
        await database.SaveChangesAsync(ct);
        return TypedResults.Ok(await queries.DefinitionAsync(id, ct));
    }

    private static async Task<Ok<IReadOnlyList<DelegationItem>>> ListDelegationsAsync(
        string? scope,
        WorkflowQueries queries,
        IPermissionProvider permissions,
        CancellationToken ct)
    {
        bool everyone = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase) && await ManagesAllDelegationsAsync(permissions, ct);
        return TypedResults.Ok(await queries.DelegationsAsync(everyone, ct));
    }

    private static async Task<Created<DelegationItem>> CreateDelegationAsync(
        DelegationRequest body,
        AppDbContext database,
        IEmployeeDirectory employees,
        IPermissionProvider permissions,
        ICurrentUser currentUser,
        WorkflowQueries queries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid delegator = body.DelegatorEmployeeId ?? currentUser.EmployeeId
            ?? throw new DomainRuleException("DELEGATION_NO_EMPLOYEE", "Your account is not linked to an employee, so there are no approvals of yours to delegate.");

        // Delegating someone else's approvals is an administrator's call.
        if (delegator != currentUser.EmployeeId && !await ManagesAllDelegationsAsync(permissions, ct))
        {
            throw new ForbiddenException("DELEGATION_OTHERS", "You can only delegate your own approvals.");
        }

        if (!await employees.IsActiveAsync(delegator, ct) || !await employees.IsActiveAsync(body.DelegateEmployeeId, ct))
        {
            throw new DomainRuleException("DELEGATION_INACTIVE", "Both people must be current employees.");
        }

        var delegation = Delegation.Create(delegator, body.DelegateEmployeeId, body.StartsOn, body.EndsOn, body.SubjectType, body.Reason);
        database.Delegations.Add(delegation);
        await database.SaveChangesAsync(ct);

        DelegationItem item = (await queries.DelegationsAsync(everyone: true, ct)).First(row => row.Id == delegation.Id);
        return TypedResults.Created($"/api/v1/workflows/delegations/{delegation.Id}", item);
    }

    private static async Task<NoContent> RevokeDelegationAsync(
        Guid id,
        AppDbContext database,
        IPermissionProvider permissions,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        Delegation delegation = await database.Delegations.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new NotFoundException(nameof(Delegation), id);

        if (delegation.DelegatorEmployeeId != currentUser.EmployeeId && !await ManagesAllDelegationsAsync(permissions, ct))
        {
            throw new NotFoundException(nameof(Delegation), id);
        }

        delegation.Revoke();
        await database.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<bool> ManagesAllDelegationsAsync(IPermissionProvider permissions, CancellationToken ct)
        => (await permissions.GetCurrentAsync(ct)).ScopeFor(Permissions.Workflow.DelegationManage) == DataScope.Tenant;

    /// <summary>The subject must be one a module registered, conditions must test its fields, named people must be current.</summary>
    private static async Task ValidateAsync(
        WorkflowDefinitionRequest body,
        IEmployeeDirectory employees,
        IEnumerable<WorkflowSubject> subjects,
        CancellationToken ct)
    {
        string subjectType = body.SubjectType?.Trim().ToLowerInvariant() ?? string.Empty;
        WorkflowSubject subject = subjects.FirstOrDefault(candidate => string.Equals(candidate.Code, subjectType, StringComparison.Ordinal))
            ?? throw new DomainRuleException("WORKFLOW_SUBJECT_UNKNOWN", $"'{body.SubjectType}' is not something that can be approved.");

        StageJson.Validate(body.Stages);

        foreach (WorkflowStage stage in body.Stages)
        {
            if (stage.Condition is { } condition
                && !subject.Fields.Any(field => string.Equals(field.Key, condition.Field, StringComparison.Ordinal)))
            {
                throw new DomainRuleException("WORKFLOW_CONDITION_FIELD", $"'{subject.Name}' requests have no field '{condition.Field}'.");
            }
        }

        IEnumerable<Guid> named = body.Stages
            .SelectMany(stage => stage.EscalateTo is null ? stage.Approvers : stage.Approvers.Append(stage.EscalateTo))
            .Where(rule => rule.Kind == ApproverKind.Employee && rule.EmployeeId is not null)
            .Select(rule => rule.EmployeeId!.Value);

        if (body.FallbackApproverEmployeeId is { } fallback)
        {
            named = named.Append(fallback);
        }

        foreach (Guid employeeId in named.Distinct())
        {
            if (!await employees.IsActiveAsync(employeeId, ct))
            {
                throw new DomainRuleException("WORKFLOW_APPROVER_INACTIVE", "Every named approver must be a current employee.");
            }
        }
    }

    private static async Task<WorkflowDefinition> FindDefinitionAsync(AppDbContext database, Guid id, CancellationToken ct)
        => await database.WorkflowDefinitions.FirstOrDefaultAsync(definition => definition.Id == id, ct)
            ?? throw new NotFoundException(nameof(WorkflowDefinition), id);
}
