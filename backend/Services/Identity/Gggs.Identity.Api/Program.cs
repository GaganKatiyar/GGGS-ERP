using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Secrets;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Identity.Api.Endpoints;
using Gggs.Identity.Api.Infrastructure;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;

const string ServiceName = "identity";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddGggsMultiTenancy();
builder.Services.AddGggsCaching(builder.Configuration, ServiceName);
builder.Services.AddGggsSecretStore(builder.Configuration, platform.DeploymentProfile);
builder.Services.AddGggsAuthorization(builder.Configuration);

builder.Services.AddGggsOpenIddict(builder.Configuration, builder.Environment);

// The session cookie this service issues after a successful sign-in.
//
// httpOnly and SameSite=Strict: the cookie is the credential, so nothing on the page may read it
// and no cross-site request may carry it. The __Host- prefix binds it to this exact origin, which
// stops a compromised sibling subdomain from writing a cookie this service would accept.
builder.Services
    .AddAuthentication(options =>
    {
        // Two ways in, and the difference matters. A bearer token is what the gateway and every
        // service-to-service caller present; the cookie exists only for this service's own sign-in
        // exchange, which has to set something before a token exists. Bearer is the default, so an
        // endpoint that says nothing gets the stricter of the two.
        options.DefaultAuthenticateScheme = GggsAuthenticationSchemes.Any;
        options.DefaultChallengeScheme = GggsAuthenticationSchemes.Any;
    })
    .AddPolicyScheme(GggsAuthenticationSchemes.Any, GggsAuthenticationSchemes.Any, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.Count > 0
                ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-gggs-identity";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

        // An API, not a website: an unauthenticated call gets a status code, never a redirect to
        // a login page that does not exist here.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddScoped<TenantAuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantSessionContextInterceptor>();

builder.Services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("IdentityDb"),
        sql => sql.EnableRetryOnFailure());

    options.AddInterceptors(
        serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
        serviceProvider.GetRequiredService<TenantAuditSaveChangesInterceptor>());
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPasswordPolicyRepository, PasswordPolicyRepository>();
builder.Services.AddScoped<SignInService>();
builder.Services.AddScoped<Gggs.Identity.Application.Users.UserManagementService>();
builder.Services.AddScoped<Gggs.Identity.Application.Users.IRoleRepository, RoleRepository>();
builder.Services.AddSingleton<Gggs.Identity.Application.Users.IInvitationTokenGenerator, InvitationTokenGenerator>();
builder.Services.AddScoped<TenantDiscoveryService>();
builder.Services.AddScoped<Gggs.Identity.Application.Provisioning.TenantBootstrapService>();

builder.Services.AddScoped<Gggs.Identity.Application.PlatformStaff.IPlatformStaffRepository, PlatformStaffRepository>();
builder.Services.AddScoped<Gggs.Identity.Application.PlatformStaff.IPlatformRoleRepository, PlatformRoleRepository>();
builder.Services.AddScoped<Gggs.Identity.Application.PlatformStaff.PlatformStaffSignInService>();
builder.Services.AddScoped<Gggs.Identity.Application.PlatformStaff.PlatformStaffManagementService>();

builder.Services.AddOptions<PlatformBootstrapOptions>()
    .Bind(builder.Configuration.GetSection(PlatformBootstrapOptions.SectionName));

// Order matters: the bootstrap seeder needs the PLATFORM_STAFF_ADMINISTRATOR role to already exist.
// IHostedService.StartAsync runs in registration order, so the role seeder is registered first.
builder.Services.AddHostedService<PlatformRoleSeeder>();
builder.Services.AddHostedService<PlatformStaffBootstrapSeeder>();

// This service owns permission resolution, so it uses the real provider rather than the temporary
// claims-based one the other services are still on.
builder.Services.AddScoped<IPermissionProvider, IdentityPermissionProvider>();

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IBreachedPasswordChecker, NoOpBreachedPasswordChecker>();

// Tenant lookups go to the Platform service. Registered here as an HTTP client so Identity holds
// no copy of the tenant registry - one source of truth for who exists and whether they are active.
builder.Services.AddHttpClient<ITenantRegistry, PlatformTenantRegistryClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gggs:Services:Platform"] ?? "http://platform");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddScoped<ITenantDomainResolver, PlatformTenantDomainResolver>();

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("identity-db", tags: ["ready"]);

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseGggsTenantContext();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapAuthenticationEndpoints();
app.MapUserEndpoints();
app.MapMeEndpoints();
app.MapTokenEndpoints();
app.MapRoleEndpoints();
app.MapTenantBootstrapEndpoints();
app.MapPlatformStaffEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

EndpointSecurityValidator.ThrowIfAnyUnprotected(
    app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>(),
    app.Services.GetRequiredService<ILogger<Program>>());

await app.RunAsync();

/// <summary>Exposed so the integration tests can host this API in-process.</summary>
public partial class Program;
