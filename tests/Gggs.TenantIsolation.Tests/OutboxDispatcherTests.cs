using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Composition;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.TenantIsolation.Tests;

[MessageType("gggs.tests.dispatch.probe")]
public sealed record DispatchProbeIntegrationEvent(string RoleName) : IntegrationEvent;

/// <summary>Writes a role named in the event, in the event's tenant - a consumer with a visible side effect.</summary>
public sealed class DispatchProbeConsumer(AppDbContext dbContext) : IIntegrationEventConsumer<DispatchProbeIntegrationEvent>
{
    public Task HandleAsync(DispatchProbeIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        dbContext.Set<Role>().Add(Role.Create(integrationEvent.RoleName, "Created by the dispatcher probe"));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The in-process outbox dispatcher (ADR-12) against a real database: what the old broker relay and
/// subscriber guaranteed between two services must still hold between two modules.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class OutboxDispatcherTests(SqlServerFixture fixture)
{
    private static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task APendingMessage_IsDeliveredToItsConsumerInTheMessagesTenant_ExactlyOnce()
    {
        await using ServiceProvider provider = BuildWorker();

        await using (AsyncServiceScope publishScope = provider.CreateAsyncScope())
        {
            publishScope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(AcmeTenantId);
            publishScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .Publish(new DispatchProbeIntegrationEvent("DISPATCHED-ROLE"));
            await publishScope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        OutboxDispatcher<AppDbContext> dispatcher = provider.GetRequiredService<OutboxDispatcher<AppDbContext>>();
        await dispatcher.DispatchPendingAsync(TestContext.Current.CancellationToken);

        // Run again: the message is already marked published, and even if it were redelivered the
        // inbox would stop the consumer running twice.
        await dispatcher.DispatchPendingAsync(TestContext.Current.CancellationToken);

        await using AppDbContext verify = fixture.CreateContext(AcmeTenantId);

        (await verify.Set<Role>().CountAsync(role => role.Name == "DISPATCHED-ROLE", TestContext.Current.CancellationToken))
            .ShouldBe(1, "the consumer ran once, in the message's tenant");

        OutboxMessage message = await verify.Set<OutboxMessage>()
            .SingleAsync(candidate => candidate.MessageType == "gggs.tests.dispatch.probe", TestContext.Current.CancellationToken);
        message.Status.ShouldBe(OutboxStatus.Published);

        (await verify.Set<InboxMessage>().CountAsync(
                inbox => inbox.MessageId == message.Id && inbox.ConsumerName == typeof(DispatchProbeConsumer).FullName,
                TestContext.Current.CancellationToken))
            .ShouldBe(1);
    }

    private ServiceProvider BuildWorker()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DataExtensions.ConnectionStringName}"] = fixture.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GggsPlatformOptions { ServiceName = "dispatcher-tests" });
        services.AddScoped<ICurrentUser, TestCurrentUser>();
        services.AddGggsMultiTenancy();
        services.AddGggsAppDbContext(configuration, ModuleCatalog.ModelAssemblies);
        services.AddGggsMessaging<AppDbContext>(configuration);
        services.AddGggsEventConsumer<DispatchProbeIntegrationEvent, DispatchProbeConsumer>();
        services.AddSingleton<OutboxDispatcher<AppDbContext>>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
