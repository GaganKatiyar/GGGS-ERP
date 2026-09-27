using System.Globalization;
using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Domain.PlatformStaff;

namespace Gggs.Identity.Application.PlatformStaff;

/// <summary>
/// Builds the claims principal for platform staff.
/// <para>
/// The mirror of <see cref="Authentication.UserPrincipalFactory"/>, with the one difference that
/// matters everywhere downstream: there is no <c>tid</c> claim, because platform staff belong to no
/// tenant. Its absence, plus <see cref="GggsClaims.UserTypes.Platform"/> in the <c>utyp</c> claim,
/// is what <c>TenantContextMiddleware</c> reads to grant platform scope instead of demanding a
/// tenant it cannot supply.
/// </para>
/// <para>
/// Permissions are absent here for the same reason they are absent from a tenant user's token: a
/// full platform role holds a handful of codes today but the token would still outlive a revoked
/// one. The permission version travels instead, and the set is resolved per request.
/// </para>
/// </summary>
public static class PlatformStaffPrincipalFactory
{
    public static ClaimsPrincipal Create(
        PlatformStaffMember member,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(member);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, member.Id.ToString()),
            new("sub", member.Id.ToString()),
            new(ClaimTypes.Name, member.DisplayName),
            new(ClaimTypes.Email, member.Email),

            new(GggsClaims.UserType, GggsClaims.UserTypes.Platform),
            new(GggsClaims.SessionId, sessionId.ToString()),
            new(GggsClaims.PermissionVersion, member.PermissionVersion.ToString(CultureInfo.InvariantCulture)),

            // Typed as an integer: OpenIddict refuses to sign a principal whose auth_time is not a
            // NumericDate, which is the same reason the tenant factory types it this way.
            new(
                "auth_time",
                authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),

            new("amr", "pwd"),
        ];

        // No GggsClaims.TenantId claim. Its absence is not an oversight to fill in later - it is
        // the whole signal TenantContextMiddleware acts on.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
