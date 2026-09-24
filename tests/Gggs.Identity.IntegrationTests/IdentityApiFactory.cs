using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Domain.Users;
using Gggs.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.IntegrationTests;

/// <summary>
/// Hosts the Identity API in-process against a real SQL Server.
/// <para>
/// Nothing about authentication is faked here - unlike the Platform tests, where only the identity
/// was stubbed. These tests exercise the real password hashing, the real lockout counting and the
/// real cookie, because that is the whole subject.
/// </para>
/// <para>
/// Only the tenant registry is substituted, so the suite does not need a running Platform service
/// to prove that sign-in works.
/// </para>
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    public const string SeededTenantCode = "testco";
    public const string OtherTenantCode = "othertestco";
    public const string SeededUserEmail = "priya@testco.example";
    public const string SeededPassword = "correct horse battery staple";

    public static Guid SeededTenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Guid OtherTenantId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public string DatabaseName { get; } =
        $"gggs_identity_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

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

        using (IServiceScope migrationScope = Services.CreateScope())
        {
            IdentityDbContext context = migrationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await context.Database.MigrateAsync();
        }

        await SeedUserAsync(SeededUserEmail, SeededPassword);
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

    /// <summary>
    /// A client that talks https.
    /// <para>
    /// The session cookie is <c>__Host-</c> prefixed and marked Secure, so a browser - and the test
    /// client - will not send it over plain http. Using https here keeps the production cookie
    /// configuration intact and proves it works, rather than weakening the cookie to suit a test.
    /// </para>
    /// </summary>
    public HttpClient CreateWebClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
    });

    /// <summary>Creates an active user with a real hashed password in the seeded tenant.</summary>
    public async Task<string> SeedUserAsync(string email, string password)
    {
        using IServiceScope scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);

        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (await context.Users.AnyAsync(user => user.NormalizedEmail == User.NormalizeEmailForLookup(email)))
        {
            return email;
        }

        User user = User.Invite(email, "Test User", UserType.TenantUser, employeeId: null, DateTime.UtcNow);
        user.CompleteInvitation(hasher.Hash(password), DateTime.UtcNow);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return email;
    }

    /// <summary>Creates a user who was invited but has never set a password.</summary>
    public async Task<string> SeedInvitedUserAsync(string email)
    {
        using IServiceScope scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);

        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        context.Users.Add(User.Invite(email, "Invited User", UserType.TenantUser, null, DateTime.UtcNow));
        await context.SaveChangesAsync();

        return email;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDb"] = ConnectionString,
                ["Gggs:Platform:DeploymentProfile"] = "OnPremise",
                ["Gggs:Secrets:File:FilePath"] = Path.Combine(Path.GetTempPath(), $"{DatabaseName}-secrets.json"),
                ["Gggs:Security:EnforceStepUp"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Two tenants, so a test can prove that a valid credential for one does not open an
            // account at the other.
            services.RemoveAll<ITenantRegistry>();
            services.AddSingleton<ITenantRegistry, StubTenantRegistry>();
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    /// <summary>Stands in for the Platform service, so this suite does not need one running.</summary>
    private sealed class StubTenantRegistry : ITenantRegistry
    {
        private static readonly Dictionary<Guid, TenantInfo> Tenants = new()
        {
            [SeededTenantId] = CreateTenant(SeededTenantId, SeededTenantCode),
            [OtherTenantId] = CreateTenant(OtherTenantId, OtherTenantCode),
        };

        public Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(Tenants.GetValueOrDefault(tenantId));

        public Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
            => Task.FromResult(Tenants.Values.FirstOrDefault(
                tenant => string.Equals(tenant.Code, tenantCode, StringComparison.OrdinalIgnoreCase)));

        public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static TenantInfo CreateTenant(Guid id, string code) => new()
        {
            Id = id,
            Code = code,
            Name = code,
            Status = TenantStatus.Active,
            IsolationTier = TenantIsolationTier.Pooled,
            DataRegion = "centralindia",
            CountryCode = "IN",
            DefaultTimeZone = "Asia/Kolkata",
            DefaultCurrency = "INR",
            DefaultLocale = "en-IN",
            EnabledFeatures = ["CoreHr"],
        };
    }
}

/// <summary>Shares one API host and database across the sign-in tests.</summary>
[CollectionDefinition(nameof(IdentityApiCollection))]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityApiFactory>;
