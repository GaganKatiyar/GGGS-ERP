using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Leave.Application;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Leave.Endpoints;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Leave;

/// <summary>Leave and holidays (spec §20-21): types, policies, the ledger, requests and accrual.</summary>
public sealed class LeaveModule : IModule
{
    public string Name => "leave";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<LeaveCalendarService>();
        services.AddScoped<ILeaveCalendar>(provider => provider.GetRequiredService<LeaveCalendarService>());
        services.AddScoped<LeaveService>();
        services.AddScoped<LeaveAccrualService>();
        services.AddScoped<LeaveCatalogSeeder>();
        services.AddWorkflowSubject(LeaveService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<LeaveAccrualSweep>();
        services.AddScoped<LeaveAccrualJob>();
        services.AddHostedService<LeaveJobRegistration>();
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedLeaveCatalogOnTenantProvisioned>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveLeaveOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectLeaveOnRejection>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapLeaveEndpoints();
}
