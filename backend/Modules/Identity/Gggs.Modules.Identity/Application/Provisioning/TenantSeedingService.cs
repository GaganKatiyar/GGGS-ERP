using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Identity.Application.Provisioning;

/// <summary>What seeding found and did, so an operator can tell a fresh run from a repeat.</summary>
public sealed record TenantSeedingResult(int RolesCreated, int RolesAlreadyPresent, Guid? AdministratorUserId);

/// <summary>
/// Gives a newly provisioned tenant the roles and the first administrator it needs to be usable.
/// <para>
/// Runs from the <c>TenantProvisioned</c> event rather than from a call by the Platform service, so
/// a broker outage delays a tenant becoming usable instead of failing the provisioning request that
/// already committed.
/// </para>
/// <para>
/// Idempotent, and specifically idempotent in a way that respects the customer: a second run
/// creates only what is missing and <b>never re-grants permissions on a role that already exists</b>.
/// Topping a role back up to the template would quietly undo an administrator's decision to narrow
/// it - the redelivery of a months-old message would widen access nobody asked to widen.
/// </para>
/// </summary>
public sealed class TenantSeedingService(
    IRoleRepository roles,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<TenantSeedingService> logger)
{
    public async Task<TenantSeedingResult> SeedAsync(
        string administratorEmail,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        IReadOnlyDictionary<string, Role> existing = await roles.GetTemplateRolesAsync(cancellationToken);

        var created = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

        foreach (RoleTemplate template in RoleTemplates.Definitions)
        {
            if (existing.ContainsKey(template.Code))
            {
                continue;
            }

            Role role = Role.Create(template.Name, template.Description, isSystem: true, templateCode: template.Code);

            if (template.Permissions.Count == 0)
            {
                // Platform and Identity have drifted: a role with no permissions is created anyway
                // so the tenant is not left short of a role, but it is worth an operator's attention.
                logger.LogWarning(
                    "Role template {TemplateCode} has no permissions defined; seeding it empty.",
                    template.Code);
            }

            foreach (PermissionAssignment permission in template.Permissions)
            {
                role.Grant(permission.PermissionCode, permission.Scope);
            }

            roles.Add(role);
            created[template.Code] = role;
        }

        Guid? administratorUserId = await EnsureAdministratorAsync(
            administratorEmail,
            existing,
            created,
            now,
            cancellationToken);

        // One transaction for the roles, the administrator and their grant. A tenant with roles but
        // no administrator, or an administrator holding none of them, is not a usable halfway state.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Created} role(s); {Existing} already present. Administrator: {AdministratorUserId}.",
            created.Count,
            existing.Count,
            administratorUserId);

        return new TenantSeedingResult(created.Count, existing.Count, administratorUserId);
    }

    /// <summary>
    /// Creates the first administrator, in <see cref="UserStatus.Invited"/> and with no credential.
    /// <para>
    /// Deliberately without an invitation token. A token is a one-time credential that only exists
    /// in the transaction that makes it, and nothing here can deliver one - the Notification service
    /// does not exist yet. Minting one now would leave a live credential in the database that its
    /// owner never received and nobody could use. The account is created instead, and the
    /// invitation is issued on demand through the platform bootstrap endpoint.
    /// </para>
    /// </summary>
    private async Task<Guid?> EnsureAdministratorAsync(
        string administratorEmail,
        IReadOnlyDictionary<string, Role> existing,
        IReadOnlyDictionary<string, Role> created,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(administratorEmail))
        {
            logger.LogWarning("No administrator email on the provisioning event; no account was created.");
            return null;
        }

        string normalized = User.NormalizeEmailForLookup(administratorEmail);
        User? administrator = await users.FindByEmailAsync(normalized, cancellationToken);

        if (administrator is not null)
        {
            // A redelivery, or an operator who already created the account by hand. Either way the
            // roles are not re-granted: whatever this account holds now is someone's decision.
            logger.LogInformation(
                "Administrator {UserId} already exists for this tenant; leaving their roles untouched.",
                administrator.Id);
            return administrator.Id;
        }

        Role? adminRole = created.GetValueOrDefault(RoleTemplates.TenantAdministratorTemplateCode)
            ?? existing.GetValueOrDefault(RoleTemplates.TenantAdministratorTemplateCode);

        if (adminRole is null)
        {
            // Cannot happen with the templates as they stand, and is worth shouting about if it
            // ever does: an administrator with no administrative role locks the customer out.
            logger.LogError(
                "No {TemplateCode} role exists; the administrator account was not created.",
                RoleTemplates.TenantAdministratorTemplateCode);
            return null;
        }

        administrator = User.Invite(
            administratorEmail,
            DisplayNameFor(administratorEmail),
            UserType.TenantUser,
            employeeId: null,
            now);

        users.Add(administrator);
        roles.AddGrant(UserRole.Grant(administrator.Id, adminRole.Id, DateOnly.FromDateTime(now)));

        logger.LogInformation("Administrator {UserId} created, awaiting their invitation.", administrator.Id);

        return administrator.Id;
    }

    /// <summary>
    /// A first display name taken from the address rather than invented. The person changes it on
    /// first sign-in, and a placeholder we made up would be worse than the address they gave us.
    /// </summary>
    private static string DisplayNameFor(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? email[..at] : email;
    }
}
