namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// Supplies the effective permission set for a user in a tenant.
/// <para>
/// Permissions are deliberately kept out of the access token: a full HR role can hold hundreds of
/// codes, and a token minted at login would keep working for its lifetime after an admin revoked a
/// role. Instead the set is cached in Redis under the permission version from the token, so a
/// revocation takes effect on the next request.
/// </para>
/// </summary>
public interface IPermissionProvider
{
    /// <summary>
    /// The permissions held by the current user in the current tenant. Implementations must treat a
    /// permission-version mismatch as a cache miss and reload.
    /// </summary>
    Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<PermissionSet> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached set after a role or permission change.</summary>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
