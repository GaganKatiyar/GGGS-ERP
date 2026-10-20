using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Endpoints;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Identity.Sessions;
using Gggs.Modules.Platform.Contracts;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace Gggs.Modules.Identity;

/// <summary>
/// Users, roles and permissions, sign-in, platform staff, and the OIDC token server.
/// <para>
/// Owns authentication for the whole application. Two credentials are accepted, and which one
/// applies is decided per request by <see cref="GggsAuthenticationSchemes.Any"/>:
/// </para>
/// <list type="bullet">
///   <item>a bearer token from <c>/connect/token</c>, for non-browser clients;</item>
///   <item>the <c>__Host-</c> session cookie from <c>/bff/login</c>, for the browser, which never
///   holds a token at all.</item>
/// </list>
/// <para>
/// Either way the same claims come out, built by the same factories, so nothing after
/// authentication can tell - or needs to know - which one was used.
/// </para>
/// </summary>
public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasswordPolicyRepository, PasswordPolicyRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPlatformStaffRepository, PlatformStaffRepository>();
        services.AddScoped<IPlatformRoleRepository, PlatformRoleRepository>();

        services.AddScoped<SignInService>();
        services.AddScoped<PasswordSignInService>();
        services.AddScoped<MfaService>();
        services.AddScoped<SsoService>();
        services.AddHttpClient(SsoService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<PrincipalRevalidator>();
        services.AddScoped<TenantDiscoveryService>();
        services.AddScoped<ITenantDomainResolver, PlatformTenantDomainResolver>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<TenantBootstrapService>();
        services.AddScoped<TenantSeedingService>();
        services.AddScoped<PlatformStaffSignInService>();
        services.AddScoped<PlatformStaffManagementService>();

        services.AddSingleton<IInvitationTokenGenerator, InvitationTokenGenerator>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IBreachedPasswordChecker, NoOpBreachedPasswordChecker>();

        // In-process and cached by permission version: a role removed a minute ago takes effect on
        // the next request, with no call to another service.
        services.AddScoped<IPermissionProvider, IdentityPermissionProvider>();
    }

    public void AddApiServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsOpenIddict(context.Configuration, context.Environment);

        services.AddOptions<BffSessionOptions>()
            .Bind(context.Configuration.GetSection(BffSessionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<BffSessionStore>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = GggsAuthenticationSchemes.Any;
                options.DefaultAuthenticateScheme = GggsAuthenticationSchemes.Any;
                options.DefaultChallengeScheme = GggsAuthenticationSchemes.Any;
            })
            .AddPolicyScheme(GggsAuthenticationSchemes.Any, GggsAuthenticationSchemes.Any, options =>
            {
                // A request carrying an Authorization header is an API client; everything else is
                // the browser. Requests with the session cookie have any Authorization header
                // stripped before this runs (see the host's browser request guard), so the two
                // credentials are never combined.
                options.ForwardDefaultSelector = httpContext =>
                    httpContext.Request.Headers.Authorization.Count > 0
                        ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                        : SessionAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationHandler.SchemeName,
                displayName: null,
                configureOptions: null);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddOptions<PlatformBootstrapOptions>()
            .Bind(context.Configuration.GetSection(PlatformBootstrapOptions.SectionName));

        // Order matters: the bootstrap seeder needs the PLATFORM_STAFF_ADMINISTRATOR role to exist
        // already, and hosted services start in registration order.
        services.AddHostedService<PlatformRoleSeeder>();
        services.AddHostedService<PlatformStaffBootstrapSeeder>();

        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, TenantProvisionedConsumer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBrowserSessionEndpoints();
        endpoints.MapTokenEndpoints();
        endpoints.MapMeEndpoints();
        endpoints.MapMfaEndpoints();
        endpoints.MapSsoEndpoints();
        endpoints.MapUserEndpoints();
        endpoints.MapRoleEndpoints();
        endpoints.MapTenantBootstrapEndpoints();
        endpoints.MapPlatformStaffEndpoints();
    }
}
