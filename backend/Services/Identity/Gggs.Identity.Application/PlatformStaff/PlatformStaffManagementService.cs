using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.Users;
using Gggs.Identity.Domain.PlatformStaff;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.PlatformStaff;

/// <summary>An invitation for a new platform staff member, and the token that must reach them rather than the database.</summary>
public sealed record InvitePlatformStaffResult(Guid PlatformStaffId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Creating, changing and removing platform staff accounts.
/// <para>
/// The mirror of <c>UserManagementService</c>, and it carries the same rule for the same reason: an
/// account able to remove the last account able to grant platform access would lock the platform
/// out of itself, with nobody but us able to fix it - which for this table means reaching into
/// production, not into a customer's data.
/// </para>
/// </summary>
public sealed class PlatformStaffManagementService(
    IPlatformStaffRepository staff,
    IPlatformRoleRepository roles,
    IPasswordHasher passwordHasher,
    IInvitationTokenGenerator tokenGenerator,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PlatformStaffManagementService> logger)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    public async Task<InvitePlatformStaffResult> InviteAsync(
        string email,
        string displayName,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        string normalized = PlatformStaffMember.NormalizeEmailForLookup(email);

        if (await staff.EmailExistsAsync(normalized, cancellationToken))
        {
            throw new DomainRuleException(
                "PLATFORM_STAFF_EMAIL_IN_USE",
                "Someone with that email address already has a platform staff account.");
        }

        PlatformStaffMember member = PlatformStaffMember.Invite(email, displayName, now);

        (string token, string tokenHash) = tokenGenerator.Generate();
        member.SetInvitationToken(tokenHash, now.Add(InvitationLifetime));

        staff.Add(member);

        foreach (Guid roleId in roleIds ?? [])
        {
            PlatformRole role = await roles.GetAsync(roleId, cancellationToken)
                ?? throw new NotFoundException(nameof(PlatformRole), roleId);

            roles.AddGrant(PlatformRoleAssignment.Grant(member.Id, role.Id));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Platform staff {PlatformStaffId} invited with {RoleCount} role(s) by {ActorId}.",
            member.Id,
            roleIds?.Count ?? 0,
            currentUser.UserId);

        return new InvitePlatformStaffResult(member.Id, token, now.Add(InvitationLifetime));
    }

    public async Task CompleteInvitationAsync(
        Guid platformStaffId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        PlatformStaffMember member = await staff.GetAsync(platformStaffId, cancellationToken)
            ?? throw new DomainRuleException("INVITATION_INVALID", "That invitation is no longer valid.");

        if (!member.IsInvitationTokenValid(tokenGenerator.HashFor(token), now))
        {
            logger.LogInformation("Invitation completion refused for platform staff {PlatformStaffId}: token invalid or expired.", platformStaffId);
            throw new DomainRuleException("INVITATION_INVALID", "That invitation is no longer valid.");
        }

        EnsurePasswordIsAcceptable(member, newPassword);

        member.CompleteInvitation(passwordHasher.Hash(newPassword), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Platform staff {PlatformStaffId} completed their invitation.", platformStaffId);
    }

    public async Task ChangeOwnPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        PlatformStaffMember member = await staff.GetAsync(currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(PlatformStaffMember), currentUser.UserId);

        if (member.PasswordHash is null
            || passwordHasher.Verify(member.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            throw new ForbiddenException("CURRENT_PASSWORD_INCORRECT", "The current password is not correct.");
        }

        EnsurePasswordIsAcceptable(member, newPassword);

        member.SetPassword(passwordHasher.Hash(newPassword), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableAsync(Guid platformStaffId, CancellationToken cancellationToken = default)
    {
        if (platformStaffId == currentUser.UserId)
        {
            throw new DomainRuleException(
                "CANNOT_DISABLE_SELF",
                "You cannot disable your own platform account. Ask another platform administrator to do it.");
        }

        PlatformStaffMember member = await staff.GetAsync(platformStaffId, cancellationToken)
            ?? throw new NotFoundException(nameof(PlatformStaffMember), platformStaffId);

        await EnsureNotLastStaffManagerAsync(platformStaffId, cancellationToken);

        member.Disable();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Platform staff {PlatformStaffId} disabled by {ActorId}.", platformStaffId, currentUser.UserId);
    }

    public async Task UnlockAsync(Guid platformStaffId, CancellationToken cancellationToken = default)
    {
        PlatformStaffMember member = await staff.GetAsync(platformStaffId, cancellationToken)
            ?? throw new NotFoundException(nameof(PlatformStaffMember), platformStaffId);

        member.Unlock();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetRolesAsync(
        Guid platformStaffId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        PlatformStaffMember member = await staff.GetAsync(platformStaffId, cancellationToken)
            ?? throw new NotFoundException(nameof(PlatformStaffMember), platformStaffId);

        IReadOnlyList<PlatformRoleAssignment> existing = await roles.GetGrantsForStaffAsync(platformStaffId, cancellationToken);

        // Checked before the change, not after: afterwards there is nobody left to complain to.
        if (existing.Count > 0 && roleIds.Count == 0)
        {
            await EnsureNotLastStaffManagerAsync(platformStaffId, cancellationToken);
        }

        foreach (PlatformRoleAssignment grant in existing.Where(grant => !roleIds.Contains(grant.PlatformRoleId)))
        {
            roles.RemoveGrant(grant);
        }

        foreach (Guid roleId in roleIds.Where(id => !existing.Any(grant => grant.PlatformRoleId == id)))
        {
            PlatformRole role = await roles.GetAsync(roleId, cancellationToken)
                ?? throw new NotFoundException(nameof(PlatformRole), roleId);

            roles.AddGrant(PlatformRoleAssignment.Grant(platformStaffId, role.Id));
        }

        member.BumpPermissionVersion();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Roles for platform staff {PlatformStaffId} replaced with {RoleCount} role(s).", platformStaffId, roleIds.Count);
    }

    /// <summary>
    /// Refuses to leave nobody holding <see cref="Permissions.Platform.StaffManage"/>.
    /// <para>
    /// The permission that can grant platform access at all is the one this guards, mirroring the
    /// tenant equivalent's guard on <c>admin.user.manage</c>.
    /// </para>
    /// </summary>
    private async Task EnsureNotLastStaffManagerAsync(Guid platformStaffId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> managers = await roles.GetStaffIdsWithPermissionAsync(
            Permissions.Platform.StaffManage,
            cancellationToken);

        if (managers.Count <= 1 && managers.Contains(platformStaffId))
        {
            throw new DomainRuleException(
                "LAST_PLATFORM_ADMINISTRATOR",
                "This is the only account that can manage platform staff. Give someone else that permission first.");
        }
    }

    private static void EnsurePasswordIsAcceptable(PlatformStaffMember member, string password)
    {
        IReadOnlyList<string> failures = PlatformStaffMember.Validate(password, member.DisplayName, member.Email);

        if (failures.Count > 0)
        {
            throw new DomainRuleException("PASSWORD_REJECTED", string.Join(" ", failures));
        }
    }
}

/// <summary>Persistence for platform roles and grants, kept as a port so this stays testable without a database.</summary>
public interface IPlatformRoleRepository
{
    Task<PlatformRole?> GetAsync(Guid platformRoleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformRoleAssignment>> GetGrantsForStaffAsync(Guid platformStaffId, CancellationToken cancellationToken = default);

    /// <summary>Staff holding a permission through any role. Used for last-administrator checks.</summary>
    Task<IReadOnlyList<Guid>> GetStaffIdsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>The system roles, keyed by template code. Seeding reads this to stay idempotent.</summary>
    Task<IReadOnlyDictionary<string, PlatformRole>> GetSystemRolesAsync(CancellationToken cancellationToken = default);

    void Add(PlatformRole role);

    void AddGrant(PlatformRoleAssignment grant);

    void RemoveGrant(PlatformRoleAssignment grant);
}
