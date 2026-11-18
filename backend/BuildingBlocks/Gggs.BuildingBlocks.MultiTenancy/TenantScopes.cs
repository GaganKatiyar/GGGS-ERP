using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// Runs work inside one tenant from a request that is not that tenant's - the platform console acting
/// on a customer (inviting their administrator, say).
/// <para>
/// A fresh service scope, not the caller's: the caller sits in the platform scope, which lifts the
/// tenant filter and leaves the database session without a tenant, so simply setting a tenant on it
/// would read and write with no isolation at all. The new scope starts clean, gets only the tenant, and
/// every query in it is filtered and row-level-secured like any request from that customer.
/// </para>
/// </summary>
public interface ITenantScopes
{
    Task<T> RunAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken = default);
}

public sealed class TenantScopes(IServiceScopeFactory scopes, ITenantRegistry registry) : ITenantScopes
{
    public async Task<T> RunAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        TenantInfo tenant = await registry.GetAsync(tenantId, cancellationToken)
            ?? throw new TenantNotResolvedException();

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenant);
        return await work(scope.ServiceProvider);
    }
}
