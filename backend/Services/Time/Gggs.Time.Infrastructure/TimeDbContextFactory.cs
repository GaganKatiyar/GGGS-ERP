using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Time.Infrastructure;

/// <summary>Design-time factory for <c>dotnet ef</c>, running in platform scope as migrations do.</summary>
public sealed class TimeDbContextFactory : IDesignTimeDbContextFactory<TimeDbContext>
{
    public TimeDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_TIME_DB")
            ?? "Server=localhost;Database=gggs_time;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<TimeDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new TimeDbContext(options, tenantContext);
    }
}
