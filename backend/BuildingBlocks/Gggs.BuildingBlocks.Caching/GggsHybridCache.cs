using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Caching;

/// <summary>Cache key layout and lifetimes.</summary>
public sealed class GggsCacheOptions
{
    public const string SectionName = "Gggs:Cache";

    /// <summary>Separates environments that share a Redis instance.</summary>
    public string Environment { get; set; } = "local";

    /// <summary>Keeps one service from reading another's entries even when the key shape matches.</summary>
    public string ServiceName { get; set; } = "unknown";

    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Tenant-scoped cache over <see cref="HybridCache"/>: an in-process L1 in front of Redis (or of
/// nothing at all on a single-node on-premise install), with stampede protection built in.
/// <para>
/// Entries are tagged by tenant and by area, so invalidating "this tenant's leave policies" does
/// not mean guessing at key names or flushing the cache for everybody.
/// </para>
/// </summary>
public sealed class GggsHybridCache(
    HybridCache cache,
    ITenantContext tenantContext,
    IOptions<GggsCacheOptions> options) : IGggsCache
{
    private readonly GggsCacheOptions _options = options.Value;

    public ValueTask<T> GetOrCreateAsync<T>(
        string area,
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = TenantKey(area, key);

        return cache.GetOrCreateAsync(
            cacheKey,
            factory,
            BuildEntryOptions(expiration),
            [TenantTag(), AreaTag(area)],
            cancellationToken);
    }

    public ValueTask RemoveAsync(string area, string key, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(TenantKey(area, key), cancellationToken);

    public ValueTask RemoveAreaAsync(string area, CancellationToken cancellationToken = default)
        => cache.RemoveByTagAsync(AreaTag(area), cancellationToken);

    public ValueTask<T> GetOrCreatePlatformAsync<T>(
        string area,
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{Prefix()}:platform:{Normalize(area)}:{Normalize(key)}";

        return cache.GetOrCreateAsync(
            cacheKey,
            factory,
            BuildEntryOptions(expiration),
            ["platform"],
            cancellationToken);
    }

    private HybridCacheEntryOptions BuildEntryOptions(TimeSpan? expiration)
    {
        TimeSpan lifetime = expiration ?? _options.DefaultExpiration;

        return new HybridCacheEntryOptions
        {
            Expiration = lifetime,
            // The in-process copy expires sooner, so a change propagates across instances within
            // about a minute even if nobody remembers to invalidate it explicitly.
            LocalCacheExpiration = lifetime < TimeSpan.FromMinutes(1) ? lifetime : TimeSpan.FromMinutes(1),
        };
    }

    private string Prefix() => $"gggs:{Normalize(_options.Environment)}:{Normalize(_options.ServiceName)}";

    private string TenantKey(string area, string key)
        => $"{Prefix()}:t:{tenantContext.TenantId:N}:{Normalize(area)}:{Normalize(key)}";

    private string TenantTag() => $"t:{tenantContext.TenantId:N}";

    private string AreaTag(string area) => $"t:{tenantContext.TenantId:N}:{Normalize(area)}";

    /// <summary>Keeps separators out of user-supplied fragments so keys cannot be forged.</summary>
    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Replace(':', '_').ToLowerInvariant();
    }
}
