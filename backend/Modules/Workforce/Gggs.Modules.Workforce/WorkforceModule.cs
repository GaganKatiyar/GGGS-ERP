using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Recruitment.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workforce.Application;
using Gggs.Modules.Workforce.Domain;

namespace Gggs.Modules.Workforce;

/// <summary>Workforce planning (spec §11).</summary>
public sealed class WorkforceModule : IModule
{
    public string Name => "workforce";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<WorkforceService>();
        services.AddWorkflowSubject(WorkforceService.PlanSubject);
        services.AddWorkflowSubject(WorkforceService.RequestSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveWorkforceOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectWorkforceOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnWorkforceOnSendBack>();
        services.AddGggsEventConsumer<RequisitionFilledIntegrationEvent, FulfilRequestOnRequisitionFilled>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/workforce").WithTags("Workforce");

        group.MapGet("/dashboard", async (WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.DashboardAsync(ct)))
            .WithName("WorkforceDashboard").RequirePermission(Permissions.Workforce.PlanView);

        group.MapGet("/plans", async (int? year, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.PlansAsync(year, ct)))
            .WithName("ListWorkforcePlans").RequirePermission(Permissions.Workforce.PlanView);
        group.MapGet("/plans/{id:guid}", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.PlanAsync(id, ct)))
            .WithName("GetWorkforcePlan").RequirePermission(Permissions.Workforce.PlanView, Permissions.Workflow.TaskAct);
        group.MapPost("/plans", async (PlanBody body, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.SavePlanAsync(null, body, ct)))
            .WithName("CreateWorkforcePlan").RequirePermission(Permissions.Workforce.PlanManage);
        group.MapPut("/plans/{id:guid}", async (Guid id, PlanBody body, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.SavePlanAsync(id, body, ct)))
            .WithName("UpdateWorkforcePlan").RequirePermission(Permissions.Workforce.PlanManage);
        group.MapPut("/plans/{id:guid}/lines", async (Guid id, PlanLinesBody body, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.SaveLinesAsync(id, body, ct)))
            .WithName("SaveWorkforcePlanLines").RequirePermission(Permissions.Workforce.PlanManage);
        group.MapPost("/plans/{id:guid}/submit", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.SubmitPlanAsync(id, ct)))
            .WithName("SubmitWorkforcePlan").RequirePermission(Permissions.Workforce.PlanManage);
        group.MapPost("/plans/{id:guid}/close", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.ClosePlanAsync(id, ct)))
            .WithName("CloseWorkforcePlan").RequirePermission(Permissions.Workforce.PlanManage);

        group.MapGet("/requests", async ([AsParameters] RequestQuery query, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.RequestsAsync(query, ct)))
            .WithName("ListManpowerRequests").RequirePermission(Permissions.Workforce.RequestView, Permissions.Workforce.RequestManage);
        group.MapGet("/requests/{id:guid}", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.RequestAsync(id, ct)))
            .WithName("GetManpowerRequest").RequirePermission(Permissions.Workforce.RequestView, Permissions.Workforce.RequestManage, Permissions.Workflow.TaskAct);
        group.MapPost("/requests", async (RequestBody body, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.RaiseAsync(body, ct)))
            .WithName("RaiseManpowerRequest").RequirePermission(Permissions.Workforce.RequestManage);
        group.MapPut("/requests/{id:guid}", async (Guid id, RequestDetails body, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.UpdateRequestAsync(id, body, ct)))
            .WithName("UpdateManpowerRequest").RequirePermission(Permissions.Workforce.RequestManage);
        group.MapPost("/requests/{id:guid}/submit", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.SubmitRequestAsync(id, ct)))
            .WithName("SubmitManpowerRequest").RequirePermission(Permissions.Workforce.RequestManage);
        group.MapPost("/requests/{id:guid}/cancel", async (Guid id, WorkforceService workforce, CancellationToken ct) => TypedResults.Ok(await workforce.CancelRequestAsync(id, ct)))
            .WithName("CancelManpowerRequest").RequirePermission(Permissions.Workforce.RequestManage);
    }
}

internal sealed class ApproveWorkforceOnApproval(WorkforceService workforce) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => WorkforceDecisions.ApplyAsync(workforce, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: true, cancellationToken);
}

internal sealed class RejectWorkforceOnRejection(WorkforceService workforce) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => WorkforceDecisions.ApplyAsync(workforce, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: false, cancellationToken);
}

internal sealed class ReturnWorkforceOnSendBack(WorkforceService workforce) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => WorkforceDecisions.ApplyAsync(workforce, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: null, cancellationToken);
}

internal static class WorkforceDecisions
{
    public static Task ApplyAsync(WorkforceService workforce, string? subjectType, Guid? subjectId, bool? approved, CancellationToken cancellationToken)
        => (subjectType, subjectId) switch
        {
            (WorkforceService.PlanSubjectType, { } id) => workforce.DecidePlanAsync(id, approved, cancellationToken),
            (WorkforceService.RequestSubjectType, { } id) => workforce.DecideRequestAsync(id, approved, cancellationToken),
            _ => Task.CompletedTask,
        };
}

internal sealed class FulfilRequestOnRequisitionFilled(WorkforceService workforce) : IIntegrationEventConsumer<RequisitionFilledIntegrationEvent>
{
    public Task HandleAsync(RequisitionFilledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.ManpowerRequestId is { } request ? workforce.FulfilAsync(request, cancellationToken) : Task.CompletedTask;
    }
}
