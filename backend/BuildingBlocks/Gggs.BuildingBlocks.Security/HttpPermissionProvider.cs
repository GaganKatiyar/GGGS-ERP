using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Security;

/// <summary>The shape Identity answers with.</summary>
public sealed record RemotePermissionsResponse(int PermissionVersion, IReadOnlyList<PermissionGrant> Permissions);

/// <summary>
/// Resolves permissions by asking the Identity service.
/// <para>
/// This is the other half of the decision not to put permissions in the token. The token carries a
/// permission version, not a permission list, so removing a role takes effect on the next request
/// rather than whenever the token happens to expire - but it means every service has to ask someone.
/// That someone is Identity, and this is how they ask.
/// </para>
/// <para>
/// The caller's own token is forwarded rather than a service credential, so Identity answers "the
/// permissions of the bearer of this token" and needs no special trust in the calling service. A
/// service cannot use this to ask about somebody else.
/// </para>
/// <para>
/// Cached per user and per permission version. The version is in the cache key, so a role change
/// makes the next request miss rather than waiting for a timeout - the same mechanism the Identity
/// service uses on its own cache.
/// </para>
/// </summary>
public sealed class HttpPermissionProvider(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    IGggsCache cache,
    ICurrentUser currentUser,
    ILogger<HttpPermissionProvider> logger) : IPermissionProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Matches how the APIs serialize: camelCase, and enums as names.
    /// <para>
    /// Spelled out here rather than taken from the host's options because this is a client, and a
    /// client that happens to work because the server shares its process is a client that breaks the
    /// day it does not. <c>DataScope</c> arrives as "Tenant", and without the converter every lookup
    /// would fail to deserialize - which would read as a permission outage.
    /// </para>
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return PermissionSet.Empty;
        }

        string? token = ReadBearerToken();

        if (token is null)
        {
            // Authenticated by something other than a bearer token - a cookie, or a test scheme.
            // There is nothing to forward, so nothing can be resolved, and granting nothing is the
            // only safe answer.
            logger.LogWarning("The request has no bearer token to forward; no permissions were resolved.");
            return PermissionSet.Empty;
        }

        int permissionVersion = ReadPermissionVersion();

        RemotePermissionsResponse resolved = await cache.GetOrCreateAsync(
            CacheAreas.Permissions,
            $"{currentUser.UserId:N}:v{permissionVersion}",
            async ct => await FetchAsync(token, ct),
            CacheDuration,
            cancellationToken);

        return new PermissionSet(resolved.Permissions, resolved.PermissionVersion);
    }

    /// <summary>
    /// Not supported here.
    /// <para>
    /// Asking about another user needs authority this service does not have: it holds the caller's
    /// token, and that token speaks only for the caller. Anything that needs someone else's
    /// permissions - an administration screen showing what a colleague can do - belongs in Identity,
    /// where the question can be authorized properly.
    /// </para>
    /// </summary>
    public Task<PermissionSet> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "A service cannot resolve another user's permissions; ask the Identity service instead.");

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
        => await cache.RemoveAreaAsync(CacheAreas.Permissions, cancellationToken);

    private async Task<RemotePermissionsResponse> FetchAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/permissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The token was rejected by Identity even though it validated here: the account has been
            // disabled, or its tenant suspended, since the token was minted. Granting nothing is
            // correct, and is not cached under a version that would make it sticky.
            logger.LogInformation("Identity refused a permission lookup with {StatusCode}.", (int)response.StatusCode);
            return new RemotePermissionsResponse(0, []);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RemotePermissionsResponse>(SerializerOptions, cancellationToken)
            ?? new RemotePermissionsResponse(0, []);
    }

    private string? ReadBearerToken()
    {
        string? header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        return !string.IsNullOrWhiteSpace(header)
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
    }

    /// <summary>
    /// The version from the token, which is what makes the cache key change when roles change.
    /// <para>
    /// Zero when absent, which simply means the first lookup for that user is not shared with a later
    /// one - a cache miss, not a security hole, because the answer still comes from Identity.
    /// </para>
    /// </summary>
    private int ReadPermissionVersion()
    {
        string? value = httpContextAccessor.HttpContext?.User
            .FindFirst(MultiTenancy.GggsClaims.PermissionVersion)?.Value;

        return int.TryParse(value, out int version) ? version : 0;
    }
}
