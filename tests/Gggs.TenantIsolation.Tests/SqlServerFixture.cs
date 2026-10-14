using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.Composition;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace Gggs.TenantIsolation.Tests;

/// <summary>
/// A real SQL Server holding the real PeopleOS schema, for the isolation suite. Row-level security,
/// session context, block predicates and filtered indexes have no in-memory equivalent, so testing
/// them against a fake would prove nothing about production.
/// <para>
/// Resolution order: an explicit connection string (CI), then a locally installed SQL Server, then
/// Docker via Testcontainers. Each run gets its own database, built by the application's own
/// migrations, and drops it afterwards.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "GGGS_TEST_SQL";

    /// <summary>Pinned so a test run does not change behaviour because a tag moved.</summary>
    private const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    private MsSqlContainer? _container;
    private ServiceProvider? _cacheProvider;

    public string DatabaseName { get; } = $"peopleos_isolation_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Which backing server was used, reported in the test output for diagnosis.</summary>
    public string Provider { get; private set; } = "unknown";

    public async ValueTask InitializeAsync()
    {
        string master = await ResolveMasterConnectionStringAsync();

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

        // Migrations run in a platform scope, exactly as they do in deployment.
        await using AppDbContext migrationContext = CreateContext(tenantId: null);
        await migrationContext.Database.MigrateAsync();

        // HybridCache needs a service provider; an in-process one is enough, since these tests are
        // about what is read, not about distributed caching.
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
            SqlConnection.ClearAllPools();
            var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];";
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // A leftover test database is noise, not a failure; the container teardown below wins.
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// The application context for one tenant (or platform scope when null), wired exactly as the
    /// hosts wire it - same model, same interceptors, same filters - so what the tests exercise is
    /// the production path.
    /// </summary>
    public AppDbContext CreateContext(Guid? tenantId = null, ICurrentUser? currentUser = null)
    {
        TenantContext tenantContext = TenantContextFor(tenantId);

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, DataExtensions.ConfigureSqlServer)
            .AddInterceptors(
                new TenantSessionContextInterceptor(tenantContext),
                new TenantAuditSaveChangesInterceptor(
                    tenantContext,
                    currentUser ?? new TestCurrentUser(),
                    new GggsPlatformOptions { ServiceName = "isolation-tests" },
                    TimeProvider.System))
            .Options;

        return new AppDbContext(options, tenantContext, new ModelAssemblies(ModuleCatalog.ModelAssemblies));
    }

    /// <summary>Opens a raw connection with the tenant session context set, as Dapper queries do.</summary>
    public async Task<SqlConnection> OpenRawConnectionAsync(Guid? tenantId)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (tenantId is not null)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";
            command.Parameters.AddWithValue("@tenantId", tenantId.Value);
            await command.ExecuteNonQueryAsync();
        }

        return connection;
    }

    /// <summary>The registry as the application builds it, over its own platform-scope context.</summary>
    public PlatformTenantRegistry CreateRegistry()
    {
        TenantContext tenantContext = TenantContextFor(tenantId: null);

        return new PlatformTenantRegistry(
            CreateContext(tenantId: null),
            new GggsHybridCache(
                _cacheProvider!.GetRequiredService<HybridCache>(),
                tenantContext,
                Options.Create(new GggsCacheOptions { Environment = "tests", ServiceName = "isolation-tests" })),
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
        await using AppDbContext context = CreateContext(tenantId: null);

        Tenant tenant = Tenant.Register(
            code, $"{code} Ltd", "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN", tier, DateTime.UtcNow);

        if (activate)
        {
            tenant.CompleteProvisioning(DateTime.UtcNow);
        }

        context.Set<Tenant>().Add(tenant);

        Plan? plan = null;
        if (withSubscription)
        {
            plan = await context.Set<Plan>().FirstOrDefaultAsync(candidate => candidate.Code == "PRO");

            if (plan is null)
            {
                plan = Plan.Create(
                    "PRO", "Professional", PlanEdition.Professional, 1000, true,
                    ["CoreHr", "Time", "Payroll"]);
                context.Set<Plan>().Add(plan);
            }

            context.Set<Subscription>().Add(Subscription.Start(
                tenant.Id, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, 100, "INV-TEST"));
        }

        await context.SaveChangesAsync();
        return (tenant, plan);
    }

    private static TenantContext TenantContextFor(Guid? tenantId)
    {
        var tenantContext = new TenantContext();

        if (tenantId is null)
        {
            tenantContext.BeginPlatformScope("integration-test-setup");
        }
        else
        {
            tenantContext.SetTenant(tenantId.Value);
        }

        return tenantContext;
    }

    private async Task<string> ResolveMasterConnectionStringAsync()
    {
        // CI sets this to the SQL Server service container.
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Provider = "configured";
            return configured;
        }

        // A developer with SQL Server installed gets the fastest loop, and no container pull.
        if (await CanConnectAsync(LocalSqlServer))
        {
            Provider = "local-sqlserver";
            return LocalSqlServer;
        }

        // Otherwise start one. Probing the Docker pipe is not enough to know the daemon is up:
        // Docker Desktop leaves the named pipe behind when it is stopped.
        try
        {
            _container = new MsSqlBuilder(MsSqlImage).Build();
            await _container.StartAsync();
            Provider = "testcontainers";
            return _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The tenant isolation suite needs SQL Server. Start a local instance, start Docker, " +
                $"or set {ConnectionStringVariable} to a connection string.",
                exception);
        }
    }

    private static async Task<bool> CanConnectAsync(string connectionString)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>A signed-in test user, so audit columns are stamped with a real-looking actor.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid UserId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    public Guid? EmployeeId => null;

    public Guid? SessionId => null;

    public bool IsAuthenticated => true;

    public Guid? ImpersonatorUserId => null;

    public bool IsPlatformStaff => false;

    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "isolation-tests";

    public string? CorrelationId => "isolation-suite";
}

/// <summary>Shares one database across the suite; tests use distinct codes so they do not collide.</summary>
[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
