using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Project.Application;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Endpoints;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Project;

/// <summary>Projects (spec §39-56, §78-79).</summary>
public sealed class ProjectModule : IModule
{
    public string Name => "projects";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IAiTool, ProjectStatusTool>();
        services.AddScoped<IMetricSource, ProjectMetrics>();
        services.AddScoped<ISearchSource, ProjectSearch>();
        services.AddScoped<ICalendarSource, ProjectCalendar>();
        services.AddScoped<ProjectAccess>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<PlanningService>();
        services.AddScoped<GovernanceService>();
        services.AddScoped<InsightService>();
        services.AddScoped<ProjectCatalogSeeder>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddScoped<IProjectStaffing, ProjectStaffing>();
        services.AddScoped<IProjectPermissions, ProjectPermissions>();
        services.AddScoped<IDocumentOwnerAccess, ProjectDocumentAccess>();
        services.AddWorkflowSubject(GovernanceService.ChangeRequestSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ProjectHealthSweep>();
        services.AddScoped<ProjectHealthJob>();
        services.AddHostedService<ProjectJobRegistration>();
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedProjectCatalogOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, ReleaseFromProjectsOnExit>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveChangeOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectChangeOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnChangeOnSendBack>();
        services.AddGggsEventConsumer<TimesheetApprovedIntegrationEvent, AddHoursOnTimesheetApproval>();
        services.AddGggsEventConsumer<TimesheetReopenedIntegrationEvent, RemoveHoursOnTimesheetReopened>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapProjectEndpoints();
}
