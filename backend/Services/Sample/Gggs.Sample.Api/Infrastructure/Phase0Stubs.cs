using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Gggs.Sample.Api.Infrastructure;

/// <summary>
/// Tenants for the Phase 0 harness, supplied from configuration.
/// <para>
/// Phase 1 replaces this with the Platform service as the source of truth, fronted by a cache. The
/// interface is what the rest of the code depends on, so that swap does not reach into any handler.
/// </para>
/// </summary>
public sealed class ConfiguredTenantRegistry(IOptions<SampleTenantOptions> options) : ITenantRegistry
{
    private readonly Dictionary<Guid, TenantInfo> _tenants = options.Value.Tenants
        .ToDictionary(tenant => tenant.Id);

    public Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.GetValueOrDefault(tenantId));

    public Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.Values.FirstOrDefault(
            tenant => string.Equals(tenant.Code, tenantCode, StringComparison.OrdinalIgnoreCase)));

    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Configuration shape for the harness tenants.</summary>
public sealed class SampleTenantOptions
{
    public const string SectionName = "Gggs:Tenants";

    public IList<TenantInfo> Tenants { get; } = [];
}

/// <summary>
/// Reads permissions from <c>perm</c> claims for the harness.
/// <para>
/// Phase 1 replaces this with the Identity-backed provider that caches the effective set in Redis
/// under the token's permission version - claims cannot carry hundreds of codes, and a revoked role
/// must take effect before the token expires.
/// </para>
/// </summary>
public sealed class ClaimsPermissionProvider(IHttpContextAccessor httpContextAccessor) : IPermissionProvider
{
    public Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(PermissionSet.Empty);
        }

        PermissionGrant[] grants =
        [
            .. user.FindAll("perm").Select(claim => new PermissionGrant
            {
                Code = claim.Value,
                Scope = Enum.TryParse(user.FindFirstValue("scope_" + claim.Value), out DataScope scope)
                    ? scope
                    : DataScope.Tenant,
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
