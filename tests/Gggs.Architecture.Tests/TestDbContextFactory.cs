using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Builds a context purely to inspect the EF model. No connection is opened, so these rules run in
/// milliseconds on every pull request rather than needing a database.
/// </summary>
internal static class TestDbContextFactory
{
    public static SampleDbContext CreateForModelInspection()
    {
        DbContextOptions<SampleDbContext> options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        return new SampleDbContext(options, tenantContext);
    }
}
