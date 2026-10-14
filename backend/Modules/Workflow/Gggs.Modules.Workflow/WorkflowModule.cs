using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Workflow.Application;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Endpoints;

namespace Gggs.Modules.Workflow;

/// <summary>
/// Approvals for every module (spec §109). Modules start requests through
/// <see cref="IWorkflowStarter"/> and react to the outcome events; nothing here knows what a policy
/// or a leave request is.
/// </summary>
public sealed class WorkflowModule : IModule
{
    public string Name => "workflow";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<IWorkflowStarter>(provider => provider.GetRequiredService<WorkflowEngine>());
        services.AddScoped<WorkflowQueries>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<WorkflowSlaSweep>();
        services.AddScoped<WorkflowSlaJob>();
        services.AddHostedService<WorkflowJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapWorkflowEndpoints();
}
