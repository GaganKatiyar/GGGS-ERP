namespace Gggs.Identity.Application.Authentication;

/// <summary>The outcome of checking a password against a stored hash.</summary>
public enum PasswordVerificationResult
{
    Failed = 0,

    Success = 1,

    /// <summary>
    /// Correct, but hashed with older parameters. The caller re-hashes it, so work factors can be
    /// raised over time without forcing anyone to reset their password.
    /// </summary>
    SuccessRehashNeeded = 2,
}

/// <summary>
/// Hashes and verifies passwords.
/// <para>
/// A port rather than a direct call so the algorithm can be replaced without touching the sign-in
/// flow - which matters because work factors have to rise over the life of a product, and a future
/// move to Argon2 should not mean editing authentication logic.
/// </para>
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string hashedPassword, string providedPassword);
}

/// <summary>
/// Checks a password against known breach corpora.
/// <para>
/// Separate from the policy because it needs the network, and because it must fail open: if the
/// service is unreachable, a user resetting a password should not be blocked from doing so. A
/// breach check that prevents password changes during an outage makes an incident worse.
/// </para>
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// True when the password is known to have been breached. Implementations return false when
    /// they cannot tell.
    /// </summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}
