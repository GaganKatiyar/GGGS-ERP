using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>Wires Hangfire so background work behaves like the rest of the platform.</summary>
public static class JobsExtensions
{
    /// <summary>
    /// Registers Hangfire with SQL Server storage, the tenant-aware activator and the client filter
    /// that stamps the tenant onto every job.
    /// <para>
    /// SQL Server storage rather than Redis: the schedule is operational data that has to survive a
    /// cache flush, and on-premise installs already have SQL Server but may not have Redis.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "JobsDb")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is required for background jobs.");

        services.AddHangfire((serviceProvider, options) =>
        {
            options
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    // Queue polling is a fallback; the sliding invisibility timeout is what governs
                    // how quickly an abandoned job is picked up again after a worker dies.
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true,
                })
                .UseFilter(new TenantJobFilter(serviceProvider.GetRequiredService<IServiceScopeFactory>()))
                // Retries are per job so a transient database blip does not lose a payroll run;
                // after that it goes to the failed list for an operator to look at.
                .UseFilter(new AutomaticRetryAttribute { Attempts = 5, DelaysInSeconds = [60, 300, 900, 3600, 7200] });
        });

        services.AddSingleton<JobActivator, TenantAwareJobActivator>();
        services.AddScoped<ITenantJobScheduler, HangfireTenantJobScheduler>();
        services.AddScoped<ITenantFanOutScheduler, TenantFanOutScheduler>();

        return services;
    }

    /// <summary>
    /// Adds the worker that actually executes jobs. API hosts usually skip this: queuing and
    /// running are separate containers so they scale on different signals.
    /// </summary>
    public static IServiceCollection AddGggsJobServer(this IServiceCollection services, int workerCount = 0)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHangfireServer(options =>
        {
            if (workerCount > 0)
            {
                options.WorkerCount = workerCount;
            }

            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
