using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Identity.Application.Provisioning;

/// <summary>An invitation for an administrator, and when it stops being usable.</summary>
public sealed record AdministratorInvitation(Guid UserId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Hands an organisation's administrator account to its owner.
/// <para>
/// This is the one chicken-and-egg problem in the whole authorization model: only a tenant's own
/// administrator can invite anyone, and at provisioning there is no administrator signed in. So
/// platform staff can do exactly two things inside a tenant: re-send the invitation of an
/// administrator nobody has signed into yet, and invite one more administrator.
/// </para>
/// <para>
/// The reach is deliberately tiny. Re-sending works only for an account that is still
/// <see cref="UserStatus.Invited"/> and holds the seeded administrator role; the moment the customer
/// completes the invitation, it stops working for them. Either way the invitation goes to the
/// person's inbox: platform staff never see the token. Neither grants any access to the tenant's
/// HR data.
/// </para>
/// <para>
/// The caller must have set the tenant (the console does so through <c>ITenantScopes</c>, in a scope
/// of its own), so every lookup here is narrowed to that one organisation.
/// </para>
/// </summary>
public sealed class TenantBootstrapService(
    IUserRepository users,
    IRoleRepository roles,
    IInvitationTokenGenerator tokenGenerator,
    IIntegrationEventPublisher publisher,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<TenantBootstrapService> logger)
{
    /// <summary>How long an administrator invitation lasts.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    /// <summary>
    /// Issues a fresh invitation for a pending administrator and emails it. Any earlier link stops
    /// working.
    /// </summary>
    public async Task<AdministratorInvitation> IssueAdministratorInvitationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        User? user = await users.FindByEmailAsync(User.NormalizeEmailForLookup(email), cancellationToken);

        if (user is null || user.Status != UserStatus.Invited)
        {
            // One message whether the account does not exist or has already been used. An operator
            // with the tenant in front of them does not need the difference, and a probe should not
            // get it.
            logger.LogWarning("Administrator invitation refused: no invited account matches the address given.");
            throw NotAvailable();
        }

        if (!await HoldsAdministratorRoleAsync(user.Id, cancellationToken))
        {
            logger.LogWarning(
                "Administrator invitation refused for user {UserId}: not an administrator.",
                user.Id);
            throw NotAvailable();
        }

        (string token, string tokenHash) = tokenGenerator.Generate();
        DateTime expires = now.Add(InvitationLifetime);
        user.SetInvitationToken(tokenHash, expires);

        Publish(user, token, expires);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Administrator invitation issued for {UserId}.", user.Id);

        return new AdministratorInvitation(user.Id, token, expires);
    }

    /// <summary>Invites one more administrator and emails them.</summary>
    public async Task<AdministratorInvitation> AddAdministratorAsync(
        string email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        if (await users.EmailExistsAsync(User.NormalizeEmailForLookup(email), cancellationToken))
        {
            throw new DomainRuleException(
                "USER_EMAIL_IN_USE",
                "Someone with that email address already has an account in this organisation.");
        }

        IReadOnlyDictionary<string, Role> templateRoles = await roles.GetTemplateRolesAsync(cancellationToken);

        if (!templateRoles.TryGetValue(RoleTemplates.TenantAdministratorTemplateCode, out Role? administratorRole))
        {
            throw new DomainRuleException(
                "ORGANISATION_NOT_READY",
                "This organisation is still being set up. Try again in a minute.");
        }

        User user = User.Invite(
            email,
            string.IsNullOrWhiteSpace(displayName) ? DisplayNameFor(email) : displayName.Trim(),
            UserType.TenantUser,
            employeeId: null,
            now);

        (string token, string tokenHash) = tokenGenerator.Generate();
        DateTime expires = now.Add(InvitationLifetime);
        user.SetInvitationToken(tokenHash, expires);

        users.Add(user);
        roles.AddGrant(UserRole.Grant(user.Id, administratorRole.Id, DateOnly.FromDateTime(now)));

        Publish(user, token, expires);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Administrator {UserId} invited by platform staff.", user.Id);

        return new AdministratorInvitation(user.Id, token, expires);
    }

    internal static string DisplayNameFor(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? email[..at] : email;
    }

    private void Publish(User user, string token, DateTime expires) =>
        publisher.Publish(new UserInvitedIntegrationEvent(
            user.Id, user.Email, user.DisplayName, token, expires,
            tenantContext.Tenant?.Code ?? string.Empty));

    private static DomainRuleException NotAvailable() => new(
        "BOOTSTRAP_NOT_AVAILABLE",
        "No pending administrator account matches that address in this organisation.");

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
