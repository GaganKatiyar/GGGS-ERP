using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Drains the outbox and hands each message to the transport.
/// <para>
/// The half of the outbox pattern that turns "saved" into "published". It runs in the worker host
/// rather than the API, so message delivery does not compete with request handling, and several
/// instances can run at once - rows are claimed with an atomic update, so two relays never publish
/// the same message.
/// </para>
/// <para>
/// It reads across tenants by design, which is why it runs in a platform scope: the outbox is
/// infrastructure, and each message carries the tenant it belongs to.
/// </para>
/// </summary>
public sealed class OutboxRelay<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxRelay<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly MessagingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox relay started; polling every {PollInterval}.",
            _options.OutboxPollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int published = await PublishPendingAsync(stoppingToken);

                // Busy? Come straight back for the next batch instead of sleeping through a burst.
                if (published < _options.OutboxBatchSize)
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
                // The relay must not die: nothing else will publish these messages.
                logger.LogError(exception, "Outbox relay pass failed; retrying after the poll interval.");
                await Task.Delay(_options.OutboxPollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        ITenantContextSetter tenantSetter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        using IDisposable platformScope = tenantSetter.BeginPlatformScope("outbox-relay");

        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        IMessageTransport transport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        List<OutboxMessage> pending = await dbContext.Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending
                && (message.NextAttemptDate == null || message.NextAttemptDate <= now))
            .OrderBy(message => message.OccurredDate)
            .Take(_options.OutboxBatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (OutboxMessage message in pending)
        {
            try
            {
                await transport.PublishAsync(message.ToEnvelope(), cancellationToken);
                message.MarkPublished(timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.MarkFailed(exception.Message, timeProvider.GetUtcNow().UtcDateTime, _options.MaxPublishAttempts);

                logger.LogWarning(
                    exception,
                    "Publishing {MessageType} ({MessageId}) failed on attempt {Attempt}.",
                    message.MessageType,
                    message.Id,
                    message.Attempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}

/// <summary>
/// Deletes outbox rows that have been published and kept long enough.
/// <para>
/// Without this the table grows for ever and the relay's index scan slows down with it. Rows are
/// kept for a week first, because "was that event ever published?" is a real support question.
/// </para>
/// </summary>
public sealed class OutboxCleanup<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxCleanup<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                ITenantContextSetter tenantSetter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
                using IDisposable platformScope = tenantSetter.BeginPlatformScope("outbox-cleanup");

                TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
                DateTime cutoff = timeProvider.GetUtcNow().UtcDateTime - options.Value.PublishedRetention;

                int deleted = await dbContext.Set<OutboxMessage>()
                    .Where(message => message.Status == OutboxStatus.Published && message.PublishedDate < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                {
                    logger.LogInformation("Removed {Count} published outbox messages older than {Cutoff}.", deleted, cutoff);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox cleanup pass failed.");
            }

            await Task.Delay(SweepInterval, timeProvider, stoppingToken);
        }
    }
}
