using System.Text.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Delivers outbox messages to their consumers inside the same process (ADR-12).
/// <para>
/// In the modular monolith every publisher and every consumer share one database, so there is no
/// broker to hand a message to: the dispatcher reads pending rows and calls the registered
/// <see cref="IIntegrationEventConsumer{TEvent}"/> implementations directly. Everything the outbox
/// pattern guarantees still holds - "saved" still implies "eventually delivered", because the row
/// was written in the publisher's own transaction, and a crash anywhere below simply leaves the row
/// pending for the next pass.
/// </para>
/// <para>
/// Each consumer runs in its own scope and its own transaction, together with its inbox row. One
/// consumer failing therefore neither rolls back nor repeats another's work: the message stays
/// pending, is retried with backoff, and consumers that already succeeded are skipped by the inbox.
/// </para>
/// </summary>
public sealed class OutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IntegrationEventRegistry registry,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly MessagingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox dispatcher started for {Count} event type(s); polling every {PollInterval}.",
            registry.SubscribedMessageTypes.Count,
            _options.OutboxPollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int processed = await DispatchPendingAsync(stoppingToken);

                // A full batch means more is probably waiting; only sleep once the backlog is drained.
                if (processed < _options.OutboxBatchSize)
                {
                    await Task.Delay(_options.OutboxPollInterval, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Must not die: nothing else delivers these messages.
                logger.LogError(exception, "Outbox dispatch pass failed; retrying after the poll interval.");
                await Task.Delay(_options.OutboxPollInterval, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>Runs one pass over due messages. Public so tests can drive delivery deterministically.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        // The outbox is infrastructure and spans tenants; each message carries its own tenant.
        using IDisposable platformScope = scope.ServiceProvider
            .GetRequiredService<ITenantContextSetter>()
            .BeginPlatformScope("outbox-dispatcher");

        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        List<OutboxMessage> pending = await dbContext.Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending
                && (message.NextAttemptDate == null || message.NextAttemptDate <= now))
            .OrderBy(message => message.OccurredDate)
            .Take(_options.OutboxBatchSize)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage message in pending)
        {
            try
            {
                await DeliverAsync(message.ToEnvelope(), cancellationToken);
                message.MarkPublished(timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.MarkFailed(exception.Message, timeProvider.GetUtcNow().UtcDateTime, _options.MaxPublishAttempts);

                logger.LogWarning(
                    exception,
                    "Delivering {MessageType} ({MessageId}) failed on attempt {Attempt}.",
                    message.MessageType,
                    message.Id,
                    message.Attempts);
            }
        }

        if (pending.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }

    private async Task DeliverAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        Type? eventType = registry.Resolve(envelope.MessageType);

        if (eventType is null)
        {
            // Nobody consumes it (yet). Marked delivered rather than left pending: a consumer added
            // later starts from the events raised after it exists, the same as a new subscription.
            return;
        }

        object integrationEvent = JsonSerializer.Deserialize(
                envelope.Payload,
                eventType,
                OutboxIntegrationEventPublisher.SerializerOptions)
            ?? throw new InvalidOperationException(
                $"Message {envelope.MessageId} of type {envelope.MessageType} could not be deserialized.");

        Type handlerType = typeof(IIntegrationEventConsumer<>).MakeGenericType(eventType);

        // Resolved once only to learn which concrete consumers exist; each is then run in a fresh
        // scope of its own so their DbContexts, and their failures, stay apart.
        List<Type> consumerTypes;
        await using (AsyncServiceScope probe = scopeFactory.CreateAsyncScope())
        {
            probe.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(envelope.TenantId);
            consumerTypes = [.. probe.ServiceProvider.GetServices(handlerType).OfType<object>().Select(handler => handler.GetType())];
        }

        foreach (Type consumerType in consumerTypes)
        {
            await DeliverToConsumerAsync(envelope, integrationEvent, handlerType, consumerType, cancellationToken);
        }
    }

    private async Task DeliverToConsumerAsync(
        MessageEnvelope envelope,
        object integrationEvent,
        Type handlerType,
        Type consumerType,
        CancellationToken cancellationToken)
    {
        string consumerName = consumerType.FullName ?? consumerType.Name;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        // Restored before anything resolves a DbContext, so the consumer's queries are scoped to the
        // tenant the message is about.
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(envelope.TenantId);

        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        bool alreadyHandled = await dbContext.Set<InboxMessage>()
            .AnyAsync(
                message => message.MessageId == envelope.MessageId && message.ConsumerName == consumerName,
                cancellationToken);

        if (alreadyHandled)
        {
            return;
        }

        object consumer = scope.ServiceProvider.GetServices(handlerType)
            .OfType<object>()
            .First(candidate => candidate.GetType() == consumerType);

        // The consumer's own writes and its inbox row commit together; a consumer that saves part
        // way through is still inside this transaction.
        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(ct);

            System.Reflection.MethodInfo handle =
                handlerType.GetMethod(nameof(IIntegrationEventConsumer<IntegrationEvent>.HandleAsync))!;
            await (Task)handle.Invoke(consumer, [integrationEvent, ct])!;

            dbContext.Set<InboxMessage>().Add(InboxMessage.Create(
                envelope.MessageId,
                consumerName,
                envelope.TenantId,
                timeProvider.GetUtcNow().UtcDateTime));

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);

        logger.LogInformation(
            "Delivered {MessageType} ({MessageId}) to {Consumer} for tenant {TenantId}.",
            envelope.MessageType,
            envelope.MessageId,
            consumerName,
            envelope.TenantId);
    }
}
