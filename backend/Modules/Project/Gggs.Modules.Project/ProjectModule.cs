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
        services.AddScoped<ServiceLevelService>();
        services.AddScoped<TaskActivityLog>();
        services.AddScoped<TaskCollaborationService>();
        services.AddScoped<TaskViewsService>();
        services.AddScoped<TaskService>();
        services.AddScoped<PlanningService>();
        services.AddScoped<GovernanceService>();
        services.AddScoped<InsightService>();
        services.AddScoped<DefaultProjectService>();
        services.AddScoped<ProjectTemplateService>();
        services.AddScoped<ProjectCatalogSeeder>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddScoped<IProjectStaffing, ProjectStaffing>();
        services.AddScoped<IProjectTasks, ProjectTasks>();
        services.AddScoped<IProjectPermissions, ProjectPermissions>();
        services.AddScoped<IDocumentOwnerAccess, ProjectDocumentAccess>();
        services.AddScoped<IDocumentOwnerAccess, ProjectTaskDocumentAccess>();
        services.AddWorkflowSubject(GovernanceService.ChangeRequestSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ProjectSlaSweep>();
        services.AddScoped<ProjectSlaJob>();
        services.AddScoped<ProjectHealthSweep>();
        services.AddScoped<ProjectHealthJob>();
        services.AddScoped<DefaultProjectSweep>();
        services.AddScoped<DefaultProjectJob>();
        services.AddHostedService<ProjectJobRegistration>();
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedProjectCatalogOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, ReleaseFromProjectsOnExit>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveChangeOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectChangeOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnChangeOnSendBack>();
        services.AddGggsEventConsumer<TimesheetHoursApprovedIntegrationEvent, AddHoursOnTimesheetApproval>();
        services.AddGggsEventConsumer<TimesheetReopenedIntegrationEvent, RemoveHoursOnTimesheetReopened>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapProjectEndpoints();
}
