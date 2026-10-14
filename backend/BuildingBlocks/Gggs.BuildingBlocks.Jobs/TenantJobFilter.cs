using System.Globalization;
using System.Reflection;
using Gggs.BuildingBlocks.MultiTenancy;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;

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
public sealed class TenantJobFilter : IClientFilter
{
    public const string TenantParameterName = "GggsTenantId";
    public const string PlatformReasonParameterName = "GggsPlatformJobReason";

    /// <summary>Set by Hangfire on a job a recurring schedule creates.</summary>
    private const string RecurringJobIdParameterName = "RecurringJobId";

    public void OnCreating(CreatingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PlatformJobAttribute? platformJob = GetPlatformJobAttribute(context.Job);
        if (platformJob is not null)
        {
            context.SetJobParameter(PlatformReasonParameterName, platformJob.Reason);
            return;
        }

        // The tenant comes from whoever is queuing: the scheduler states it explicitly around each
        // call (EnqueueingTenant), and a recurring schedule carries it in its id. It is never read
        // from a DI scope here - this filter is a singleton, and a fresh scope has no tenant in it.
        Guid? tenantId = EnqueueingTenant.Current ?? TenantFromRecurringJobId(context);

        if (tenantId is not { } resolved || resolved == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Job '{context.Job.Type.Name}.{context.Job.Method.Name}' was queued without a tenant. " +
                "Queue it through ITenantJobScheduler or ITenantFanOutScheduler, or mark it [PlatformJob] with a reason.");
        }

        context.SetJobParameter(TenantParameterName, resolved);
    }

    public void OnCreated(CreatedContext context)
    {
    }

    internal static PlatformJobAttribute? GetPlatformJobAttribute(Job job)
        => job.Method.GetCustomAttribute<PlatformJobAttribute>()
            ?? job.Type.GetCustomAttribute<PlatformJobAttribute>();

    /// <summary>Recurring ids are <c>name:tenantId</c> (<see cref="RecurringJobId.For"/>).</summary>
    private static Guid? TenantFromRecurringJobId(CreatingContext context)
    {
        string? recurringId = context.GetJobParameter<string>(RecurringJobIdParameterName);
        if (string.IsNullOrEmpty(recurringId))
        {
            return null;
        }

        int separator = recurringId.LastIndexOf(':');
        return separator > 0 && Guid.TryParseExact(recurringId[(separator + 1)..], "N", out Guid tenantId) ? tenantId : null;
    }
}

/// <summary>
/// The tenant a job is being queued for, stated by the scheduler around the Hangfire call so the
/// client filter can stamp it. Async-local, so concurrent requests queuing jobs never see each
/// other's tenant.
/// </summary>
public static class EnqueueingTenant
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    public static Guid? Current => Ambient.Value;

    public static IDisposable Begin(Guid tenantId)
    {
        Guid? previous = Ambient.Value;
        Ambient.Value = tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

public static class RecurringJobId
{
    public static string For(string jobName, Guid tenantId)
        => string.Create(CultureInfo.InvariantCulture, $"{jobName}:{tenantId:N}");
}
