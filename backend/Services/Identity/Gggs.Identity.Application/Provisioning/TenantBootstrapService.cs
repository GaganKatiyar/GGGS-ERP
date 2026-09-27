using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.Users;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.Provisioning;

/// <summary>An invitation for the first administrator, and when it stops being usable.</summary>
public sealed record AdministratorInvitation(Guid UserId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Issues the credential that lets a brand-new tenant be opened for the first time.
/// <para>
/// This is the one chicken-and-egg problem in the whole authorization model: only a tenant's own
/// administrator can invite anyone, and at provisioning there is no administrator signed in. Seeding
/// creates the account but cannot deliver an invitation, so someone has to be able to issue one from
/// outside the tenant.
/// </para>
/// <para>
/// That someone is platform staff, and the reach is deliberately tiny. It works only for an account
/// that is still <see cref="UserStatus.Invited"/> - nobody has ever signed into it - and only if
/// that account holds the seeded administrator role. The moment the customer completes the
/// invitation, this stops working for them. It grants no access to the tenant's HR data, only the
/// ability to hand over the first key to a door platform staff created.
/// </para>
/// </summary>
public sealed class TenantBootstrapService(
    IUserRepository users,
    IRoleRepository roles,
    IInvitationTokenGenerator tokenGenerator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<TenantBootstrapService> logger)
{
    /// <summary>How long a bootstrap invitation lasts. Short: it is handed over during onboarding.</summary>
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    public async Task<AdministratorInvitation> IssueAdministratorInvitationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        // The tenant context is already set by the caller, so this lookup cannot reach into
        // another customer even though the acting user is platform staff.
        User? user = await users.FindByEmailAsync(User.NormalizeEmailForLookup(email), cancellationToken);

        if (user is null || user.Status != UserStatus.Invited)
        {
            // One message whether the account does not exist or has already been used. An operator
            // with the tenant in front of them does not need the difference, and a probe should not
            // get it.
            logger.LogWarning("Bootstrap invitation refused: no invited account matches the address given.");

            throw new DomainRuleException(
                "BOOTSTRAP_NOT_AVAILABLE",
                "No pending administrator account matches that address in this tenant.");
        }

        if (!await HoldsAdministratorRoleAsync(user.Id, cancellationToken))
        {
            logger.LogWarning(
                "Bootstrap invitation refused for user {UserId}: not the seeded administrator.",
                user.Id);

            throw new DomainRuleException(
                "BOOTSTRAP_NOT_AVAILABLE",
                "No pending administrator account matches that address in this tenant.");
        }

        (string token, string tokenHash) = tokenGenerator.Generate();
        user.SetInvitationToken(tokenHash, now.Add(InvitationLifetime));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Logged without the token. Issuing one is an event worth having in the audit trail; the
        // credential itself belongs only in the response.
        logger.LogInformation("Bootstrap invitation issued for administrator {UserId}.", user.Id);

        return new AdministratorInvitation(user.Id, token, now.Add(InvitationLifetime));
    }

    private async Task<bool> HoldsAdministratorRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserRole> grants = await roles.GetGrantsForUserAsync(userId, cancellationToken);

        foreach (UserRole grant in grants)
        {
            Role? role = await roles.GetAsync(grant.RoleId, cancellationToken);

            if (string.Equals(
                    role?.TemplateCode,
                    RoleTemplates.TenantAdministratorTemplateCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
