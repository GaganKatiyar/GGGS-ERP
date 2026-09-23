namespace Gggs.BuildingBlocks.Messaging;

/// <summary>What has happened to an outbox row.</summary>
public enum OutboxStatus
{
    Pending = 0,
    Published = 1,
    /// <summary>Exhausted its attempts; an operator decides whether to replay or discard it.</summary>
    Failed = 2,
}

/// <summary>
/// A message waiting to be published, stored in the owning service's own database.
/// <para>
/// This row is written in the same transaction as the business change that produced it. That is the
/// whole point: a process that dies between "leave request approved" and "message published" leaves
/// the row behind, and the relay picks it up. The alternative - publishing inside the handler -
/// loses the message on a crash, or publishes one for a transaction that then rolls back.
/// </para>
/// <para>
/// It carries a <see cref="TenantId"/> but is deliberately <b>not</b> <c>ITenantOwned</c>. The
/// outbox is infrastructure that the relay reads across tenants; marking it tenant-owned would
/// apply the query filter and the row-level security policy to it, and the relay - which runs in a
/// platform scope with no session context - would then see an empty table and silently publish
/// nothing. The tenant still travels with every message, in the envelope, which is where consumers
/// need it.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        MessageType = null!;
        Payload = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string MessageType { get; private set; }

    public int SchemaVersion { get; private set; }

    public string Payload { get; private set; }

    public string? CorrelationId { get; private set; }

    public Guid? CausationId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public DateTime OccurredDate { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>When the relay should try again; backoff is written here after a failure.</summary>
    public DateTime? NextAttemptDate { get; private set; }

    public DateTime? PublishedDate { get; private set; }

    /// <summary>Last failure, kept so an operator can see why without trawling logs.</summary>
    public string? LastError { get; private set; }

    public static OutboxMessage Create(MessageEnvelope envelope, DateTime now) => new()
    {
        Id = envelope.MessageId,
        TenantId = envelope.TenantId,
        MessageType = envelope.MessageType,
        SchemaVersion = envelope.SchemaVersion,
        Payload = envelope.Payload,
        CorrelationId = envelope.CorrelationId,
        CausationId = envelope.CausationId,
        ActorUserId = envelope.ActorUserId,
        OccurredDate = envelope.OccurredDate,
        Status = OutboxStatus.Pending,
        NextAttemptDate = now,
    };

    public MessageEnvelope ToEnvelope() => new()
    {
        MessageId = Id,
        TenantId = TenantId,
        MessageType = MessageType,
        SchemaVersion = SchemaVersion,
        Payload = Payload,
        CorrelationId = CorrelationId,
        CausationId = CausationId,
        ActorUserId = ActorUserId,
        OccurredDate = OccurredDate,
    };

    public void MarkPublished(DateTime now)
    {
        Status = OutboxStatus.Published;
        PublishedDate = now;
        NextAttemptDate = null;
        LastError = null;
    }

    /// <summary>
    /// Records a failed attempt and schedules the next one with exponential backoff.
    /// <para>
    /// After <paramref name="maxAttempts"/> the row is parked as Failed rather than retried for
    /// ever: a message that cannot be published is usually a broker misconfiguration or a poison
    /// payload, and retrying it every few seconds buries the logs that would reveal which.
    /// </para>
    /// </summary>
    public void MarkFailed(string error, DateTime now, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;

        if (Attempts >= maxAttempts)
        {
            Status = OutboxStatus.Failed;
            NextAttemptDate = null;
            return;
        }

        // 30s, 2m, 8m, 32m … capped so a long outage does not push delivery hours out.
        double delaySeconds = Math.Min(30 * Math.Pow(4, Attempts - 1), 3600);
        NextAttemptDate = now.AddSeconds(delaySeconds);
    }
}

/// <summary>
/// Records that a consumer has already handled a message.
/// <para>
/// Delivery is at least once, so duplicates are normal rather than exceptional: a consumer that
/// commits its work and then fails to acknowledge will see the message again. The primary key is
/// (MessageId, ConsumerName), so two different consumers each handle a message once.
/// </para>
/// </summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
        ConsumerName = null!;
    }

    public Guid MessageId { get; private set; }

    public string ConsumerName { get; private set; }

    public Guid TenantId { get; private set; }

    public DateTime ProcessedDate { get; private set; }

    public static InboxMessage Create(Guid messageId, string consumerName, Guid tenantId, DateTime now) => new()
    {
        MessageId = messageId,
        ConsumerName = consumerName,
        TenantId = tenantId,
        ProcessedDate = now,
    };
}
