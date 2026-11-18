using Gggs.Data;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Organization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Application;

/// <summary>
/// Keeps each role's designations in step with the role: one per active level ("Business Analyst
/// L2", code <c>TS-BA-L2</c>), renamed when the role is, and active only while both the role and the
/// level are. Free-standing designations (no role) are never touched.
/// </summary>
internal static class RoleDesignations
{
    /// <summary>Brings the designations of <paramref name="roles"/> in line. Adds to the context; the caller saves.</summary>
    public static async Task<int> SyncAsync(AppDbContext db, IReadOnlyCollection<Role> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Count == 0)
        {
            return 0;
        }

        // Loaded into the context and read from there, so changes not yet saved (a level just
        // retired, a role just added) count.
        await db.Levels.LoadAsync(cancellationToken);
        await db.Designations.LoadAsync(cancellationToken);
        List<Level> levels = [.. db.Levels.Local.Where(level => level.IsActive).OrderBy(level => level.Rank)];
        List<Designation> designations = [.. db.Designations.Local];

        return Sync(roles, levels, designations, designation => db.Designations.Add(designation));
    }

    /// <summary>Every role's designations, after a level was added, retired or recoded.</summary>
    public static async Task<int> SyncAllAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Roles.LoadAsync(cancellationToken);
        return await SyncAsync(db, [.. db.Roles.Local], cancellationToken);
    }

    /// <summary>The pure part, over lists in memory: returns how many designations were created.</summary>
    public static int Sync(IEnumerable<Role> roles, IReadOnlyList<Level> activeLevels, List<Designation> designations, Action<Designation> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        int created = 0;
        HashSet<Guid> activeLevelIds = [.. activeLevels.Select(level => level.Id)];

        foreach (Role role in roles)
        {
            foreach (Level level in activeLevels)
            {
                if (!Designation.FitsRoleCode(role, level))
                {
                    continue;
                }

                string code = Designation.RoleCode(role, level);
                Designation? designation = designations.FirstOrDefault(row => row.RoleId == role.Id && row.LevelId == level.Id)
                    ?? designations.FirstOrDefault(row => row.RoleId is null && row.Code == code);

                if (designation is null)
                {
                    // An inactive role gets nothing new; it only has its existing designations switched off.
                    if (!role.IsActive)
                    {
                        continue;
                    }

                    designation = Designation.ForRole(role, level);
                    designations.Add(designation);
                    add(designation);
                    created++;
                }
                else
                {
                    designation.SetRole(role.Id);
                    designation.SetLevel(level.Id);
                    designation.Rename(Designation.RoleName(role, level));

                    // The code follows the role's, unless another designation already holds it.
                    if (designation.Code != code && !designations.Any(row => row != designation && row.Code == code))
                    {
                        designation.ChangeCode(code);
                    }
                }

                SetActive(designation, role.IsActive);
            }

            // A level that was retired takes its designations with it.
            foreach (Designation stale in designations.Where(row => row.RoleId == role.Id && (row.LevelId is null || !activeLevelIds.Contains(row.LevelId.Value))))
            {
                SetActive(stale, false);
            }
        }

        return created;
    }

    private static void SetActive(Designation designation, bool active)
    {
        if (active && !designation.IsActive)
        {
            designation.Activate();
        }
        else if (!active && designation.IsActive)
        {
            designation.Deactivate();
        }
    }
}
