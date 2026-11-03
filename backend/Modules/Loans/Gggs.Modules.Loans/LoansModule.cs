using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Loans.Application;
using Gggs.Modules.Loans.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Hangfire;

namespace Gggs.Modules.Loans;

/// <summary>Employee loans and salary advances.</summary>
public sealed class LoansModule : IModule
{
    public string Name => "loans";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<LoanService>();
        services.AddScoped<LoanSeeder>();
        services.AddScoped<ILoanLedger, LoanLedger>();
        services.AddScoped<IAiTool, MyLoansTool>();
        services.AddWorkflowSubject(LoanService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedLoansOnTenantProvisioned>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveLoanOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectLoanOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, RejectLoanOnSendBack>();
        services.AddGggsEventConsumer<PayrollRunFinalizedIntegrationEvent, RecordInstalmentsOnPayroll>();
        services.AddScoped<LoanInstalments>();
        services.AddScoped<LoanInstalmentsJob>();
        services.AddHostedService<LoanJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/loans").WithTags("Loans");

        group.MapGet("/types", async (LoanService service, CancellationToken ct) => TypedResults.Ok(await service.TypesAsync(ct)))
            .WithName("ListLoanTypes").RequirePermission(Permissions.Loan.View, Permissions.Loan.TypeManage, Permissions.Self.LoanApply);
        group.MapPost("/types", async (LoanTypeBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.SaveTypeAsync(null, body, ct)))
            .WithName("CreateLoanType").RequirePermission(Permissions.Loan.TypeManage);
        group.MapPut("/types/{id:guid}", async (Guid id, LoanTypeBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.SaveTypeAsync(id, body, ct)))
            .WithName("UpdateLoanType").RequirePermission(Permissions.Loan.TypeManage);

        group.MapGet("/summary", async (LoanService service, CancellationToken ct) => TypedResults.Ok(await service.SummaryAsync(ct)))
            .WithName("LoanSummary").RequirePermission(Permissions.Loan.View);
        group.MapGet("", async ([AsParameters] LoanQuery query, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(query, ct)))
            .WithName("ListLoans").RequirePermission(Permissions.Loan.View);
        group.MapPost("", async (ApplyBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.ApplyAsync(body, ct)))
            .WithName("ApplyLoanForEmployee").RequirePermission(Permissions.Loan.Manage);
        group.MapPost("/quote", async (QuoteBody body, Guid employeeId, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.QuoteAsync(body, employeeId, ct)))
            .WithName("QuoteLoanForEmployee").RequirePermission(Permissions.Loan.Manage);
        group.MapGet("/{id:guid}", async (Guid id, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .WithName("GetLoan").RequirePermission(Permissions.Loan.View, Permissions.Self.LoanApply, Permissions.Workflow.TaskAct);
        group.MapPost("/{id:guid}/disburse", async (Guid id, DisburseBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.DisburseAsync(id, body, ct)))
            .WithName("DisburseLoan").RequirePermission(Permissions.Loan.Manage);
        group.MapPost("/{id:guid}/prepay", async (Guid id, PrepayBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.PrepayAsync(id, body, ct)))
            .WithName("PrepayLoan").RequirePermission(Permissions.Loan.Manage);
        group.MapPost("/{id:guid}/write-off", async (Guid id, WriteOffBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.WriteOffAsync(id, body, ct)))
            .WithName("WriteOffLoan").RequirePermission(Permissions.Loan.Manage);
        group.MapPost("/{id:guid}/cancel", async (Guid id, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.CancelAsync(id, ct)))
            .WithName("CancelLoan").RequirePermission(Permissions.Self.LoanApply, Permissions.Loan.Manage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/loans").WithTags("Loans");
        me.MapGet("", async (LoanService service, CancellationToken ct) => TypedResults.Ok(await service.MineAsync(ct)))
            .WithName("MyLoans").RequirePermission(Permissions.Self.LoanApply);
        me.MapPost("", async (ApplyBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.ApplyAsync(body with { EmployeeId = null }, ct)))
            .WithName("ApplyLoan").RequirePermission(Permissions.Self.LoanApply);
        me.MapPost("/quote", async (QuoteBody body, LoanService service, CancellationToken ct) => TypedResults.Ok(await service.QuoteAsync(body, null, ct)))
            .WithName("QuoteLoan").RequirePermission(Permissions.Self.LoanApply);
    }
}

internal sealed class SeedLoansOnTenantProvisioned(LoanSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal sealed class ApproveLoanOnApproval(LoanService loans) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent is { SubjectType: LoanService.SubjectType, SubjectId: { } id } ? loans.DecideAsync(id, approved: true, cancellationToken) : Task.CompletedTask;
}

internal sealed class RejectLoanOnRejection(LoanService loans) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent is { SubjectType: LoanService.SubjectType, SubjectId: { } id } ? loans.DecideAsync(id, approved: false, cancellationToken) : Task.CompletedTask;
}

/// <summary>A loan sent back is not approved; the employee applies again with what was asked for.</summary>
internal sealed class RejectLoanOnSendBack(LoanService loans) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent is { SubjectType: LoanService.SubjectType, SubjectId: { } id } ? loans.DecideAsync(id, approved: false, cancellationToken) : Task.CompletedTask;
}

/// <summary>Instalments a finalized payroll deducted come off the loans' balances.</summary>
internal sealed class RecordInstalmentsOnPayroll(LoanService loans) : IIntegrationEventConsumer<PayrollRunFinalizedIntegrationEvent>
{
    public Task HandleAsync(PayrollRunFinalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        List<Guid> instalments = [.. integrationEvent.PaidInputs.Where(input => input.Source == LoanService.InstalmentSource).Select(input => input.SourceId)];
        return loans.DeductedAsync(instalments, integrationEvent.Period.AddMonths(1).AddDays(-1), cancellationToken);
    }
}

/// <summary>Fans the daily instalment pass out to every tenant.</summary>
[PlatformJob("Loan instalments: fans out one pass per active tenant raising instalments due to payroll.")]
internal sealed class LoanInstalments(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "loans-instalments";

    public Task RunAsync() => fanOut.FanOutAsync<LoanInstalmentsJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class LoanInstalmentsJob(LoanService loans, ILogger<LoanInstalmentsJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int raised = await loans.RaiseDueAsync(CancellationToken.None);
        if (raised > 0)
        {
            logger.LogInformation("Loan instalments for tenant {TenantId}: {Raised} instalment(s) sent to payroll.", tenantId, raised);
        }
    }
}

internal sealed class LoanJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<LoanInstalments>(LoanInstalments.RecurringJobName, job => job.RunAsync(), "15 1 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
