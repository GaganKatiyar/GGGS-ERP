using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Time.Application.Holidays;
using Gggs.Time.Application.Snapshots;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Domain.Snapshots;
using Gggs.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Time.Application.Leave;

/// <summary>
/// Submitting and cancelling leave requests - the template every future request-and-approve module
/// follows, per architecture.md §10.4's own worked example (that example is <c>LeaveRequestService</c>
/// itself, one phase ahead of when this was built).
/// </summary>
public sealed class LeaveRequestService(
    ILeaveTypeRepository leaveTypes,
    LeavePolicyResolver policyResolver,
    IWorkCalendar calendar,
    ILeaveLedgerRepository ledger,
    LeaveLedgerService ledgerService,
    ILeaveRequestRepository requests,
    IEmployeeSnapshotRepository snapshots,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<LeaveRequestService> logger)
{
    private const string LeaveRequestWorkflowCode = "LEAVE_REQUEST";
    private const string LeaveRequestEntityType = "LEAVE_REQUEST";

    public async Task<LeaveRequest> SubmitAsync(
        Guid? employeeId,
        Guid leaveTypeId,
        DateOnly fromDate,
        LeaveDaySession fromSession,
        DateOnly toDate,
        LeaveDaySession toSession,
        string? reason,
        Guid? attachmentDocumentId,
        CancellationToken cancellationToken = default)
    {
        // Applying on someone else's behalf needs leave.request.create-on-behalf, not just
        // leave.request.create - LeaveRequestEndpoints checks which permission the caller actually
        // holds against whether employeeId names someone other than themselves, since the endpoint
        // is the layer that knows both the caller's permission set and their own employeeId.
        Guid resolvedEmployeeId = employeeId ?? currentUser.EmployeeId
            ?? throw new ForbiddenException("LEAVE_NO_EMPLOYEE_PROFILE", "Your account has no linked employee profile.");

        LeaveType leaveType = await leaveTypes.GetAsync(leaveTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveType), leaveTypeId);

        EmployeeSnapshot? employee = await snapshots.GetAsync(resolvedEmployeeId, cancellationToken);

        Guard(employee is not null, "LEAVE_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "LEAVE_EMPLOYEE_EXITED", "This employee has already exited.");

        LeavePolicyRule? rule = await policyResolver.ResolveRuleAsync(resolvedEmployeeId, leaveTypeId, cancellationToken);
        Guard(rule is not null, "LEAVE_NO_POLICY_CONFIGURED", $"No leave policy is configured for '{leaveType.Name}'.");

        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        int tenureDays = today.DayNumber - employee.DateOfJoining.DayNumber;
        Guard(
            tenureDays >= rule!.EligibleAfterDays,
            "LEAVE_NOT_ELIGIBLE",
            $"'{leaveType.Name}' is available after {rule.EligibleAfterDays} days of employment.");

        int noticeDays = fromDate.DayNumber - today.DayNumber;
        Guard(
            noticeDays >= rule.MinNoticeDays,
            "LEAVE_INSUFFICIENT_NOTICE",
            $"'{leaveType.Name}' requires at least {rule.MinNoticeDays} days' notice.");

        if (!rule.AllowHalfDay)
        {
            Guard(
                fromSession == LeaveDaySession.Full && toSession == LeaveDaySession.Full,
                "LEAVE_HALF_DAY_NOT_ALLOWED",
                $"'{leaveType.Name}' does not allow half-day requests.");
        }

        IReadOnlyList<(DateOnly Date, decimal DayFraction)> workDays =
            await calendar.GetWorkDaysAsync(resolvedEmployeeId, fromDate, fromSession, toDate, toSession, cancellationToken);

        decimal totalDays = workDays.Sum(d => d.DayFraction);

        Guard(totalDays >= rule.MinDaysPerRequest, "LEAVE_BELOW_MINIMUM_DAYS", $"A request must be at least {rule.MinDaysPerRequest} day(s).");

        if (rule.MaxConsecutiveDays is { } maxConsecutive)
        {
            Guard(
                toDate.DayNumber - fromDate.DayNumber + 1 <= maxConsecutive,
                "LEAVE_EXCEEDS_MAX_CONSECUTIVE_DAYS",
                $"'{leaveType.Name}' cannot be requested for more than {maxConsecutive} consecutive days.");
        }

        bool hasOverlap = await requests.HasOverlapAsync(resolvedEmployeeId, fromDate, toDate, cancellationToken);
        Guard(!hasOverlap, "LEAVE_OVERLAPS_EXISTING_REQUEST", "This date range overlaps an existing leave request.");

        if (!leaveType.IsLop)
        {
            LeaveBalance? balance = await ledger.GetBalanceAsync(resolvedEmployeeId, leaveTypeId, fromDate.Year, cancellationToken);
            decimal available = balance?.Available ?? 0m;

            if (!rule.AllowNegativeBalance)
            {
                Guard(
                    available - totalDays >= 0,
                    "LEAVE_INSUFFICIENT_BALANCE",
                    $"Insufficient balance: {available} day(s) available, {totalDays} requested.");
            }
            else
            {
                Guard(
                    available - totalDays >= -rule.NegativeLimit,
                    "LEAVE_INSUFFICIENT_BALANCE",
                    $"This request would exceed the allowed negative balance for '{leaveType.Name}'.");
            }
        }

        LeaveRequest request = LeaveRequest.Submit(
            GenerateRequestNo(clock),
            resolvedEmployeeId,
            currentUser.UserId,
            leaveTypeId,
            fromDate, fromSession, toDate, toSession,
            reason, attachmentDocumentId, totalDays);

        requests.Add(request);

        foreach ((DateOnly date, decimal fraction) in workDays)
        {
            requests.AddDay(LeaveRequestDay.Create(request.Id, date, fraction));
        }

        // The debit posts on approval, not here - see WorkflowOutcomeConsumer. Submission only
        // moves the balance's PendingApproval bucket, by rebuilding it from every other open
        // request (LeaveLedgerService.RebuildBalanceAsync reads pending totals fresh each time, so
        // a newly-added Pending request is picked up as soon as it is visible in this transaction).
        await ledgerService.RebuildBalanceAsync(resolvedEmployeeId, leaveTypeId, fromDate.Year, cancellationToken);

        publisher.Publish(new StartWorkflowIntegrationEvent(
            LeaveRequestWorkflowCode,
            LeaveRequestEntityType,
            request.Id,
            SourceService: "time",
            InitiatorUserId: currentUser.UserId,
            SubjectEmployeeId: resolvedEmployeeId,
            ContextJson: BuildWorkflowContextJson(leaveType, totalDays),
            CorrelationId: currentUser.CorrelationId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Leave request {LeaveRequestId} submitted for employee {EmployeeId} ({Days} days of {LeaveTypeCode}).",
            request.Id, resolvedEmployeeId, totalDays, leaveType.Code);

        return request;
    }

    public async Task CancelAsync(Guid leaveRequestId, CancellationToken cancellationToken = default)
    {
        LeaveRequest request = await requests.GetAsync(leaveRequestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), leaveRequestId);

        bool wasApproved = request.Status == LeaveRequestStatus.Approved;
        request.Cancel();

        if (wasApproved)
        {
            // Reverses the debit LeaveWorkflowOutcomeConsumer posted on approval - keyed on the
            // same request id but a different transaction type, so it does not collide with the
            // original debit's own idempotency key (see LeaveLedgerService.PostAsync's own remarks).
            await ledgerService.PostAsync(
                request.EmployeeId, request.LeaveTypeId, request.FromDate.Year,
                LeaveLedgerTransactionType.Reversal, request.TotalDays, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                sourceType: "LeaveRequest", sourceId: request.Id, remarks: "Cancelled after approval", cancellationToken);
        }
        else
        {
            await ledgerService.RebuildBalanceAsync(request.EmployeeId, request.LeaveTypeId, request.FromDate.Year, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string BuildWorkflowContextJson(LeaveType leaveType, decimal totalDays)
        => System.Text.Json.JsonSerializer.Serialize(new { leaveTypeCode = leaveType.Code, days = totalDays });

    private static string GenerateRequestNo(TimeProvider clock)
        => $"LR-{clock.GetUtcNow():yyyyMMdd}-{Guid.CreateVersion7().ToString("N")[..6].ToUpperInvariant()}";

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
