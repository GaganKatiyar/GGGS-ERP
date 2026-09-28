using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Notification.Domain.Notifications;

/// <summary>Where a rendered notification goes. SMS/WhatsApp/push are architecture.md §14's later
/// phases; only these two ship in this slice.</summary>
public enum NotificationChannel
{
    Email = 1,
    InApp = 2,
}

/// <summary>
/// A tenant's own wording for one event on one channel.
/// <para>
/// Lookup in this slice is by <c>(TenantId, EventCode, Channel)</c> only - <see cref="Locale"/> is
/// stored but not yet part of the match. Architecture.md §14's full fallback chain
/// (tenant+locale → tenant+default locale → platform+locale → platform+en) needs platform-owned
/// default templates, which do not exist yet: a tenant that never creates a template for an event
/// simply gets no notification for it on that channel, and <c>NotificationDispatchService</c> logs
/// a warning rather than failing the request that triggered it.
/// </para>
/// </summary>
public sealed class NotificationTemplate : TenantAggregateRoot
{
    private NotificationTemplate()
    {
        EventCode = null!;
        Locale = null!;
        BodyTemplate = null!;
    }

    public string EventCode { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string Locale { get; private set; }

    /// <summary>Required for <see cref="NotificationChannel.Email"/>; unused for in-app.</summary>
    public string? Subject { get; private set; }

    /// <summary><c>{{ key }}</c> placeholders, substituted by <c>NotificationTemplateRenderer</c>
    /// against the event's own data only - see its own remarks for why this is plain substitution
    /// rather than a full template engine.</summary>
    public string BodyTemplate { get; private set; }

    public static NotificationTemplate Create(
        string eventCode, NotificationChannel channel, string locale, string? subject, string bodyTemplate)
    {
        Guard.Against(
            channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(subject),
            "NOTIFICATION_TEMPLATE_SUBJECT_REQUIRED",
            "An email template needs a subject.");

        return new NotificationTemplate
        {
            EventCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(eventCode).Trim().ToLowerInvariant(), 100),
            Channel = channel,
            Locale = Guard.MaxLength(Guard.NotNullOrWhiteSpace(locale).Trim().ToLowerInvariant(), 10),
            Subject = string.IsNullOrWhiteSpace(subject) ? null : Guard.MaxLength(subject.Trim(), 200),
            BodyTemplate = Guard.NotNullOrWhiteSpace(bodyTemplate),
            IsActive = true,
        };
    }

    public void UpdateContent(string? subject, string bodyTemplate)
    {
        Guard.Against(
            Channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(subject),
            "NOTIFICATION_TEMPLATE_SUBJECT_REQUIRED",
            "An email template needs a subject.");

        Subject = string.IsNullOrWhiteSpace(subject) ? null : Guard.MaxLength(subject.Trim(), 200);
        BodyTemplate = Guard.NotNullOrWhiteSpace(bodyTemplate);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
