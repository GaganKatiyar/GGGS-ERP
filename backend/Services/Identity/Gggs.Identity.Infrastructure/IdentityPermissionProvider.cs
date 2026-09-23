using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authorization;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Infrastructure;

/// <summary>
/// The real permission provider, replacing the claims-based stub.
/// <para>
/// Permissions are resolved from role grants and cached per user, keyed by their permission
/// version. That version is the whole reason this is not simply put in the token: a token carrying
/// permissions keeps working until it expires, so revoking a role would take effect whenever the
/// token happened to run out. Here, bumping the version on the user makes the next request miss
/// the cache and rebuild - which is what an administrator expects when they remove a role.
/// </para>
/// </summary>
public sealed class IdentityPermissionProvider(
    IdentityDbContext dbContext,
    IGggsCache cache,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<IdentityPermissionProvider> logger) : IPermissionProvider
{
    /// <summary>
    /// Long, because the key already contains the permission version: a role change invalidates by
    /// changing the key rather than by waiting for a timeout. The duration only bounds how long a
    /// stale entry could survive some other kind of drift.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public async Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
        => currentUser.IsAuthenticated
            ? await GetForUserAsync(currentUser.UserId, cancellationToken)
            : PermissionSet.Empty;

    public async Task<PermissionSet> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User? user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return PermissionSet.Empty;
        }

        // A disabled or locked user holds nothing, whatever their roles say. Checked here as well
        // as at sign-in, because a session that was already open must stop working too.
        if (!user.CanSignIn)
        {
            logger.LogInformation("User {UserId} is {Status}; no permissions are granted.", userId, user.Status);
            return PermissionSet.Empty;
        }

        return await cache.GetOrCreateAsync(
            CacheAreas.Permissions,
            $"{userId:N}:v{user.PermissionVersion}",
            async token => await ResolveAsync(userId, user.PermissionVersion, token),
            CacheDuration,
            cancellationToken);
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Drops the whole area rather than one key. The version-suffixed key for this user is
        // already unreachable once the version changes; this covers the case where a caller
        // invalidates without having bumped it.
        await cache.RemoveAreaAsync(CacheAreas.Permissions, cancellationToken);
        logger.LogInformation("Permission cache invalidated after a change affecting user {UserId}.", userId);
    }

    private async Task<PermissionSet> ResolveAsync(Guid userId, int permissionVersion, CancellationToken cancellationToken)
    {
        List<UserRole> grants = await dbContext.UserRoles
            .AsNoTracking()
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
        {
            return new PermissionSet([], permissionVersion);
        }

        List<Guid> roleIds = [.. grants.Select(grant => grant.RoleId).Distinct()];

        Dictionary<Guid, Role> roles = await dbContext.Roles
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return PermissionResolver.Resolve(grants, roles, today, permissionVersion);
    }
}
