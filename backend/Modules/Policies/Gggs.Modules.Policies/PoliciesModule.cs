using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Policies.Application;
using Gggs.Modules.Policies.Endpoints;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Policies;

/// <summary>Company policies (spec §110): versions, approval-gated publishing, and acknowledgements.</summary>
public sealed class PoliciesModule : IModule
{
    public string Name => "policies";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<PolicyService>();
        services.AddWorkflowSubject(PolicyService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, PublishPolicyOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, ReturnPolicyOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnPolicyOnSendBack>();
        services.AddGggsEventConsumer<WorkflowWithdrawnIntegrationEvent, ReturnPolicyOnWithdrawal>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPolicyEndpoints();
}
