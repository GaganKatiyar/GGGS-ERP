using Gggs.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Declares the permission an endpoint requires:
/// <c>group.MapPost("/", CreateAsync).RequirePermission(Permissions.Leave.RequestCreate);</c>
/// <para>
/// A thin wrapper over the policy name, but it keeps the declaration readable and, more usefully,
/// greppable: "who can approve leave" should be answerable by searching for the permission code,
/// not by decoding a constructed policy string.
/// </para>
/// </summary>
public static class EndpointPermissionExtensions
{
    /// <summary>Requires any one of the listed permissions.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(permissions);

        if (permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }

        return builder.RequireAuthorization(
            RequirePermissionAttribute.BuildPolicyName(permissions, PermissionMode.Any));
    }

    /// <summary>Requires every listed permission, for endpoints that combine two capabilities.</summary>
    public static TBuilder RequireAllPermissions<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(permissions);

        return builder.RequireAuthorization(
            RequirePermissionAttribute.BuildPolicyName(permissions, PermissionMode.All));
    }
}
