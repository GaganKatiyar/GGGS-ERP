using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Identity.Infrastructure;

/// <summary>Design-time factory for <c>dotnet ef</c>, running in platform scope as migrations do.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_IDENTITY_DB")
            ?? "Server=localhost;Database=gggs_identity;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new IdentityDbContext(options, tenantContext);
    }
}
