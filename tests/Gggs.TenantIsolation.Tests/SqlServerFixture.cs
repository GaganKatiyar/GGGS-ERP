using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Gggs.TenantIsolation.Tests;

/// <summary>
/// A real SQL Server for the isolation suite. Row-level security, session context, block predicates
/// and filtered indexes have no in-memory equivalent, so testing them against a fake would prove
/// nothing about production.
/// <para>
/// Resolution order: an explicit connection string (CI), then Docker via Testcontainers, then a
/// locally installed SQL Server. Each run gets its own database and drops it afterwards.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "GGGS_TEST_SQL";

    /// <summary>Pinned so a test run does not change behaviour because a tag moved.</summary>
    private const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? _container;
    private string _masterConnectionString = string.Empty;

    public string DatabaseName { get; } = $"gggs_isolation_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Which backing server was used, reported in the test output for diagnosis.</summary>
    public string Provider { get; private set; } = "unknown";

    public async ValueTask InitializeAsync()
    {
        _masterConnectionString = await ResolveMasterConnectionStringAsync();

        var builder = new SqlConnectionStringBuilder(_masterConnectionString) { InitialCatalog = "master" };
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
        await using SampleDbContext migrationContext = CreateContext(tenantId: null);
        await migrationContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
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
    /// Builds a context for one tenant, wired exactly as the API wires it - same interceptors, same
    /// filters - so what the tests exercise is the production path.
    /// </summary>
    public SampleDbContext CreateContext(Guid? tenantId, ICurrentUser? currentUser = null)
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

        DbContextOptions<SampleDbContext> options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(
                new TenantSessionContextInterceptor(tenantContext),
                new TenantAuditSaveChangesInterceptor(
                    tenantContext,
                    currentUser ?? new SystemCurrentUserStub(),
                    TimeProvider.System))
            .Options;

        return new SampleDbContext(options, tenantContext);
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

    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

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

    private sealed class SystemCurrentUserStub : ICurrentUser
    {
        public Guid UserId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

        public Guid? EmployeeId => null;

        public Guid? SessionId => null;

        public bool IsAuthenticated => true;

        public Guid? ImpersonatorUserId => null;

        public string? IpAddress => "127.0.0.1";

        public string? UserAgent => "integration-tests";

        public string? CorrelationId => "isolation-suite";
    }
}

/// <summary>Shares one database across the isolation tests; they are read-mostly and use distinct codes.</summary>
[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
