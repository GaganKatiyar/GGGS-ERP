using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Notification.Domain;

/// <summary>
/// The wording of one kind of notification, per tenant. Seeded from the catalog; the tenant may
/// reword it and choose its channels, but not invent codes - a template only matters if some
/// module sends it.
/// </summary>
public sealed class NotificationTemplate : TenantAggregateRoot
{
    private NotificationTemplate()
    {
        Code = null!;
        Name = null!;
        Subject = null!;
        Body = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public bool SendInApp { get; private set; }

    public bool SendEmail { get; private set; }

    public static NotificationTemplate Create(string code, string name, string subject, string body, bool sendInApp, bool sendEmail) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToLowerInvariant(), 64),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 300),
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000),
        SendInApp = sendInApp,
        SendEmail = sendEmail,
    };

    public void Update(string subject, string body, bool sendInApp, bool sendEmail, bool isActive)
    {
        Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 300);
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000);
        SendInApp = sendInApp;
        SendEmail = sendEmail;
        IsActive = isActive;
    }
}

/// <summary>One notification in someone's in-app inbox.</summary>
public sealed class InAppNotification : TenantAggregateRoot
{
    private InAppNotification()
    {
        EventCode = null!;
        Title = null!;
        Body = null!;
    }

    public Guid? RecipientEmployeeId { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string EventCode { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public string? Link { get; private set; }

    public DateTime? ReadDate { get; private set; }

    public static InAppNotification Create(Guid? employeeId, Guid? userId, string eventCode, string title, string body, string? link)
    {
        if (employeeId is null && userId is null)
        {
            throw new ArgumentException("An in-app notification needs an employee or a user to belong to.", nameof(employeeId));
        }

        return new InAppNotification
        {
            RecipientEmployeeId = employeeId,
            RecipientUserId = userId,
            EventCode = Guard.MaxLength(eventCode, 64),
            Title = Guard.MaxLength(title, 300),
            Body = Guard.MaxLength(body, 4000),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.MaxLength(link, 300),
        };
    }

    public void MarkRead(DateTime now) => ReadDate ??= now;
}

public enum EmailStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3,
}

/// <summary>
/// An email on its way out, kept as a record of what was sent to whom. Queued in the same
/// transaction as the change that caused it; delivered by the worker.
/// </summary>
public sealed class EmailMessage : TenantAggregateRoot
{
    private EmailMessage()
    {
        ToAddress = null!;
        Subject = null!;
        Body = null!;
        EventCode = null!;
    }

    public string ToAddress { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public string EventCode { get; private set; }

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? SentDate { get; private set; }

    public static EmailMessage Queue(string toAddress, string subject, string body, string eventCode) => new()
    {
        ToAddress = Guard.MaxLength(Guard.NotNullOrWhiteSpace(toAddress).Trim(), 256),
        Subject = Guard.MaxLength(subject, 300),
        Body = Guard.MaxLength(body, 8000),
        EventCode = Guard.MaxLength(eventCode, 64),
        Status = EmailStatus.Queued,
    };

    public void MarkSent(DateTime now)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentDate = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        Status = EmailStatus.Failed;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
