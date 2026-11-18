using System.Globalization;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// The organisation's accounts and how its people sign in, for the platform console. Counts only:
/// no names or addresses leave the tenant.
/// </summary>
internal sealed class IdentityUsageProbe(AppDbContext dbContext) : ITenantUsageProbe
{
    public async Task ReadAsync(TenantUsageReading reading, DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reading);

        IQueryable<User> people = dbContext.Users.AsNoTracking()
            .Where(user => user.UserType == UserType.TenantUser && user.Status != UserStatus.Disabled);

        reading.Users = await people.CountAsync(cancellationToken);
        reading.SignedInToday = await people.CountAsync(user => user.LastLoginDate >= todayStartUtc, cancellationToken);
        reading.LastSignIn = await people.MaxAsync(user => user.LastLoginDate, cancellationToken);

        List<string> sso = await dbContext.SsoProviders.AsNoTracking()
            .Where(provider => provider.Enabled)
            .OrderBy(provider => provider.DisplayName)
            .Select(provider => provider.DisplayName)
            .ToListAsync(cancellationToken);

        int minimumLength = await dbContext.PasswordPolicies.AsNoTracking()
            .Select(policy => (int?)policy.MinimumLength)
            .FirstOrDefaultAsync(cancellationToken) ?? PasswordPolicy.DefaultMinimumLength;

        reading.SignIn = sso.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Password, minimum {minimumLength} characters")
            : string.Create(CultureInfo.InvariantCulture, $"{string.Join(", ", sso)} SSO, or password (minimum {minimumLength} characters)");
    }
}
