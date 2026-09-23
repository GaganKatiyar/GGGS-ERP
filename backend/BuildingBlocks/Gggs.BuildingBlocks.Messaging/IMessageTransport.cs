namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Moves a message to the broker.
/// <para>
/// The only place a broker SDK appears. Azure Service Bus in the cloud, RabbitMQ on-premise, and
/// nothing above this line knows which (ADR-06, ADR-11).
/// </para>
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Publishes one message. Throwing is expected and safe: the outbox row stays pending and the
    /// relay retries with backoff, so a broker outage delays delivery rather than losing it.
    /// </summary>
    Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to messages, invoking <paramref name="handler"/> for each. The handler must
    /// complete before the message is acknowledged; if it throws, the message is retried and
    /// eventually dead-lettered by the broker.
    /// </summary>
    Task SubscribeAsync(
        string subscriptionName,
        IReadOnlyCollection<string> messageTypes,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}

/// <summary>Broker settings shared by both adapters.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Gggs:Messaging";

    /// <summary>Topic or exchange every integration event is published to.</summary>
    public string TopicName { get; set; } = "gggs-events";

    /// <summary>How many outbox rows the relay drains per pass.</summary>
    public int OutboxBatchSize { get; set; } = 100;

    /// <summary>How often the relay looks for pending messages.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Attempts before a message is parked for an operator to look at.</summary>
    public int MaxPublishAttempts { get; set; } = 8;

    /// <summary>Published rows kept this long, so a delivery question can be answered after the fact.</summary>
    public TimeSpan PublishedRetention { get; set; } = TimeSpan.FromDays(7);
}
