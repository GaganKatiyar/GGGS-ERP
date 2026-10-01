using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Spend.Infrastructure;

/// <summary>Design-time factory for <c>dotnet ef</c>, running in platform scope as migrations do.</summary>
public sealed class SpendDbContextFactory : IDesignTimeDbContextFactory<SpendDbContext>
{
    public SpendDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_SPEND_DB")
            ?? "Server=localhost;Database=gggs_spend;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<SpendDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new SpendDbContext(options, tenantContext);
    }
}
