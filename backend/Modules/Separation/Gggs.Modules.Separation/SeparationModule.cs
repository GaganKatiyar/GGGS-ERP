using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Leave.Contracts;
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
        services.AddScoped<IMetricSource, SeparationMetrics>();
        services.AddScoped<ICalendarSource, SeparationCalendar>();
        services.AddScoped<SeparationService>();
        services.AddScoped<SeparationPolicyService>();
        services.AddScoped<OffboardingService>();
        services.AddScoped<ILeaveApplicationRule, SeparationLeaveRule>();
        services.AddWorkflowSubject(SeparationService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, AcceptResignationOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, CloseResignationOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, CloseResignationOnSendBack>();
        services.AddScoped<SeparationSweep>();
        services.AddScoped<SeparationSweepJob>();
        services.AddScoped<OffboardingReminders>();
        services.AddScoped<OffboardingRemindersJob>();
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

        group.MapPut("/cases/{id:guid}/classification", async (Guid id, ClassificationBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.ClassifyAsync(id, body, ct)))
            .WithName("ClassifySeparation").RequirePermission(Permissions.Separation.CaseManage);
        group.MapPost("/cases/{id:guid}/early-release/{decision:regex(^(grant|decline)$)}", async (Guid id, string decision, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.DecideEarlyReleaseAsync(id, decision == "grant", ct)))
            .WithName("DecideEarlyRelease").RequirePermission(Permissions.Separation.CaseManage);

        group.MapGet("/reasons", () => TypedResults.Ok(SeparationPolicyService.Reasons))
            .WithName("SeparationReasons").RequirePermission(Permissions.Separation.CaseView, Permissions.Self.ResignationSubmit);
        group.MapGet("/policy", async (SeparationPolicyService policy, CancellationToken ct) => TypedResults.Ok(await policy.GetAsync(ct)))
            .WithName("SeparationPolicy").RequirePermission(Permissions.Separation.CaseView, Permissions.Separation.PolicyManage, Permissions.Self.ResignationSubmit);
        group.MapPut("/policy", async (PolicyBody body, SeparationPolicyService policy, CancellationToken ct) => TypedResults.Ok(await policy.UpdateAsync(body, ct)))
            .WithName("UpdateSeparationPolicy").RequirePermission(Permissions.Separation.PolicyManage);
        group.MapGet("/policy/notice-rules", async (SeparationPolicyService policy, CancellationToken ct) => TypedResults.Ok(await policy.RulesAsync(ct)))
            .WithName("NoticeRules").RequirePermission(Permissions.Separation.CaseView, Permissions.Separation.PolicyManage);
        group.MapPost("/policy/notice-rules", async (NoticeRuleBody body, SeparationPolicyService policy, CancellationToken ct) => TypedResults.Ok(await policy.AddRuleAsync(body, ct)))
            .WithName("AddNoticeRule").RequirePermission(Permissions.Separation.PolicyManage);
        group.MapDelete("/policy/notice-rules/{id:guid}", async (Guid id, SeparationPolicyService policy, CancellationToken ct) =>
            {
                await policy.RemoveRuleAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveNoticeRule").RequirePermission(Permissions.Separation.PolicyManage);

        group.MapGet("/offboarding/tasks", async (OffboardingService offboarding, CancellationToken ct) => TypedResults.Ok(await offboarding.MyTasksAsync(ct)))
            .WithName("MyOffboardingTasks").RequirePermission(Permissions.Separation.OffboardingManage, Permissions.Separation.ClearanceManage, Permissions.Separation.CaseView, Permissions.Workflow.TaskAct, Permissions.Self.ResignationSubmit);
        group.MapPost("/offboarding/tasks/{taskId:guid}", async (Guid taskId, FinishTaskBody body, OffboardingService offboarding, SeparationService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CaseAsync(await offboarding.FinishTaskAsync(taskId, body, ct), ct)))
            .WithName("FinishOffboardingTask").RequirePermission(Permissions.Separation.OffboardingManage, Permissions.Separation.ClearanceManage, Permissions.Separation.CaseView, Permissions.Workflow.TaskAct, Permissions.Self.ResignationSubmit);
        group.MapPut("/cases/{id:guid}/handover", async (Guid id, HandoverBody body, OffboardingService offboarding, SeparationService service, CancellationToken ct) =>
            {
                await offboarding.SetHandoverAsync(id, body, ct);
                return TypedResults.Ok(await service.CaseAsync(id, ct));
            })
            .WithName("SetHandover").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapPost("/cases/{id:guid}/handover/refresh", async (Guid id, OffboardingService offboarding, SeparationService service, CancellationToken ct) =>
            {
                await offboarding.RefreshAsync(id, ct);
                return TypedResults.Ok(await service.CaseAsync(id, ct));
            })
            .WithName("RefreshHandover").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapPost("/handover/{itemId:guid}", async (Guid itemId, HandoverActionBody body, OffboardingService offboarding, SeparationService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CaseAsync(await offboarding.ActAsync(itemId, body, ct), ct)))
            .WithName("ActOnHandoverItem").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapPost("/cases/{id:guid}/access/disable", async (Guid id, OffboardingService offboarding, SeparationService service, CancellationToken ct) =>
            {
                await offboarding.DisableAccessNowAsync(id, ct);
                return TypedResults.Ok(await service.CaseAsync(id, ct));
            })
            .WithName("DisableLeaverAccess").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapGet("/offboarding/templates", async (OffboardingService offboarding, CancellationToken ct) => TypedResults.Ok(await offboarding.TemplatesAsync(ct)))
            .WithName("OffboardingTemplates").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapPost("/offboarding/templates", async (OffboardingTemplateBody body, OffboardingService offboarding, CancellationToken ct) => TypedResults.Ok(await offboarding.SaveTemplateAsync(null, body, ct)))
            .WithName("CreateOffboardingTemplate").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapPut("/offboarding/templates/{id:guid}", async (Guid id, OffboardingTemplateBody body, OffboardingService offboarding, CancellationToken ct) => TypedResults.Ok(await offboarding.SaveTemplateAsync(id, body, ct)))
            .WithName("UpdateOffboardingTemplate").RequirePermission(Permissions.Separation.OffboardingManage);
        group.MapDelete("/offboarding/templates/{id:guid}", async (Guid id, OffboardingService offboarding, CancellationToken ct) =>
            {
                await offboarding.RemoveTemplateAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveOffboardingTemplate").RequirePermission(Permissions.Separation.OffboardingManage);

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
        me.MapPost("/early-release", async (EarlyReleaseBody body, SeparationService service, CancellationToken ct) => TypedResults.Ok(await service.RequestEarlyReleaseAsync(body, ct)))
            .WithName("RequestEarlyRelease").RequirePermission(Permissions.Self.ResignationSubmit);
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
[PlatformJob("Separation exit sweep: fans out one exit pass per active tenant.")]
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

/// <summary>Fans the daily overdue-offboarding reminders out to every tenant.</summary>
[PlatformJob("Offboarding reminders: fans out one overdue-task reminder pass per active tenant.")]
internal sealed class OffboardingReminders(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "separation-offboarding-reminders";

    public Task RunAsync() => fanOut.FanOutAsync<OffboardingRemindersJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class OffboardingRemindersJob(OffboardingService offboarding, ILogger<OffboardingRemindersJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int sent = await offboarding.RemindAsync(CancellationToken.None);
        if (sent > 0)
        {
            logger.LogInformation("Offboarding reminders for tenant {TenantId}: {Sent} overdue task reminder(s) sent.", tenantId, sent);
        }
    }
}

internal sealed class SeparationJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<SeparationSweep>(SeparationSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 1 * * *");
        recurringJobs.AddOrUpdate<OffboardingReminders>(OffboardingReminders.RecurringJobName, reminders => reminders.RunAsync(), "30 3 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
