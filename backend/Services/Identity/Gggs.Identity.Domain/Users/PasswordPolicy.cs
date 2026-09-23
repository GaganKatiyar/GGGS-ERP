using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Identity.Domain.Users;

/// <summary>How much authentication a tenant demands.</summary>
public enum MfaRequirement
{
    Off = 1,

    /// <summary>Required of administrators and anyone holding a sensitive permission.</summary>
    AdministratorsOnly = 2,

    Everyone = 3,
}

/// <summary>
/// A tenant's password and session rules.
/// <para>
/// The defaults follow NIST 800-63B rather than the older complexity-and-rotation orthodoxy:
/// length matters far more than character classes, and forced rotation is not on by default
/// because it reliably produces Password1, Password2, Password3. Customers whose auditors still
/// demand rotation can switch it on; the default should be the one that produces better passwords,
/// not the one that looks stricter.
/// </para>
/// </summary>
public sealed class PasswordPolicy : TenantAggregateRoot
{
    private PasswordPolicy()
    {
    }

    /// <summary>
    /// Twelve, not eight. Length is the property that actually resists offline cracking, and it is
    /// the one users can satisfy with a passphrase instead of a symbol they will forget.
    /// </summary>
    public int MinimumLength { get; private set; } = 12;

    public int MaximumLength { get; private set; } = 256;

    /// <summary>
    /// Upper, lower, digit and symbol. Off by default: it pushes people towards predictable
    /// substitutions rather than longer secrets.
    /// </summary>
    public bool RequireComplexity { get; private set; }

    /// <summary>
    /// Checks the password against known breach corpora. On by default, because a breached
    /// password is compromised no matter how long or complex it looks.
    /// </summary>
    public bool RejectBreachedPasswords { get; private set; } = true;

    /// <summary>How many previous passwords may not be reused.</summary>
    public int HistoryCount { get; private set; } = 5;

    /// <summary>Zero means no forced rotation, which is the default.</summary>
    public int MaximumAgeDays { get; private set; }

    public int LockoutThreshold { get; private set; } = 5;

    public TimeSpan LockoutDuration { get; private set; } = TimeSpan.FromMinutes(15);

    /// <summary>Signed out after this long without activity.</summary>
    public TimeSpan IdleTimeout { get; private set; } = TimeSpan.FromMinutes(30);

    /// <summary>Signed out after this long regardless of activity.</summary>
    public TimeSpan AbsoluteSessionLifetime { get; private set; } = TimeSpan.FromHours(12);

    public MfaRequirement MfaRequirement { get; private set; } = MfaRequirement.AdministratorsOnly;

    /// <summary>The policy a tenant starts with.</summary>
    public static PasswordPolicy CreateDefault() => new();

    public void Update(
        int minimumLength,
        bool requireComplexity,
        bool rejectBreachedPasswords,
        int historyCount,
        int maximumAgeDays,
        int lockoutThreshold,
        TimeSpan lockoutDuration,
        TimeSpan idleTimeout,
        TimeSpan absoluteSessionLifetime,
        MfaRequirement mfaRequirement)
    {
        // A floor rather than a free choice: a customer must not be able to configure their way
        // into passwords this platform would be embarrassed to be holding.
        Guard.Against(
            minimumLength is < 8 or > 128,
            "PASSWORD_POLICY_LENGTH_INVALID",
            "Minimum password length must be between 8 and 128 characters.");

        Guard.Against(
            historyCount is < 0 or > 24,
            "PASSWORD_POLICY_HISTORY_INVALID",
            "Password history must be between 0 and 24 entries.");

        Guard.Against(
            lockoutThreshold is < 3 or > 50,
            "PASSWORD_POLICY_LOCKOUT_INVALID",
            "Lockout threshold must be between 3 and 50 attempts.");

        // A lockout long enough to be indistinguishable from a permanent one just generates
        // support tickets, and one measured in seconds does not slow an attacker down.
        Guard.Against(
            lockoutDuration < TimeSpan.FromMinutes(1) || lockoutDuration > TimeSpan.FromHours(24),
            "PASSWORD_POLICY_LOCKOUT_DURATION_INVALID",
            "Lockout duration must be between one minute and 24 hours.");

        Guard.Against(
            absoluteSessionLifetime < idleTimeout,
            "PASSWORD_POLICY_SESSION_INVALID",
            "The absolute session lifetime cannot be shorter than the idle timeout.");

        MinimumLength = minimumLength;
        RequireComplexity = requireComplexity;
        RejectBreachedPasswords = rejectBreachedPasswords;
        HistoryCount = historyCount;
        MaximumAgeDays = maximumAgeDays;
        LockoutThreshold = lockoutThreshold;
        LockoutDuration = lockoutDuration;
        IdleTimeout = idleTimeout;
        AbsoluteSessionLifetime = absoluteSessionLifetime;
        MfaRequirement = mfaRequirement;
    }

    /// <summary>
    /// Checks a candidate password against the structural rules.
    /// <para>
    /// Breach checking and reuse are handled by the caller, which has the corpus and the user's
    /// history; this covers only what the policy itself can judge.
    /// </para>
    /// </summary>
    public PasswordValidationResult Validate(string password, string? displayName, string? email)
    {
        List<string> failures = [];

        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            failures.Add($"Password must be at least {MinimumLength} characters.");
        }

        if (password?.Length > MaximumLength)
        {
            failures.Add($"Password must be no more than {MaximumLength} characters.");
        }

        if (RequireComplexity && password is not null && !HasComplexity(password))
        {
            failures.Add("Password must contain upper case, lower case, a digit and a symbol.");
        }

        // A password containing the user's own name or email local part is guessable by anyone who
        // can see a staff directory, whatever its length.
        if (password is not null && ContainsPersonalInformation(password, displayName, email))
        {
            failures.Add("Password must not contain your name or email address.");
        }

        return failures.Count == 0
            ? PasswordValidationResult.Success
            : new PasswordValidationResult(false, failures);
    }

    /// <summary>Whether a password set at this time is now too old, when rotation is enabled.</summary>
    public bool IsExpired(DateTime? passwordChangedDate, DateTime now)
    {
        if (MaximumAgeDays <= 0 || passwordChangedDate is null)
        {
            return false;
        }

        return passwordChangedDate.Value.AddDays(MaximumAgeDays) < now;
    }

    private static bool HasComplexity(string password)
        => password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(char.IsDigit)
            && password.Any(character => !char.IsLetterOrDigit(character));

    private static bool ContainsPersonalInformation(string password, string? displayName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            foreach (string part in displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 4 && password.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            string localPart = email.Split('@')[0];
            if (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Why a password was refused. Every failure is returned at once, so a user is not told about one
/// problem, fixes it, and is then told about the next.
/// </summary>
public sealed record PasswordValidationResult(bool IsValid, IReadOnlyList<string> Failures)
{
    public static PasswordValidationResult Success { get; } = new(true, []);
}
