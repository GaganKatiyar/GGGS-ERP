using Gggs.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Fails the host at startup if any endpoint is reachable without a declared permission.
/// <para>
/// This is the mechanism behind "deny by default". Reviews and tests can miss an endpoint that was
/// added without <c>[RequirePermission]</c>; a startup check cannot. An endpoint that really is
/// public says so with <c>[AllowAnonymous]</c>, which is a visible, reviewable decision.
/// </para>
/// </summary>
public static class EndpointSecurityValidator
{
    /// <summary>Route prefixes that serve infrastructure rather than tenant data.</summary>
    private static readonly string[] InfrastructureRoutes =
    [
        "/health",
        "/alive",
        "/metrics",
        "/openapi",
        "/swagger",
        "/scalar",
        "/.well-known",
        "/connect",
        "/hangfire",
    ];

    /// <summary>
    /// Walks the route table and reports endpoints that neither require a permission nor opt out.
    /// </summary>
    public static IReadOnlyList<string> FindUnprotectedEndpoints(IEnumerable<EndpointDataSource> endpointSources)
    {
        ArgumentNullException.ThrowIfNull(endpointSources);

        var unprotected = new List<string>();

        foreach (Endpoint endpoint in endpointSources.SelectMany(source => source.Endpoints))
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            string route = "/" + (routeEndpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty);

            if (InfrastructureRoutes.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            bool hasPermission = endpoint.Metadata
                .OfType<IAuthorizeData>()
                .Any(data => data.Policy?.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal) == true);

            if (!hasPermission)
            {
                unprotected.Add($"{string.Join(',', routeEndpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["*"])} {route}");
            }
        }

        return unprotected;
    }

    /// <summary>
    /// Throws when an unprotected endpoint exists. Called from the host after the app is built, so
    /// a mistake stops the deployment instead of shipping an open endpoint.
    /// </summary>
    public static void ThrowIfAnyUnprotected(IEnumerable<EndpointDataSource> endpointSources, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        IReadOnlyList<string> unprotected = FindUnprotectedEndpoints(endpointSources);
        if (unprotected.Count == 0)
        {
            return;
        }

        foreach (string endpoint in unprotected)
        {
            logger.LogCritical("Endpoint {Endpoint} declares no permission and is not marked [AllowAnonymous].", endpoint);
        }

        throw new InvalidOperationException(
            $"{unprotected.Count} endpoint(s) are missing a [RequirePermission] attribute: {string.Join("; ", unprotected)}");
    }
}
