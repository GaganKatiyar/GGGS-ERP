using Gggs.BuildingBlocks.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gggs.BuildingBlocks.Security;

/// <summary>Wires permission-based authorization into a service host.</summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Registers the permission policy provider, handler and current-user accessor. Every service
    /// calls this, so authorization behaves identically across the platform.
    /// </summary>
    public static IServiceCollection AddGggsAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, HttpCurrentUser>();
        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();

        services.AddAuthorization();

        return services;
    }
}
