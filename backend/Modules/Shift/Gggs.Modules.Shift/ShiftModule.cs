using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Shift.Application;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Shift.Endpoints;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Shift;

/// <summary>Shifts and rosters (spec §19): patterns, assignments, one-day changes and swaps.</summary>
public sealed class ShiftModule : IModule
{
    public string Name => "shift";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ShiftService>();
        services.AddScoped<IShiftSchedule>(provider => provider.GetRequiredService<ShiftService>());
        services.AddWorkflowSubject(ShiftService.SwapSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApplySwapOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectSwapOnRejection>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapShiftEndpoints();
}
