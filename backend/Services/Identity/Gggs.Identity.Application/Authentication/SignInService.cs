using Gggs.BuildingBlocks.Application;
using Gggs.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.Authentication;

/// <summary>Why a sign-in attempt did not succeed.</summary>
public enum SignInFailureReason
{
    None = 0,

    /// <summary>
    /// No such user, or the wrong password. Deliberately one reason rather than two: telling a
    /// caller which of the two it was turns the sign-in page into a way to discover who works here.
    /// </summary>
    InvalidCredentials = 1,

    LockedOut = 2,

    /// <summary>Disabled by an administrator.</summary>
    AccountDisabled = 3,

    /// <summary>Invited but has never set a password.</summary>
    InvitationPending = 4,

    /// <summary>Correct, but the policy says it is too old.</summary>
    PasswordExpired = 5,

    /// <summary>Correct, and a second factor is now required.</summary>
    MfaRequired = 6,

    /// <summary>The tenant is suspended, provisioning or leaving.</summary>
    TenantUnavailable = 7,
}

/// <summary>The outcome of a sign-in attempt.</summary>
public sealed record SignInResult
{
    public required bool Succeeded { get; init; }

    public SignInFailureReason FailureReason { get; init; }

    public Guid? UserId { get; init; }

    /// <summary>Set when the user must change their password before continuing.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>When a lockout is in force, when it lifts - so the UI can say so.</summary>
    public DateTime? LockoutEndDate { get; init; }

    public static SignInResult Success(Guid userId, bool mustChangePassword = false) => new()
    {
        Succeeded = true,
        UserId = userId,
        MustChangePassword = mustChangePassword,
    };

    public static SignInResult Failure(SignInFailureReason reason, DateTime? lockoutEndDate = null) => new()
    {
        Succeeded = false,
        FailureReason = reason,
        LockoutEndDate = lockoutEndDate,
    };
}

/// <summary>
/// Verifies a local username and password.
/// <para>
/// Every refusal takes roughly the same path and the same time. An attacker who can tell "no such
/// user" from "wrong password" - by the message, the status code or how quickly the answer comes
/// back - can enumerate who works at a company before trying a single password.
/// </para>
/// <para>
/// Federated sign-in is not handled here. When Entra ID federation ships it arrives as a separate
/// flow that ends at the same place: a verified user and a session. This class stays about
/// passwords.
/// </para>
/// </summary>
public sealed class SignInService(
    IUserRepository users,
    IPasswordPolicyRepository policies,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<SignInService> logger)
{
    public async Task<SignInResult> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        User? user = await users.FindByEmailAsync(User.NormalizeEmailForLookup(email), cancellationToken);
        PasswordPolicy policy = await policies.GetAsync(cancellationToken);

        if (user is null)
        {
            // The hash of a dummy password is computed anyway, so an unknown address does not come
            // back measurably faster than a known one with the wrong password.
            passwordHasher.Verify(DummyHash, password);

            logger.LogInformation("Sign-in failed: no account matched the address supplied.");
            return SignInResult.Failure(SignInFailureReason.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            logger.LogInformation("Sign-in refused for user {UserId}: locked out until {LockoutEnd}.",
                user.Id, user.LockoutEndDate);
            return SignInResult.Failure(SignInFailureReason.LockedOut, user.LockoutEndDate);
        }

        if (user.Status == UserStatus.Disabled)
        {
            logger.LogInformation("Sign-in refused for user {UserId}: the account is disabled.", user.Id);
            return SignInResult.Failure(SignInFailureReason.AccountDisabled);
        }

        if (user.PasswordHash is null)
        {
            // Either an unfinished invitation or a federated account with no password. Both are
            // reported the same way to the caller; the difference is for support, not the login page.
            logger.LogInformation("Sign-in refused for user {UserId}: no password is set.", user.Id);
            return SignInResult.Failure(SignInFailureReason.InvitationPending);
        }

        PasswordVerificationResult verification = passwordHasher.Verify(user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RecordFailedSignIn(policy, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Logged without the address: failed sign-ins are exactly where an address would end
            // up in a log aggregator that half the company can read.
            logger.LogInformation(
                "Sign-in failed for user {UserId}; {FailedCount} consecutive failures.",
                user.Id,
                user.AccessFailedCount);

            return user.IsLockedOut(now)
                ? SignInResult.Failure(SignInFailureReason.LockedOut, user.LockoutEndDate)
                : SignInResult.Failure(SignInFailureReason.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Correct, but hashed with older parameters. Upgrading here is how work factors rise
            // over the life of the product without anyone being asked to reset a password.
            user.SetPassword(passwordHasher.Hash(password), policy, now);
            logger.LogInformation("Password for user {UserId} re-hashed with current parameters.", user.Id);
        }

        bool mustChangePassword = policy.IsExpired(user.PasswordChangedDate, now);

        user.RecordSuccessfulSignIn(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return SignInResult.Success(user.Id, mustChangePassword);
    }

    /// <summary>
    /// A hash of a value nobody will ever supply, verified against when no user matches so the
    /// unknown-address path costs the same as the wrong-password path.
    /// </summary>
    private const string DummyHash =
        "AQAAAAIAAYagAAAAEJ8cJm7tJ8YVJ0Xh0Qp9mEYKQbZ5dQ8oQ3vQ2wJ0xY7yD5vX9kZ4nP2mR6sT8uW1g==";
}

/// <summary>Persistence for users, kept as a port so the sign-in rules are testable in isolation.</summary>
public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(User user);
}

/// <summary>The current tenant's password policy, created on first use if they have never set one.</summary>
public interface IPasswordPolicyRepository
{
    Task<PasswordPolicy> GetAsync(CancellationToken cancellationToken = default);
}
