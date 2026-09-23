using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gggs.TenantIsolation.Tests;

/// <summary>A real platform database for the registry tests, torn down afterwards.</summary>
public sealed class PlatformFixture : IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private ServiceProvider? _cacheProvider;

    public string DatabaseName { get; } =
        $"gggs_platform_tests_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        string master = Environment.GetEnvironmentVariable("GGGS_TEST_SQL") ?? LocalSqlServer;

        var builder = new SqlConnectionStringBuilder(master) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}];";
            await command.ExecuteNonQueryAsync();
        }

        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;

        await using PlatformDbContext migrationContext = CreateContext();
        await migrationContext.Database.MigrateAsync();

        // HybridCache needs a service provider; an in-process one is enough, since these tests are
        // about what the registry reads, not about distributed caching.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _cacheProvider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        _cacheProvider?.Dispose();

        try
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // A leftover test database is noise, not a failure.
        }
    }

    /// <summary>
    /// A context in platform scope by default, because most of this database is platform-owned.
    /// Pass a tenant id to exercise the tenant-owned settings table.
    /// </summary>
    public PlatformDbContext CreateContext(Guid? tenantId = null)
    {
        var tenantContext = new TenantContext();
        if (tenantId is null)
        {
            tenantContext.BeginPlatformScope("platform-tests");
        }
        else
        {
            tenantContext.SetTenant(tenantId.Value);
        }

        DbContextOptions<PlatformDbContext> options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(
                new TenantSessionContextInterceptor(tenantContext),
                new TenantAuditSaveChangesInterceptor(tenantContext, new TestUser(), TimeProvider.System))
            .Options;

        return new PlatformDbContext(options, tenantContext);
    }

    public PlatformTenantRegistry CreateRegistry()
    {
        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("platform-tests");

        return new PlatformTenantRegistry(
            CreateContext(),
            new GggsHybridCache(
                _cacheProvider!.GetRequiredService<HybridCache>(),
                tenantContext,
                Options.Create(new GggsCacheOptions { Environment = "tests", ServiceName = "platform" })),
            TimeProvider.System,
            NullLogger<PlatformTenantRegistry>.Instance);
    }

    /// <summary>Creates a tenant, optionally activated and optionally with a live subscription.</summary>
    public async Task<(Tenant Tenant, Plan? Plan)> SeedTenantAsync(
        string code,
        bool activate,
        bool withSubscription,
        TenantIsolationTier tier = TenantIsolationTier.Pooled)
    {
        await using PlatformDbContext context = CreateContext();

        Tenant tenant = Tenant.Register(
            code, $"{code} Ltd", "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN", tier, DateTime.UtcNow);

        if (activate)
        {
            tenant.CompleteProvisioning(DateTime.UtcNow);
        }

        context.Tenants.Add(tenant);

        Plan? plan = null;
        if (withSubscription)
        {
            plan = await context.Plans.FirstOrDefaultAsync(candidate => candidate.Code == "PRO");

            if (plan is null)
            {
                plan = Plan.Create(
                    "PRO", "Professional", PlanEdition.Professional, 1000, true,
                    ["CoreHr", "Attendance", "Leave", "Payroll"]);
                context.Plans.Add(plan);
            }

            context.Subscriptions.Add(Subscription.Start(
                tenant.Id, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, 100, "INV-TEST"));
        }

        await context.SaveChangesAsync();
        return (tenant, plan);
    }

    private sealed class TestUser : ICurrentUser
    {
        public Guid UserId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

        public Guid? EmployeeId => null;

        public Guid? SessionId => null;

        public bool IsAuthenticated => true;

        public Guid? ImpersonatorUserId => null;

        public string? IpAddress => "127.0.0.1";

        public string? UserAgent => "platform-tests";

        public string? CorrelationId => "platform-suite";
    }
}

/// <summary>Shares one platform database across these tests; each uses its own tenant codes.</summary>
[CollectionDefinition(nameof(PlatformCollection))]
public sealed class PlatformCollection : ICollectionFixture<PlatformFixture>;
