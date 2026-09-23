using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Sample.Api.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context at design time.
/// <para>
/// Design-time tooling has no request and therefore no tenant, so it runs in a platform scope. That
/// is also how migrations behave at runtime: they connect as the bypass principal, because they
/// must see and alter every tenant's rows.
/// </para>
/// </summary>
public sealed class SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    public SampleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlServer("Server=localhost;Database=gggs_sample_design;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new SampleDbContext(options, tenantContext);
    }
}
