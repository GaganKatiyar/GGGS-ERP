using System.Text.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Subscribes to the broker and runs handlers.
/// <para>
/// Three things happen before a handler is called, and each exists because of a specific failure:
/// the tenant is restored from the envelope (or the handler would query with no tenant and quietly
/// do nothing), the inbox is checked (or a redelivery would apply the same change twice), and the
/// inbox row is written in the same transaction as the handler's own work (or a crash between them
/// would leave the message marked done having done nothing).
/// </para>
/// </summary>
public sealed class IntegrationEventSubscriber<TDbContext>(
    IMessageTransport transport,
    IntegrationEventRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<IntegrationEventSubscriber<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly MessagingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<string> messageTypes = registry.SubscribedMessageTypes;

        if (messageTypes.Count == 0)
        {
            logger.LogInformation("No integration events are registered; the consumer will not subscribe.");
            return;
        }

        logger.LogInformation(
            "Subscribing to {Count} integration event type(s) as {Subscription}.",
            messageTypes.Count,
            _options.SubscriptionName);

        await transport.SubscribeAsync(
            _options.SubscriptionName,
            messageTypes,
            HandleAsync,
            stoppingToken);
    }

    private async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        Type? eventType = registry.Resolve(envelope.MessageType);

        if (eventType is null)
        {
            // Another service's event. Acknowledged rather than dead-lettered: one topic carries
            // everything, so seeing messages meant for others is normal.
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        // Restored before anything resolves a DbContext, so the handler's queries are scoped to
        // the tenant the message is about.
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(envelope.TenantId);

        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        string consumerName = _options.SubscriptionName;

        bool alreadyHandled = await dbContext.Set<InboxMessage>()
            .AnyAsync(
                message => message.MessageId == envelope.MessageId && message.ConsumerName == consumerName,
                cancellationToken);

        if (alreadyHandled)
        {
            logger.LogInformation(
                "Message {MessageId} ({MessageType}) was already handled; skipping.",
                envelope.MessageId,
                envelope.MessageType);
            return;
        }

        object? integrationEvent = JsonSerializer.Deserialize(
            envelope.Payload,
            eventType,
            OutboxIntegrationEventPublisher.SerializerOptions);

        if (integrationEvent is null)
        {
            // A payload that cannot be read will never become readable, so retrying is pointless;
            // throwing sends it to the dead-letter queue where someone can look at it.
            throw new InvalidOperationException(
                $"Message {envelope.MessageId} of type {envelope.MessageType} could not be deserialized.");
        }

        Type handlerType = typeof(IIntegrationEventConsumer<>).MakeGenericType(eventType);
        IEnumerable<object?> handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (object? handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            await InvokeAsync(handler, handlerType, integrationEvent, cancellationToken);
        }

        dbContext.Set<InboxMessage>().Add(InboxMessage.Create(
            envelope.MessageId,
            consumerName,
            envelope.TenantId,
            timeProvider.GetUtcNow().UtcDateTime));

        // The handler's own changes and the inbox row commit together. Marking a message handled
        // in a separate transaction would let a crash between the two lose the work while
        // recording it as done - the same mistake the outbox exists to prevent on the way out.
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Handled {MessageType} ({MessageId}) for tenant {TenantId}.",
            envelope.MessageType,
            envelope.MessageId,
            envelope.TenantId);
    }

    private static async Task InvokeAsync(
        object handler,
        Type handlerType,
        object integrationEvent,
        CancellationToken cancellationToken)
    {
        System.Reflection.MethodInfo method = handlerType.GetMethod(nameof(IIntegrationEventConsumer<IntegrationEvent>.HandleAsync))!;
        await (Task)method.Invoke(handler, [integrationEvent, cancellationToken])!;
    }
}
