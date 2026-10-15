using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Timesheets.Application;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Timesheets.Endpoints;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Timesheets;

/// <summary>Timesheets (spec §70-71).</summary>
public sealed class TimesheetsModule : IModule
{
    public string Name => "timesheets";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<TimesheetService>();
        services.AddScoped<ITimesheetLedger, TimesheetLedger>();
        services.AddWorkflowSubject(TimesheetService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<TimesheetReminderSweep>();
        services.AddScoped<TimesheetReminderJob>();
        services.AddHostedService<TimesheetJobRegistration>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveTimesheetOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectTimesheetOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnTimesheetOnSendBack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapTimesheetEndpoints();
}
