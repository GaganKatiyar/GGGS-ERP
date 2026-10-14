using System.Globalization;
using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Domain.Users;

namespace Gggs.Modules.Identity.Application.Authentication;

/// <summary>
/// Builds the claims principal every other service reads.
/// <para>
/// This is the contract the whole platform depends on: the tenant middleware takes the tenant from
/// here, the permission provider takes the user and version from here, and the audit trail takes
/// the actor from here. Adding a claim is cheap; changing or removing one breaks services that
/// have already shipped.
/// </para>
/// <para>
/// What is deliberately absent is permissions. A full HR role holds hundreds of codes, and a token
/// carrying them keeps granting them until it expires - so removing a role would not take effect
/// when an administrator expects. The permission version is carried instead, and the set is
/// resolved per request against a cache keyed by it.
/// </para>
/// </summary>
public static class UserPrincipalFactory
{
    /// <summary>Authentication-method values, as the <c>amr</c> claim reports them.</summary>
    public static class AuthenticationMethods
    {
        public const string Password = "pwd";

        /// <summary>A second factor was satisfied; step-up checks look for this.</summary>
        public const string MultiFactor = "mfa";

        /// <summary>Federated. Unused until Entra ID federation ships.</summary>
        public const string ExternalProvider = "ext";
    }

    /// <summary>
    /// Builds the principal for a signed-in user.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant from the <b>user record</b>, never from what the caller asked for. Discovery
    /// decides where to look; this decides what the token says.
    /// </param>
    public static ClaimsPrincipal Create(
        User user,
        Guid tenantId,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        bool mfaSatisfied,
        string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(user);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),

            new(GggsClaims.SessionId, sessionId.ToString()),
            new(GggsClaims.UserType, user.UserType.ToString()),
            new(GggsClaims.PermissionVersion, user.PermissionVersion.ToString(CultureInfo.InvariantCulture)),

            // Step-up checks compare this against the clock, so sensitive actions can demand a
            // recent authentication rather than merely a valid token.
            //
            // Typed as an integer, not left as a bare string. It is a NumericDate in OIDC, and the
            // token issuer rejects a principal whose auth_time is not typed that way - so a plain
            // string claim here fails token issuance while working fine for the cookie.
            new(
                "auth_time",
                authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        ];

        // Platform staff belong to no customer, and the absence of the claim is what the tenant
        // middleware uses to put them in platform scope. Emitting an empty or placeholder tenant
        // would quietly place platform operations inside somebody's boundary.
        if (user.UserType != UserType.PlatformUser)
        {
            claims.Add(new Claim(GggsClaims.TenantId, tenantId.ToString()));
        }

        if (user.EmployeeId is not null)
        {
            claims.Add(new Claim(GggsClaims.EmployeeId, user.EmployeeId.Value.ToString()));
        }

        claims.Add(new Claim("amr", AuthenticationMethods.Password));

        if (mfaSatisfied)
        {
            claims.Add(new Claim("amr", AuthenticationMethods.MultiFactor));
        }

        if (user.PreferredLocale is not null)
        {
            claims.Add(new Claim(GggsClaims.Locale, user.PreferredLocale));
        }

        if (user.TimeZone is not null)
        {
            claims.Add(new Claim(GggsClaims.TimeZone, user.TimeZone));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
