using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Attendance.Application;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Attendance.Endpoints;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Attendance;

/// <summary>Attendance (spec §18): punches, judged days, regularization and device imports.</summary>
public sealed class AttendanceModule : IModule
{
    public string Name => "attendance";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<AttendanceService>();
        services.AddScoped<IAttendanceLedger, AttendanceLedger>();
        services.AddWorkflowSubject(AttendanceService.RegularizationSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<AttendanceCloseSweep>();
        services.AddScoped<AttendanceCloseJob>();
        services.AddHostedService<AttendanceJobRegistration>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, RegularizeOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectRegularizationOnRejection>();
        services.AddGggsEventConsumer<LeaveApprovedIntegrationEvent, RecloseOnLeaveApproved>();
        services.AddGggsEventConsumer<LeaveCancelledIntegrationEvent, RecloseOnLeaveCancelled>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAttendanceEndpoints();
}
