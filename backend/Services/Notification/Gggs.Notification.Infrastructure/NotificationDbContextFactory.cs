using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Notification.Infrastructure;

/// <summary>Design-time factory for <c>dotnet ef</c>, running in platform scope as migrations do.</summary>
public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_NOTIFICATION_DB")
            ?? "Server=localhost;Database=gggs_notification;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new NotificationDbContext(options, tenantContext);
    }
}
