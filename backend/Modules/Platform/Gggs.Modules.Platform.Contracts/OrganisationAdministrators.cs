namespace Gggs.Modules.Platform.Contracts;

/// <summary>An organisation's administrator, as reported to the platform console.</summary>
public sealed record OrganisationAdministratorInfo(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    DateTime? LastLoginDate,
    DateTime? InvitationExpiresDate);

/// <summary>
/// The administrators of the current organisation, for the platform console's requests. Implemented by
/// Identity; called by the Platform module's sync job inside the organisation (the tenant is set by the
/// caller), so every lookup is filtered and row-level-secured as usual.
/// <para>
/// The invitation itself is always emailed to the person. Platform staff never see a token.
/// </para>
/// </summary>
public interface IOrganisationAdministrators
{
    Task<IReadOnlyList<OrganisationAdministratorInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Invites one more administrator. Returns what happened, in words.</summary>
    Task<string> AddAsync(string email, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Emails a fresh invitation to an administrator nobody has signed into yet.</summary>
    Task<string> ResendInvitationAsync(string email, CancellationToken cancellationToken = default);
}
