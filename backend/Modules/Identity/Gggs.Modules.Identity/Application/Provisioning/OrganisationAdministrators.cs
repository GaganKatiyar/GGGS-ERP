using Gggs.Data;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Application.Provisioning;

/// <summary>
/// The current organisation's administrators, for the platform console: listed for its report, and
/// invited or re-invited when platform staff ask (through <see cref="TenantBootstrapService"/>, which
/// emails the invitation). The caller sets the tenant.
/// </summary>
internal sealed class OrganisationAdministrators(
    AppDbContext dbContext,
    TenantBootstrapService bootstrap,
    TimeProvider timeProvider) : IOrganisationAdministrators
{
    public async Task<IReadOnlyList<OrganisationAdministratorInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        List<Guid> adminRoleIds = await dbContext.Roles.AsNoTracking()
            .Where(role => role.TemplateCode == RoleTemplates.TenantAdministratorTemplateCode)
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);

        List<Guid> userIds = await dbContext.UserRoles.AsNoTracking()
            .Where(grant => adminRoleIds.Contains(grant.RoleId) && grant.ValidFrom <= today && (grant.ValidTo == null || grant.ValidTo >= today))
            .Select(grant => grant.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .OrderBy(user => user.DisplayName)
            .Select(user => new OrganisationAdministratorInfo(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Status.ToString(),
                user.LastLoginDate,
                user.Status == UserStatus.Invited ? user.InvitationTokenExpiresDate : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<string> AddAsync(string email, string? displayName, CancellationToken cancellationToken = default)
    {
        AdministratorInvitation invitation = await bootstrap.AddAdministratorAsync(email, displayName, cancellationToken);
        return $"Invited {email.Trim()} as an administrator; the invitation works until {invitation.ExpiresDate:d MMM yyyy, h:mm tt} UTC.";
    }

    public async Task<string> ResendInvitationAsync(string email, CancellationToken cancellationToken = default)
    {
        AdministratorInvitation invitation = await bootstrap.IssueAdministratorInvitationAsync(email, cancellationToken);
        return $"Emailed a new invitation to {email.Trim()}; it works until {invitation.ExpiresDate:d MMM yyyy, h:mm tt} UTC.";
    }
}
