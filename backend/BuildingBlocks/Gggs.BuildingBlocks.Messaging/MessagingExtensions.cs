using Azure.Messaging.ServiceBus;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging.Transports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>
/// Wires integration events.
/// <para>
/// In the modular monolith (ADR-12) delivery is in-process: <see cref="AddGggsMessaging{TDbContext}"/>
/// in both hosts, <see cref="AddGggsOutboxDispatcher{TDbContext}"/> in the worker. The broker
/// transports remain available through <see cref="AddGggsBrokerTransport"/> for events that must
/// leave the process - external integrations - and are not registered by default.
/// </para>
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Registers the outbox publisher and the consumer registry. Call it in every host: the API
    /// stages messages even though only the worker delivers them.
    /// </summary>
    public static IServiceCollection AddGggsMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IntegrationEventRegistry>();

        // The publisher writes through the shared DbContext, so the message and the business change
        // share one transaction.
        services.AddScoped<IIntegrationEventPublisher>(serviceProvider =>
            new OutboxIntegrationEventPublisher(
                serviceProvider.GetRequiredService<TDbContext>(),
                serviceProvider.GetRequiredService<MultiTenancy.ITenantContext>(),
                serviceProvider.GetRequiredService<Application.ICurrentUser>(),
                serviceProvider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Declares an event this host consumes, and the handler that processes it.
    /// <para>
    /// Several handlers may consume the same event; each is delivered to, and deduplicated,
    /// independently.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where TConsumer : class, IIntegrationEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new IntegrationEventRegistration(typeof(TEvent)));
        services.AddScoped<IIntegrationEventConsumer<TEvent>, TConsumer>();

        return services;
    }

    /// <summary>
    /// Adds in-process delivery and outbox cleanup. Worker host only, so delivery does not compete
    /// with request handling.
    /// </summary>
    public static IServiceCollection AddGggsOutboxDispatcher<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OutboxDispatcher<TDbContext>>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<OutboxDispatcher<TDbContext>>());
        services.AddHostedService<OutboxCleanup<TDbContext>>();

        return services;
    }

    /// <summary>
    /// Registers the broker adapter for the deployment profile: RabbitMQ on-premise, Service Bus in
    /// the cloud (ADR-06, ADR-11). Only for events that leave the process.
    /// </summary>
    public static IServiceCollection AddGggsBrokerTransport(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (profile == DeploymentProfile.OnPremise)
        {
            services.AddSingleton<IConnection>(_ =>
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration.GetConnectionString("RabbitMq")
                        ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is required on-premise.")),
                    // The client recovers from a broker restart on its own; on a customer's server
                    // nobody is watching to restart our process for us.
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                };

                return factory.CreateConnectionAsync().GetAwaiter().GetResult();
            });

            services.AddSingleton<IMessageTransport, RabbitMqTransport>();
            return services;
        }

        services.AddSingleton(_ =>
        {
            string? connectionString = configuration.GetConnectionString("ServiceBus");

            return string.IsNullOrWhiteSpace(connectionString)
                ? new ServiceBusClient(
                    configuration["Gggs:Messaging:Namespace"]
                        ?? throw new InvalidOperationException("Gggs:Messaging:Namespace is required for the cloud profile."),
                    new Azure.Identity.DefaultAzureCredential())
                : new ServiceBusClient(connectionString);
        });

        services.AddSingleton<IMessageTransport, AzureServiceBusTransport>();
        return services;
    }

    /// <summary>Adds the broker subscriber. Requires <see cref="AddGggsBrokerTransport"/>.</summary>
    public static IServiceCollection AddGggsEventSubscriber<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<IntegrationEventSubscriber<TDbContext>>();
        return services;
    }

    /// <summary>Adds the broker relay and cleanup. Requires <see cref="AddGggsBrokerTransport"/>.</summary>
    public static IServiceCollection AddGggsOutboxRelay<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<OutboxRelay<TDbContext>>();
        services.AddHostedService<OutboxCleanup<TDbContext>>();

        return services;
    }
}
