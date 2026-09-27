using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Platform.Api.Endpoints;

/// <summary>
/// The tenant registry, read by every other service through <c>RemoteTenantRegistry</c> - never
/// called by a browser, and never by a tenant user's own token.
/// <para>
/// Anonymous rather than permission-gated, unlike everything in <see cref="TenantEndpoints"/>: a
/// tenant-scoped permission cannot gate the one call that resolves a tenant before any tenant
/// context exists (Identity looks a tenant up by its login-time <c>tenant_code</c> before it has
/// issued anything to check a permission against), and there is no service-identity mechanism in
/// this codebase yet to require instead. The route is unguessable-by-tenant-code-only in the sense
/// that it returns nothing sensitive - no HR data, no secrets, the same slice <see cref="TenantInfo"/>
/// already is - but real deployments must still keep it off the public edge (§9, layer 2: the BFF
/// and any WAF should refuse <c>/internal/*</c> from outside the private network). Tracked as a gap:
/// this needs a real service-to-service credential before this ships, not just network placement.
/// </para>
/// </summary>
public static class InternalTenantEndpoints
{
    public static IEndpointRouteBuilder MapInternalTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/internal/tenants").WithTags("Internal");

        group.MapGet("/{tenantId:guid}", GetByIdAsync)
            .WithName("InternalGetTenant")
            .AllowAnonymous();

        group.MapGet("/by-code/{code}", GetByCodeAsync)
            .WithName("InternalGetTenantByCode")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<Results<Ok<TenantInfo>, NotFound>> GetByIdAsync(
        Guid tenantId,
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        Tenant? tenant = await database.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);

        return tenant is null ? TypedResults.NotFound() : TypedResults.Ok(ToTenantInfo(tenant));
    }

    private static async Task<Results<Ok<TenantInfo>, NotFound>> GetByCodeAsync(
        string code,
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        string normalized = code.Trim().ToLowerInvariant();

        Tenant? tenant = await database.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == normalized, cancellationToken);

        return tenant is null ? TypedResults.NotFound() : TypedResults.Ok(ToTenantInfo(tenant));
    }

    // Plan-driven feature flags (platform.PlanFeature / TenantFeature) are not built yet, so every
    // tenant resolves with an empty feature list rather than a hard-coded guess at what it should
    // eventually contain.
    private static TenantInfo ToTenantInfo(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Code = tenant.Code,
        Name = tenant.Name,
        Status = tenant.Status,
        IsolationTier = tenant.IsolationTier,
        DataRegion = tenant.DataRegion,
        CountryCode = tenant.CountryCode,
        DefaultTimeZone = tenant.DefaultTimeZone,
        DefaultCurrency = tenant.DefaultCurrency,
        DefaultLocale = tenant.DefaultLocale,
    };
}
