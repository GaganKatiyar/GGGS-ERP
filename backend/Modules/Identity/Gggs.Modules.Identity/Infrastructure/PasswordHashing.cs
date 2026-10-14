using System.Security.Cryptography;
using Gggs.Modules.Identity.Application.Authentication;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// PBKDF2-HMAC-SHA512 password hashing.
/// <para>
/// Chosen over Argon2 for one practical reason: it is available in the framework and is accepted
/// by FIPS-constrained customers, who are exactly the enterprises this product targets. Argon2 is
/// the better algorithm on paper; it would also be a third-party dependency in the most
/// security-sensitive path we have, and unavailable to some of the customers who care most.
/// </para>
/// <para>
/// The format carries its own parameters, so the iteration count can be raised later and existing
/// passwords still verify - they are re-hashed on the next successful sign-in.
/// </para>
/// </summary>
public sealed class Pbkdf2PasswordHasher(ILogger<Pbkdf2PasswordHasher> logger) : IPasswordHasher
{
    /// <summary>Format marker, so a future move to another algorithm can be told apart.</summary>
    private const byte FormatVersion = 1;

    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>
    /// Raise this over time. Existing hashes keep their own count, so raising it costs nobody a
    /// password reset.
    /// </summary>
    private const int CurrentIterations = 210_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, HashAlgorithmName.SHA512, HashSize);

        // version | iterations | salt | hash
        byte[] payload = new byte[1 + 4 + SaltSize + HashSize];
        payload[0] = FormatVersion;
        BitConverter.TryWriteBytes(payload.AsSpan(1, 4), CurrentIterations);
        salt.CopyTo(payload.AsSpan(5, SaltSize));
        hash.CopyTo(payload.AsSpan(5 + SaltSize, HashSize));

        return Convert.ToBase64String(payload);
    }

    public PasswordVerificationResult Verify(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(hashedPassword);
        }
        catch (FormatException)
        {
            // A stored value that is not a hash at all. Logged without the value itself.
            logger.LogError("A stored password hash could not be decoded.");
            return PasswordVerificationResult.Failed;
        }

        if (payload.Length != 1 + 4 + SaltSize + HashSize || payload[0] != FormatVersion)
        {
            logger.LogError("A stored password hash had an unexpected format or version.");
            return PasswordVerificationResult.Failed;
        }

        int iterations = BitConverter.ToInt32(payload, 1);
        byte[] salt = payload[5..(5 + SaltSize)];
        byte[] expected = payload[(5 + SaltSize)..];

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            providedPassword, salt, iterations, HashAlgorithmName.SHA512, HashSize);

        // Constant-time: a byte-by-byte comparison that returns early leaks how much of the hash
        // matched, which is enough to reconstruct it given enough attempts.
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < CurrentIterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }
}

/// <summary>
/// Breach checking that always answers "not breached".
/// <para>
/// A deliberate placeholder so the policy flag is honoured end to end before the k-anonymity range
/// query against a breach corpus is wired up. It fails open by design, which is also how the real
/// implementation must behave when the service is unreachable: blocking password changes during an
/// outage makes an incident worse.
/// </para>
/// </summary>
public sealed class NoOpBreachedPasswordChecker(ILogger<NoOpBreachedPasswordChecker> logger) : IBreachedPasswordChecker
{
    private bool _warned;

    public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
    {
        if (!_warned)
        {
            logger.LogWarning(
                "Breached-password checking is enabled in policy but no corpus is configured; passwords are not being checked.");
            _warned = true;
        }

        return Task.FromResult(false);
    }
}
