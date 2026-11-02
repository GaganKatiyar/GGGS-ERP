using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Recruitment.Contracts;
using Gggs.Modules.Studio.Application;
using Gggs.Modules.Studio.Endpoints;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Studio;

/// <summary>Integration Studio: email and messaging connectors, which of them sends the tenant's notifications, automations that act on business events through its connections, and accounts in outside directories.</summary>
public sealed class StudioModule : IModule
{
    public string Name => "studio";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddHttpClient(StudioHttp.ClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(OutboundUrlGuard.CreateHandler);
        services.AddScoped<IIntegrationAdapter, SlackAdapter>();
        services.AddScoped<IIntegrationAdapter, TeamsAdapter>();
        services.AddScoped<IIntegrationAdapter, SmtpAdapter>();
        services.AddScoped<IIntegrationAdapter, SendGridAdapter>();
        services.AddScoped<IIntegrationAdapter, Microsoft365MailAdapter>();
        services.AddScoped<IIntegrationAdapter, TwilioSmsAdapter>();
        services.AddScoped<IIntegrationAdapter, TwilioWhatsAppAdapter>();
        services.AddScoped<IIntegrationAdapter, Msg91Adapter>();
        services.AddScoped<IIntegrationAdapter, WhatsAppCloudAdapter>();
        services.AddScoped<IIntegrationAdapter, Microsoft365AccountsAdapter>();
        services.AddScoped<IIntegrationAdapter, GoogleWorkspaceAccountsAdapter>();
        services.AddScoped<TriggerFieldResolver>();
        services.AddScoped<AutomationEngine>();
        services.AddScoped<AutomationService>();
        services.AddScoped<AccountService>();
        services.AddScoped<ChannelService>();
        services.AddScoped<ITenantMessageSender>(provider => provider.GetRequiredService<ChannelService>());
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<AccountEngine>();
        services.AddGggsEventConsumer<EmployeeHiredIntegrationEvent, ProvisionAccounts>();
        services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, ProvisionAccounts>();
        services.AddScoped<AutomationRetrySweep>();
        services.AddScoped<AutomationRetryJob>();
        services.AddHostedService<StudioJobRegistration>();
        services.AddGggsEventConsumer<EmployeeHiredIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<EmployeeAssignmentChangedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<DepartmentHeadChangedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<LeaveApprovedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<LeaveCancelledIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<TimesheetApprovedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<WorkflowTaskAssignedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<PayrollRunFinalizedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<PayrollRunPaidIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<CandidateHiredIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<DocumentSignedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<ReviewReleasedIntegrationEvent, AutomationTriggers>();
        services.AddGggsEventConsumer<CourseCompletedIntegrationEvent, AutomationTriggers>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapStudioEndpoints();
}
