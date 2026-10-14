using System.Reflection;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.Data;

/// <summary>Registers the shared database for a host.</summary>
public static class DataExtensions
{
    /// <summary>The single connection string every module uses.</summary>
    public const string ConnectionStringName = "AppDb";

    /// <summary>Where migrations live: a separate assembly, because it must reference every module.</summary>
    public const string MigrationsAssembly = "Gggs.Data.Migrations";

    public static IServiceCollection AddGggsAppDbContext(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> modelAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modelAssemblies);

        services.AddSingleton(new ModelAssemblies(modelAssemblies));
        services.AddScoped<TenantAuditSaveChangesInterceptor>();
        services.AddScoped<TenantSessionContextInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            // Read when the context is built, not when it is registered: configuration layered on
            // after registration - an on-premise override file, a container's environment, a test
            // host - must still decide which database this is.
            string connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is required.");

            options.UseSqlServer(connectionString, sql => ConfigureSqlServer(sql));
            options.AddInterceptors(
                serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
                serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<AppDbContext>());

        return services;
    }

    /// <summary>SQL Server options shared by the hosts and the design-time factory, so they cannot drift.</summary>
    public static void ConfigureSqlServer(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        sql.EnableRetryOnFailure();
        sql.MigrationsAssembly(MigrationsAssembly);
        sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.MigrationsHistorySchema);
    }
}
