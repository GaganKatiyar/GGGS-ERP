using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

public sealed class JobRequisitionService(
    IJobRequisitionRepository requisitions, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<JobRequisition> CreateAsync(
        string title,
        Guid departmentId,
        Guid designationId,
        Guid branchId,
        int openings,
        Guid? hiringManagerEmployeeId,
        string? justification,
        CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = JobRequisition.Create(
            GenerateRequisitionNo(clock), title, departmentId, designationId, branchId,
            openings, hiringManagerEmployeeId, justification);

        requisitions.Add(requisition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    public async Task<JobRequisition> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await GetOrThrowAsync(id, cancellationToken);
        requisition.Open();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    public async Task<JobRequisition> PutOnHoldAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await GetOrThrowAsync(id, cancellationToken);
        requisition.PutOnHold();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    public async Task<JobRequisition> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await GetOrThrowAsync(id, cancellationToken);
        requisition.Resume();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    public async Task<JobRequisition> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await GetOrThrowAsync(id, cancellationToken);
        requisition.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    public async Task<JobRequisition> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        JobRequisition requisition = await GetOrThrowAsync(id, cancellationToken);
        requisition.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return requisition;
    }

    private async Task<JobRequisition> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await requisitions.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(JobRequisition), id);

    // Version 7 GUIDs are timestamp-ordered, so their leading hex digits repeat within the same
    // millisecond (unlike LeaveRequestService.GenerateRequestNo's non-time-ordered v4/v7 usage
    // elsewhere) - taking the trailing digits instead of the leading ones keeps two requisitions
    // created back-to-back in a test run from colliding on the same suffix.
    private static string GenerateRequisitionNo(TimeProvider clock)
        => $"REQ-{clock.GetUtcNow():yyyyMMdd}-{Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant()}";
}
