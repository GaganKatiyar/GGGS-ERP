namespace Gggs.Modules.Notification.Domain;

/// <summary>What kind of trouble an email ran into, which decides whether trying again can help.</summary>
public enum EmailFailureKind
{
    /// <summary>Nothing known: the mail system's own words are shown.</summary>
    Other = 0,

    /// <summary>The mail server was busy, slow or unreachable. The same email may well go later.</summary>
    Temporary = 1,

    /// <summary>The mail account refused the sign-in, or may not send as this address. Every email fails until it is fixed.</summary>
    Account = 2,

    /// <summary>The recipient's address is wrong or does not exist. Only that person's emails fail.</summary>
    Address = 3,
}

/// <summary>A failure in plain words, with what to do about it.</summary>
public sealed record EmailFailure(EmailFailureKind Kind, string Reason, string? Fix);

/// <summary>
/// Reads a failed send's message, which is all a mail connection hands back, into a kind, a reason and a fix.
/// Pure, so the rules are tested on their own. Account and address problems are checked first: their
/// messages can also mention a timeout or a retry, and trying again would never fix them.
/// </summary>
public static class EmailFailures
{
    /// <summary>Waits before each automatic retry of a temporary failure; after the last one the failure is final.</summary>
    public static IReadOnlyList<TimeSpan> RetryWaits { get; } = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    private static readonly string[] AccountSigns =
    [
        "authenticat", "credential", "unauthorized", "401 ", "403 ", "invalid_grant", "password", "username and password",
        "535", "5.7.0", "5.7.1", "5.7.3", "5.7.8", "5.7.9", "relay", "not allowed to send", "sender address rejected", "send as",
    ];

    private static readonly string[] AddressSigns =
    [
        "5.1.1", "5.1.2", "5.1.3", "5.1.10", "550", "551", "553", "user unknown", "unknown user", "no such user", "mailbox unavailable",
        "mailbox not found", "recipient address rejected", "does not exist", "not in the form required for an e-mail address", "invalid address",
        "is not a valid email",
    ];

    private static readonly string[] TemporarySigns =
    [
        "timed out", "timeout", "did not answer in time", "failure sending mail", "unable to connect", "unable to read data",
        "connection refused", "connection reset", "connection was closed", "temporar", "try again", "service not available",
        "too many", "throttl", "rate limit", "421", "450", "451", "452", "429 ", "502 ", "503 ", "504 ",
    ];

    public static EmailFailure Classify(string? message)
    {
        string text = (message ?? string.Empty).Trim();
        string lower = text.ToLowerInvariant();

        if (AccountSigns.Any(lower.Contains))
        {
            return new EmailFailure(
                EmailFailureKind.Account,
                "The mail account refused the sign-in, or may not send from this address.",
                "Check the mail connection under Studio › Connectors and the address under Email senders, then resend.");
        }

        if (AddressSigns.Any(lower.Contains))
        {
            return new EmailFailure(
                EmailFailureKind.Address,
                "The recipient's address doesn't exist or isn't a valid email address.",
                "Correct the person's email address, then resend.");
        }

        if (TemporarySigns.Any(lower.Contains))
        {
            return new EmailFailure(EmailFailureKind.Temporary, "The mail server was busy or couldn't be reached.", null);
        }

        return new EmailFailure(EmailFailureKind.Other, text.Length > 0 ? text : "The email could not be sent.", null);
    }

    /// <summary>When to try a temporary failure again after this many attempts; null once the retries are used up.</summary>
    public static TimeSpan? RetryWait(int attemptsSoFar)
        => attemptsSoFar >= 1 && attemptsSoFar <= RetryWaits.Count ? RetryWaits[attemptsSoFar - 1] : null;
}
