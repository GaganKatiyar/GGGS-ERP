using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Data.Migrations;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>, built from the same module catalog the hosts use so a
/// migration always describes the database the application actually expects. Runs in platform
/// scope, as migrations do.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_APP_DB")
            ?? "Server=localhost;Database=peopleos;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, DataExtensions.ConfigureSqlServer)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new AppDbContext(options, tenantContext, new ModelAssemblies(ModuleCatalog.ModelAssemblies));
    }
}
