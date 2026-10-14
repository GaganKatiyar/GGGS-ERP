using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Endpoints;
using Gggs.Modules.Notification.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Notification;

/// <summary>
/// Notifications (spec §108): templates per tenant, the in-app inbox, and email through a channel
/// port. Other modules call <see cref="INotifier"/>; this module also listens to the events that
/// always notify someone (approvals, invitations).
/// </summary>
public sealed class NotificationModule : IModule
{
    public string Name => "notification";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        services.AddOptions<NotificationOptions>().Bind(context.Configuration.GetSection(NotificationOptions.SectionName));
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<NotificationTemplateSeeder>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool smtp = string.Equals(
            context.Configuration[$"{NotificationOptions.SectionName}:{nameof(NotificationOptions.EmailProvider)}"],
            "Smtp",
            StringComparison.OrdinalIgnoreCase);

        if (smtp)
        {
            services.AddScoped<INotificationChannel, SmtpEmailChannel>();
        }
        else
        {
            services.AddScoped<INotificationChannel, LoggingEmailChannel>();
        }

        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedTemplatesOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmailQueuedIntegrationEvent, DeliverQueuedEmail>();
        services.AddGggsEventConsumer<WorkflowTaskAssignedIntegrationEvent, NotifyTaskAssigned>();
        services.AddGggsEventConsumer<WorkflowTaskEscalatedIntegrationEvent, NotifyTaskEscalated>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, NotifyRequestApproved>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, NotifyRequestRejected>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, NotifyRequestSentBack>();
        services.AddGggsEventConsumer<UserInvitedIntegrationEvent, SendInvitation>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNotificationEndpoints();
}
