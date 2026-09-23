using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Caching;

/// <summary>Wires the cache. The same code serves both profiles; only the L2 backing differs.</summary>
public static class CachingExtensions
{
    /// <summary>
    /// Adds the tenant-scoped cache. With a Redis connection string it becomes a shared L2 behind
    /// the in-process L1; without one (a single-node on-premise install) it stays in-process, which
    /// is correct there because there is no second instance to disagree with.
    /// </summary>
    public static IServiceCollection AddGggsCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GggsCacheOptions>()
            .Bind(configuration.GetSection(GggsCacheOptions.SectionName))
            .Configure(options => options.ServiceName = serviceName)
            .ValidateOnStart();

        string? redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = $"gggs:{serviceName}:";
            });
        }

        services.AddHybridCache();
        services.AddScoped<IGggsCache, GggsHybridCache>();

        return services;
    }
}
