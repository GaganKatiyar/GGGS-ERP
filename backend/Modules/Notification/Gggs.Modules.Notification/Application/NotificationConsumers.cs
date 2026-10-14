using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Notification.Application;

/// <summary>
/// Gives the current tenant every catalog template it does not have yet. Idempotent: existing
/// templates, including a tenant's rewording, are never touched.
/// </summary>
public sealed class NotificationTemplateSeeder(AppDbContext dbContext, ILogger<NotificationTemplateSeeder> logger)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> existing = [.. await dbContext.NotificationTemplates
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(template => template.Code)
            .ToListAsync(cancellationToken)];

        int added = 0;
        foreach (TemplateSeed seed in NotificationTemplateCatalog.Defaults.Where(seed => !existing.Contains(seed.Code)))
        {
            dbContext.NotificationTemplates.Add(NotificationTemplate.Create(seed.Code, seed.Name, seed.Subject, seed.Body, seed.SendInApp, seed.SendEmail));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Notification templates seeded: {Added} added.", added);
        return added;
    }
}

internal sealed class SeedTemplatesOnTenantProvisioned(NotificationTemplateSeeder seeder)
    : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>
/// Sends one queued email. A failure is recorded on the message and not retried here: a bad
/// address or a rejected relay would fail the same way again, and the record says what happened.
/// </summary>
internal sealed class DeliverQueuedEmail(
    AppDbContext dbContext,
    INotificationChannel channel,
    TimeProvider timeProvider,
    ILogger<DeliverQueuedEmail> logger) : IIntegrationEventConsumer<EmailQueuedIntegrationEvent>
{
    public async Task HandleAsync(EmailQueuedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EmailMessage? email = await dbContext.EmailMessages.FirstOrDefaultAsync(message => message.Id == integrationEvent.EmailMessageId, cancellationToken);
        if (email is null || email.Status != EmailStatus.Queued)
        {
            return;
        }

        try
        {
            await channel.SendAsync(new OutboundMessage(email.ToAddress, email.Subject, email.Body), cancellationToken);
            email.MarkSent(timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Email {EmailId} via {Channel} failed.", email.Id, channel.Name);
            email.MarkFailed(exception.Message);
        }
    }
}

internal sealed class NotifyTaskAssigned(INotifier notifier, Gggs.Modules.Employee.Contracts.IEmployeeDirectory employees)
    : IIntegrationEventConsumer<WorkflowTaskAssignedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowTaskAssignedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        string delegatedNote = string.Empty;
        if (integrationEvent.DelegatedFromEmployeeId is { } from && await employees.GetAsync(from, cancellationToken) is { } delegator)
        {
            delegatedNote = $", on behalf of {delegator.DisplayName}";
        }

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.WorkflowTaskAssigned,
                [new NotificationRecipient(integrationEvent.AssigneeEmployeeId)],
                new Dictionary<string, string> { ["title"] = integrationEvent.Title, ["delegatedNote"] = delegatedNote },
                "/approvals"),
            cancellationToken);
    }
}

internal sealed class NotifyTaskEscalated(INotifier notifier, Gggs.Modules.Employee.Contracts.IEmployeeDirectory employees)
    : IIntegrationEventConsumer<WorkflowTaskEscalatedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowTaskEscalatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        string fromName = (await employees.GetAsync(integrationEvent.FromEmployeeId, cancellationToken))?.DisplayName ?? "the previous approver";

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.WorkflowTaskEscalated,
                [new NotificationRecipient(integrationEvent.ToEmployeeId)],
                new Dictionary<string, string> { ["title"] = integrationEvent.Title, ["fromName"] = fromName },
                "/approvals"),
            cancellationToken);
    }
}

internal sealed class NotifyRequestApproved(INotifier notifier) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RequesterNotice.SendAsync(notifier, NotificationTemplates.WorkflowApproved, integrationEvent.RequesterEmployeeId, integrationEvent.Title, null, integrationEvent.InstanceId, cancellationToken);
    }
}

internal sealed class NotifyRequestRejected(INotifier notifier) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RequesterNotice.SendAsync(notifier, NotificationTemplates.WorkflowRejected, integrationEvent.RequesterEmployeeId, integrationEvent.Title, integrationEvent.Comment, integrationEvent.InstanceId, cancellationToken);
    }
}

internal sealed class NotifyRequestSentBack(INotifier notifier) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RequesterNotice.SendAsync(notifier, NotificationTemplates.WorkflowSentBack, integrationEvent.RequesterEmployeeId, integrationEvent.Title, integrationEvent.Comment, integrationEvent.InstanceId, cancellationToken);
    }
}

internal static class RequesterNotice
{
    public static async Task SendAsync(
        INotifier notifier,
        string template,
        Guid? requesterEmployeeId,
        string title,
        string? comment,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        if (requesterEmployeeId is not { } requester)
        {
            return;
        }

        await notifier.NotifyAsync(
            new NotificationRequest(
                template,
                [new NotificationRecipient(requester)],
                new Dictionary<string, string> { ["title"] = title, ["comment"] = comment ?? string.Empty },
                $"/approvals/requests/{instanceId}"),
            cancellationToken);
    }
}

/// <summary>Invitations go by email: the invitee cannot sign in to see anything in-app yet.</summary>
internal sealed class SendInvitation(INotifier notifier, IOptions<NotificationOptions> options)
    : IIntegrationEventConsumer<UserInvitedIntegrationEvent>
{
    public async Task HandleAsync(UserInvitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        string link = string.Concat(
            options.Value.AppBaseUrl.TrimEnd('/'),
            "/complete-invitation?tenantCode=", Uri.EscapeDataString(integrationEvent.TenantCode),
            "&userId=", integrationEvent.UserId.ToString(),
            "&token=", Uri.EscapeDataString(integrationEvent.InvitationToken));

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.UserInvited,
                [new NotificationRecipient(Email: integrationEvent.Email)],
                new Dictionary<string, string>
                {
                    ["recipientName"] = integrationEvent.DisplayName,
                    ["invitationLink"] = link,
                    ["expires"] = integrationEvent.ExpiresDate.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                }),
            cancellationToken);
    }
}
