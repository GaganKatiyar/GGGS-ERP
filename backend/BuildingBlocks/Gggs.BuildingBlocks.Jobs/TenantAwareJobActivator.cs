using Gggs.BuildingBlocks.MultiTenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gggs.BuildingBlocks.Jobs;

/// <summary>
/// Creates the dependency-injection scope a job runs in, with the tenant already restored.
/// <para>
/// This has to happen in the activator rather than in a server filter: the job's dependencies -
/// its DbContext, repositories and caches - are built from this scope, so the tenant must be set
/// before any of them are resolved. A filter running after activation would be too late, and the
/// DbContext would already have captured an unresolved tenant.
/// </para>
/// </summary>
public sealed class TenantAwareJobActivator(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantAwareJobActivator> logger) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IServiceScope scope = scopeFactory.CreateScope();

        try
        {
            ITenantContextSetter tenantSetter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();

            string? platformReason = context.GetJobParameter<string>(TenantJobFilter.PlatformReasonParameterName);
            if (!string.IsNullOrEmpty(platformReason))
            {
                // Deliberate cross-tenant work: provisioning, platform metrics, retention sweeps.
                // Logged every time, because it is an exception to the platform's core rule.
                logger.LogInformation(
                    "Job {JobId} runs in a platform scope: {Reason}",
                    context.BackgroundJob.Id,
                    platformReason);

                return new TenantJobScope(scope, tenantSetter.BeginPlatformScope(platformReason));
            }

            Guid tenantId = context.GetJobParameter<Guid>(TenantJobFilter.TenantParameterName);
            if (tenantId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Job {context.BackgroundJob.Id} carries no tenant and is not marked [PlatformJob]. Refusing to run it.");
            }

            tenantSetter.SetTenant(tenantId);
            return new TenantJobScope(scope, platformScope: null);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class TenantJobScope(IServiceScope scope, IDisposable? platformScope) : JobActivatorScope
    {
        public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);

        public override void DisposeScope()
        {
            platformScope?.Dispose();
            scope.Dispose();
        }
    }
}
