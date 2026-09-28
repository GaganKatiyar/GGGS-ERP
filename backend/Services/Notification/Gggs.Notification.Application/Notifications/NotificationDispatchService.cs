using Gggs.BuildingBlocks.Application;
using Gggs.Notification.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Gggs.Notification.Application.Notifications;

/// <summary>
/// One notification to raise, in whatever channels have an active template for it.
/// <para>
/// <see cref="SourceMessageId"/> is the triggering integration event's own id (or any other value a
/// caller can guarantee is stable across a redelivery) - the idempotency key is built from it, not
/// from <see cref="EventCode"/> and <see cref="RecipientUserId"/> alone, so a user invited a second
/// time still gets a second email instead of the first delivery silently absorbing every future one.
/// </para>
/// </summary>
public sealed record NotificationRequest(
    Guid SourceMessageId,
    string EventCode,
    Guid RecipientUserId,
    string? RecipientEmail,
    string Title,
    IReadOnlyDictionary<string, object?> TemplateData,
    string? Link = null);

/// <summary>
/// Turns one notification request into whatever rows and side effects its active templates call
/// for: an in-app row, a queued-and-sent email, both, or - when the tenant has defined no template
/// for this event on any channel - neither, logged rather than failed.
/// </summary>
public sealed class NotificationDispatchService(
    INotificationTemplateRepository templates,
    IInAppNotificationRepository inAppNotifications,
    IOutboundMessageRepository outboundMessages,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork,
    ILogger<NotificationDispatchService> logger)
{
    public async Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool dispatchedAnything = false;

        NotificationTemplate? inAppTemplate = await templates.GetActiveAsync(
            request.EventCode, NotificationChannel.InApp, cancellationToken);

        if (inAppTemplate is not null)
        {
            string body = NotificationTemplateRenderer.Render(inAppTemplate.BodyTemplate, request.TemplateData);
            inAppNotifications.Add(InAppNotification.Create(
                request.RecipientUserId, request.EventCode, request.Title, body, request.Link));
            dispatchedAnything = true;
        }

        if (!string.IsNullOrWhiteSpace(request.RecipientEmail))
        {
            await DispatchEmailAsync(request, cancellationToken);
            dispatchedAnything = true;
        }

        if (!dispatchedAnything)
        {
            logger.LogWarning(
                "No active template exists for {EventCode} on any channel; nothing was dispatched.",
                request.EventCode);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchEmailAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        NotificationTemplate? emailTemplate = await templates.GetActiveAsync(
            request.EventCode, NotificationChannel.Email, cancellationToken);

        if (emailTemplate is null)
        {
            return;
        }

        string idempotencyKey = $"{request.SourceMessageId}:{NotificationChannel.Email}";
        if (await outboundMessages.IdempotencyKeyExistsAsync(idempotencyKey, cancellationToken))
        {
            logger.LogInformation(
                "Email for {EventCode} ({SourceMessageId}) was already queued; skipping.",
                request.EventCode, request.SourceMessageId);
            return;
        }

        string subject = NotificationTemplateRenderer.Render(emailTemplate.Subject!, request.TemplateData);
        string body = NotificationTemplateRenderer.Render(emailTemplate.BodyTemplate, request.TemplateData);

        OutboundMessage message = OutboundMessage.Create(
            request.RecipientUserId, request.RecipientEmail!, request.EventCode, subject, body, idempotencyKey);
        outboundMessages.Add(message);

        try
        {
            string? providerMessageId = await emailSender.SendAsync(
                request.RecipientEmail!, subject, body, cancellationToken);
            message.MarkSent(providerMessageId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Delivery failing is not this request's failure: the row it was for still exists and
            // the caller's own work (inviting the user, approving the leave, ...) already committed
            // by the time this runs. The queued row - now marked Failed - is the record an operator
            // needs to retry it by hand; no automatic retry is built yet.
            message.MarkFailed(exception.Message);
            logger.LogWarning(
                exception, "Sending the {EventCode} email to a recipient failed.", request.EventCode);
        }
    }
}
