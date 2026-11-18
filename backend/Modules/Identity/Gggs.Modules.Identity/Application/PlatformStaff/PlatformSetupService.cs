using System.Security.Cryptography;
using System.Text;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Identity.Application.PlatformStaff;

/// <summary>
/// The platform console's first-run setup: the very first platform administrator creates their own
/// account with the environment's setup key.
/// <para>
/// The alternative to reading an invitation token out of the worker's log
/// (<see cref="PlatformStaffBootstrapSeeder"/>, which still works). Closed for good the moment any
/// platform staff account exists, closed when no key is configured, and the key is compared in
/// constant time; the endpoint is rate limited as well.
/// </para>
/// </summary>
public sealed class PlatformSetupService(
    IPlatformStaffRepository staff,
    IPlatformRoleRepository roles,
    IPasswordHasher passwordHasher,
    IOptions<PlatformBootstrapOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PlatformSetupService> logger)
{
    public async Task<bool> IsOpenAsync(CancellationToken cancellationToken = default)
        => !string.IsNullOrWhiteSpace(options.Value.SetupKey) && !await staff.AnyAsync(cancellationToken);

    public async Task<Guid> CreateFirstAdministratorAsync(
        string setupKey,
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        string? expected = options.Value.SetupKey;

        if (string.IsNullOrWhiteSpace(expected) || !KeyMatches(expected, setupKey))
        {
            logger.LogWarning("Platform setup refused: wrong or missing setup key.");
            throw new ForbiddenException("SETUP_KEY_INVALID", "That setup key is not correct.");
        }

        if (await staff.AnyAsync(cancellationToken))
        {
            throw new DomainRuleException("SETUP_CLOSED", "Setup has already been completed. Sign in instead.");
        }

        IReadOnlyDictionary<string, PlatformRole> systemRoles = await roles.GetSystemRolesAsync(cancellationToken);

        if (!systemRoles.TryGetValue(PlatformRoleTemplates.StaffAdministratorCode, out PlatformRole? administratorRole))
        {
            throw new DomainRuleException(
                "SETUP_NOT_READY",
                "The platform roles have not been created yet. Make sure the database is migrated and restart the platform API.");
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        PlatformStaffMember member = PlatformStaffMember.Invite(email, displayName, now);

        IReadOnlyList<string> failures = PlatformStaffMember.Validate(password, member.DisplayName, member.Email);

        if (failures.Count > 0)
        {
            throw new DomainRuleException("PASSWORD_REJECTED", string.Join(" ", failures));
        }

        member.CompleteInvitation(passwordHasher.Hash(password), now);
        staff.Add(member);
        roles.AddGrant(PlatformRoleAssignment.Grant(member.Id, administratorRole.Id));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Platform setup completed: first administrator {PlatformStaffId} created.", member.Id);
        return member.Id;
    }

    private static bool KeyMatches(string expected, string? given) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(given ?? string.Empty)));
}
