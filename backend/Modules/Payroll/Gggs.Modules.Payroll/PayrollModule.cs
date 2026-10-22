using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Endpoints;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Payroll;

/// <summary>Payroll (spec §21-22, §94, §103).</summary>
public sealed class PayrollModule : IModule
{
    public string Name => "payroll";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IMetricSource, PayrollMetrics>();
        services.AddScoped<PayrollRunFinder>();
        services.AddScoped<IIntegrationAdapter, BankTransferAdapter>();
        services.AddScoped<IIntegrationAdapter, AccountingJournalAdapter>();
        services.AddScoped<PayrollCatalog>();
        services.AddScoped<PayrollSetupService>();
        services.AddScoped<PayrollSeeder>();
        services.AddScoped<SalaryService>();
        services.AddScoped<IPayrollSalaries>(provider => provider.GetRequiredService<SalaryService>());
        services.AddScoped<InputService>();
        services.AddScoped<IPayrollInputs>(provider => provider.GetRequiredService<InputService>());
        services.AddScoped<DeclarationService>();
        services.AddScoped<RunService>();
        services.AddScoped<FinalSettlementPayroll>();
        services.AddScoped<IFinalSettlementPayroll>(provider => provider.GetRequiredService<FinalSettlementPayroll>());
        services.AddScoped<PayslipService>();
        services.AddScoped<PayrollReports>();
        services.AddWorkflowSubject(RunService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedPayrollOnTenantProvisioned>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApprovePayrollRunOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, ReworkPayrollRunOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReworkPayrollRunOnSendBack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPayrollEndpoints();
}

internal sealed class ApprovePayrollRunOnApproval(RunService runs) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == RunService.SubjectType)
        {
            await runs.ApplyDecisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class ReworkPayrollRunOnRejection(RunService runs) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == RunService.SubjectType)
        {
            await runs.ApplyDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}

internal sealed class ReworkPayrollRunOnSendBack(RunService runs) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public async Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == RunService.SubjectType)
        {
            await runs.ApplyDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
