using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// User persistence.
/// <para>
/// None of these methods takes a tenant: the query filter applies the current one, and row-level
/// security enforces it underneath. A lookup by email therefore cannot return a user from another
/// customer even though the same address may exist in several.
/// </para>
/// </summary>
public sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => await dbContext.Users.FirstOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

    public async Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);
}

/// <summary>
/// The tenant's password policy.
/// <para>
/// Created on first use rather than at provisioning, so a tenant that has never opened the security
/// settings still has one. Defaults that only exist once someone visits a screen are defaults that
/// sometimes do not exist.
/// </para>
/// </summary>
public sealed class PasswordPolicyRepository(AppDbContext dbContext) : IPasswordPolicyRepository
{
    public async Task<PasswordPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        PasswordPolicy? policy = await dbContext.PasswordPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (policy is not null)
        {
            return policy;
        }

        // Not persisted here: this runs on the sign-in path, and writing on a read would turn
        // every first sign-in into a write and every concurrent one into a race.
        return PasswordPolicy.CreateDefault();
    }
}
