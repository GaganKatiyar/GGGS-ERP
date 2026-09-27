using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Domain.PlatformStaff;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Identity.Infrastructure;

/// <summary>Platform role and grant persistence. No tenant to filter by, and no tenant to require one.</summary>
public sealed class PlatformRoleRepository(IdentityDbContext dbContext) : IPlatformRoleRepository
{
    public async Task<PlatformRole?> GetAsync(Guid platformRoleId, CancellationToken cancellationToken = default)
        => await dbContext.PlatformRoles.FirstOrDefaultAsync(role => role.Id == platformRoleId, cancellationToken);

    public async Task<IReadOnlyList<PlatformRoleAssignment>> GetGrantsForStaffAsync(
        Guid platformStaffId,
        CancellationToken cancellationToken = default)
        => await dbContext.PlatformRoleAssignments
            .Where(grant => grant.PlatformStaffId == platformStaffId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Evaluated in memory rather than in SQL, for the same reason the tenant equivalent is: the
    /// permission list is JSON, and this runs only when disabling someone or clearing their roles -
    /// a handful of roles and grants, never a table scan.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetStaffIdsWithPermissionAsync(
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        List<PlatformRole> roles = await dbContext.PlatformRoles.AsNoTracking().ToListAsync(cancellationToken);

        List<Guid> roleIds =
        [
            .. roles
                .Where(role => role.PermissionCodes.Contains(permissionCode, StringComparer.OrdinalIgnoreCase))
                .Select(role => role.Id)
        ];

        if (roleIds.Count == 0)
        {
            return [];
        }

        return await dbContext.PlatformRoleAssignments
            .AsNoTracking()
            .Where(grant => roleIds.Contains(grant.PlatformRoleId))
            .Select(grant => grant.PlatformStaffId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, PlatformRole>> GetSystemRolesAsync(CancellationToken cancellationToken = default)
    {
        List<PlatformRole> roles = await dbContext.PlatformRoles
            .Where(role => role.IsSystem && role.TemplateCode != null)
            .ToListAsync(cancellationToken);

        return roles.ToDictionary(role => role.TemplateCode!, StringComparer.OrdinalIgnoreCase);
    }

    public void Add(PlatformRole role) => dbContext.PlatformRoles.Add(role);

    public void AddGrant(PlatformRoleAssignment grant) => dbContext.PlatformRoleAssignments.Add(grant);

    public void RemoveGrant(PlatformRoleAssignment grant) => dbContext.PlatformRoleAssignments.Remove(grant);
}
