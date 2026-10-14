using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Publishes an integration event.
/// <para>
/// The only messaging type application code ever names. It does not send anything: it stages the
/// message, and the caller's own <c>SaveChanges</c> commits the business change and the message
/// together. Swapping the transport - or adopting a messaging framework later - happens behind
/// this interface without touching a handler.
/// </para>
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Stages an event for publication. It is sent after the current transaction commits; if the
    /// transaction rolls back, the message goes with it and no consumer ever sees an event for
    /// something that did not happen.
    /// </summary>
    void Publish<TEvent>(TEvent integrationEvent, Guid? causationId = null)
        where TEvent : IntegrationEvent;

    /// <summary>
    /// Stages an event about a named tenant from platform code that runs with no tenant of its own -
    /// provisioning, suspension, offboarding. Only valid inside a platform scope: tenant code always
    /// publishes for its own tenant through <see cref="Publish{TEvent}"/>.
    /// </summary>
    void PublishForTenant<TEvent>(Guid tenantId, TEvent integrationEvent, Guid? causationId = null)
        where TEvent : IntegrationEvent;
}

/// <summary>
/// Writes staged events into the service's own outbox table.
/// <para>
/// Nothing is sent here, deliberately. Publishing inside a handler is the mistake this design
/// exists to prevent: it either loses messages when the process dies before the commit, or emits
/// messages for work that then rolls back. Both are worse than a few seconds of delivery latency.
/// </para>
/// </summary>
public sealed class OutboxIntegrationEventPublisher(
    DbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IIntegrationEventPublisher
{
    /// <summary>
    /// Web defaults, so payloads are camelCase like the APIs. Fixed rather than configurable: the
    /// serialized shape is part of the contract between services.
    /// <para>
    /// Public rather than internal: <c>TenantAuditSaveChangesInterceptor</c> (a different
    /// assembly) serializes <c>AuditEntryIntegrationEvent</c> the same way, and the two must never
    /// drift - one writes messages this same shape has to deserialize back on the way out.
    /// </para>
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public void Publish<TEvent>(TEvent integrationEvent, Guid? causationId = null)
        where TEvent : IntegrationEvent
        => Stage(tenantContext.TenantId, integrationEvent, causationId);

    public void PublishForTenant<TEvent>(Guid tenantId, TEvent integrationEvent, Guid? causationId = null)
        where TEvent : IntegrationEvent
    {
        if (!tenantContext.IsPlatformScope)
        {
            // A tenant publishing on another tenant's behalf is exactly the crossing the outbox's
            // TenantId exists to prevent.
            throw new InvalidOperationException("PublishForTenant is only valid inside a platform scope.");
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        Stage(tenantId, integrationEvent, causationId);
    }

    private void Stage<TEvent>(Guid tenantId, TEvent integrationEvent, Guid? causationId)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var envelope = new MessageEnvelope
        {
            MessageId = integrationEvent.MessageId,
            TenantId = tenantId,
            MessageType = MessageTypeName.For<TEvent>(),
            SchemaVersion = integrationEvent.SchemaVersion,
            OccurredDate = integrationEvent.OccurredDate,
            CorrelationId = currentUser.CorrelationId,
            CausationId = causationId,
            ActorUserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
            Payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions),
        };

        dbContext.Set<OutboxMessage>().Add(
            OutboxMessage.Create(envelope, timeProvider.GetUtcNow().UtcDateTime));
    }
}

/// <summary>
/// Turns an event type into the stable name used for routing and stored in the outbox.
/// <para>
/// A CLR type name would tie the wire contract to our namespaces, so renaming a class or moving it
/// between projects would break every consumer. Events declare their own name instead.
/// </para>
/// </summary>
public static class MessageTypeName
{
    public static string For<TEvent>() => For(typeof(TEvent));

    public static string For(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        MessageTypeAttribute? attribute = (MessageTypeAttribute?)Attribute.GetCustomAttribute(
            eventType,
            typeof(MessageTypeAttribute));

        return attribute?.Name
            ?? throw new InvalidOperationException(
                $"Integration event '{eventType.Name}' must declare [MessageType(\"gggs.<service>.<entity>.<event>\")]. " +
                "The name is a published contract and cannot be derived from the class name.");
    }
}

/// <summary>Declares the wire name of an integration event, e.g. <c>gggs.corehr.employee.hired</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageTypeAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Message type name is required.", nameof(name))
        : name;
}
