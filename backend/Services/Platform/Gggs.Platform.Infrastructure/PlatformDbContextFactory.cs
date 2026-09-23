using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Platform.Infrastructure;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>.
/// <para>
/// Runs in a platform scope, matching how migrations behave at runtime: they connect as the bypass
/// principal because they must see and alter every tenant's rows.
/// </para>
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_PLATFORM_DB")
            ?? "Server=localhost;Database=gggs_platform;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new PlatformDbContext(options, tenantContext);
    }
}
