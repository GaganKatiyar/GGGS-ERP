using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Contract.Application;
using Gggs.Modules.Contract.Contracts;
using Gggs.Modules.Contract.Endpoints;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Contract;

/// <summary>Client contracts (spec §38).</summary>
public sealed class ContractModule : IModule
{
    public string Name => "contracts";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ContractService>();
        services.AddScoped<IContractDirectory, ContractDirectory>();
        services.AddWorkflowSubject(ContractService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ContractSweep>();
        services.AddScoped<ContractSweepJob>();
        services.AddHostedService<ContractJobRegistration>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ActivateContractOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, ReturnContractOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnContractOnSendBack>();
        services.AddGggsEventConsumer<WorkflowWithdrawnIntegrationEvent, ReturnContractOnWithdrawal>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapContractEndpoints();
}
