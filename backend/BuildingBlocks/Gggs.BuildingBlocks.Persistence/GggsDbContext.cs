using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gggs.BuildingBlocks.Persistence;

/// <summary>Names of the global query filters, so a query can waive one without waiving the other.</summary>
public static class QueryFilters
{
    /// <summary>Restricts every query to the current tenant.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Hides soft-deleted rows.</summary>
    public const string SoftDelete = "SoftDelete";
}

/// <summary>
/// Base context for every service database.
/// <para>
/// It applies the tenant and soft-delete filters to all entities by convention, so isolation does
/// not depend on each query remembering to filter. That is the third of the four layers in the
/// isolation design: token, EF filter, SQL row-level security, and same-tenant foreign keys. The
/// database-level ones stay in force even for hand-written SQL and for bugs in this layer.
/// </para>
/// </summary>
public abstract class GggsDbContext(DbContextOptions options, ITenantContext tenantContext)
    : DbContext(options), IUnitOfWork
{
    /// <summary>
    /// Exposed as a property because EF embeds a reference to it in the compiled query filters and
    /// re-reads it per query, which is what makes one cached model safe for every tenant.
    /// </summary>
    public Guid CurrentTenantId => tenantContext.IsResolved ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// True only inside an explicit platform scope (migrations, provisioning, platform metrics).
    /// Those paths connect as a principal that is also exempt from row-level security.
    /// </summary>
    public bool IsPlatformScope => tenantContext.IsPlatformScope;

    /// <summary>The schema that owns this service's tables, for example <c>emp</c> or <c>leave</c>.</summary>
    protected abstract string DefaultSchema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultSchema);

        base.OnModelCreating(modelBuilder);

        // Conventions run last so they also cover entities configured by the derived context.
        modelBuilder.ApplyGggsConventions(this);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // The execution strategy owns the retry loop, so a transient failure retries the whole
        // transaction rather than replaying half of it.
        IExecutionStrategy strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction = await Database.BeginTransactionAsync(ct);
            T result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}
