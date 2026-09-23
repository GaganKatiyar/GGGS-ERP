using System.Linq.Expressions;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>
/// Schedules background work that remembers which tenant it belongs to.
/// <para>
/// A job runs long after the request that queued it, with no token and no HTTP context. If the
/// tenant were not captured at enqueue time and restored at execution time, a leave-accrual job
/// would run with no tenant at all - and the database, which fails closed, would quietly do nothing.
/// Worse, a job that guessed a tenant would act on the wrong company's payroll.
/// </para>
/// </summary>
public interface ITenantJobScheduler
{
    /// <summary>Runs as soon as a worker is free, in the current tenant's context.</summary>
    string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall);

    /// <summary>Runs after a delay, in the current tenant's context.</summary>
    string Schedule<TJob>(Expression<Func<TJob, Task>> methodCall, TimeSpan delay);

    /// <summary>
    /// Registers a recurring job for one tenant. The identifier includes the tenant, so two tenants
    /// never share a schedule entry and one tenant's payroll run cannot cancel another's.
    /// </summary>
    void AddOrUpdateRecurring<TJob>(string jobName, Expression<Func<TJob, Task>> methodCall, string cronExpression);

    void RemoveRecurring(string jobName, Guid tenantId);
}

/// <summary>
/// Work that must run for every active tenant, such as nightly leave accrual or document expiry.
/// <para>
/// Implementations fan out into one job per tenant rather than looping inside a single job. A loop
/// would let one large tenant's failure or slowness hold up everyone behind it, and would make
/// retries all-or-nothing.
/// </para>
/// </summary>
public interface ITenantFanOutScheduler
{
    /// <summary>Queues <typeparamref name="TJob"/> once per active tenant.</summary>
    Task FanOutAsync<TJob>(
        Expression<Func<TJob, Guid, Task>> methodCall,
        CancellationToken cancellationToken = default);
}

/// <summary>Marks a job that runs outside any tenant, such as tenant provisioning or platform metrics.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PlatformJobAttribute : Attribute
{
    public PlatformJobAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why this job may cross tenants; recorded with every run.</summary>
    public string Reason { get; }
}
