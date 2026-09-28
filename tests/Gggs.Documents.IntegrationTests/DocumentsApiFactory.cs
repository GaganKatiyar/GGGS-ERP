using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Documents.Infrastructure;
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

namespace Gggs.Documents.IntegrationTests;

/// <summary>
/// Hosts the Documents API in-process against a real SQL Server and a real local folder for file
/// storage - the on-premise <c>FileSystemFileStorage</c> adapter, not a mock, so a test that reads a
/// file back is proving the actual save/open round trip, the same standard the other API fixtures
/// hold their own infrastructure to.
/// </summary>
public sealed class DocumentsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    public static Guid SeededTenantId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public string DatabaseName { get; } =
        $"gggs_document_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

    public string ConnectionString { get; private set; } = string.Empty;

    private readonly string _storageRootPath =
        Path.Combine(Path.GetTempPath(), $"gggs-document-tests-{Guid.CreateVersion7():N}");

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

        Directory.CreateDirectory(_storageRootPath);

        using IServiceScope scope = Services.CreateScope();
        DocumentDbContext context = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
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

        try
        {
            if (Directory.Exists(_storageRootPath))
            {
                Directory.Delete(_storageRootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is noise, not a failure.
        }
    }

    public HttpClient CreateClientFor(params string[] permissions)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededTenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    public DocumentDbContext CreateContext()
    {
        IServiceScope scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DocumentDb"] = ConnectionString,
                ["Gggs:Platform:DeploymentProfile"] = "OnPremise",
                ["Gggs:Secrets:File:FilePath"] = Path.Combine(Path.GetTempPath(), $"{DatabaseName}-secrets.json"),
                ["Gggs:Security:EnforceStepUp"] = "false",
                ["Gggs:Storage:FileSystem:RootPath"] = _storageRootPath,
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

            // No Platform host in this fixture; the seeded tenant is answered from memory, matching
            // every other service's own API fixture.
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
            Code = "doctestco",
            Name = "Document Test Company",
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

/// <summary>Turns two request headers into a signed-in caller, exactly as the other API fixtures' does.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string PermissionsHeader = "X-Test-Permissions";

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

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Shares one API host and database across the endpoint tests.</summary>
[CollectionDefinition(nameof(DocumentsApiCollection))]
public sealed class DocumentsApiCollection : ICollectionFixture<DocumentsApiFactory>;
