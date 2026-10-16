using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Compensation.Application;
using Gggs.Modules.Compensation.Domain;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Compensation;

/// <summary>Compensation (spec §23).</summary>
public sealed class CompensationModule : IModule
{
    public string Name => "compensation";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<CompensationService>();
        services.AddWorkflowSubject(CompensationService.RevisionSubject);
        services.AddWorkflowSubject(CompensationService.AwardSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveCompensationOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectCompensationOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnRevisionOnSendBack>();
        services.AddGggsEventConsumer<PayrollRunFinalizedIntegrationEvent, MarkAwardsPaidOnPayroll>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/compensation", async (CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.MyRewardsAsync(ct)))
            .WithTags("Compensation").WithName("MyCompensation").RequirePermission(Permissions.Self.CompensationView);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/compensation").WithTags("Compensation");

        group.MapGet("/bands", async (CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.BandsAsync(ct)))
            .WithName("ListSalaryBands").RequirePermission(Permissions.Compensation.BandView);
        group.MapPost("/bands", async (BandBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveBandAsync(null, body, ct)))
            .WithName("CreateSalaryBand").RequirePermission(Permissions.Compensation.BandManage);
        group.MapPut("/bands/{id:guid}", async (Guid id, BandBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveBandAsync(id, body, ct)))
            .WithName("UpdateSalaryBand").RequirePermission(Permissions.Compensation.BandManage);
        group.MapDelete("/bands/{id:guid}", async (Guid id, CompensationService compensation, CancellationToken ct) =>
            {
                await compensation.DeleteBandAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteSalaryBand").RequirePermission(Permissions.Compensation.BandManage);

        group.MapGet("/cycles", async (CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.CyclesAsync(ct)))
            .WithName("ListCompensationCycles").RequirePermission(Permissions.Compensation.RevisionView);
        group.MapPost("/cycles", async (CycleBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveCycleAsync(null, body, ct)))
            .WithName("CreateCompensationCycle").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPut("/cycles/{id:guid}", async (Guid id, CycleBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveCycleAsync(id, body, ct)))
            .WithName("UpdateCompensationCycle").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPost("/cycles/{id:guid}/open", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SetCycleStatusAsync(id, CycleStatus.Open, ct)))
            .WithName("OpenCompensationCycle").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPost("/cycles/{id:guid}/close", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SetCycleStatusAsync(id, CycleStatus.Closed, ct)))
            .WithName("CloseCompensationCycle").RequirePermission(Permissions.Compensation.RevisionManage);

        group.MapGet("/revisions", async ([AsParameters] RevisionQuery query, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.RevisionsAsync(query, ct)))
            .WithName("ListCompensationRevisions").RequirePermission(Permissions.Compensation.RevisionView);
        group.MapPost("/revisions", async (RevisionBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.ProposeAsync(body, ct)))
            .WithName("ProposeCompensationRevision").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPut("/revisions/{id:guid}", async (Guid id, RevisionDetails body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.UpdateRevisionAsync(id, body, ct)))
            .WithName("UpdateCompensationRevision").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPost("/revisions/{id:guid}/submit", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SubmitRevisionAsync(id, ct)))
            .WithName("SubmitCompensationRevision").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPost("/revisions/{id:guid}/cancel", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.CancelRevisionAsync(id, ct)))
            .WithName("CancelCompensationRevision").RequirePermission(Permissions.Compensation.RevisionManage);
        group.MapPost("/revisions/{id:guid}/reschedule", async (Guid id, RescheduleRevisionBody body, CompensationService compensation, CancellationToken ct) =>
                TypedResults.Ok(await compensation.RescheduleRevisionAsync(id, body.EffectiveDate, ct)))
            .WithName("RescheduleCompensationRevision").RequirePermission(Permissions.Compensation.RevisionManage);

        group.MapGet("/variable-pay", async (AwardStatus? status, Guid? employeeId, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.AwardsAsync(status, employeeId, ct)))
            .WithName("ListVariablePay").RequirePermission(Permissions.Compensation.VariablePayView);
        group.MapPost("/variable-pay", async (AwardBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.ProposeAwardAsync(body, ct)))
            .WithName("ProposeVariablePay").RequirePermission(Permissions.Compensation.VariablePayManage);
        group.MapPost("/variable-pay/{id:guid}/cancel", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.CancelAwardAsync(id, ct)))
            .WithName("CancelVariablePay").RequirePermission(Permissions.Compensation.VariablePayManage);

        group.MapGet("/equity", async (Guid? employeeId, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.GrantsAsync(employeeId, ct)))
            .WithName("ListEquityGrants").RequirePermission(Permissions.Compensation.EquityView);
        group.MapPost("/equity", async (GrantBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveGrantAsync(null, body, ct)))
            .WithName("CreateEquityGrant").RequirePermission(Permissions.Compensation.EquityManage);
        group.MapPut("/equity/{id:guid}", async (Guid id, GrantBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.SaveGrantAsync(id, body, ct)))
            .WithName("UpdateEquityGrant").RequirePermission(Permissions.Compensation.EquityManage);
        group.MapPost("/equity/{id:guid}/exercise", async (Guid id, ExerciseBody body, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.ExerciseAsync(id, body.Units, ct)))
            .WithName("ExerciseEquityGrant").RequirePermission(Permissions.Compensation.EquityManage);
        group.MapPost("/equity/{id:guid}/cancel", async (Guid id, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.CancelGrantAsync(id, ct)))
            .WithName("CancelEquityGrant").RequirePermission(Permissions.Compensation.EquityManage);

        group.MapGet("/employees/{employeeId:guid}", async (Guid employeeId, CompensationService compensation, CancellationToken ct) => TypedResults.Ok(await compensation.EmployeeRewardsAsync(employeeId, ct)))
            .WithName("EmployeeTotalRewards").RequirePermission(Permissions.Compensation.RevisionView);
    }
}

internal sealed class ApproveCompensationOnApproval(CompensationService compensation) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == CompensationService.RevisionSubjectType)
        {
            await compensation.DecideRevisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
        else if (integrationEvent.SubjectType == CompensationService.AwardSubjectType)
        {
            await compensation.DecideAwardAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectCompensationOnRejection(CompensationService compensation) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == CompensationService.RevisionSubjectType)
        {
            await compensation.DecideRevisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
        else if (integrationEvent.SubjectType == CompensationService.AwardSubjectType)
        {
            await compensation.DecideAwardAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}

internal sealed class ReturnRevisionOnSendBack(CompensationService compensation) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == CompensationService.RevisionSubjectType)
        {
            await compensation.DecideRevisionAsync(integrationEvent.SubjectId, approved: null, cancellationToken);
        }
    }
}

internal sealed class MarkAwardsPaidOnPayroll(CompensationService compensation) : IIntegrationEventConsumer<PayrollRunFinalizedIntegrationEvent>
{
    public Task HandleAsync(PayrollRunFinalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return compensation.MarkPaidAsync(
            integrationEvent.PaidInputs.Where(input => input.Source == CompensationService.PayrollSource).Select(input => input.SourceId),
            cancellationToken);
    }
}
