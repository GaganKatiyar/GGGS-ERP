using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Identity.Contracts;

/*
 * Identity's published contract.
 *
 * Other services reference this package and nothing else of ours. Payloads carry ids plus the few
 * fields a consumer needs to act; anything more is fetched from the API, which applies its own
 * permissions.
 */

/// <summary>
/// Someone was invited and needs to be told, with the link that lets them set a password.
/// <para>
/// <see cref="InvitationToken"/> is the same one-time credential <c>InviteAsync</c> returns to its
/// caller - it is already sensitive there, and it has to reach the recipient's inbox somehow, so
/// carrying it on this event is not a new exposure. It is never logged: <c>OutboundMessage</c>
/// stores the rendered email body, not this payload, and the message envelope itself is the only
/// other place it briefly exists in transit.
/// </para>
/// </summary>
[MessageType("gggs.identity.user.invited")]
public sealed record UserInvitedIntegrationEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    string InvitationToken,
    DateTime ExpiresDate,

    /// <summary>Needed to build the completion link (<c>/complete-invitation?tenantCode=...</c>) -
    /// the login-discovery slug, not a secret, so carrying it here is unremarkable next to the
    /// token.</summary>
    string TenantCode) : IntegrationEvent;
