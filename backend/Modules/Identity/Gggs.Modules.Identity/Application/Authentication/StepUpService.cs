using System.Globalization;
using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Domain.Users;

namespace Gggs.Modules.Identity.Application.Authentication;

/// <summary>
/// Step-up re-authentication: before a sensitive action, the signed-in person proves they are still
/// at the keyboard by typing their password again, which restarts the step-up window.
/// <para>
/// Wrong passwords count towards the same lockout as a failed sign-in, so this cannot be used to
/// guess a password more cheaply than the sign-in page.
/// </para>
/// </summary>
public sealed class StepUpService(
    IUserRepository users,
    IPasswordPolicyRepository policies,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    public const string InvalidPassword = "STEP_UP_INVALID_PASSWORD";

    /// <summary>Checks the signed-in user's password; throws when it is wrong or the account is locked.</summary>
    public async Task ConfirmAsync(string? password, CancellationToken cancellationToken)
    {
        _ = tenantContext.TenantId;
        User user = await users.GetAsync(currentUser.UserId, cancellationToken) ?? throw new NotFoundException(nameof(User), currentUser.UserId);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (user.IsLockedOut(now))
        {
            throw new DomainRuleException(SignInErrorCodes.AccountLocked, "The account is locked for a while after too many attempts.");
        }

        bool ok = user.PasswordHash is { } hash && !string.IsNullOrEmpty(password) && passwordHasher.Verify(hash, password) != PasswordVerificationResult.Failed;
        if (!ok)
        {
            user.RecordFailedSignIn(await policies.GetAsync(cancellationToken), now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainRuleException(InvalidPassword, "That password is not right.");
        }
    }

    /// <summary>The session's claims with a fresh authentication time, which is what the step-up window measures.</summary>
    public static IEnumerable<Claim> SteppedUp(IEnumerable<Claim> claims, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claims);
        List<Claim> updated = [.. claims.Where(claim => claim.Type != "auth_time")];
        updated.Add(new Claim("auth_time", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        return updated;
    }
}
