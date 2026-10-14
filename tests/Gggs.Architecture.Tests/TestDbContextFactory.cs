using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Composition;
using Gggs.Data;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Builds the real application context, from the same module catalog the hosts use, purely to
/// inspect its EF model. No connection is opened, so these rules run in milliseconds on every pull
/// request rather than needing a database.
/// </summary>
internal static class TestDbContextFactory
{
    public static AppDbContext CreateForModelInspection()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none", DataExtensions.ConfigureSqlServer)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        return new AppDbContext(options, tenantContext, new ModelAssemblies(ModuleCatalog.ModelAssemblies));
    }
}
