using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Data;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Domain;
using Gggs.Modules.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workflow.Application;

/// <summary>
/// Gives an organisation the approval a subject asks for by default. Only a subject that has never had a
/// definition gets one: one an administrator changed, switched off or deleted is their decision and stays so.
/// </summary>
internal sealed class WorkflowDefaultsSeeder(AppDbContext dbContext, IEnumerable<WorkflowSubject> subjects)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        List<WorkflowSubject> wanted = [.. subjects.Where(subject => subject.OwnerApprovesByDefault)];
        if (wanted.Count == 0)
        {
            return 0;
        }

        HashSet<string> existing = [.. await dbContext.WorkflowDefinitions
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Select(definition => definition.SubjectType)
            .Distinct()
            .ToListAsync(cancellationToken)];

        int added = 0;
        foreach (WorkflowSubject subject in wanted.Where(subject => !existing.Contains(subject.Code)))
        {
            WorkflowDefinition definition = WorkflowDefinition.Create(
                CodeFor(subject.Code),
                subject.Name,
                "Added automatically. Goes to whoever owns the record; change or switch it off here.",
                subject.Code,
                [new WorkflowStage("Approver", StageMode.Any, [new ApproverRule(ApproverKind.SubjectOwner)])],
                null);
            definition.Activate();
            dbContext.WorkflowDefinitions.Add(definition);
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    private static string CodeFor(string subject)
    {
        string code = "DEFAULT_" + subject.Replace('.', '_').Replace('-', '_').ToUpperInvariant();
        return code[..Math.Min(code.Length, 32)];
    }
}

internal sealed class SeedWorkflowDefaultsOnTenantProvisioned(WorkflowDefaultsSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>At start-up: gives existing organisations the default approvals they lack.</summary>
[PlatformJob("Workflow defaults: fans out one check per active tenant, adding only approvals never set up.")]
internal sealed class WorkflowDefaultsSweep(ITenantFanOutScheduler fanOut)
{
    public Task RunAsync() => fanOut.FanOutAsync<WorkflowDefaultsJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class WorkflowDefaultsJob(WorkflowDefaultsSeeder seeder)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        await seeder.SeedCurrentTenantAsync(CancellationToken.None);
    }
}
