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

/// <summary>One consumed event type, as declared by <see cref="MessagingExtensions.AddGggsEventConsumer{TEvent, TConsumer}"/>.</summary>
public sealed record IntegrationEventRegistration(Type EventType);

/// <summary>
/// Which event types this host consumes, so the dispatcher (or broker subscriber) knows what to bind
/// and what to deserialize into.
/// <para>
/// Built from the <see cref="IntegrationEventRegistration"/>s in the container, so declaring a
/// consumer is the only step: there is no separate list to keep in step with the handlers.
/// </para>
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, Type> _byMessageType = new(StringComparer.Ordinal);

    public IntegrationEventRegistry(IEnumerable<IntegrationEventRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (IntegrationEventRegistration registration in registrations)
        {
            _byMessageType[MessageTypeName.For(registration.EventType)] = registration.EventType;
        }
    }

    public IReadOnlyCollection<string> SubscribedMessageTypes => _byMessageType.Keys;

    /// <summary>
    /// The CLR type for a wire name, or null when nothing in this host consumes it.
    /// </summary>
    public Type? Resolve(string messageType) => _byMessageType.GetValueOrDefault(messageType);
}
