using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Gggs.Gateway.Bff.Session;

/// <summary>
/// Where sessions live.
/// <para>
/// A distributed store rather than an in-process one, for two reasons that both matter in
/// production: the gateway runs as several replicas, so a session created on one must be readable by
/// the next; and signing out has to actually revoke, which means deleting a record somewhere rather
/// than hoping a cookie is discarded.
/// </para>
/// <para>
/// The session key is 256 bits of cryptographic randomness and is the only thing the browser holds.
/// It is stored hashed, so a dump of the cache is not a set of usable session keys - the same reason
/// passwords are not stored in the clear.
/// </para>
/// </summary>
public sealed class BffSessionStore(
    IDistributedCache cache,
    IOptions<BffSessionOptions> options,
    TimeProvider timeProvider,
    ILogger<BffSessionStore> logger)
{
    private const string KeyPrefix = "gggs:bff:session:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BffSessionOptions _options = options.Value;

    /// <summary>Creates a session and returns the key the cookie carries.</summary>
    public async Task<string> CreateAsync(
        Guid userId,
        Guid? tenantId,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessTokenExpiresAt,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string sessionKey = RandomNumberGenerator.GetHexString(64, lowercase: true);

        var session = new BffSession
        {
            UserId = userId,
            TenantId = tenantId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            SignedInAt = now,
            LastSeenAt = now,
            AbsoluteExpiresAt = now.Add(_options.AbsoluteTimeout),
            UserAgentHash = _options.BindToUserAgent ? HashOf(userAgent) : null,
        };

        await WriteAsync(sessionKey, session, cancellationToken);
        return sessionKey;
    }

    /// <summary>
    /// Replaces the tokens after a refresh, keeping everything else about the session.
    /// <para>
    /// The absolute expiry is deliberately not extended. Refreshing proves the session is in use, not
    /// that the person authenticated again - and a ceiling that moves every ten minutes is no ceiling.
    /// </para>
    /// </summary>
    public async Task<BffSession> UpdateTokensAsync(
        string sessionKey,
        BffSession session,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessTokenExpiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        BffSession updated = session with
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
        };

        await WriteAsync(sessionKey, updated, cancellationToken);
        return updated;
    }

    /// <summary>Reads a session without sliding its idle window, for a refresh re-check under a lock.</summary>
    public async Task<BffSession?> PeekAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        byte[]? stored = await cache.GetAsync(CacheKeyFor(sessionKey), cancellationToken);

        return stored is null ? null : JsonSerializer.Deserialize<BffSession>(stored, SerializerOptions);
    }

    /// <summary>
    /// Reads a session and slides its idle window forward, or returns null when it has expired, was
    /// revoked, or is being replayed from a different browser.
    /// </summary>
    public async Task<BffSession?> TouchAsync(
        string sessionKey,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            return null;
        }

        byte[]? stored = await cache.GetAsync(CacheKeyFor(sessionKey), cancellationToken);

        if (stored is null)
        {
            return null;
        }

        BffSession? session = JsonSerializer.Deserialize<BffSession>(stored, SerializerOptions);

        if (session is null)
        {
            return null;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (now >= session.AbsoluteExpiresAt)
        {
            // The idle window would otherwise keep sliding past the hard ceiling, because the entry
            // is re-written on every request.
            await RemoveAsync(sessionKey, cancellationToken);
            return null;
        }

        if (_options.BindToUserAgent && session.UserAgentHash is not null
            && !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(session.UserAgentHash),
                Convert.FromHexString(HashOf(userAgent))))
        {
            // Treated as theft rather than as a browser upgrade: ending the session costs the user
            // one sign-in, and not ending it costs them everything if the key really was taken.
            logger.LogWarning("Session {SessionUserId} was presented from a different browser; it has been ended.", session.UserId);
            await RemoveAsync(sessionKey, cancellationToken);
            return null;
        }

        BffSession touched = session with { LastSeenAt = now };
        await WriteAsync(sessionKey, touched, cancellationToken);

        return touched;
    }

    public async Task RemoveAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            await cache.RemoveAsync(CacheKeyFor(sessionKey), cancellationToken);
        }
    }

    private async Task WriteAsync(string sessionKey, BffSession session, CancellationToken cancellationToken)
    {
        // The entry's own lifetime enforces the idle timeout, so a session nobody comes back to
        // disappears without anything having to sweep it up.
        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = session.AbsoluteExpiresAt,
            SlidingExpiration = _options.IdleTimeout,
        };

        await cache.SetAsync(
            CacheKeyFor(sessionKey),
            JsonSerializer.SerializeToUtf8Bytes(session, SerializerOptions),
            entryOptions,
            cancellationToken);
    }

    /// <summary>
    /// The key in the cache is a hash of the key in the cookie, so the store never holds a value
    /// that could be presented as a credential.
    /// </summary>
    private static string CacheKeyFor(string sessionKey) => KeyPrefix + HashOf(sessionKey);

    private static string HashOf(string? value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
}
