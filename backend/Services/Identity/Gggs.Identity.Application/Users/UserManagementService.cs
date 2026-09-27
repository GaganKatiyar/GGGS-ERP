using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.Users;

/// <summary>An invitation, and the token that must reach the person rather than the database.</summary>
public sealed record InviteUserResult(Guid UserId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Creating, changing and removing accounts.
/// <para>
/// Several rules here exist to stop an administrator locking their own organization out. They are
/// enforced in this service rather than the UI because the API is what actually has to refuse: a
/// disabled confirmation dialog is not a safeguard.
/// </para>
/// </summary>
public sealed class UserManagementService(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordPolicyRepository policies,
    IPasswordHasher passwordHasher,
    IBreachedPasswordChecker breachChecker,
    IInvitationTokenGenerator tokenGenerator,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<UserManagementService> logger)
{
    /// <summary>How long an invitation stays usable. Long enough for a holiday, short enough to matter.</summary>
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    /// <summary>
    /// Invites someone. The returned token is the only copy: it is emailed, and what is stored is
    /// a hash, so a leaked database does not hand over the accounts nobody has signed into yet.
    /// </summary>
    public async Task<InviteUserResult> InviteAsync(
        string email,
        string displayName,
        UserType userType,
        Guid? employeeId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        string normalized = User.NormalizeEmailForLookup(email);

        if (await users.EmailExistsAsync(normalized, cancellationToken))
        {
            throw new DomainRuleException(
                "USER_EMAIL_IN_USE",
                "Someone with that email address already has an account in this organization.");
        }

        User user = User.Invite(email, displayName, userType, employeeId, now);

        (string token, string tokenHash) = tokenGenerator.Generate();
        user.SetInvitationToken(tokenHash, now.Add(InvitationLifetime));

        users.Add(user);

        foreach (Guid roleId in roleIds ?? [])
        {
            Role role = await roles.GetAsync(roleId, cancellationToken)
                ?? throw new NotFoundException(nameof(Role), roleId);

            roles.AddGrant(UserRole.Grant(user.Id, role.Id, DateOnly.FromDateTime(now)));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} invited with {RoleCount} role(s).", user.Id, roleIds?.Count ?? 0);

        return new InviteUserResult(user.Id, token, now.Add(InvitationLifetime));
    }

    /// <summary>
    /// Completes an invitation. Runs anonymously - the token is the credential - so it is
    /// deliberately narrow: it sets a first password and nothing else.
    /// </summary>
    public async Task CompleteInvitationAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        User user = await users.GetAsync(userId, cancellationToken)
            ?? throw new DomainRuleException("INVITATION_INVALID", "That invitation is no longer valid.");

        if (!user.IsInvitationTokenValid(tokenGenerator.HashFor(token), now))
        {
            // One message for a wrong token, an expired one and a used one. Telling them apart
            // would let someone probe which invitations exist.
            logger.LogInformation("Invitation completion refused for user {UserId}: token invalid or expired.", userId);
            throw new DomainRuleException("INVITATION_INVALID", "That invitation is no longer valid.");
        }

        PasswordPolicy policy = await policies.GetAsync(cancellationToken);
        await EnsurePasswordIsAcceptableAsync(user, newPassword, policy, cancellationToken);

        user.CompleteInvitation(passwordHasher.Hash(newPassword), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} completed their invitation.", userId);
    }

    /// <summary>
    /// Changes the signed-in user's own password, which requires the current one.
    /// <para>
    /// Requiring it is what stops someone at an unlocked laptop from taking the account over: they
    /// could otherwise set a new password and lock the owner out.
    /// </para>
    /// </summary>
    public async Task ChangeOwnPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        User user = await users.GetAsync(currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), currentUser.UserId);

        if (user.PasswordHash is null
            || passwordHasher.Verify(user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            throw new ForbiddenException("CURRENT_PASSWORD_INCORRECT", "The current password is not correct.");
        }

        PasswordPolicy policy = await policies.GetAsync(cancellationToken);
        await EnsurePasswordIsAcceptableAsync(user, newPassword, policy, cancellationToken);

        user.SetPassword(passwordHasher.Hash(newPassword), policy, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Disables an account.
    /// <para>
    /// Refuses to disable the caller's own account, and refuses to remove the last person who can
    /// administer users. Either would leave the organization unable to manage itself, recoverable
    /// only by us reaching into their database.
    /// </para>
    /// </summary>
    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == currentUser.UserId)
        {
            throw new DomainRuleException(
                "CANNOT_DISABLE_SELF",
                "You cannot disable your own account. Ask another administrator to do it.");
        }

        User user = await users.GetAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        await EnsureNotLastAdministratorAsync(userId, cancellationToken);

        user.Disable();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning("User {UserId} disabled by {ActorId}.", userId, currentUser.UserId);
    }

    public async Task EnableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User user = await users.GetAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        user.Enable();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Clears a lockout early, so somebody who is simply locked out need not wait it out.</summary>
    public async Task UnlockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User user = await users.GetAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        user.Unlock();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Replaces a user's roles, bumping their permission version so the change is immediate.</summary>
    public async Task SetRolesAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        User user = await users.GetAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        IReadOnlyList<UserRole> existing = await roles.GetGrantsForUserAsync(userId, cancellationToken);

        // Checked before the change, not after: afterwards there is nobody left to complain to.
        if (existing.Count > 0 && roleIds.Count == 0)
        {
            await EnsureNotLastAdministratorAsync(userId, cancellationToken);
        }

        foreach (UserRole grant in existing.Where(grant => !roleIds.Contains(grant.RoleId)))
        {
            // Ended rather than deleted, so the audit trail can still answer what someone could do
            // last March.
            grant.EndOn(today);
        }

        foreach (Guid roleId in roleIds.Where(id => !existing.Any(grant => grant.RoleId == id && grant.IsActiveOn(today))))
        {
            Role role = await roles.GetAsync(roleId, cancellationToken)
                ?? throw new NotFoundException(nameof(Role), roleId);

            roles.AddGrant(UserRole.Grant(userId, role.Id, today));
        }

        // Without this the cached permission set stays keyed to the old version, and the change
        // would take effect whenever the entry happened to expire.
        user.BumpPermissionVersion();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Roles for user {UserId} replaced with {RoleCount} role(s).", userId, roleIds.Count);
    }

    /// <summary>
    /// Refuses a change that would leave nobody able to administer users.
    /// <para>
    /// A tenant that locks itself out cannot be recovered by its own administrators, by definition.
    /// The only remedy is us reaching into their data, which is exactly the kind of access this
    /// platform is built to avoid needing.
    /// </para>
    /// </summary>
    private async Task EnsureNotLastAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> administrators = await roles.GetUserIdsWithPermissionAsync(
            Permissions.Admin.UserManage,
            cancellationToken);

        if (administrators.Count <= 1 && administrators.Contains(userId))
        {
            throw new DomainRuleException(
                "LAST_ADMINISTRATOR",
                "This is the only account that can manage users. Give someone else that permission first.");
        }
    }

    private async Task EnsurePasswordIsAcceptableAsync(
        User user,
        string password,
        PasswordPolicy policy,
        CancellationToken cancellationToken)
    {
        PasswordValidationResult validation = policy.Validate(password, user.DisplayName, user.Email);

        if (!validation.IsValid)
        {
            throw new DomainRuleException("PASSWORD_REJECTED", string.Join(" ", validation.Failures));
        }

        if (policy.HistoryCount > 0)
        {
            foreach (string previous in user.RecentPasswordHashes(policy.HistoryCount))
            {
                if (passwordHasher.Verify(previous, password) != PasswordVerificationResult.Failed)
                {
                    throw new DomainRuleException(
                        "PASSWORD_REUSED",
                        $"That password was used recently. Choose one you have not used in your last {policy.HistoryCount}.");
                }
            }
        }

        if (policy.RejectBreachedPasswords && await breachChecker.IsBreachedAsync(password, cancellationToken))
        {
            throw new DomainRuleException(
                "PASSWORD_BREACHED",
                "That password has appeared in a known data breach. Please choose another.");
        }
    }
}

/// <summary>Roles and grants, kept as a port so the rules above are testable without a database.</summary>
public interface IRoleRepository
{
    Task<Role?> GetAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserRole>> GetGrantsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Users holding a permission through any active grant. Used for last-administrator checks.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenant's roles that came from a template, keyed by template code.
    /// <para>
    /// Seeding reads this to tell a fresh tenant from one being seeded a second time, which is the
    /// whole of its idempotency: the provisioning event is delivered at least once and is re-run by
    /// hand when a step fails.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, Role>> GetTemplateRolesAsync(CancellationToken cancellationToken = default);

    void Add(Role role);

    void AddGrant(UserRole grant);
}

/// <summary>
/// Produces invitation and reset tokens, and the hashes stored against them.
/// <para>
/// A port because the token is a credential: how it is generated and hashed should be changeable
/// in one place, and testable without guessing at randomness.
/// </para>
/// </summary>
public interface IInvitationTokenGenerator
{
    /// <summary>Returns the token to send and the hash to store.</summary>
    (string Token, string TokenHash) Generate();

    string HashFor(string token);
}
