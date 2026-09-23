namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Something one service tells the others has happened, for example
/// <c>EmployeeHiredIntegrationEvent</c>.
/// <para>
/// These are a published contract, not an internal type: once a consumer exists, changing a
/// property breaks it. Additive changes are safe; anything else is a new version of the event.
/// </para>
/// <para>
/// Payloads stay small and carry ids plus display fields, never a copy of the employee record. A
/// consumer that needs more calls the owning service's API, which applies its own permission and
/// masking rules - putting salary in a broadcast message would hand it to every subscriber.
/// </para>
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Unique per message; the inbox uses it to reject duplicates.</summary>
    public Guid MessageId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredDate { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Schema version, so a consumer can refuse a message it does not understand rather than
    /// silently mapping it onto the wrong shape.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>
/// The delivery envelope: routing and context that every message carries, whatever its payload.
/// </summary>
public sealed record MessageEnvelope
{
    public required Guid MessageId { get; init; }

    /// <summary>
    /// The tenant the message belongs to. Every message has one - including provisioning messages,
    /// which name the tenant being provisioned. A consumer receiving none rejects the message
    /// rather than guessing, exactly as background jobs do.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>Stable name used for routing, e.g. <c>gggs.corehr.employee.hired</c>.</summary>
    public required string MessageType { get; init; }

    public required int SchemaVersion { get; init; }

    public required DateTime OccurredDate { get; init; }

    /// <summary>Ties the message to the request that caused it, through to its audit rows.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The message this one was produced in response to, for tracing a chain of events.</summary>
    public Guid? CausationId { get; init; }

    /// <summary>User whose action produced the event, or the system identity for background work.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>JSON payload of the event itself.</summary>
    public required string Payload { get; init; }
}

/// <summary>Header names shared by both transports, so a message written by one is readable by the other.</summary>
public static class MessageHeaders
{
    public const string TenantId = "gggs-tenant-id";
    public const string MessageType = "gggs-message-type";
    public const string SchemaVersion = "gggs-schema-version";
    public const string CorrelationId = "gggs-correlation-id";
    public const string CausationId = "gggs-causation-id";
    public const string ActorUserId = "gggs-actor-user-id";
    public const string OccurredDate = "gggs-occurred-date";
}
