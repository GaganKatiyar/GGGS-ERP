using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.Platform.IntegrationTests;

/// <summary>
/// Hosts the Platform API in-process against a real SQL Server.
/// <para>
/// Authentication is replaced with a test scheme so a test can say "this caller holds these
/// permissions" directly. Authorization itself is <b>not</b> replaced: the real policy provider and
/// handler run, which is the whole point - these tests exist to prove the permission checks hold at
/// the HTTP boundary, not that a mock returns true.
/// </para>
/// </summary>
public sealed class PlatformApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    public string DatabaseName { get; } =
        $"gggs_platform_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

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
        PlatformDbContext context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await context.Database.MigrateAsync();

        // A real, active tenant, because the tenant middleware resolves every tenant-scoped
        // request through the registry: a token naming a tenant that does not exist is rejected,
        // which is correct behaviour and means these tests need a genuine one.
        await SeedTenantAsync(context);
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

    /// <summary>The tenant seeded for tenant-scoped tests.</summary>
    public static Guid SeededTenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task SeedTenantAsync(PlatformDbContext context)
    {
        if (await context.Tenants.AnyAsync(tenant => tenant.Id == SeededTenantId))
        {
            return;
        }

        Tenant tenant = Tenant.Register(
            "testco", "Test Company", "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN",
            TenantIsolationTier.Pooled, DateTime.UtcNow);

        tenant.CompleteProvisioning(DateTime.UtcNow);

        // The seeded id is fixed so tests can name it; Register generates its own.
        context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = SeededTenantId;

        Plan plan = Plan.Create(
            "PRO", "Professional", PlanEdition.Professional, 1000, true,
            ["CoreHr", "Attendance", "Leave", "Payroll"]);

        context.Tenants.Add(tenant);
        context.Plans.Add(plan);
        context.Subscriptions.Add(Subscription.Start(
            SeededTenantId, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, 100, "INV-TEST"));

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A client for platform staff: no tenant, because they belong to none. This is how the
    /// platform console authenticates.
    /// </summary>
    public HttpClient CreatePlatformClient(params string[] permissions)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserTypeHeader, "Platform");
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>A client for a user inside a tenant, as an ordinary customer request arrives.</summary>
    public HttpClient CreateClientFor(Guid tenantId, params string[] permissions)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>A client with no credentials at all.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    public PlatformDbContext CreateContext()
    {
        IServiceScope scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PlatformDb"] = ConnectionString,

                // On-premise profile so the test host needs no Key Vault and no Azure credentials.
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
                // DefaultScheme as well as the two specific ones: JwtBearer is registered by the
                // host and would otherwise win.
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // The registry normally reads the database; here it also has to see tenants the test
            // has just created, so the real implementation is kept and only its cache is local.
            services.AddSingleton<IMessageTransportStub>(new MessageTransportStub());
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}

/// <summary>Marker so the fixture can assert nothing tried to reach a real broker.</summary>
public interface IMessageTransportStub;

internal sealed class MessageTransportStub : IMessageTransportStub;

/// <summary>
/// Turns two request headers into a signed-in caller. Only the identity is faked; the permission
/// policy, the handler and the tenant middleware are all the production ones.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string UserTypeHeader = "X-Test-User-Type";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        bool hasTenant = Request.Headers.TryGetValue(TenantHeader, out Microsoft.Extensions.Primitives.StringValues tenant);
        bool isPlatform = Request.Headers.TryGetValue(UserTypeHeader, out Microsoft.Extensions.Primitives.StringValues userType)
            && userType.ToString() == "Platform";

        if (!hasTenant && !isPlatform)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "aaaaaaaa-0000-0000-0000-00000000000a"),
            new(GggsClaims.PermissionVersion, "1"),

            // Recent enough that step-up never blocks; sensitive endpoints are tested separately.
            new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        ];

        if (isPlatform)
        {
            claims.Add(new Claim(GggsClaims.UserType, "Platform"));
        }
        else
        {
            claims.Add(new Claim(GggsClaims.TenantId, tenant.ToString()));
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
[CollectionDefinition(nameof(PlatformApiCollection))]
public sealed class PlatformApiCollection : ICollectionFixture<PlatformApiFactory>;
