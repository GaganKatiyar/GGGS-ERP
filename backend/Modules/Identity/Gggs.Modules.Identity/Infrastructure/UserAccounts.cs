using Gggs.Data;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// Deprovisioning for leavers. Unlike the administrator's disable, there is no last-administrator
/// guard: an account whose employee has left must stop working whoever else can manage users.
/// </summary>
internal sealed class UserAccounts(AppDbContext dbContext, IRoleRepository roles, ILogger<UserAccounts> logger) : IUserAccounts
{
    /// <summary>Enabled accounts only: a disabled one cannot sign in to see the notice.</summary>
    public async Task<IReadOnlyList<Guid>> UserIdsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> holders = await roles.GetUserIdsWithPermissionAsync(permissionCode, cancellationToken);
        if (holders.Count == 0)
        {
            return [];
        }

        return await dbContext.Users.AsNoTracking()
            .Where(user => holders.Contains(user.Id) && user.Status != UserStatus.Disabled)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAccountInfo?> FindByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.Users.AsNoTracking()
            .Where(user => user.EmployeeId == employeeId)
            .OrderBy(user => user.Status == UserStatus.Disabled)
            .Select(user => new UserAccountInfo(user.Id, user.Email, user.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> DisableForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        List<User> accounts = await dbContext.Users.Where(user => user.EmployeeId == employeeId && user.Status != UserStatus.Disabled).ToListAsync(cancellationToken);
        foreach (User user in accounts)
        {
            user.Disable();
            logger.LogWarning("User {UserId} disabled: employee {EmployeeId} has left.", user.Id, employeeId);
        }

        return accounts.Count;
    }
}
