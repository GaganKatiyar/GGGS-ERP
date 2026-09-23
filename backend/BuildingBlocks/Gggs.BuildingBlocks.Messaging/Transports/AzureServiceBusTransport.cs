using System.Globalization;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Messaging.Transports;

/// <summary>
/// Cloud transport: Azure Service Bus topics with a subscription per consuming service.
/// <para>
/// Topics rather than queues, because an event usually interests more than one service - an
/// employee transfer reaches Time, Payroll and Reporting - and each needs its own copy and its own
/// dead-letter queue.
/// </para>
/// </summary>
public sealed class AzureServiceBusTransport(
    ServiceBusClient client,
    IOptions<MessagingOptions> options,
    ILogger<AzureServiceBusTransport> logger) : IMessageTransport, IAsyncDisposable
{
    private readonly MessagingOptions _options = options.Value;
    private readonly List<ServiceBusProcessor> _processors = [];
    private ServiceBusSender? _sender;

    public async Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _sender ??= client.CreateSender(_options.TopicName);

        var message = new ServiceBusMessage(envelope.Payload)
        {
            MessageId = envelope.MessageId.ToString(),
            Subject = envelope.MessageType,
            ContentType = "application/json",
            // Correlation flows into Service Bus' own diagnostics as well as our headers.
            CorrelationId = envelope.CorrelationId,
        };

        ApplyHeaders(message.ApplicationProperties, envelope);

        await _sender.SendMessageAsync(message, cancellationToken);
    }

    public async Task SubscribeAsync(
        string subscriptionName,
        IReadOnlyCollection<string> messageTypes,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ServiceBusProcessor processor = client.CreateProcessor(
            _options.TopicName,
            subscriptionName,
            new ServiceBusProcessorOptions
            {
                // Manual completion: the message is only acknowledged once the handler has
                // committed, so a crash mid-handler redelivers rather than silently dropping.
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 4,
            });

        processor.ProcessMessageAsync += async args =>
        {
            string messageType = args.Message.Subject;

            if (messageTypes.Count > 0 && !messageTypes.Contains(messageType))
            {
                // Not ours: complete it so it does not cycle through the subscription forever.
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            MessageEnvelope envelope = ToEnvelope(args.Message);
            await handler(envelope, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(
                args.Exception,
                "Service Bus processor error on {EntityPath} during {Operation}.",
                args.EntityPath,
                args.ErrorSource);
            return Task.CompletedTask;
        };

        _processors.Add(processor);
        await processor.StartProcessingAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ServiceBusProcessor processor in _processors)
        {
            await processor.DisposeAsync();
        }

        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }
    }

    private static void ApplyHeaders(IDictionary<string, object> properties, MessageEnvelope envelope)
    {
        properties[MessageHeaders.TenantId] = envelope.TenantId.ToString();
        properties[MessageHeaders.MessageType] = envelope.MessageType;
        properties[MessageHeaders.SchemaVersion] = envelope.SchemaVersion;
        properties[MessageHeaders.OccurredDate] = envelope.OccurredDate.ToString("O", CultureInfo.InvariantCulture);

        if (envelope.CorrelationId is not null)
        {
            properties[MessageHeaders.CorrelationId] = envelope.CorrelationId;
        }

        if (envelope.CausationId is not null)
        {
            properties[MessageHeaders.CausationId] = envelope.CausationId.Value.ToString();
        }

        if (envelope.ActorUserId is not null)
        {
            properties[MessageHeaders.ActorUserId] = envelope.ActorUserId.Value.ToString();
        }
    }

    private static MessageEnvelope ToEnvelope(ServiceBusReceivedMessage message)
    {
        string? tenantHeader = message.ApplicationProperties
            .GetValueOrDefault(MessageHeaders.TenantId)?.ToString();

        if (!Guid.TryParse(tenantHeader, out Guid tenantId) || tenantId == Guid.Empty)
        {
            // Refusing beats guessing: a message with no tenant could be applied to the wrong one.
            throw new InvalidOperationException(
                $"Message {message.MessageId} carries no usable tenant header and cannot be processed.");
        }

        return new MessageEnvelope
        {
            MessageId = Guid.TryParse(message.MessageId, out Guid id) ? id : Guid.CreateVersion7(),
            TenantId = tenantId,
            MessageType = message.Subject,
            SchemaVersion = Convert.ToInt32(
                message.ApplicationProperties.GetValueOrDefault(MessageHeaders.SchemaVersion) ?? 1,
                CultureInfo.InvariantCulture),
            OccurredDate = message.EnqueuedTime.UtcDateTime,
            CorrelationId = message.CorrelationId,
            Payload = Encoding.UTF8.GetString(message.Body),
        };
    }
}
