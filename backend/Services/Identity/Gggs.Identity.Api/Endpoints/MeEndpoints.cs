using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Identity.Api.Endpoints;

/// <summary>A permission the caller holds, and how far it reaches.</summary>
public sealed record PermissionGrantResponse(string Code, string Scope, Guid? ScopeId);

/// <summary>The tenant, as the shell needs to render it.</summary>
public sealed record TenantContextResponse(
    Guid Id,
    string Code,
    string Name,
    string CountryCode,
    string DefaultCurrency,
    string DefaultTimeZone,
    string DefaultLocale);

/// <summary>Another tenant this user could switch into.</summary>
public sealed record TenantSummaryResponse(Guid Id, string Code, string Name);

/// <summary>
/// Everything the application shell needs about the signed-in user, in one call.
/// <para>
/// One call rather than three because the shell cannot draw anything until it has all of it: the
/// navigation is filtered by permissions, the theme comes from the tenant, and the route guards need
/// the feature list. Three round trips would give three chances to render a half-built page.
/// </para>
/// <para>
/// Permissions are here for the user interface only - to hide what would fail anyway. Every endpoint
/// still checks for itself, because a caller can send any request they like regardless of what the
/// menu offered them.
/// </para>
/// </summary>
public sealed record SessionResponse
{
    public required Guid UserId { get; init; }

    public required string DisplayName { get; init; }

    public required string Email { get; init; }

    public Guid? EmployeeId { get; init; }

    public required string UserType { get; init; }

    /// <summary>Absent for platform staff, who belong to no customer.</summary>
    public TenantContextResponse? Tenant { get; init; }

    public IReadOnlyList<PermissionGrantResponse> Permissions { get; init; } = [];

    /// <summary>Feature codes the tenant's plan enables, which drive navigation and route guards.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    public required string Locale { get; init; }

    public required string TimeZone { get; init; }

    /// <summary>
    /// Bumped whenever the user's roles change. The shell sends it back on later calls so a
    /// permission change can be noticed without polling the whole session.
    /// </summary>
    public required int PermissionVersion { get; init; }

    public bool IsImpersonating { get; init; }

    /// <summary>
    /// Tenants this user can switch between.
    /// <para>
    /// Always one today: a user row belongs to exactly one tenant, so there is no membership table
    /// to read. Consultants and platform support working across several customers need one, and the
    /// shape is here so adding it does not change this contract.
    /// </para>
    /// </summary>
    public IReadOnlyList<TenantSummaryResponse> AvailableTenants { get; init; } = [];
}

/// <summary>
/// Just the permissions, for the services that ask.
/// <para>
/// Separate from the session document because the caller is different: this is another service
/// resolving what the bearer of a token may do, on every request that needs a decision, so it carries
/// nothing it does not need.
/// </para>
/// </summary>
public sealed record PermissionsResponse(int PermissionVersion, IReadOnlyList<PermissionGrantResponse> Permissions);

/// <summary>What the signed-in user may know about themselves.</summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // No permission beyond being signed in. Reading your own identity and your own permissions
        // is not a privilege that could be withheld - a user with no roles still has to load the
        // application to be told they have none.
        endpoints.MapGet("/api/v1/me/session", GetSessionAsync)
            .WithName("CurrentSession")
            .WithTags("Me")
            .RequireAuthorization();

        // Called by every other service on requests that need an authorization decision, which makes
        // it the busiest endpoint on the platform. Deliberately trivial: read the cached set and
        // return it.
        endpoints.MapGet("/api/v1/me/permissions", GetPermissionsAsync)
            .WithName("CurrentPermissions")
            .WithTags("Me")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<Ok<PermissionsResponse>> GetPermissionsAsync(
        IPermissionProvider permissions,
        CancellationToken cancellationToken)
    {
        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

        return TypedResults.Ok(new PermissionsResponse(
            held.PermissionVersion,
            [
                .. held.All
                    .OrderBy(grant => grant.Code, StringComparer.Ordinal)
                    .Select(grant => new PermissionGrantResponse(grant.Code, grant.Scope.ToString(), grant.ScopeId))
            ]));
    }

    private static async Task<Ok<SessionResponse>> GetSessionAsync(
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        ITenantRegistry registry,
        IPermissionProvider permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(tenantContext);

        PermissionSet held = await permissions.GetCurrentAsync(cancellationToken);

        TenantInfo? tenant = tenantContext.IsResolved
            ? await registry.GetAsync(tenantContext.TenantId, cancellationToken)
            : null;

        return TypedResults.Ok(new SessionResponse
        {
            UserId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)
                ? userId
                : Guid.Empty,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            EmployeeId = Guid.TryParse(principal.FindFirstValue(GggsClaims.EmployeeId), out Guid employeeId)
                ? employeeId
                : null,
            UserType = principal.FindFirstValue(GggsClaims.UserType) ?? string.Empty,
            Tenant = tenant is null
                ? null
                : new TenantContextResponse(
                    tenant.Id,
                    tenant.Code,
                    tenant.Name,
                    tenant.CountryCode,
                    tenant.DefaultCurrency,
                    tenant.DefaultTimeZone,
                    tenant.DefaultLocale),
            Permissions =
            [
                .. held.All
                    .OrderBy(grant => grant.Code, StringComparer.Ordinal)
                    .Select(grant => new PermissionGrantResponse(grant.Code, grant.Scope.ToString(), grant.ScopeId))
            ],
            Features = tenant?.EnabledFeatures ?? [],

            // The user's own preference wins over the tenant default, which is why both exist.
            Locale = principal.FindFirstValue(GggsClaims.Locale)
                ?? tenant?.DefaultLocale
                ?? "en",
            TimeZone = principal.FindFirstValue(GggsClaims.TimeZone)
                ?? tenant?.DefaultTimeZone
                ?? "UTC",
            PermissionVersion = held.PermissionVersion,
            IsImpersonating = principal.FindFirstValue(GggsClaims.Impersonator) is not null,
            AvailableTenants = tenant is null
                ? []
                : [new TenantSummaryResponse(tenant.Id, tenant.Code, tenant.Name)],
        });
    }
}
