using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Timesheets.Domain;

public enum ApprovalStatus
{
    /// <summary>With its approver.</summary>
    Submitted = 1,
    Approved = 2,
    Rejected = 3,

    /// <summary>Withdrawn, sent back or reopened: the person can change it and submit it again.</summary>
    Returned = 4,
}

/// <summary>
/// One approver's share of a week: the hours on one project, for its manager. Hours on a department's
/// default project, or on no project, form one share (no <see cref="ProjectId"/>) for the person's own
/// manager. Hours count once their share is approved, whatever happens to the rest of the week.
/// </summary>
public sealed class TimesheetApproval : TenantAggregateRoot
{
    private TimesheetApproval()
    {
    }

    public Guid TimesheetId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateOnly WeekStart { get; private set; }

    public Guid? ProjectId { get; private set; }

    /// <summary>Who it went to when submitted. Workflow may hand the task on, through delegation or escalation.</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    public ApprovalStatus Status { get; private set; }

    public decimal Hours { get; private set; }

    public decimal BillableHours { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime SubmittedDate { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    /// <summary>Its hours cannot change: an approver has them, or has approved them.</summary>
    public bool IsLocked => Status is ApprovalStatus.Submitted or ApprovalStatus.Approved;

    public static TimesheetApproval Open(Timesheet timesheet, Guid? projectId)
    {
        ArgumentNullException.ThrowIfNull(timesheet);
        return new TimesheetApproval
        {
            TimesheetId = timesheet.Id,
            EmployeeId = Guard.NotEmpty(timesheet.EmployeeId),
            WeekStart = timesheet.WeekStart,
            ProjectId = projectId,
            Status = ApprovalStatus.Returned,
        };
    }

    public void Submit(decimal hours, decimal billableHours, Guid? ownerEmployeeId, DateTime now)
    {
        if (IsLocked)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "These hours are already with their approver, or approved.");
        }

        Hours = hours;
        BillableHours = billableHours;
        OwnerEmployeeId = ownerEmployeeId;
        Status = ApprovalStatus.Submitted;
        WorkflowInstanceId = null;
        SubmittedDate = now;
        DecidedDate = null;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Approve(DateTime now)
    {
        if (Status != ApprovalStatus.Submitted)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only submitted hours can be approved.");
        }

        Status = ApprovalStatus.Approved;
        DecidedDate = now;
    }

    public void Reject(DateTime now)
    {
        if (Status == ApprovalStatus.Submitted)
        {
            Status = ApprovalStatus.Rejected;
            WorkflowInstanceId = null;
            DecidedDate = now;
        }
    }

    /// <summary>Withdrawn or sent back, before a decision.</summary>
    public void Return()
    {
        if (Status == ApprovalStatus.Submitted)
        {
            Status = ApprovalStatus.Returned;
            WorkflowInstanceId = null;
        }
    }

    /// <summary>Approved hours opened again for a correction: they stop counting until approved again.</summary>
    public void Reopen()
    {
        if (Status != ApprovalStatus.Approved)
        {
            throw new DomainRuleException("TIMESHEET_STATE", "Only approved hours are reopened.");
        }

        Status = ApprovalStatus.Returned;
        WorkflowInstanceId = null;
        DecidedDate = null;
    }
}

/// <summary>How a week's status follows from its shares.</summary>
public static class TimesheetApprovals
{
    /// <summary>
    /// Any share rejected needs the person's attention first. Otherwise any share still with an approver
    /// means the week is submitted. The week is approved only when every hour in it is.
    /// </summary>
    public static TimesheetStatus WeekStatus(IReadOnlyCollection<ApprovalStatus> shares, bool hasHoursInNoShare)
    {
        ArgumentNullException.ThrowIfNull(shares);

        if (shares.Contains(ApprovalStatus.Rejected))
        {
            return TimesheetStatus.Rejected;
        }

        if (shares.Contains(ApprovalStatus.Submitted))
        {
            return TimesheetStatus.Submitted;
        }

        return shares.Count > 0 && !hasHoursInNoShare && shares.All(status => status == ApprovalStatus.Approved)
            ? TimesheetStatus.Approved
            : TimesheetStatus.Draft;
    }
}
