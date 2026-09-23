using System.Linq.Expressions;
using Gggs.BuildingBlocks.MultiTenancy;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>Hangfire-backed scheduling, with the tenant carried on every job.</summary>
public sealed class HangfireTenantJobScheduler(
    IBackgroundJobClient backgroundJobs,
    IRecurringJobManager recurringJobs,
    ITenantContext tenantContext) : ITenantJobScheduler
{
    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall)
        => backgroundJobs.Enqueue(methodCall);

    public string Schedule<TJob>(Expression<Func<TJob, Task>> methodCall, TimeSpan delay)
        => backgroundJobs.Schedule(methodCall, delay);

    public void AddOrUpdateRecurring<TJob>(string jobName, Expression<Func<TJob, Task>> methodCall, string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        recurringJobs.AddOrUpdate(
            RecurringJobId.For(jobName, tenantContext.TenantId),
            methodCall,
            cronExpression,
            new RecurringJobOptions());
    }

    public void RemoveRecurring(string jobName, Guid tenantId)
        => recurringJobs.RemoveIfExists(RecurringJobId.For(jobName, tenantId));
}

/// <summary>
/// Queues one job per active tenant instead of looping over tenants inside a single job.
/// <para>
/// A loop would let one large tenant's failure or slowness hold everyone behind it, and retries
/// would be all-or-nothing: re-running a nightly accrual because tenant 40 of 100 failed would
/// double-post for the 39 that already succeeded. One job per tenant retries, fails and reports
/// independently.
/// </para>
/// </summary>
public sealed class TenantFanOutScheduler(
    IBackgroundJobClient backgroundJobs,
    IActiveTenantProvider activeTenants,
    ILogger<TenantFanOutScheduler> logger) : ITenantFanOutScheduler
{
    public async Task FanOutAsync<TJob>(
        Expression<Func<TJob, Guid, Task>> methodCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(methodCall);

        IReadOnlyCollection<Guid> tenantIds = await activeTenants.GetActiveTenantIdsAsync(cancellationToken);

        foreach (Guid tenantId in tenantIds)
        {
            // The tenant id is baked into the queued call, and the client filter stamps it as a job
            // parameter too, so the activator can restore the context before resolving dependencies.
            Expression<Func<TJob, Task>> tenantCall = BindTenant(methodCall, tenantId);
            backgroundJobs.Create(tenantCall, new Hangfire.States.EnqueuedState());
        }

        logger.LogInformation(
            "Fanned out {JobType} to {TenantCount} active tenants.",
            typeof(TJob).Name,
            tenantIds.Count);
    }

    private static Expression<Func<TJob, Task>> BindTenant<TJob>(
        Expression<Func<TJob, Guid, Task>> methodCall,
        Guid tenantId)
    {
        var replaced = new ParameterReplacer(methodCall.Parameters[1], Expression.Constant(tenantId))
            .Visit(methodCall.Body);

        return Expression.Lambda<Func<TJob, Task>>(replaced, methodCall.Parameters[0]);
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == parameter ? replacement : base.VisitParameter(node);
    }
}

/// <summary>
/// Lists the tenants a fan-out should cover. Backed by the Platform service, which owns the tenant
/// registry; suspended and offboarding tenants are excluded so their data is left alone.
/// </summary>
public interface IActiveTenantProvider
{
    Task<IReadOnlyCollection<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default);
}
