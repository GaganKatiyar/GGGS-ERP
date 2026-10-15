using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Composition;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Creates the PeopleOS database (if needed) and one demo tenant with a working administrator, so a
// developer can sign in through the Angular app with nothing running but the API.
//
//   dotnet run --project tools/DevSeed -- [tenantCode] [adminEmail] [adminPassword]
//
// It composes the same modules as the hosts and provisions through the real services - plan
// catalog, tenant provisioning, role seeding - rather than writing rows by hand, so what it creates
// is exactly what a real tenant gets. The one shortcut: the administrator is given a password
// directly instead of being sent an invitation, because no mail is configured locally.

string tenantCode = args.Length > 0 ? args[0] : "acme";
string adminEmail = args.Length > 1 ? args[1] : $"admin@{tenantCode}.example";
string adminPassword = args.Length > 2 ? args[2] : "Passw0rd!2026";

string connectionString = Environment.GetEnvironmentVariable("GGGS_APP_DB")
    ?? "Server=localhost;Database=peopleos;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

Console.WriteLine($"Database: {connectionString}");

IConfiguration configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"ConnectionStrings:{DataExtensions.ConnectionStringName}"] = connectionString,
    })
    .Build();

var services = new ServiceCollection();
services.AddSingleton(configuration);
services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
services.AddSingleton(TimeProvider.System);
services.AddSingleton(new GggsPlatformOptions { ServiceName = "dev-seed" });
services.AddGggsMultiTenancy();
services.AddGggsCaching(configuration, "dev-seed");
services.AddGggsSensitiveData();
services.AddScoped<ICurrentUser, SeedActor>();
services.AddGggsAppDbContext(configuration, ModuleCatalog.ModelAssemblies);
services.AddGggsMessaging<AppDbContext>(configuration);

var moduleContext = new ModuleContext(configuration, new SeedEnvironment());
foreach (IModule module in ModuleCatalog.All)
{
    module.AddServices(services, moduleContext);
}

await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

await RunAsync(provider, "migrate", async (scope, tenant) =>
{
    using IDisposable platform = tenant.BeginPlatformScope("dev-seed");
    await scope.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    Console.WriteLine("Database migrated.");
});

await RunAsync(provider, "plans", async (scope, _) =>
    await scope.GetRequiredService<PlanCatalogSynchronizer>().SyncAsync());

Guid tenantId = Guid.Empty;

await RunAsync(provider, "tenant", async (scope, tenant) =>
{
    using IDisposable platform = tenant.BeginPlatformScope("dev-seed");

    TenantInfo? existing = await scope.GetRequiredService<ITenantRegistry>().GetByCodeAsync(tenantCode);
    if (existing is not null)
    {
        tenantId = existing.Id;
        Console.WriteLine($"Tenant '{tenantCode}' already exists ({tenantId}), status {existing.Status}.");
        return;
    }

    ProvisionTenantResult result = await scope.GetRequiredService<TenantProvisioningService>().ProvisionAsync(new ProvisionTenantRequest
    {
        Code = tenantCode,
        Name = "Acme Technologies (dev seed)",
        CountryCode = "IN",
        DataRegion = "centralindia",
        DefaultTimeZone = "Asia/Kolkata",
        DefaultCurrency = "INR",
        DefaultLocale = "en-IN",
        PlanCode = PlanCatalog.Enterprise,
        AdministratorEmail = adminEmail,
    });

    tenantId = result.TenantId;
    Console.WriteLine($"Tenant '{tenantCode}' provisioned on {PlanCatalog.Enterprise} ({tenantId}).");
});

// What the worker does on TenantProvisioned: seed the role templates and the first administrator.
// Run inline here so the seed does not depend on the worker being up; the event it staged is still
// delivered later, and seeding is idempotent, so that delivery is a no-op.
await RunAsync(provider, "roles", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    TenantSeedingResult seeded = await scope.GetRequiredService<TenantSeedingService>().SeedAsync(adminEmail);
    Console.WriteLine($"Roles: {seeded.RolesCreated} created, {seeded.RolesAlreadyPresent} already present.");
});

await RunAsync(provider, "admin password", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);

    IUserRepository users = scope.GetRequiredService<IUserRepository>();
    User admin = await users.FindByEmailAsync(User.NormalizeEmailForLookup(adminEmail))
        ?? throw new InvalidOperationException($"The administrator '{adminEmail}' was not created.");

    if (admin.Status != UserStatus.Invited)
    {
        Console.WriteLine($"Administrator '{adminEmail}' is already {admin.Status}; password left unchanged.");
        return;
    }

    string hash = scope.GetRequiredService<IPasswordHasher>().Hash(adminPassword);
    admin.CompleteInvitation(hash, DateTime.UtcNow);
    await scope.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    Console.WriteLine($"Administrator '{adminEmail}' activated.");
});

await RunAsync(provider, "master data", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.SeedMasterDataAsync(scope);
});

await RunAsync(provider, "platform catalogs", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.SeedPlatformCatalogsAsync(scope);
});

await RunAsync(provider, "administrator role", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.TopUpAdministratorRoleAsync(scope);
});

await RunAsync(provider, "activate", async (scope, tenant) =>
{
    using IDisposable platform = tenant.BeginPlatformScope("dev-seed");

    TenantInfo? current = await scope.GetRequiredService<ITenantRegistry>().GetAsync(tenantId);
    if (current?.IsOperational == true)
    {
        return;
    }

    await scope.GetRequiredService<TenantProvisioningService>().ActivateAsync(tenantId);
    Console.WriteLine($"Tenant '{tenantCode}' activated.");
});

// After activation: hiring checks lookups and structure exactly as the API would.
await RunAsync(provider, "demo company", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.SeedDemoCompanyAsync(scope);
});

await RunAsync(provider, "leave", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.SeedLeaveAsync(scope);
});

await RunAsync(provider, "administrator employee", async (scope, tenant) =>
{
    tenant.SetTenant(tenantId);
    await Gggs.DevSeed.DemoData.LinkAdministratorToEmployeeAsync(scope, adminEmail);
});

Console.WriteLine();
Console.WriteLine("Done. Sign in with:");
Console.WriteLine($"  Organisation code: {tenantCode}");
Console.WriteLine($"  Work email:        {adminEmail}");
Console.WriteLine($"  Password:          {adminPassword}");

// Each step in its own scope, as each request or message would be: a fresh DbContext and a fresh
// tenant context, so one step's tenant can never leak into the next.
static async Task RunAsync(IServiceProvider provider, string step, Func<IServiceProvider, ITenantContextSetter, Task> work)
{
    await using AsyncServiceScope scope = provider.CreateAsyncScope();

    try
    {
        await work(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ITenantContextSetter>());
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Step '{step}' failed: {exception.Message}");
        throw;
    }
}

/// <summary>
/// No human is behind these writes. Authenticated so the audit interceptor records
/// <see cref="SystemUsers.Migration"/> rather than the generic system user: this really is seed data.
/// </summary>
file sealed class SeedActor : ICurrentUser
{
    public Guid UserId => SystemUsers.Migration;

    public Guid? EmployeeId => null;

    public Guid? SessionId => null;

    public bool IsAuthenticated => true;

    public Guid? ImpersonatorUserId => null;

    public bool IsPlatformStaff => false;

    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? CorrelationId => null;
}

file sealed class SeedEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "Gggs.DevSeed";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
