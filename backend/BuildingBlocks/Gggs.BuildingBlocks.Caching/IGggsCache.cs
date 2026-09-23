namespace Gggs.BuildingBlocks.Caching;

/// <summary>
/// Cache access that cannot be used across tenants.
/// <para>
/// There is deliberately no method taking a raw key. Cached HR data is as sensitive as stored HR
/// data, and a shared Redis with hand-built keys is the easiest place in a platform like this to
/// serve one tenant another tenant's employee list. Every key is composed here from the ambient
/// tenant, and anything genuinely tenant-independent has to say so by name.
/// </para>
/// </summary>
public interface IGggsCache
{
    /// <summary>Reads a tenant-scoped entry, creating it via <paramref name="factory"/> on a miss.</summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string area,
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string area, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every entry in one area for the current tenant, for example all cached leave policies
    /// after an admin edits them.
    /// </summary>
    ValueTask RemoveAreaAsync(string area, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an entry that genuinely belongs to no tenant, such as the platform permission catalog
    /// or a published compliance pack. Separate method, so using it is a visible choice.
    /// </summary>
    ValueTask<T> GetOrCreatePlatformAsync<T>(
        string area,
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Cache areas, named once so invalidation and key layout do not drift.</summary>
public static class CacheAreas
{
    public const string TenantInfo = "tenant";
    public const string Permissions = "perm";
    public const string Settings = "settings";
    public const string Lookups = "lookup";
    public const string OrgStructure = "org";
    public const string Policies = "policy";
    public const string HolidayCalendar = "holiday";
    public const string Dashboard = "dashboard";
}
