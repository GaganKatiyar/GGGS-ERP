using Gggs.Notification.Domain.Notifications;

namespace Gggs.Notification.Application.Notifications;

public interface INotificationTemplateRepository
{
    /// <summary>The active template for this event on this channel, or null - see
    /// <see cref="NotificationTemplate"/>'s own remarks on why there is no fallback chain yet.</summary>
    Task<NotificationTemplate?> GetActiveAsync(
        string eventCode, NotificationChannel channel, CancellationToken cancellationToken = default);

    Task<NotificationTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(NotificationTemplate notificationTemplate);
}

public interface IInAppNotificationRepository
{
    Task<InAppNotification?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(InAppNotification notification);
}

public interface IOutboundMessageRepository
{
    Task<bool> IdempotencyKeyExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    void Add(OutboundMessage message);
}
