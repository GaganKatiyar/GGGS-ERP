using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Reads permissions from <c>perm</c> claims on the token.
/// <para>
/// <b>Temporary.</b> It exists so services can enforce permissions before the Identity service
/// lands, and it is replaced by the Identity-backed provider that caches the effective set in
/// Redis under the token's permission version.
/// </para>
/// <para>
/// It is not a long-term answer for two reasons: a full HR role holds hundreds of codes, which
/// would make tokens enormous, and a permission carried in a token keeps working until that token
/// expires - so revoking a role would not take effect when an administrator expects it to.
/// </para>
/// </summary>
public sealed class ClaimsPermissionProvider(IHttpContextAccessor httpContextAccessor) : IPermissionProvider
{
    public const string PermissionClaimType = "perm";

    public Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(PermissionSet.Empty);
        }

        PermissionGrant[] grants =
        [
            .. user.FindAll(PermissionClaimType).Select(claim => new PermissionGrant
            {
                Code = claim.Value,

                // Claims carry no scope, so the widest is assumed. Safe only because this is used
                // by platform endpoints and by services before Identity exists; the real provider
                // resolves the scope actually granted.
                Scope = DataScope.Tenant,
            })
        ];

        int version = int.TryParse(user.FindFirstValue(GggsClaims.PermissionVersion), out int parsed) ? parsed : 1;
        return Task.FromResult(new PermissionSet(grants, version));
    }

    public Task<PermissionSet> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => GetCurrentAsync(cancellationToken);

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public static class ClaimsPermissionExtensions
{
    /// <summary>
    /// Registers the claims-based provider. Remove the call once the Identity-backed provider is
    /// available; a service left on this one silently ignores role revocations.
    /// </summary>
    public static IServiceCollection AddClaimsPermissionProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IPermissionProvider, ClaimsPermissionProvider>();

        return services;
    }
}
