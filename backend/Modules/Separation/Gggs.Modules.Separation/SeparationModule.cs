using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Separation.Application;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Separation;

/// <summary>Separation, clearance, full and final settlement, letters and alumni (spec: separation).</summary>
public sealed class SeparationModule : IModule
{
    public string Name => "separation";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<SeparationService>();
        services.AddWorkflowSubject(SeparationService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, AcceptResignationOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, CloseResignationOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, CloseResignationOnSendBack>();
        services.AddScoped<SeparationSweep>();
        services.AddScoped<SeparationSweepJob>();
        services.AddHostedService<SeparationJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/separation").WithTags("Separation");

        group.MapGet("/dashboard", async (SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("SeparationDashboard").RequirePermission(Permissions.Separation.CaseView);
        group.MapGet("/cases", async (CaseStatus? status, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.CasesAsync(status, ct)))
            .WithName("ListSeparationCases").RequirePermission(Permissions.Separation.CaseView);
        group.MapGet("/cases/{id:guid}", async (Guid id, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.CaseAsync(id, ct)))
            .WithName("GetSeparationCase").RequirePermission(Permissions.Separation.CaseView, Permissions.Separation.ClearanceManage, Permissions.Self.ResignationSubmit, Permissions.Workflow.TaskAct);
        group.MapPost("/cases", async (InitiateBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.InitiateAsync(body, ct)))
            .WithName("InitiateSeparation").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPut("/cases/{id:guid}/terms", async (Guid id, TermsBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.SetTermsAsync(id, body, ct)))
            .WithName("SetSeparationTerms").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPost("/cases/{id:guid}/withdraw", async (Guid id, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.WithdrawAsync(id, ct)))
            .WithName("WithdrawSeparation").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPost("/cases/{id:guid}/clearance", async (Guid id, ClearanceBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.AddClearanceAsync(id, body, ct)))
            .WithName("AddClearanceItem").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPut("/cases/{id:guid}/interview", async (Guid id, InterviewBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.RecordInterviewAsync(id, body, ct)))
            .WithName("RecordExitInterview").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPost("/cases/{id:guid}/letters", async (Guid id, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.IssueLettersAsync(id, ct)))
            .WithName("IssueSeparationLetters").RequirePermission(Permissions.Separation.CaseManage);

        group.MapPost("/cases/{id:guid}/settlement/calculate", async (Guid id, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.CalculateAsync(id, ct)))
            .WithName("CalculateSettlement").RequirePermission(Permissions.Separation.SettlementManage);
        group.MapPost("/cases/{id:guid}/settlement/lines", async (Guid id, LineBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.AddLineAsync(id, body, ct)))
            .WithName("AddSettlementLine").RequirePermission(Permissions.Separation.SettlementManage);
        group.MapDelete("/cases/{id:guid}/settlement/lines/{lineId:guid}", async (Guid id, Guid lineId, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.RemoveLineAsync(id, lineId, ct)))
            .WithName("RemoveSettlementLine").RequirePermission(Permissions.Separation.SettlementManage);
        group.MapPost("/cases/{id:guid}/settlement/{action:regex(^(approve|reopen|pay)$)}", async (Guid id, string action, PayBody? body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.SettlementActionAsync(id, action, body?.Reference, ct)))
            .WithName("ChangeSettlement").RequirePermission(Permissions.Separation.SettlementManage);

        group.MapGet("/clearance", async (SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.MyClearancesAsync(ct)))
            .WithName("MyClearanceItems").RequirePermission(Permissions.Separation.ClearanceManage, Permissions.Separation.CaseView, Permissions.Workflow.TaskAct);
        group.MapPost("/clearance/{itemId:guid}", async (Guid itemId, ResolveBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.ResolveAsync(itemId, body, ct)))
            .WithName("ResolveClearanceItem").RequirePermission(Permissions.Separation.ClearanceManage, Permissions.Separation.CaseView, Permissions.Workflow.TaskAct);

        group.MapGet("/alumni", async (string? search, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.AlumniAsync(search, ct)))
            .WithName("ListAlumni").RequirePermission(Permissions.Separation.AlumniView);
        group.MapPut("/alumni/{id:guid}", async (Guid id, AlumniBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAlumniAsync(id, body, ct)))
            .WithName("UpdateAlumni").RequirePermission(Permissions.Separation.CaseManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/separation").WithTags("Separation");
        me.MapGet("/", async (SeparationService service, CancellationToken ct) => await service.MineAsync(ct) is { } view ? Results.Ok(view) : Results.NoContent())
            .WithName("MySeparation").RequirePermission(Permissions.Self.ResignationSubmit);
        me.MapPost("/", async (ResignBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.ResignAsync(body, ct)))
            .WithName("Resign").RequirePermission(Permissions.Self.ResignationSubmit);
        me.MapPost("/withdraw", async (SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.WithdrawAsync(null, ct)))
            .WithName("WithdrawResignation").RequirePermission(Permissions.Self.ResignationSubmit);
        me.MapPut("/interview", async (InterviewBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.RecordInterviewAsync(null, body, ct)))
            .WithName("SubmitExitInterview").RequirePermission(Permissions.Self.ResignationSubmit);
    }
}

internal sealed class AcceptResignationOnApproval(SeparationService separation) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == SeparationService.SubjectType ? separation.DecideAsync(integrationEvent.SubjectId, approved: true, cancellationToken) : Task.CompletedTask;
}

internal sealed class CloseResignationOnRejection(SeparationService separation) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == SeparationService.SubjectType ? separation.DecideAsync(integrationEvent.SubjectId, approved: false, cancellationToken) : Task.CompletedTask;
}

internal sealed class CloseResignationOnSendBack(SeparationService separation) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == SeparationService.SubjectType ? separation.DecideAsync(integrationEvent.SubjectId, approved: null, cancellationToken) : Task.CompletedTask;
}

/// <summary>Fans the daily exit sweep out to every tenant.</summary>
internal sealed class SeparationSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "separation-exit-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<SeparationSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class SeparationSweepJob(SeparationService separation, ILogger<SeparationSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int exited = await separation.SweepAsync(CancellationToken.None);
        if (exited > 0)
        {
            logger.LogInformation("Separation sweep for tenant {TenantId}: {Exited} employee(s) exited on their last working day.", tenantId, exited);
        }
    }
}

internal sealed class SeparationJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<SeparationSweep>(SeparationSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 1 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
