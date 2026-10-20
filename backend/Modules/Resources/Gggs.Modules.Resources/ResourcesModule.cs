using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Resources.Application;
using Gggs.Modules.Resources.Endpoints;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Resources;

/// <summary>Resource management (spec §57-69, §72-73, §80).</summary>
public sealed class ResourcesModule : IModule
{
    public string Name => "resources";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IAiTool, AvailablePeopleTool>();
        services.AddScoped<IMetricSource, ResourceMetrics>();
        services.AddScoped<WorkingCalendar>();
        services.AddScoped<ResourceService>();
        services.AddScoped<RequestService>();
        services.AddScoped<ResourceInsights>();
        services.AddWorkflowSubject(ResourceService.TransferSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, CompleteTransferOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectTransferOnRejection>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapResourceEndpoints();
}

/// <summary>Completes an approved transfer: released from one project, allocated on the other.</summary>
internal sealed class CompleteTransferOnApproval(ResourceService resources) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ResourceService.TransferSubjectType)
        {
            await resources.ApplyTransferDecisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectTransferOnRejection(ResourceService resources) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == ResourceService.TransferSubjectType)
        {
            await resources.ApplyTransferDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
