using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Recruitment;

public enum JobRequisitionStatus
{
    Draft = 1,
    Open = 2,
    OnHold = 3,
    Closed = 4,
    Cancelled = 5,
}

/// <summary>
/// A request to hire: "we need 2 more Engineers in the Bangalore office." The first step of the
/// recruitment pipeline - postings, candidates, applications, interviews and offers all hang off a
/// requisition, none of which exist yet in this v1 slice.
/// <para>
/// Organizational references (<see cref="DepartmentId"/>, <see cref="DesignationId"/>,
/// <see cref="BranchId"/>) are stored as-is rather than validated against Core HR synchronously:
/// unlike <c>EmployeeService.HireAsync</c>'s reference checks, a wrong department on a draft
/// requisition is a low-stakes mistake an editor catches before it goes live, not a data-integrity
/// risk worth a cross-service call on every save. The hiring manager, if named, is shown by display
/// name through <see cref="Snapshots.EmployeeSnapshot"/> - the same read-only copy pattern Time and
/// Workflow already use (architecture.md §5.2) - rather than validated either.
/// </para>
/// </summary>
public sealed class JobRequisition : TenantAggregateRoot
{
    private JobRequisition()
    {
        RequisitionNo = null!;
        Title = null!;
    }

    public string RequisitionNo { get; private set; }

    public string Title { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid DesignationId { get; private set; }

    public Guid BranchId { get; private set; }

    public int Openings { get; private set; }

    public Guid? HiringManagerEmployeeId { get; private set; }

    public string? Justification { get; private set; }

    public JobRequisitionStatus Status { get; private set; }

    public static JobRequisition Create(
        string requisitionNo,
        string title,
        Guid departmentId,
        Guid designationId,
        Guid branchId,
        int openings,
        Guid? hiringManagerEmployeeId,
        string? justification)
    {
        Guard.Against(openings < 1, "REQUISITION_OPENINGS_MUST_BE_POSITIVE", "Openings must be at least 1.");

        return new JobRequisition
        {
            RequisitionNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(requisitionNo).Trim().ToUpperInvariant(), 30),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
            DepartmentId = Guard.NotEmpty(departmentId),
            DesignationId = Guard.NotEmpty(designationId),
            BranchId = Guard.NotEmpty(branchId),
            Openings = openings,
            HiringManagerEmployeeId = hiringManagerEmployeeId,
            Justification = string.IsNullOrWhiteSpace(justification) ? null : Guard.MaxLength(justification.Trim(), 1000),
            Status = JobRequisitionStatus.Draft,
            IsActive = true,
        };
    }

    /// <summary>Draft -> Open: the requisition is now hiring-ready and postings may be created
    /// against it (once postings exist, in a later slice).</summary>
    public void Open()
    {
        Guard.Against(
            Status != JobRequisitionStatus.Draft,
            "REQUISITION_NOT_DRAFT",
            $"Requisition '{RequisitionNo}' must be Draft to open; it is {Status}.");

        Status = JobRequisitionStatus.Open;
    }

    public void PutOnHold()
    {
        Guard.Against(
            Status != JobRequisitionStatus.Open,
            "REQUISITION_NOT_OPEN",
            $"Requisition '{RequisitionNo}' must be Open to put on hold; it is {Status}.");

        Status = JobRequisitionStatus.OnHold;
    }

    public void Resume()
    {
        Guard.Against(
            Status != JobRequisitionStatus.OnHold,
            "REQUISITION_NOT_ON_HOLD",
            $"Requisition '{RequisitionNo}' must be OnHold to resume; it is {Status}.");

        Status = JobRequisitionStatus.Open;
    }

    public void Close()
    {
        Guard.Against(
            Status is not (JobRequisitionStatus.Open or JobRequisitionStatus.OnHold),
            "REQUISITION_NOT_CLOSABLE",
            $"Requisition '{RequisitionNo}' must be Open or OnHold to close; it is {Status}.");

        Status = JobRequisitionStatus.Closed;
    }

    public void Cancel()
    {
        Guard.Against(
            Status is JobRequisitionStatus.Closed or JobRequisitionStatus.Cancelled,
            "REQUISITION_ALREADY_TERMINAL",
            $"Requisition '{RequisitionNo}' is already {Status}.");

        Status = JobRequisitionStatus.Cancelled;
    }
}
