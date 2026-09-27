namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Handles one integration event.
/// <para>
/// Handlers must be idempotent. Delivery is at least once, so the same message will sometimes
/// arrive twice - after a broker redelivery, or when an operator replays a dead-lettered batch.
/// The inbox rejects exact duplicates, but a handler that would double-post if it ran twice is
/// still wrong: the inbox is a safety net, not the design.
/// </para>
/// <para>
/// The tenant is already established when a handler runs. It does not need to read the envelope to
/// find out who the message is about.
/// </para>
/// </summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registers which event types a service consumes, so the subscriber knows what to bind and the
/// dispatcher knows what to deserialize into.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, Type> _byMessageType = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> SubscribedMessageTypes => _byMessageType.Keys;

    public IntegrationEventRegistry Register<TEvent>()
        where TEvent : IntegrationEvent
    {
        _byMessageType[MessageTypeName.For<TEvent>()] = typeof(TEvent);
        return this;
    }

    /// <summary>
    /// The CLR type for a wire name, or null when this service does not consume it.
    /// <para>
    /// An unknown type is not an error: a topic carries every service's events, and each subscriber
    /// sees messages meant for others.
    /// </para>
    /// </summary>
    public Type? Resolve(string messageType) => _byMessageType.GetValueOrDefault(messageType);
}
