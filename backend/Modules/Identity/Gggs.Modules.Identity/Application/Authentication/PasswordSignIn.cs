using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Domain.Users;

namespace Gggs.Modules.Identity.Application.Authentication;

/// <summary>Stable codes a failed sign-in reports, whichever entry point was used.</summary>
public static class SignInErrorCodes
{
    /// <summary>
    /// Covers a wrong password, an unknown address and a tenant that does not exist or is suspended,
    /// deliberately: anything finer is a way to find out who has an account here.
    /// </summary>
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>
    /// The one distinguishable case: a user who cannot get in for fifteen minutes needs to know,
    /// rather than retrying and extending the lockout.
    /// </summary>
    public const string AccountLocked = "ACCOUNT_LOCKED";
}

/// <summary>The result of checking a password, with the principal to issue when it succeeded.</summary>
public sealed record PasswordSignInOutcome
{
    public required bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>The user or platform staff member signed in.</summary>
    public Guid? SubjectId { get; init; }

    public bool MustChangePassword { get; init; }

    public DateTime? LockoutEndDate { get; init; }

    internal static PasswordSignInOutcome Failed(string errorCode, DateTime? lockoutEndDate = null) => new()
    {
        Succeeded = false,
        ErrorCode = errorCode,
        LockoutEndDate = lockoutEndDate,
    };
}

/// <summary>
/// Password sign-in for an organisation's users, shared by every entry point.
/// <para>
/// The browser session (<c>/bff/login</c>) and the OAuth token endpoint (<c>/connect/token</c>) both
/// end here, so there is exactly one place that decides who a credential belongs to - and one
/// place to get the anti-enumeration rules right. GGGS's own staff sign in to the platform console,
/// a separate application with its own database; they cannot sign in here.
/// </para>
/// </summary>
public sealed class PasswordSignInService(
    TenantDiscoveryService discovery,
    ITenantContextSetter tenantSetter,
    SignInService signInService,
    IUserRepository users,
    TimeProvider timeProvider)
{
    public async Task<PasswordSignInOutcome> SignInAsync(
        string email,
        string password,
        string? tenantCode,
        string authenticationScheme,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return PasswordSignInOutcome.Failed(SignInErrorCodes.InvalidCredentials);
        }

        TenantDiscoveryResult? discovered = await discovery.DiscoverAsync(tenantCode, email, cancellationToken);

        if (discovered is null || !discovered.Tenant.IsOperational)
        {
            // An unknown customer, a suspended one and a wrong password are one answer.
            return PasswordSignInOutcome.Failed(SignInErrorCodes.InvalidCredentials);
        }

        // Set before any user query: users are tenant-owned, so a lookup without a tenant finds
        // nothing and a lookup with the wrong one would find somebody else's account.
        tenantSetter.SetTenant(discovered.Tenant);

        SignInResult result = await signInService.SignInWithPasswordAsync(email, password, cancellationToken);

        if (!result.Succeeded)
        {
            return PasswordSignInOutcome.Failed(
                result.FailureReason == SignInFailureReason.LockedOut
                    ? SignInErrorCodes.AccountLocked
                    : SignInErrorCodes.InvalidCredentials,
                result.LockoutEndDate);
        }

        User user = await users.GetAsync(result.UserId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("The user vanished between authentication and sign-in.");

        return new PasswordSignInOutcome
        {
            Succeeded = true,
            SubjectId = user.Id,
            MustChangePassword = result.MustChangePassword,
            Principal = UserPrincipalFactory.Create(
                user,
                discovered.Tenant.Id,
                sessionId: Guid.CreateVersion7(),
                authenticatedAt: timeProvider.GetUtcNow(),
                authenticationScheme),
        };
    }
}
