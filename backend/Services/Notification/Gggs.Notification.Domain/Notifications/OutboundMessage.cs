using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Notification.Domain.Notifications;

public enum OutboundMessageStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// One rendered email, queued and then sent (or not) by <c>IEmailSender</c>.
/// <para>
/// Kept even after sending, unlike a fire-and-forget log line: rendered subject and body are the
/// only record of what a recipient actually received, which is what a support ticket asks about
/// days later.
/// </para>
/// <para>
/// Delivery is synchronous in this slice - <c>NotificationDispatchService</c> calls the sender and
/// records the outcome in the same request, rather than a separate dispatcher draining a queue.
/// Provider delivery webhooks (bounce, complaint) are not wired, so <see cref="OutboundMessageStatus.Sent"/>
/// means "handed to the SMTP server", not "the recipient's inbox has it".
/// </para>
/// </summary>
public sealed class OutboundMessage : TenantAggregateRoot
{
    private OutboundMessage()
    {
        RecipientAddress = null!;
        EventCode = null!;
        Subject = null!;
        Body = null!;
        IdempotencyKey = null!;
    }

    public Guid? RecipientUserId { get; private set; }

    public string RecipientAddress { get; private set; }

    public string EventCode { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public OutboundMessageStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Stops the same event from emailing the same recipient twice on a message redelivery.</summary>
    public string IdempotencyKey { get; private set; }

    public static OutboundMessage Create(
        Guid? recipientUserId, string recipientAddress, string eventCode, string subject, string body, string idempotencyKey)
        => new()
        {
            RecipientUserId = recipientUserId,
            RecipientAddress = Guard.MaxLength(Guard.NotNullOrWhiteSpace(recipientAddress).Trim(), 256),
            EventCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(eventCode).Trim().ToLowerInvariant(), 100),
            Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 200),
            Body = Guard.NotNullOrWhiteSpace(body),
            Status = OutboundMessageStatus.Queued,
            IdempotencyKey = Guard.MaxLength(Guard.NotNullOrWhiteSpace(idempotencyKey), 300),
        };

    public void MarkSent(string? providerMessageId)
    {
        Status = OutboundMessageStatus.Sent;
        Attempts++;
        ProviderMessageId = providerMessageId;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Status = OutboundMessageStatus.Failed;
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
