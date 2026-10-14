using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Composition;
using Gggs.Data;
using Gggs.Modules.Documents.Application;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Endpoints;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Identity.Sessions;
using Gggs.Modules.MasterData.Infrastructure;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Hosts the whole PeopleOS API in-process against a real SQL Server, built by the real migrations.
/// <para>
/// Nothing about authentication is faked by default: the browser clients sign in through
/// <c>/bff/login</c> with real password hashing, a real session cookie and real permission
/// resolution from role grants. Two real tenants exist, so a test can prove that a credential for
/// one does not open the other.
/// </para>
/// <para>
/// One opt-in shortcut remains for endpoint authorization tests: a request carrying the
/// <see cref="TestAuthHandler"/> headers is authenticated as a caller holding exactly the
/// permissions it names. Only the identity is faked; the permission policy, the handler and the
/// tenant middleware are the production ones.
/// </para>
/// </summary>
public sealed class PeopleOsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalSqlServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    public const string SeededTenantCode = "testco";
    public const string OtherTenantCode = "othertestco";
    public const string SeededUserEmail = "priya@testco.example";
    public const string SeededPassword = "correct horse battery staple";
    public const string AdminEmail = "admin@testco.example";
    public const string AdminPassword = "administrator passphrase 1";

    /// <summary>The OAuth client non-browser callers use, as configured in the API's appsettings.</summary>
    public const string ApiClientId = "peopleos-api";

    /// <summary>The header every state-changing browser request sends, as the SPA does.</summary>
    public const string CsrfHeader = "X-CSRF";

    public static Guid SeededTenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Guid OtherTenantId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The administrator these tests act as.</summary>
    public static Guid AdminUserId { get; private set; }

    public string DatabaseName { get; } =
        $"peopleos_api_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Uploaded documents for this run; removed with the database.</summary>
    public string FileRoot { get; } = Path.Combine(Path.GetTempPath(), $"peopleos-files-{Guid.NewGuid():N}");

    private ServiceProvider? _worker;

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

        // Migrated and seeded through a standalone context before the host starts, because the host
        // registers the OAuth client at startup and that needs the tables. In deployment migrations
        // are a pre-deployment step for the same reason.
        await MigrateAndSeedTenantsAsync();

        await SeedUserAsync(SeededUserEmail, SeededPassword);
        await SeedAdministratorAsync();

        // Every tenant starts with the lookup lists, as provisioning gives them.
        foreach (Guid tenantId in new[] { SeededTenantId, OtherTenantId })
        {
            using IServiceScope scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
            await scope.ServiceProvider.GetRequiredService<MasterDataSeeder>().SeedCurrentTenantAsync();
            await scope.ServiceProvider.GetRequiredService<NotificationTemplateSeeder>().SeedCurrentTenantAsync();
            await scope.ServiceProvider.GetRequiredService<DocumentCatalogSeeder>().SeedCurrentTenantAsync();
        }

        // The worker seeds the platform roles at start-up; this host is the API alone, so run the
        // same seeder here.
        await ActivatorUtilities.CreateInstance<PlatformRoleSeeder>(Services).StartAsync(CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        await base.DisposeAsync();

        try
        {
            if (Directory.Exists(FileRoot))
            {
                Directory.Delete(FileRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is noise, not a failure.
        }

        try
        {
            SqlConnection.ClearAllPools();
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
    /// Delivers every pending integration event to its consumers, as the worker would - repeated
    /// until nothing is left, so events raised by consumers are delivered too.
    /// </summary>
    public async Task DeliverEventsAsync()
    {
        OutboxDispatcher<AppDbContext> dispatcher = Worker.GetRequiredService<OutboxDispatcher<AppDbContext>>();
        for (int pass = 0; pass < 10 && await dispatcher.DispatchPendingAsync(TestContext.Current.CancellationToken) > 0; pass++)
        {
        }
    }

    /// <summary>Runs work as the worker does: in a tenant, as the system identity, with background services available.</summary>
    public async Task RunAsWorkerAsync(Guid tenantId, Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using AsyncServiceScope scope = Worker.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        await work(scope.ServiceProvider);
    }

    /// <summary>
    /// The worker host's composition - every module's services and background consumers - against
    /// this run's database. Hosted services are registered but never started; tests drive delivery.
    /// </summary>
    private ServiceProvider Worker => _worker ??= BuildWorker();

    private ServiceProvider BuildWorker()
    {
        IConfiguration configuration = Services.GetRequiredService<IConfiguration>();
        IHostEnvironment environment = Services.GetRequiredService<IHostEnvironment>();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.AddSingleton(environment);
        services.AddSingleton(Services.GetRequiredService<GggsPlatformOptions>());
        services.AddSingleton(TimeProvider.System);
        services.AddDistributedMemoryCache();
        services.AddGggsMultiTenancy();
        services.AddGggsCaching(configuration, "worker-test");
        services.AddGggsSecretStore(configuration, DeploymentProfile.OnPremise);
        services.AddGggsSensitiveData();
        services.AddGggsFileStorage(configuration, DeploymentProfile.OnPremise);
        services.AddScoped<ICurrentUser>(_ => new SystemCurrentUser(SystemUsers.System));
        services.AddGggsAppDbContext(configuration, ModuleCatalog.ModelAssemblies);
        services.AddGggsMessaging<AppDbContext>(configuration);

        var moduleContext = new ModuleContext(configuration, environment);
        foreach (IModule module in ModuleCatalog.All)
        {
            module.AddServices(services, moduleContext);
            module.AddBackgroundServices(services, moduleContext);
        }

        services.AddWorkflowSubject(PlatformServicesApi.TestSubjectDefinition);
        services.AddSingleton<OutboxDispatcher<AppDbContext>>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A browser: https (the session cookie is <c>__Host-</c> and Secure), cookies kept between
    /// requests, and the CSRF header on every request as the SPA sends it.
    /// </summary>
    public HttpClient CreateWebClient()
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Add(CsrfHeader, "1");
        return client;
    }

    /// <summary>The same browser, for tests written against the old gateway's name for it.</summary>
    public HttpClient CreateBrowser() => CreateWebClient();

    /// <summary>A client that keeps no cookies and adds no headers: each request is exactly as written.</summary>
    public HttpClient CreateRawClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
        HandleCookies = false,
    });

    /// <summary>A client with no credentials at all.</summary>
    public HttpClient CreateAnonymousClient() => CreateRawClient();

    /// <summary>A browser already signed in as the seeded administrator.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
        => await CreateSignedInClientAsync(AdminEmail, AdminPassword, SeededTenantCode);

    /// <summary>A browser signed in through <c>/bff/login</c>.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string email, string password, string? tenantCode)
    {
        HttpClient client = CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/bff/login",
            new { Email = email, Password = password, TenantCode = tenantCode });

        BffLoginResponse? body = await response.Content.ReadFromJsonAsync<BffLoginResponse>();

        if (!response.IsSuccessStatusCode || body?.Succeeded != true)
        {
            throw new InvalidOperationException($"{email} could not sign in: {response.StatusCode} {body?.ErrorCode}.");
        }

        return client;
    }

    /// <summary>A platform staff caller holding exactly <paramref name="permissions"/>.</summary>
    public HttpClient CreatePlatformClient(params string[] permissions)
    {
        HttpClient client = CreateRawClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserTypeHeader, "Platform");
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>A user inside <paramref name="tenantId"/> holding exactly <paramref name="permissions"/>.</summary>
    public HttpClient CreateClientFor(Guid tenantId, params string[] permissions)
    {
        HttpClient client = CreateRawClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>The application context for one tenant (or platform scope when null).</summary>
    public AppDbContext CreateContext(Guid? tenantId = null)
    {
        IServiceScope scope = Services.CreateScope();
        ITenantContextSetter tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();

        if (tenantId is null)
        {
            tenant.BeginPlatformScope("integration-tests");
        }
        else
        {
            tenant.SetTenant(tenantId.Value);
        }

        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>Creates an active user with a real hashed password in the seeded tenant.</summary>
    public async Task<string> SeedUserAsync(string email, string password)
    {
        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (await context.Set<User>().AnyAsync(user => user.NormalizedEmail == User.NormalizeEmailForLookup(email)))
        {
            return email;
        }

        User user = User.Invite(email, "Test User", UserType.TenantUser, employeeId: null, DateTime.UtcNow);
        user.CompleteInvitation(hasher.Hash(password), DateTime.UtcNow);

        context.Set<User>().Add(user);
        await context.SaveChangesAsync();

        return email;
    }

    /// <summary>
    /// A signed-in browser for a new user linked to <paramref name="employeeId"/>, holding exactly
    /// the given permissions at the given scopes - for testing data scope with the real provider.
    /// </summary>
    public async Task<HttpClient> CreateScopedUserClientAsync(string email, Guid? employeeId, params (string Permission, DataScope Scope)[] grants)
    {
        const string password = "a scoped test passphrase";

        using (IServiceScope scope = Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);
            AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            Role role = Role.Create($"Scoped {Guid.NewGuid():N}", "Test role");
            foreach ((string permission, DataScope dataScope) in grants)
            {
                role.Grant(permission, dataScope);
            }

            User user = User.Invite(email, email, UserType.TenantUser, employeeId, DateTime.UtcNow);
            user.CompleteInvitation(hasher.Hash(password), DateTime.UtcNow);

            context.Set<Role>().Add(role);
            context.Set<User>().Add(user);
            context.Set<UserRole>().Add(UserRole.Grant(user.Id, role.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
            await context.SaveChangesAsync();
        }

        return await CreateSignedInClientAsync(email, password, SeededTenantCode);
    }

    /// <summary>Creates a user who was invited but has never set a password.</summary>
    public async Task<string> SeedInvitedUserAsync(string email)
    {
        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Set<User>().Add(User.Invite(email, "Invited User", UserType.TenantUser, null, DateTime.UtcNow));
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
                [$"ConnectionStrings:{DataExtensions.ConnectionStringName}"] = ConnectionString,
                ["ConnectionStrings:Redis"] = string.Empty,
                ["Gggs:Platform:DeploymentProfile"] = "OnPremise",
                ["Gggs:Secrets:File:FilePath"] = Path.Combine(Path.GetTempPath(), $"{DatabaseName}-secrets.json"),
                ["Gggs:Security:EnforceStepUp"] = "false",
                ["Gggs:Identity:Tokens:Issuer"] = "https://localhost/",
                ["Gggs:Storage:FileSystem:RootPath"] = FileRoot,
                ["Gggs:Notifications:EmailProvider"] = "Log",
            });
        });

        builder.ConfigureServices(services =>
        {
            // A workflow subject only the tests start, so approval tests never collide with a module's own.
            services.AddWorkflowSubject(PlatformServicesApi.TestSubjectDefinition);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Requests carrying the test headers use the test identity; everything else keeps the
            // production selection between bearer token and session cookie.
            services.PostConfigure<PolicySchemeOptions>(GggsAuthenticationSchemes.Any, options =>
            {
                Func<HttpContext, string?>? production = options.ForwardDefaultSelector;
                options.ForwardDefaultSelector = context =>
                    TestAuthHandler.IsTestRequest(context.Request) ? TestAuthHandler.SchemeName : production?.Invoke(context);
            });

            services.AddScoped<IPermissionProvider>(serviceProvider =>
                new TestAwarePermissionProvider(
                    serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                    ActivatorUtilities.CreateInstance<IdentityPermissionProvider>(serviceProvider),
                    ActivatorUtilities.CreateInstance<ClaimsPermissionProvider>(serviceProvider)));
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    private async Task MigrateAndSeedTenantsAsync()
    {
        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("test-migration");

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, DataExtensions.ConfigureSqlServer)
            .AddInterceptors(new TenantSessionContextInterceptor(tenantContext))
            .Options;

        await using var context = new AppDbContext(options, tenantContext, new ModelAssemblies(ModuleCatalog.ModelAssemblies));
        await context.Database.MigrateAsync();

        Plan plan = Plan.Create("PRO", "Professional", PlanEdition.Professional, 1000, true, [Features.CoreHr, Features.Time, Features.Payroll]);
        context.Set<Plan>().Add(plan);

        foreach ((Guid id, string code) in new[] { (SeededTenantId, SeededTenantCode), (OtherTenantId, OtherTenantCode) })
        {
            Tenant tenant = Tenant.Register(
                code, code, "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN", TenantIsolationTier.Pooled, DateTime.UtcNow);
            tenant.CompleteProvisioning(DateTime.UtcNow);

            // Fixed ids so tests can name them; Register generates its own.
            context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = id;
            context.Set<Tenant>().Add(tenant);
            context.Set<Subscription>().Add(Subscription.Start(id, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, 100, "INV-TEST"));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// An administrator whose permissions come from a real role, resolved by the real provider -
    /// the same path a real administrator takes.
    /// </summary>
    private async Task SeedAdministratorAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(SeededTenantId);

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        Role role = Role.Create("Tenant Administrator", "Full administrative control", isSystem: true, templateCode: "TENANT_ADMIN");
        role.Grant(Permissions.Admin.UserView, DataScope.Tenant);
        role.Grant(Permissions.Admin.UserInvite, DataScope.Tenant);
        role.Grant(Permissions.Admin.UserManage, DataScope.Tenant);
        role.Grant(Permissions.Admin.UserLock, DataScope.Tenant);
        role.Grant(Permissions.Admin.RoleView, DataScope.Tenant);
        role.Grant(Permissions.Admin.RoleManage, DataScope.Tenant);
        role.Grant(Permissions.Audit.LogView, DataScope.Tenant);

        // Core HR, including the sensitive fields: the suite tests that they are guarded, so the
        // administrator it acts as must be able to reach them.
        foreach (string permission in new[]
        {
            Permissions.MasterData.View, Permissions.MasterData.Manage,
            Permissions.Organization.View, Permissions.Organization.Manage, Permissions.Organization.LegalEntityManage, Permissions.Organization.DepartmentManage,
            Permissions.Employee.View, Permissions.Employee.Create, Permissions.Employee.Edit, Permissions.Employee.EmploymentManage,
            Permissions.Employee.HistoryView, Permissions.Employee.ExitManage,
            Permissions.Employee.IdentityDocumentView, Permissions.Employee.IdentityDocumentManage,
            Permissions.Employee.BankView, Permissions.Employee.BankManage,
            Permissions.Workflow.DefinitionView, Permissions.Workflow.DefinitionManage, Permissions.Workflow.TaskReassign,
            Permissions.Workflow.InstanceViewAll, Permissions.Workflow.DelegationManage,
            Permissions.Document.View, Permissions.Document.Upload, Permissions.Document.Delete,
            Permissions.Document.TypeManage, Permissions.Document.TemplateManage,
            Permissions.Policy.View, Permissions.Policy.Manage, Permissions.Policy.AcknowledgementView,
            Permissions.Notification.TemplateView, Permissions.Notification.TemplateManage,
        })
        {
            role.Grant(permission, DataScope.Tenant);
        }

        User admin = User.Invite(AdminEmail, "Test Administrator", UserType.TenantUser, null, DateTime.UtcNow);
        admin.CompleteInvitation(hasher.Hash(AdminPassword), DateTime.UtcNow);
        AdminUserId = admin.Id;

        context.Set<Role>().Add(role);
        context.Set<User>().Add(admin);
        context.Set<UserRole>().Add(UserRole.Grant(admin.Id, role.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

        await context.SaveChangesAsync();
    }
}

/// <summary>
/// Permissions for a test-scheme caller come from the headers it sent; everyone else's come from the
/// real provider, from role grants in the database.
/// </summary>
internal sealed class TestAwarePermissionProvider(
    IHttpContextAccessor httpContextAccessor,
    IdentityPermissionProvider real,
    ClaimsPermissionProvider fromClaims) : IPermissionProvider
{
    private bool IsTestCaller
        => httpContextAccessor.HttpContext?.User.Identity?.AuthenticationType == TestAuthHandler.SchemeName;

    public Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
        => IsTestCaller ? fromClaims.GetCurrentAsync(cancellationToken) : real.GetCurrentAsync(cancellationToken);

    public Task<PermissionSet> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => IsTestCaller ? fromClaims.GetForUserAsync(userId, cancellationToken) : real.GetForUserAsync(userId, cancellationToken);

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
        => real.InvalidateAsync(userId, cancellationToken);
}

/// <summary>
/// Turns request headers into a signed-in caller holding exactly the permissions named.
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

    public static bool IsTestRequest(HttpRequest request)
        => request.Headers.ContainsKey(TenantHeader) || request.Headers.ContainsKey(UserTypeHeader);

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
            new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];

        claims.Add(isPlatform
            ? new Claim(GggsClaims.UserType, GggsClaims.UserTypes.Platform)
            : new Claim(GggsClaims.TenantId, tenant.ToString()));

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

/// <summary>Shares one API host and database across the suite.</summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<PeopleOsApiFactory>;
