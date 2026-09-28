using Gggs.Notification.Application.Notifications;
using Gggs.Notification.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Notification.Infrastructure;

public sealed class NotificationTemplateRepository(NotificationDbContext dbContext) : INotificationTemplateRepository
{
    public async Task<NotificationTemplate?> GetActiveAsync(
        string eventCode, NotificationChannel channel, CancellationToken cancellationToken = default)
        => await dbContext.Templates.FirstOrDefaultAsync(
            x => x.EventCode == eventCode && x.Channel == channel && x.IsActive,
            cancellationToken);

    public async Task<NotificationTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Templates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(NotificationTemplate notificationTemplate) => dbContext.Templates.Add(notificationTemplate);
}

public sealed class InAppNotificationRepository(NotificationDbContext dbContext) : IInAppNotificationRepository
{
    public async Task<InAppNotification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.InAppNotifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(InAppNotification notification) => dbContext.InAppNotifications.Add(notification);
}

public sealed class OutboundMessageRepository(NotificationDbContext dbContext) : IOutboundMessageRepository
{
    public async Task<bool> IdempotencyKeyExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => await dbContext.OutboundMessages.AnyAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Add(OutboundMessage message) => dbContext.OutboundMessages.Add(message);
}
