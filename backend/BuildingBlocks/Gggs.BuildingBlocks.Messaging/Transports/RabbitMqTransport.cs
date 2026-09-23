using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gggs.BuildingBlocks.Messaging.Transports;

/// <summary>
/// On-premise transport: a RabbitMQ topic exchange with a durable queue per consuming service.
/// <para>
/// The same shape as the Service Bus adapter - one exchange, many subscribers, each with its own
/// queue and dead-letter queue - so an event behaves identically on a customer's server and in our
/// cloud. Everything is declared durable: a broker restart must not lose HR events.
/// </para>
/// </summary>
public sealed class RabbitMqTransport(
    IConnection connection,
    IOptions<MessagingOptions> options,
    ILogger<RabbitMqTransport> logger) : IMessageTransport, IAsyncDisposable
{
    private readonly MessagingOptions _options = options.Value;
    private readonly List<IChannel> _channels = [];
    private IChannel? _publishChannel;

    public async Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _publishChannel ??= await CreateExchangeChannelAsync(cancellationToken);

        var properties = new BasicProperties
        {
            MessageId = envelope.MessageId.ToString(),
            ContentType = "application/json",
            // Persistent: an unrouted message must survive a broker restart.
            Persistent = true,
            CorrelationId = envelope.CorrelationId,
            Type = envelope.MessageType,
            Headers = BuildHeaders(envelope),
        };

        await _publishChannel.BasicPublishAsync(
            exchange: _options.TopicName,
            routingKey: envelope.MessageType,
            mandatory: false,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(envelope.Payload),
            cancellationToken: cancellationToken);
    }

    public async Task SubscribeAsync(
        string subscriptionName,
        IReadOnlyCollection<string> messageTypes,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        IChannel channel = await CreateExchangeChannelAsync(cancellationToken);
        string queueName = $"{_options.TopicName}.{subscriptionName}";
        string deadLetterQueue = $"{queueName}.dead-letter";

        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                // A message the handler keeps rejecting lands here instead of spinning forever.
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = deadLetterQueue,
            },
            cancellationToken: cancellationToken);

        foreach (string messageType in messageTypes)
        {
            await channel.QueueBindAsync(queueName, _options.TopicName, messageType, cancellationToken: cancellationToken);
        }

        // One unacknowledged message at a time per consumer keeps ordering predictable and stops a
        // slow consumer from hoarding a batch it cannot process.
        await channel.BasicQosAsync(0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                MessageEnvelope envelope = ToEnvelope(args);
                await handler(envelope, cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Handling {MessageType} from {Queue} failed; dead-lettering it.",
                    args.BasicProperties.Type,
                    queueName);

                // requeue: false sends it to the dead-letter queue rather than straight back into
                // the same queue, which would spin at full speed on a poison message.
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, cancellationToken);
        _channels.Add(channel);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IChannel channel in _channels)
        {
            await channel.DisposeAsync();
        }

        if (_publishChannel is not null)
        {
            await _publishChannel.DisposeAsync();
        }
    }

    private async Task<IChannel> CreateExchangeChannelAsync(CancellationToken cancellationToken)
    {
        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            _options.TopicName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        return channel;
    }

    private static Dictionary<string, object?> BuildHeaders(MessageEnvelope envelope) => new()
    {
        [MessageHeaders.TenantId] = envelope.TenantId.ToString(),
        [MessageHeaders.MessageType] = envelope.MessageType,
        [MessageHeaders.SchemaVersion] = envelope.SchemaVersion.ToString(CultureInfo.InvariantCulture),
        [MessageHeaders.OccurredDate] = envelope.OccurredDate.ToString("O", CultureInfo.InvariantCulture),
        [MessageHeaders.CorrelationId] = envelope.CorrelationId,
        [MessageHeaders.CausationId] = envelope.CausationId?.ToString(),
        [MessageHeaders.ActorUserId] = envelope.ActorUserId?.ToString(),
    };

    private static MessageEnvelope ToEnvelope(BasicDeliverEventArgs args)
    {
        string? tenantHeader = ReadHeader(args, MessageHeaders.TenantId);

        if (!Guid.TryParse(tenantHeader, out Guid tenantId) || tenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Message {args.BasicProperties.MessageId} carries no usable tenant header and cannot be processed.");
        }

        return new MessageEnvelope
        {
            MessageId = Guid.TryParse(args.BasicProperties.MessageId, out Guid id) ? id : Guid.CreateVersion7(),
            TenantId = tenantId,
            MessageType = args.BasicProperties.Type ?? args.RoutingKey,
            SchemaVersion = int.TryParse(ReadHeader(args, MessageHeaders.SchemaVersion), out int version) ? version : 1,
            OccurredDate = DateTime.TryParse(
                ReadHeader(args, MessageHeaders.OccurredDate),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime occurred)
                ? occurred
                : DateTime.UtcNow,
            CorrelationId = args.BasicProperties.CorrelationId,
            Payload = Encoding.UTF8.GetString(args.Body.Span),
        };
    }

    /// <summary>RabbitMQ hands header values back as byte arrays, not strings.</summary>
    private static string? ReadHeader(BasicDeliverEventArgs args, string name)
    {
        if (args.BasicProperties.Headers?.TryGetValue(name, out object? value) != true)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            null => null,
            _ => value.ToString(),
        };
    }
}
