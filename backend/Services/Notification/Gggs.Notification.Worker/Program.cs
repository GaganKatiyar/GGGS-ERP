using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Contracts;
using Gggs.Notification.Application.Notifications;
using Gggs.Notification.Infrastructure;
using Microsoft.EntityFrameworkCore;

/*
 * Notification's background half - and the only half that renders or sends anything. The Api
 * project serves a user's own inbox and lets admins edit templates; it never dispatches, so this
 * process is where "someone was invited" actually becomes an email.
 *
 * No request, so no signed-in user - writes are attributed to the system identity, same as every
 * other worker.
 */

const string ServiceName = "notification-worker";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);

builder.Services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<NotificationDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("NotificationDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());

builder.Services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
builder.Services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
builder.Services.AddScoped<IOutboundMessageRepository, OutboundMessageRepository>();
builder.Services.AddScoped<NotificationDispatchService>();

builder.Services.AddOptions<SmtpEmailSenderOptions>()
    .Bind(builder.Configuration.GetSection(SmtpEmailSenderOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddGggsMessaging<NotificationDbContext>(builder.Configuration, platform.DeploymentProfile);

// What this service listens for. An event with no consumer registered here is never delivered to
// this subscription at all - see AddGggsEventConsumer's own remarks on why that failure is silent.
builder.Services.AddGggsEventConsumer<UserInvitedIntegrationEvent, UserInvitedConsumer>();

builder.Services.AddGggsEventSubscriber<NotificationDbContext>();

// This process also drains Notification's own outbox, for whatever it raises later - nothing does
// yet, but every worker follows this shape so the Api project never has to.
builder.Services.AddGggsOutboxRelay<NotificationDbContext>();

IHost host = builder.Build();
await host.RunAsync();
