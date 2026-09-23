using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Gggs.Sample.Api.Infrastructure;

/// <summary>
/// Tenants for the Phase 0 harness, supplied from configuration.
/// <para>
/// Phase 1 replaces this with the Platform service as the source of truth, fronted by a cache. The
/// interface is what the rest of the code depends on, so that swap does not reach into any handler.
/// </para>
/// </summary>
public sealed class ConfiguredTenantRegistry(IOptions<SampleTenantOptions> options) : ITenantRegistry
{
    private readonly Dictionary<Guid, TenantInfo> _tenants = options.Value.Tenants
        .ToDictionary(tenant => tenant.Id);

    public Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.GetValueOrDefault(tenantId));

    public Task<TenantInfo?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.Values.FirstOrDefault(
            tenant => string.Equals(tenant.Code, tenantCode, StringComparison.OrdinalIgnoreCase)));

    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Configuration shape for the harness tenants.</summary>
public sealed class SampleTenantOptions
{
    public const string SectionName = "Gggs:Tenants";

    public IList<TenantInfo> Tenants { get; } = [];
}

