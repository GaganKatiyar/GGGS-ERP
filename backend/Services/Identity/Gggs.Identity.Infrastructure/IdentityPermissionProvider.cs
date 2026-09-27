using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authorization;
using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Domain.PlatformStaff;
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
    {
        if (!currentUser.IsAuthenticated)
        {
            return PermissionSet.Empty;
        }

        // Two disjoint tables, so which one to ask is decided before either is touched - the same
        // rule the token endpoint uses to decide which one to sign in against in the first place.
        return currentUser.IsPlatformStaff
            ? await GetForPlatformStaffAsync(currentUser.UserId, cancellationToken)
            : await GetForUserAsync(currentUser.UserId, cancellationToken);
    }

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

        // A plain snapshot is cached rather than the PermissionSet itself. PermissionSet is a
        // behaviour type with a constructor the serializer cannot bind to, and more generally a
        // rich type crossing a cache boundary is a type that has to survive being serialized -
        // which is a constraint worth keeping off the domain.
        PermissionSnapshot snapshot = await cache.GetOrCreateAsync(
            CacheAreas.Permissions,
            $"{userId:N}:v{user.PermissionVersion}",
            async token => await ResolveAsync(userId, user.PermissionVersion, token),
            CacheDuration,
            cancellationToken);

        return new PermissionSet(snapshot.Grants, snapshot.PermissionVersion);
    }

    /// <summary>The platform staff equivalent of <see cref="GetForUserAsync"/>. Simpler: no tenant filter, no anchoring, no expiry.</summary>
    public async Task<PermissionSet> GetForPlatformStaffAsync(Guid platformStaffId, CancellationToken cancellationToken = default)
    {
        PlatformStaffMember? member = await dbContext.PlatformStaff
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == platformStaffId, cancellationToken);

        if (member is null)
        {
            return PermissionSet.Empty;
        }

        if (!member.CanSignIn)
        {
            logger.LogInformation("Platform staff {PlatformStaffId} is {Status}; no permissions are granted.", platformStaffId, member.Status);
            return PermissionSet.Empty;
        }

        // The platform-scoped cache, not the tenant-scoped one: a platform staff request never
        // resolves a tenant, and the ordinary GetOrCreateAsync builds its key from
        // ITenantContext.TenantId, which throws exactly because none was resolved. The key space is
        // also naturally separate from every tenant user's, so there is no question of one
        // identity's cached grants ever answering for the other's.
        PermissionSnapshot snapshot = await cache.GetOrCreatePlatformAsync(
            CacheAreas.Permissions,
            $"staff:{platformStaffId:N}:v{member.PermissionVersion}",
            async token => await ResolvePlatformStaffAsync(platformStaffId, member.PermissionVersion, token),
            CacheDuration,
            cancellationToken);

        return new PermissionSet(snapshot.Grants, snapshot.PermissionVersion);
    }

    /// <summary>
    /// Tenant-scoped, like <see cref="IGggsCache.RemoveAreaAsync"/> underneath it - nothing calls
    /// this for a platform staff change today, since <c>PlatformStaffManagementService</c> only
    /// bumps the permission version, and a version-suffixed key is already unreachable once the
    /// version changes. If a caller ever needs to force-invalidate a platform staff member's cache
    /// without bumping the version, this method is not yet that caller's friend.
    /// </summary>
    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Drops the whole area rather than one key. The version-suffixed key for this user is
        // already unreachable once the version changes; this covers the case where a caller
        // invalidates without having bumped it.
        await cache.RemoveAreaAsync(CacheAreas.Permissions, cancellationToken);
        logger.LogInformation("Permission cache invalidated after a change affecting user {UserId}.", userId);
    }

    private async Task<PermissionSnapshot> ResolveAsync(Guid userId, int permissionVersion, CancellationToken cancellationToken)
    {
        List<UserRole> grants = await dbContext.UserRoles
            .AsNoTracking()
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
        {
            return new PermissionSnapshot([], permissionVersion);
        }

        List<Guid> roleIds = [.. grants.Select(grant => grant.RoleId).Distinct()];

        Dictionary<Guid, Role> roles = await dbContext.Roles
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        PermissionSet resolved = PermissionResolver.Resolve(grants, roles, today, permissionVersion);
        return new PermissionSnapshot([.. resolved.All], permissionVersion);
    }

    private async Task<PermissionSnapshot> ResolvePlatformStaffAsync(
        Guid platformStaffId,
        int permissionVersion,
        CancellationToken cancellationToken)
    {
        List<PlatformRoleAssignment> grants = await dbContext.PlatformRoleAssignments
            .AsNoTracking()
            .Where(grant => grant.PlatformStaffId == platformStaffId)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
        {
            return new PermissionSnapshot([], permissionVersion);
        }

        List<Guid> roleIds = [.. grants.Select(grant => grant.PlatformRoleId).Distinct()];

        Dictionary<Guid, PlatformRole> roles = await dbContext.PlatformRoles
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, cancellationToken);

        PermissionSet resolved = PlatformPermissionResolver.Resolve(grants, roles, permissionVersion);
        return new PermissionSnapshot([.. resolved.All], permissionVersion);
    }
}

/// <summary>
/// The cacheable form of a permission set: plain data with a parameterless-friendly shape.
/// <para>
/// Separate from <see cref="PermissionSet"/> on purpose. Anything stored in a distributed cache has
/// to round-trip through JSON, and discovering that only when a second instance reads the entry -
/// rather than at compile time - is how a cache bug reaches production looking like an
/// authorization outage.
/// </para>
/// </summary>
public sealed record PermissionSnapshot(IReadOnlyList<PermissionGrant> Grants, int PermissionVersion);
