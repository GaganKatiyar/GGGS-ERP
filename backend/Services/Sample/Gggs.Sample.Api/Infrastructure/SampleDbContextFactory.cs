using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Gggs.Sample.Api.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context at design time.
/// <para>
/// The connection comes from the same configuration the service uses, so "update the database"
/// targets the database you are actually running against rather than a hardcoded one that drifts.
/// </para>
/// <para>
/// Design-time tooling has no request and therefore no tenant, so it runs in a platform scope. That
/// matches runtime: migrations connect as the bypass principal, because they must see and alter
/// every tenant's rows.
/// </para>
/// </summary>
public sealed class SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    public SampleDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString = configuration.GetConnectionString("SampleDb")
            ?? "Server=localhost;Database=gggs_sample;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new SampleDbContext(options, tenantContext);
    }
}
