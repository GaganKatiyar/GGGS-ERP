using Gggs.Notification.Domain.Notifications;

namespace Gggs.Notification.Api.Contracts;

public sealed record InAppNotificationItem(
    Guid Id,
    string EventCode,
    string Title,
    string Body,
    string? Link,
    bool IsRead,
    DateTime CreatedDate);

public sealed record UnreadCountResponse(int UnreadCount);

public sealed record NotificationTemplateResponse(
    Guid Id,
    string EventCode,
    NotificationChannel Channel,
    string Locale,
    string? Subject,
    string BodyTemplate,
    bool IsActive);

public sealed record CreateNotificationTemplateBody(
    string EventCode,
    NotificationChannel Channel,
    string Locale,
    string? Subject,
    string BodyTemplate);

public sealed record UpdateNotificationTemplateContentBody(string? Subject, string BodyTemplate);
