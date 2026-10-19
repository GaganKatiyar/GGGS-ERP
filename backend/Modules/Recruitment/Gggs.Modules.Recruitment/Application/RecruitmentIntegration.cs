using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Recruitment.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workforce.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Application;

/// <summary>Candidate documents (CVs, certificates, signed offer letters) follow application access.</summary>
internal sealed class CandidateDocumentAccess(AppDbContext dbContext, IPermissionProvider permissions) : IDocumentOwnerAccess
{
    public string OwnerType => "Candidate";

    public async Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Recruitment.ApplicationView) && await ExistsAsync(ownerId, cancellationToken);

    public async Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Recruitment.ApplicationManage) && await ExistsAsync(ownerId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. ownerIds.Distinct()];
        return await dbContext.Candidates.AsNoTracking().Where(candidate => ids.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.LastName == null ? candidate.FirstName : candidate.FirstName + " " + candidate.LastName, cancellationToken);
    }

    private Task<bool> ExistsAsync(Guid candidateId, CancellationToken cancellationToken)
        => dbContext.Candidates.AnyAsync(candidate => candidate.Id == candidateId, cancellationToken);
}

/// <summary>An approved manpower request opens a requisition.</summary>
internal sealed class OpenRequisitionOnRequestApproved(RecruitmentService recruitment) : IIntegrationEventConsumer<ManpowerRequestApprovedIntegrationEvent>
{
    public Task HandleAsync(ManpowerRequestApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => recruitment.OpenFromRequestAsync(integrationEvent, cancellationToken);
}

internal sealed class ApproveOfferOnApproval(OfferService offers) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == OfferService.SubjectType ? offers.DecideAsync(integrationEvent.SubjectId, approved: true, cancellationToken) : Task.CompletedTask;
}

internal sealed class RejectOfferOnRejection(OfferService offers) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == OfferService.SubjectType ? offers.DecideAsync(integrationEvent.SubjectId, approved: false, cancellationToken) : Task.CompletedTask;
}

internal sealed class ReturnOfferOnSendBack(OfferService offers) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => integrationEvent?.SubjectType == OfferService.SubjectType ? offers.DecideAsync(integrationEvent.SubjectId, approved: null, cancellationToken) : Task.CompletedTask;
}

/// <summary>Fans the daily referral-bonus check out to every tenant.</summary>
internal sealed class ReferralBonusSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "referral-bonuses";

    public Task RunAsync() => fanOut.FanOutAsync<ReferralBonusJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class ReferralBonusJob(OfferService offers, ILogger<ReferralBonusJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int awarded = await offers.AwardDueReferralBonusesAsync(CancellationToken.None);
        if (awarded > 0)
        {
            logger.LogInformation("Referral bonuses for tenant {TenantId}: {Count} added to payroll.", tenantId, awarded);
        }
    }
}

internal sealed class RecruitmentJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<ReferralBonusSweep>(ReferralBonusSweep.RecurringJobName, sweep => sweep.RunAsync(), "30 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
