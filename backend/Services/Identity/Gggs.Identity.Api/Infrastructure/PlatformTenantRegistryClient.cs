using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Identity.Application.Authentication;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Api.Infrastructure;

/// <summary>
/// Reads the tenant registry from the Platform service.
/// <para>
/// Identity keeps no copy of it. Two services each holding their own view of who exists and
/// whether they are active is how a suspended customer ends up still being able to sign in - the
/// registry says suspended, the copy says active, and nobody notices until they do.
/// </para>
/// <para>
/// Cached briefly, because this is on the sign-in path. The window is short and the Platform
/// service invalidates on status change, so a suspension bites immediately in practice.
/// </para>
/// </summary>
public sealed class PlatformTenantRegistryClient(
    HttpClient httpClient,
    IGggsCache cache,
    ILogger<PlatformTenantRegistryClient> logger) : ITenantRegistry
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => tenantId == Guid.Empty
            ? null
            : await cache.GetOrCreatePlatformAsync(
                CacheAreas.TenantInfo,
                tenantId.ToString("N"),
                async token => await FetchAsync($"/internal/tenants/{tenantId}", token),
                CacheDuration,
                cancellationToken);

    public async Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return null;
        }

        string normalized = tenantCode.Trim().ToLowerInvariant();

        return await cache.GetOrCreatePlatformAsync(
            CacheAreas.TenantInfo,
            $"code:{normalized}",
            async token => await FetchAsync($"/internal/tenants/by-code/{Uri.EscapeDataString(normalized)}", token),
            CacheDuration,
            cancellationToken);
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await cache.RemoveAreaAsync(CacheAreas.TenantInfo, cancellationToken);

    private async Task<TenantInfo?> FetchAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(path, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TenantInfo>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Fails closed. If the registry cannot be reached we cannot establish that a tenant is
            // active, and letting sign-ins through on the assumption that it is would mean an
            // outage in one service quietly disabling suspension in another.
            logger.LogError(exception, "The tenant registry could not be reached; sign-in cannot proceed.");
            return null;
        }
    }
}

/// <summary>
/// Maps a verified email domain to its tenant, via the Platform service.
/// <para>
/// Not yet implemented: domain verification is Platform work that has not landed. It returns no
/// match rather than guessing, so sign-in falls back to requiring a tenant code - which is what the
/// subdomain supplies anyway. Guessing here would mean sending someone's password to the wrong
/// customer's user store.
/// </para>
/// </summary>
public sealed class PlatformTenantDomainResolver(ILogger<PlatformTenantDomainResolver> logger) : ITenantDomainResolver
{
    private bool _warned;

    public Task<Guid?> ResolveTenantIdAsync(string emailDomain, CancellationToken cancellationToken = default)
    {
        if (!_warned)
        {
            logger.LogInformation(
                "Email-domain tenant discovery is not configured; sign-in requires a tenant code.");
            _warned = true;
        }

        return Task.FromResult<Guid?>(null);
    }
}
