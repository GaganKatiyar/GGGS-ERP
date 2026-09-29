using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary>
/// Hosts the Core HR API in-process against a real SQL Server.
/// <para>
/// Authentication is replaced with a test scheme so a test can say "this caller holds these
/// permissions" directly, mirroring the Platform and Identity fixtures. Authorization itself is
/// <b>not</b> replaced: the real policy provider and handler run.
/// </para>
/// </summary>
public sealed class CoreHrApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    /// <summary>The tenant every test in this fixture acts as, so there is one caller identity to reason about.</summary>
    public static Guid SeededTenantId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The caller's own employee id, when a test needs one - see <see cref="CreateClientForEmployee"/>.</summary>
    public static Guid CurrentEmployeeId { get; } = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    public string DatabaseName { get; } =
        $"gggs_corehr_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

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

        using IServiceScope scope = Services.CreateScope();
        CoreHrDbContext context = scope.ServiceProvider.GetRequiredService<CoreHrDbContext>();
        await context.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

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

    /// <summary>A client acting inside the seeded tenant, with the given permissions.</summary>
    public HttpClient CreateClientFor(params string[] permissions)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededTenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>Same as <see cref="CreateClientFor"/>, but the caller is also linked to
    /// <paramref name="employeeId"/> (<see cref="CurrentEmployeeId"/> by default) - for endpoints
    /// that read <c>ICurrentUser.EmployeeId</c> (e.g. "my team"). A test that needs the id to
    /// resolve to a real <c>Employee</c> row (anything checking <c>ReportingManagerId</c> exists)
    /// passes the id of an employee it already hired, rather than the constant.</summary>
    public HttpClient CreateClientForEmployee(params string[] permissions) => CreateClientForEmployee(CurrentEmployeeId, permissions);

    public HttpClient CreateClientForEmployee(Guid employeeId, params string[] permissions)
    {
        HttpClient client = CreateClientFor(permissions);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmployeeHeader, employeeId.ToString());
        return client;
    }

    public CoreHrDbContext CreateContext()
    {
        IServiceScope scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CoreHrDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CoreHrDb"] = ConnectionString,
                ["Gggs:Platform:DeploymentProfile"] = "OnPremise",
                ["Gggs:Secrets:File:FilePath"] = Path.Combine(Path.GetTempPath(), $"{DatabaseName}-secrets.json"),
                ["Gggs:Security:EnforceStepUp"] = "false",
                ["ConnectionStrings:RabbitMq"] = "amqp://localhost",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // The registry normally calls Platform over HTTP; there is no Platform host in this
            // fixture, so the seeded tenant is answered from memory instead. What is under test
            // here is Core HR's own behaviour, not Platform's.
            services.RemoveAll<ITenantRegistry>();
            services.AddSingleton<ITenantRegistry>(new StubTenantRegistry());

            // Permissions come from the test principal rather than from Identity, for the same
            // reason: there is no Identity host here, and standing one up to grant a permission the
            // test already decided on would prove nothing extra.
            services.RemoveAll<IPermissionProvider>();
            services.AddScoped<IPermissionProvider, ClaimsPermissionProvider>();
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    private sealed class StubTenantRegistry : ITenantRegistry
    {
        private static readonly TenantInfo Tenant = new()
        {
            Id = SeededTenantId,
            Code = "testco",
            Name = "Test Company",
            Status = TenantStatus.Active,
            IsolationTier = TenantIsolationTier.Pooled,
            DataRegion = "centralindia",
            CountryCode = "IN",
            DefaultTimeZone = "Asia/Kolkata",
            DefaultCurrency = "INR",
            DefaultLocale = "en-IN",
        };

        public Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(tenantId == SeededTenantId ? Tenant : null);

        public Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
            => Task.FromResult(tenantCode == Tenant.Code ? Tenant : null);

        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

/// <summary>Turns two request headers into a signed-in caller, exactly as the Platform fixture's does.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string EmployeeHeader = "X-Test-Employee";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out Microsoft.Extensions.Primitives.StringValues tenant))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "aaaaaaaa-0000-0000-0000-00000000000a"),
            new(GggsClaims.TenantId, tenant.ToString()),
            new(GggsClaims.PermissionVersion, "1"),
            new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        ];

        if (Request.Headers.TryGetValue(PermissionsHeader, out Microsoft.Extensions.Primitives.StringValues permissions))
        {
            claims.AddRange(permissions
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim("perm", permission)));
        }

        if (Request.Headers.TryGetValue(EmployeeHeader, out Microsoft.Extensions.Primitives.StringValues employeeId))
        {
            claims.Add(new Claim(GggsClaims.EmployeeId, employeeId.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Shares one API host and database across the endpoint tests.</summary>
[CollectionDefinition(nameof(CoreHrApiCollection))]
public sealed class CoreHrApiCollection : ICollectionFixture<CoreHrApiFactory>;
