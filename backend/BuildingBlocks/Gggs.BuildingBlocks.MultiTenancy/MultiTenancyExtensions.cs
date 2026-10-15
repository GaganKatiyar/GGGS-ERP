using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>Registration helpers so every service wires tenancy the same way.</summary>
public static class MultiTenancyExtensions
{
    /// <summary>
    /// Registers the scoped tenant context. The registry implementation is supplied by the host:
    /// the Platform service owns the data, every other service caches it.
    /// </summary>
    public static IServiceCollection AddGggsMultiTenancy(this IServiceCollection services)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantClock, TenantClock>();
        return services;
    }

    /// <summary>
    /// Must run after authentication and before authorization, so the tenant is known before any
    /// permission check, query filter or cache lookup happens.
    /// </summary>
    public static IApplicationBuilder UseGggsTenantContext(this IApplicationBuilder app)
        => app.UseMiddleware<TenantContextMiddleware>();

    /// <summary>
    /// Runs work under an explicit platform scope. Reserved for migrations, provisioning and
    /// cross-tenant platform metrics; these paths use a database principal that bypasses RLS.
    /// </summary>
    public static async Task RunInPlatformScopeAsync(
        this ITenantContextSetter tenantSetter,
        string reason,
        Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        using (tenantSetter.BeginPlatformScope(reason))
        {
            await work();
        }
    }
}
