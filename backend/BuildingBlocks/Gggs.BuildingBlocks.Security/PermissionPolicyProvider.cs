using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Builds an authorization policy on demand for each permission the code declares, so nobody has to
/// register hundreds of named policies at startup (and no endpoint silently falls back to "any
/// authenticated user" because its policy was never registered).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <summary>
    /// The fallback policy applies to endpoints with no authorization metadata at all: authenticated
    /// by default. Endpoints that genuinely serve anonymous traffic opt out with [AllowAnonymous].
    /// </summary>
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (RequirePermissionAttribute.TryParsePolicyName(policyName, out string[] permissions, out PermissionMode mode))
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permissions, mode))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
