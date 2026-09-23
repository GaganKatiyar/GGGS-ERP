using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// Resolves the tenant for the request from the validated access token, and only from there.
/// <para>
/// Anything the caller supplies - a header, a query string, a route value, a field in the body - is
/// ignored, because a client that could name its own tenant could read every tenant. Requests that
/// try it are logged as a security signal.
/// </para>
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    private static readonly string[] SpoofableHeaders = ["X-Tenant-Id", "X-TenantId", "X-Tenant"];

    public async Task InvokeAsync(HttpContext context, ITenantContextSetter tenantSetter, ITenantRegistry registry)
    {
        WarnOnSuppliedTenantHeaders(context);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Anonymous endpoints (health, OIDC, careers portal) run without a tenant scope.
            await next(context);
            return;
        }

        // Platform staff belong to no customer. Their console manages tenants from the outside, so
        // requiring one would make the tenant list unreachable - and inventing a tenant for them
        // would be worse, because it would put platform operations inside a customer's boundary.
        //
        // The scope this grants is not a licence to read HR data: platform permissions cover the
        // registry and commercial records only, and reaching into a tenant's own data still needs
        // an explicit, audited support grant.
        if (IsPlatformUser(context.User))
        {
            using IDisposable platformScope = tenantSetter.BeginPlatformScope("platform-console");
            await next(context);
            return;
        }

        string? rawTenantId = context.User.FindFirstValue(GggsClaims.TenantId);
        if (!Guid.TryParse(rawTenantId, out Guid tenantId) || tenantId == Guid.Empty)
        {
            // A token without a usable tenant claim is not something to guess about.
            logger.LogWarning(
                "Authenticated request to {Path} carried no usable '{Claim}' claim.",
                context.Request.Path,
                GggsClaims.TenantId);
            throw new TenantNotResolvedException();
        }

        TenantInfo? tenant = await registry.GetAsync(tenantId, context.RequestAborted);
        if (tenant is null)
        {
            logger.LogWarning("Token referenced unknown tenant {TenantId}.", tenantId);
            throw new TenantNotResolvedException();
        }

        if (!tenant.IsOperational)
        {
            throw new TenantUnavailableException(tenant.Id, tenant.Status);
        }

        tenantSetter.SetTenant(tenant);

        // Every log line and trace for the rest of this request carries the tenant.
        using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id }))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Platform staff are identified by the user-type claim the Identity service issues, not by a
    /// role name or an email domain - either of which a tenant could arrange to match.
    /// </summary>
    private static bool IsPlatformUser(ClaimsPrincipal user)
        => string.Equals(user.FindFirstValue(GggsClaims.UserType), "Platform", StringComparison.Ordinal);

    private void WarnOnSuppliedTenantHeaders(HttpContext context)
    {
        foreach (string header in SpoofableHeaders)
        {
            if (context.Request.Headers.ContainsKey(header))
            {
                logger.LogWarning(
                    "Request to {Path} supplied header {Header}, which is ignored. The tenant always comes from the token.",
                    context.Request.Path,
                    header);
            }
        }
    }
}
