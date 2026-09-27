using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Caching;

/// <summary>
/// Reads the tenant registry from the Platform service over HTTP.
/// <para>
/// Every service except Platform itself needs this, and it is the same client for all of them: none
/// of them keeps its own copy of tenant status, because two services each holding their own view of
/// who exists and whether they are active is how a suspended customer ends up still able to reach
/// one of them - the registry says suspended, the copy says active, and nobody notices until they
/// do.
/// </para>
/// <para>
/// Cached briefly rather than not at all, because this sits on the request path of everything the
/// service does. The window is short and Platform invalidates on status change, so a suspension
/// bites within minutes in practice, not instantly - the trade this class exists to make.
/// </para>
/// </summary>
public sealed class RemoteTenantRegistry(
    HttpClient httpClient,
    IGggsCache cache,
    ILogger<RemoteTenantRegistry> logger) : ITenantRegistry
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
            // Fails closed. If the registry cannot be reached, this service cannot establish that a
            // tenant is active, and letting requests through on the assumption that it is would mean
            // an outage in Platform quietly disabling suspension everywhere else.
            logger.LogError(exception, "The tenant registry could not be reached.");
            return null;
        }
    }
}

/// <summary>Registers the tenant registry client every downstream service uses.</summary>
public static class RemoteTenantRegistryExtensions
{
    /// <summary>
    /// Points this service at Platform for tenant lookups.
    /// <para>
    /// Not for the Platform service itself, which owns the registry and reads its own database
    /// directly rather than calling itself over HTTP.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsRemoteTenantRegistry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient<ITenantRegistry, RemoteTenantRegistry>(client =>
        {
            client.BaseAddress = new Uri(configuration["Gggs:Services:Platform"] ?? "http://platform");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
