using System.Collections.Concurrent;
using Gggs.Gateway.Bff.Infrastructure;
using Microsoft.Extensions.Options;

namespace Gggs.Gateway.Bff.Session;

/// <summary>
/// Keeps a session's access token fresh, one refresh at a time.
/// <para>
/// The lock matters more than it looks. Refresh tokens are one-time, and a browser loading a dashboard
/// fires several requests at once - so without coordination two of them would refresh with the same
/// token, the second would be treated as a replay, and the whole chain would be revoked. The user
/// would be signed out by their own page loading.
/// </para>
/// <para>
/// This removes the race inside one gateway process. Across replicas a small reuse leeway on the
/// Identity side covers the remaining window, which is the standard pairing: the lock handles the
/// common case, the leeway handles the rest, and a token replayed minutes later is still caught.
/// </para>
/// </summary>
public sealed class SessionTokenRefresher(
    BffSessionStore sessions,
    IdentityClient identity,
    IOptions<BffSessionOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionTokenRefresher> logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private readonly BffSessionOptions _options = options.Value;

    /// <summary>
    /// Returns a session whose access token is good for the next request, or null when the session
    /// could not be kept alive and has been ended.
    /// </summary>
    public async Task<BffSession?> EnsureFreshAsync(
        string sessionKey,
        BffSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.NeedsRefresh(timeProvider.GetUtcNow(), _options.RefreshMargin))
        {
            return session;
        }

        SemaphoreSlim gate = Locks.GetOrAdd(sessionKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);

        try
        {
            // Re-read under the lock. Whoever held it first has probably already refreshed, and this
            // is what stops the queue behind them all doing it again with a dead token.
            BffSession? current = await sessions.PeekAsync(sessionKey, cancellationToken);

            if (current is null)
            {
                return null;
            }

            if (!current.NeedsRefresh(timeProvider.GetUtcNow(), _options.RefreshMargin))
            {
                return current;
            }

            TokenResult refreshed = await identity.RefreshAsync(current.RefreshToken, cancellationToken);

            if (!refreshed.Succeeded || refreshed.AccessToken is null || refreshed.RefreshToken is null)
            {
                // The refresh token is gone: expired, revoked, or the chain was broken by a replay.
                // Either way the session cannot continue, and leaving it in place would mean every
                // later request retrying a refresh that will never work.
                logger.LogInformation(
                    "Session for user {UserId} could not be refreshed ({ErrorCode}); it has been ended.",
                    current.UserId,
                    refreshed.ErrorCode);

                await sessions.RemoveAsync(sessionKey, cancellationToken);
                return null;
            }

            return await sessions.UpdateTokensAsync(
                sessionKey,
                current,
                refreshed.AccessToken,
                refreshed.RefreshToken,
                refreshed.AccessTokenExpiresAt,
                cancellationToken);
        }
        finally
        {
            gate.Release();

            // Dropped once nobody is waiting, so a long-running process does not accumulate one
            // semaphore per session it has ever seen.
            if (gate.CurrentCount == 1)
            {
                Locks.TryRemove(sessionKey, out _);
            }
        }
    }
}
