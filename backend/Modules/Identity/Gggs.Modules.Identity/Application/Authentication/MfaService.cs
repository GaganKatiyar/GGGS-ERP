using System.Globalization;
using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.DataProtection;

namespace Gggs.Modules.Identity.Application.Authentication;

public sealed record MfaStatus(bool Enabled, DateTime? EnrolledDate, int RecoveryCodesLeft, bool Required);

public sealed record MfaSetup(string Secret, string ProvisioningUri);

public sealed record MfaCodes(IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Multi-factor sign-in with an authenticator app (TOTP) and one-time recovery codes: enrolment,
/// the second step of sign-in, and step-up re-authentication for sensitive actions.
/// <para>
/// The second step is bound to the first by a short-lived, encrypted challenge that names the user
/// and tenant, so a code can only finish a sign-in whose password was just checked. Wrong codes count
/// towards the same lockout as wrong passwords.
/// </para>
/// </summary>
public sealed class MfaService(
    IUserRepository users,
    IPasswordPolicyRepository policies,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ISensitiveDataProtector protector,
    IDataProtectionProvider dataProtection,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITenantContextSetter tenantSetter,
    ITenantRegistry tenants,
    TimeProvider timeProvider)
{
    public const string SecretPurpose = "identity.mfa-secret";
    public const string Issuer = "PeopleOS";
    public const string InvalidCode = "MFA_INVALID_CODE";
    public const string ChallengeExpired = "MFA_CHALLENGE_EXPIRED";

    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private ITimeLimitedDataProtector Challenges => dataProtection.CreateProtector("Gggs.Identity.MfaChallenge.v1").ToTimeLimitedDataProtector();

    // ----- Enrolment (the signed-in user) ------------------------------------------------------

    public async Task<MfaStatus> StatusAsync(CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        return new MfaStatus(user.MfaEnabled, user.MfaEnrolledDate, user.MfaRecoveryCodesLeft, await IsRequiredAsync(user, cancellationToken));
    }

    public async Task<MfaSetup> SetupAsync(CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        string secret = Totp.NewSecret();
        user.BeginMfaEnrollment(protector.Protect(SecretPurpose, secret, 2).Ciphertext);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MfaSetup(secret, Totp.ProvisioningUri(Issuer, user.Email, secret));
    }

    public async Task<MfaCodes> ConfirmAsync(string code, CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        string secret = user.MfaPendingSecret is { } pending ? Reveal(pending) : throw new DomainRuleException("MFA_NO_ENROLMENT", "Start setting up the authenticator app first.");
        long step = Totp.Verify(secret, code, timeProvider.GetUtcNow(), 0) ?? throw new DomainRuleException(InvalidCode, "That code does not match. Check the time on your phone and try the newest code.");
        IReadOnlyList<string> recovery = Totp.NewRecoveryCodes();
        user.ConfirmMfa(step, [.. recovery.Select(Totp.HashRecoveryCode)], Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MfaCodes(recovery);
    }

    public async Task<MfaCodes> RegenerateRecoveryCodesAsync(string code, CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        await RequireCodeAsync(user, code, allowRecovery: false, cancellationToken);
        IReadOnlyList<string> recovery = Totp.NewRecoveryCodes();
        user.ReplaceRecoveryCodes([.. recovery.Select(Totp.HashRecoveryCode)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MfaCodes(recovery);
    }

    public async Task DisableAsync(string code, CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        if (await IsRequiredAsync(user, cancellationToken))
        {
            throw new DomainRuleException("MFA_REQUIRED_BY_POLICY", "Your organisation requires multi-factor sign-in for your account.");
        }

        await RequireCodeAsync(user, code, allowRecovery: true, cancellationToken);
        user.DisableMfa();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ----- Sign-in --------------------------------------------------------------------------

    /// <summary>Whether this user must use multi-factor sign-in under the tenant's policy.</summary>
    public async Task<bool> IsRequiredAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        PasswordPolicy policy = await policies.GetAsync(cancellationToken);
        return policy.MfaRequirement switch
        {
            MfaRequirement.Everyone => true,
            MfaRequirement.AdministratorsOnly => IsPrivileged(await permissions.GetForUserAsync(user.Id, cancellationToken)),
            _ => false,
        };
    }

    /// <summary>The encrypted challenge that carries a password-verified user to the second step.</summary>
    public string IssueChallenge(Guid userId, Guid tenantId)
        => Challenges.Protect($"{userId:N}|{tenantId:N}", ChallengeLifetime);

    /// <summary>Finishes a sign-in with a code from the app (or a recovery code).</summary>
    public async Task<PasswordSignInOutcome> CompleteSignInAsync(string challenge, string code, string authenticationScheme, CancellationToken cancellationToken)
    {
        string[] parts;
        try
        {
            parts = Challenges.Unprotect(challenge ?? string.Empty).Split('|');
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return PasswordSignInOutcome.Failed(ChallengeExpired);
        }

        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out Guid userId) || !Guid.TryParseExact(parts[1], "N", out Guid tenantId)
            || await tenants.GetAsync(tenantId, cancellationToken) is not { IsOperational: true } tenant)
        {
            return PasswordSignInOutcome.Failed(ChallengeExpired);
        }

        tenantSetter.SetTenant(tenant);
        User? user = await users.GetAsync(userId, cancellationToken);
        if (user is null || !user.MfaEnabled || user.Status == UserStatus.Disabled)
        {
            return PasswordSignInOutcome.Failed(ChallengeExpired);
        }

        DateTime now = Now;
        if (user.IsLockedOut(now))
        {
            return PasswordSignInOutcome.Failed(SignInErrorCodes.AccountLocked, user.LockoutEndDate);
        }

        if (!Accept(user, code, allowRecovery: true))
        {
            user.RecordFailedSignIn(await policies.GetAsync(cancellationToken), now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return user.IsLockedOut(now) ? PasswordSignInOutcome.Failed(SignInErrorCodes.AccountLocked, user.LockoutEndDate) : PasswordSignInOutcome.Failed(InvalidCode);
        }

        user.RecordSuccessfulSignIn(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PasswordSignInOutcome
        {
            Succeeded = true,
            SubjectId = user.Id,
            Principal = UserPrincipalFactory.Create(user, tenant.Id, Guid.CreateVersion7(), timeProvider.GetUtcNow(), mfaSatisfied: true, authenticationScheme),
        };
    }

    /// <summary>
    /// Step-up: proves the signed-in person is still at the keyboard before a sensitive action - with
    /// a code when they have an authenticator, otherwise their password. Returns whether a second factor was used.
    /// </summary>
    public async Task<bool> StepUpAsync(string? code, string? password, CancellationToken cancellationToken)
    {
        User user = await CurrentAsync(cancellationToken);
        DateTime now = Now;
        if (user.IsLockedOut(now))
        {
            throw new DomainRuleException(SignInErrorCodes.AccountLocked, "The account is locked for a while after too many attempts.");
        }

        bool ok = user.MfaEnabled
            ? Accept(user, code, allowRecovery: false)
            : user.PasswordHash is { } hash && !string.IsNullOrEmpty(password) && passwordHasher.Verify(hash, password) != PasswordVerificationResult.Failed;
        if (!ok)
        {
            user.RecordFailedSignIn(await policies.GetAsync(cancellationToken), now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainRuleException(InvalidCode, user.MfaEnabled ? "That code does not match." : "That password is not right.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return user.MfaEnabled;
    }

    /// <summary>Claims refreshed after a step-up: a new authentication time, and the second factor when one was used.</summary>
    public static IEnumerable<Claim> SteppedUp(IEnumerable<Claim> claims, DateTimeOffset now, bool mfa)
    {
        ArgumentNullException.ThrowIfNull(claims);
        List<Claim> updated = [.. claims.Where(claim => claim.Type != "auth_time")];
        updated.Add(new Claim("auth_time", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        if (mfa && !updated.Any(claim => claim.Type == "amr" && claim.Value == UserPrincipalFactory.AuthenticationMethods.MultiFactor))
        {
            updated.Add(new Claim("amr", UserPrincipalFactory.AuthenticationMethods.MultiFactor));
        }

        return updated;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static bool IsPrivileged(PermissionSet granted)
        => granted.All.Any(grant => PermissionCatalog.IsSensitive(grant.Code) || grant.Code.StartsWith("admin.", StringComparison.Ordinal) || grant.Code.StartsWith("platform.", StringComparison.Ordinal));

    private string Reveal(string ciphertext) => protector.Reveal(SecretPurpose, new ProtectedValue(ciphertext, "-"));

    /// <summary>Accepts a current authenticator code (never the same step twice) or an unused recovery code.</summary>
    private bool Accept(User user, string? code, bool allowRecovery)
    {
        if (user.MfaSecret is { } secret && Totp.Verify(Reveal(secret), code, timeProvider.GetUtcNow(), user.MfaLastStep) is { } step)
        {
            return user.AcceptMfaStep(step);
        }

        return allowRecovery && !string.IsNullOrWhiteSpace(code) && code.Trim().Length >= 8 && user.UseRecoveryCode(Totp.HashRecoveryCode(code));
    }

    private async Task RequireCodeAsync(User user, string code, bool allowRecovery, CancellationToken cancellationToken)
    {
        if (!user.MfaEnabled)
        {
            throw new DomainRuleException("MFA_NOT_ENABLED", "Multi-factor sign-in is not set up.");
        }

        if (!Accept(user, code, allowRecovery))
        {
            user.RecordFailedSignIn(await policies.GetAsync(cancellationToken), Now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainRuleException(InvalidCode, "That code does not match.");
        }
    }

    private async Task<User> CurrentAsync(CancellationToken cancellationToken)
    {
        _ = tenantContext.TenantId;
        return await users.GetAsync(currentUser.UserId, cancellationToken) ?? throw new NotFoundException(nameof(User), currentUser.UserId);
    }
}
