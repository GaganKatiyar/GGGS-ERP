using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Notification.Domain.Notifications;

/// <summary>One rendered in-app notification for one user's bell/inbox.</summary>
public sealed class InAppNotification : TenantAggregateRoot
{
    private InAppNotification()
    {
        EventCode = null!;
        Title = null!;
        Body = null!;
    }

    public Guid UserId { get; private set; }

    public string EventCode { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>Where clicking the notification takes the user, e.g. a deep link into the app.</summary>
    public string? Link { get; private set; }

    public bool IsRead { get; private set; }

    public DateTime? ReadDate { get; private set; }

    public static InAppNotification Create(Guid userId, string eventCode, string title, string body, string? link)
        => new()
        {
            UserId = Guard.NotEmpty(userId),
            EventCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(eventCode).Trim().ToLowerInvariant(), 100),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 2000),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.MaxLength(link.Trim(), 500),
        };

    public void MarkRead(DateTime now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadDate = now;
    }
}
