using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Spend.Infrastructure;
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

namespace Gggs.Spend.IntegrationTests;

/// <summary>Hosts the Spend API in-process against a real SQL Server, mirroring the other
/// services' own API fixtures (e.g. <c>Gggs.Payroll.IntegrationTests.PayrollApiFactory</c>).</summary>
public sealed class SpendApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    public static Guid SeededTenantId { get; } = Guid.Parse("88888888-8888-8888-8888-888888888888");

    /// <summary>Every TestAuthHandler-authenticated request is this user - see its own remarks.</summary>
    public static Guid CurrentUserId { get; } = Guid.Parse("eeeeeeee-1111-1111-1111-11111111111e");

    public string DatabaseName { get; } =
        $"gggs_spend_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

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
        SpendDbContext context = scope.ServiceProvider.GetRequiredService<SpendDbContext>();
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

    public HttpClient CreateClientFor(params string[] permissions)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededTenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>Same as <see cref="CreateClientFor"/>, but authenticated as a specific employee -
    /// what self-service tests need, since "my own claim" has to resolve against a real employee
    /// id a claim was actually created for.</summary>
    public HttpClient CreateClientForEmployee(Guid employeeId, params string[] permissions)
    {
        HttpClient client = CreateClientFor(permissions);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmployeeHeader, employeeId.ToString());
        return client;
    }

    /// <summary>A raw context resolved to <see cref="SeededTenantId"/>, for seeding rows (an
    /// EmployeeSnapshot) the API itself never writes directly.</summary>
    public SpendDbContext CreateTenantScopedContext(out IDisposable scopeHandle)
    {
        IServiceScope scope = Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.SetTenant(SeededTenantId);
        scopeHandle = scope;
        return scope.ServiceProvider.GetRequiredService<SpendDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SpendDb"] = ConnectionString,
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

            services.RemoveAll<ITenantRegistry>();
            services.AddSingleton<ITenantRegistry>(new StubTenantRegistry());

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
            Code = "spendtestco",
            Name = "Spend Test Company",
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

/// <summary>Turns two request headers into a signed-in caller, exactly as the other API fixtures'
/// does.</summary>
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
            new(ClaimTypes.NameIdentifier, SpendApiFactory.CurrentUserId.ToString()),
            new(GggsClaims.TenantId, tenant.ToString()),
            new(GggsClaims.PermissionVersion, "1"),
            new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];

        if (Request.Headers.TryGetValue(EmployeeHeader, out Microsoft.Extensions.Primitives.StringValues employeeId))
        {
            claims.Add(new(GggsClaims.EmployeeId, employeeId.ToString()));
        }

        if (Request.Headers.TryGetValue(PermissionsHeader, out Microsoft.Extensions.Primitives.StringValues permissions))
        {
            claims.AddRange(permissions
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim("perm", permission)));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Shares one API host and database across the endpoint tests.</summary>
[CollectionDefinition(nameof(SpendApiCollection))]
public sealed class SpendApiCollection : ICollectionFixture<SpendApiFactory>;
