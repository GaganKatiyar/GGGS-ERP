using Azure.Messaging.ServiceBus;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Messaging.Transports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>Wires messaging, picking the transport that matches the deployment profile.</summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Registers the publisher and the transport adapter. Call this in every host: an API stages
    /// messages even when it never runs the relay.
    /// </summary>
    public static IServiceCollection AddGggsMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentProfile profile)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IntegrationEventRegistry>();

        // The publisher writes through the service's own DbContext, so the message and the
        // business change share one transaction.
        services.AddScoped<IIntegrationEventPublisher>(serviceProvider =>
            new OutboxIntegrationEventPublisher(
                serviceProvider.GetRequiredService<TDbContext>(),
                serviceProvider.GetRequiredService<MultiTenancy.ITenantContext>(),
                serviceProvider.GetRequiredService<Application.ICurrentUser>(),
                serviceProvider.GetRequiredService<TimeProvider>()));

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

    /// <summary>
    /// Declares an event this service consumes, and the handler that processes it.
    /// <para>
    /// Registering the type is what makes the subscriber bind to it, so an event with no handler
    /// is simply never delivered here rather than piling up unread.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where TConsumer : class, IIntegrationEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IntegrationEventRegistry>();
        services.AddScoped<IIntegrationEventConsumer<TEvent>, TConsumer>();

        // Resolved immediately so the registry is populated before the consumer starts; a handler
        // registered but not bound would look wired up and receive nothing.
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IntegrationEventRegistry>().Register<TEvent>();

        return services;
    }

    /// <summary>Adds the subscriber that delivers registered events to their handlers.</summary>
    public static IServiceCollection AddGggsEventSubscriber<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<IntegrationEventSubscriber<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Adds the relay and cleanup workers. Worker hosts call this; API hosts usually do not, so
    /// delivery scales on queue depth rather than on request traffic.
    /// </summary>
    public static IServiceCollection AddGggsOutboxRelay<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<OutboxRelay<TDbContext>>();
        services.AddHostedService<OutboxCleanup<TDbContext>>();

        return services;
    }
}
