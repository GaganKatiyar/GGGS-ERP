using System.Globalization;
using System.Reflection;
using Gggs.BuildingBlocks.MultiTenancy;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>
/// Stamps the current tenant onto a job as it is queued.
/// <para>
/// A job runs long after the request that queued it, with no token and no HTTP context. The tenant
/// has to travel with the job, or every tenant-owned query inside it returns nothing - a failure
/// that looks like "the accrual job did nothing last night" rather than an error.
/// </para>
/// <para>
/// Queuing without a tenant is refused unless the job is marked <see cref="PlatformJobAttribute"/>,
/// so forgetting is a loud failure at enqueue time rather than a silent one at 2am.
/// </para>
/// </summary>
public sealed class TenantJobFilter(IServiceScopeFactory scopeFactory) : IClientFilter
{
    public const string TenantParameterName = "GggsTenantId";
    public const string PlatformReasonParameterName = "GggsPlatformJobReason";

    public void OnCreating(CreatingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PlatformJobAttribute? platformJob = GetPlatformJobAttribute(context.Job);
        if (platformJob is not null)
        {
            context.SetJobParameter(PlatformReasonParameterName, platformJob.Reason);
            return;
        }

        // Resolved per call: this filter is a singleton, while the tenant context is scoped to the
        // request queuing the job.
        using IServiceScope scope = scopeFactory.CreateScope();
        ITenantContext tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        if (!tenantContext.IsResolved)
        {
            throw new InvalidOperationException(
                $"Job '{context.Job.Type.Name}.{context.Job.Method.Name}' was queued without a tenant. " +
                "Queue it inside a tenant scope, or mark it [PlatformJob] with a reason.");
        }

        context.SetJobParameter(TenantParameterName, tenantContext.TenantId);
    }

    public void OnCreated(CreatedContext context)
    {
    }

    internal static PlatformJobAttribute? GetPlatformJobAttribute(Job job)
        => job.Method.GetCustomAttribute<PlatformJobAttribute>()
            ?? job.Type.GetCustomAttribute<PlatformJobAttribute>();
}

/// <summary>Builds a per-tenant recurring job id, so tenants never share a schedule entry.</summary>
public static class RecurringJobId
{
    public static string For(string jobName, Guid tenantId)
        => string.Create(CultureInfo.InvariantCulture, $"{jobName}:{tenantId:N}");
}
