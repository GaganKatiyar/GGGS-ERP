using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Assets.Contracts;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Expense.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record CaseItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string? Designation,
    SeparationType Type,
    string Reason,
    DateOnly NoticeDate,
    int NoticePeriodDays,
    DateOnly RequestedLastDay,
    DateOnly LastWorkingDay,
    int ShortfallDays,
    bool ShortfallWaived,
    bool EligibleForRehire,
    CaseStatus Status,
    Guid? WorkflowInstanceId,
    int ClearancePending,
    int ClearanceTotal,
    bool InterviewDone,
    SettlementStatus? SettlementStatus,
    decimal? SettlementNet,
    Guid? RelievingLetterId,
    Guid? ExperienceLetterId,
    string? ReasonCode = null,
    string? ReasonLabel = null,
    string? SecondaryReasonCode = null,
    bool? Regretted = null,
    string? RetentionNote = null,
    DateOnly? RequestedReleaseDay = null,
    bool ReleaseWithBuyout = false,
    bool GardenLeave = false,
    int PayInLieuDays = 0,
    string? NomineeName = null,
    string? NomineeRelation = null,
    string? NomineeContact = null,
    bool AwaitingConfirmation = false,
    DateOnly? SettlementDueOn = null,
    DateOnly? GratuityDueOn = null,
    bool SettlementOverdue = false);

public sealed record ClearanceView(Guid Id, ClearanceArea Area, string Title, Guid? OwnerEmployeeId, string? OwnerName, ClearanceStatus Status, decimal RecoveryAmount, string? Note, DateOnly? ClearedOn, bool CanResolve);

public sealed record InterviewView(string PrimaryReason, int OverallRating, bool WouldRecommend, bool WouldReturn, string? WhatWentWell, string? WhatToImprove, DateOnly ConductedOn);

public sealed record SettlementLineView(Guid Id, string Code, string Label, bool IsDeduction, decimal Amount, bool IsManual, string? Basis);

public sealed record SettlementView(
    Guid Id,
    SettlementStatus Status,
    decimal Earnings,
    decimal Deductions,
    decimal Net,
    DateOnly? PaidOn,
    string? PaymentReference,
    IReadOnlyList<SettlementLineView> Lines,
    Guid? PayrollRunId = null,
    string? PayrollStatus = null,
    decimal? PayrollNet = null,
    bool? PayrollSalaryIncluded = null,
    IReadOnlyList<string>? PayrollIssues = null);

public sealed record CaseEventView(string Kind, string Title, string? Note, string? ActorName, DateTime OccurredAt);

public sealed record CaseView(CaseItem Case, IReadOnlyList<ClearanceView> Clearance, InterviewView? Interview, SettlementView? Settlement, IReadOnlyList<CaseEventView>? Events = null, OffboardingView? Offboarding = null, SurveyResponseView? Survey = null);

public sealed record ResignBody(string Reason, DateOnly? RequestedLastDay, string? ReasonCode = null);

public sealed record InitiateBody(
    Guid EmployeeId,
    SeparationType Type,
    string Reason,
    DateOnly NoticeDate,
    DateOnly LastWorkingDay,
    int? NoticePeriodDays,
    string? ReasonCode = null,
    int? PayInLieuDays = null,
    string? NomineeName = null,
    string? NomineeRelation = null,
    string? NomineeContact = null);

public sealed record TermsBody(DateOnly LastWorkingDay, bool ShortfallWaived, bool EligibleForRehire, bool? GardenLeave = null);

public sealed record ClassificationBody(string? ReasonCode, string? SecondaryReasonCode, bool? Regretted, string? RetentionNote);

public sealed record EarlyReleaseBody(DateOnly ReleaseDay, bool WithBuyout);

public sealed record ResolveBody(ClearanceStatus Status, decimal? RecoveryAmount, string? Note);

public sealed record ClearanceBody(ClearanceArea Area, string Title, Guid? OwnerEmployeeId, decimal RecoveryAmount);

public sealed record InterviewBody(string PrimaryReason, int OverallRating, bool WouldRecommend, bool WouldReturn, string? WhatWentWell, string? WhatToImprove);

public sealed record LineBody(string Code, string Label, bool IsDeduction, decimal Amount);

public sealed record PayBody(string? Reference);

public sealed record AlumniItem(Guid Id, Guid EmployeeId, string Name, string? Email, string? LastDesignation, DateOnly ExitDate, SeparationType SeparationType, bool EligibleForRehire, bool OptedIn, string? Notes, string? Mobile = null);

public sealed record AlumniBody(string? Email, bool EligibleForRehire, bool OptedIn, string? Notes, string? Mobile = null);

public sealed record SeparationDashboard(
    int InProgress,
    int PendingApproval,
    int LeavingThisMonth,
    int ClearancePending,
    int SettlementsDue,
    double AttritionLast12Months,
    IReadOnlyDictionary<string, int> ByType,
    IReadOnlyDictionary<string, int> TopExitReasons,
    int SettlementsOverdue = 0);

/// <summary>
/// Separation and full and final (spec: separation): resignation through the workflow or an exit
/// HR starts, notice to an agreed last day, clearance across manager, IT, admin, assets and finance,
/// the exit interview, the settlement (salary, leave encashment, gratuity, notice and other
/// recoveries), relieving and experience letters, the exit itself, and the alumni network.
/// </summary>
internal sealed class SeparationService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeeSeparations separations,
    IAssetLedger assets,
    IExpenseLedger expenses,
    ILeaveBalances leave,
    IPayrollSalaries salaries,
    IStatutoryRuleBook rules,
    ILetterGenerator letters,
    IWorkflowStarter workflow,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider,
    SeparationPolicyService policy,
    OffboardingService offboarding,
    ILeaveCalendar calendar,
    IFinalSettlementPayroll payrollSettlements,
    ExitSurveyService exitSurveys)
{
    public const string SubjectType = "separation.resignation";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Accept a resignation",
        "separation",
        [
            new WorkflowSubjectField("noticeDays", "Notice days offered", WorkflowFieldType.Number),
            new WorkflowSubjectField("shortfallDays", "Notice shortfall in days", WorkflowFieldType.Number),
        ]);

    // ----- Cases --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CaseItem>> CasesAsync(CaseStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<SeparationCase> rows = await VisibleAsync(cancellationToken);
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await ItemsAsync(await rows.OrderBy(row => row.LastWorkingDay).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<CaseView> CaseAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await (await VisibleAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? await ClearerCaseAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SeparationCase), id);
        return await ViewAsync(separation, includeSettlement: await HasAsync(Permissions.Separation.SettlementManage, cancellationToken) || separation.EmployeeId == currentUser.EmployeeId, cancellationToken);
    }

    /// <summary>The caller's own separation in progress (or the latest one), for self-service.</summary>
    public async Task<CaseView?> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        SeparationCase? separation = await dbContext.SeparationCases.AsNoTracking().Where(row => row.EmployeeId == me)
            .OrderByDescending(row => row.CreatedDate).FirstOrDefaultAsync(cancellationToken);
        if (separation is null)
        {
            return null;
        }

        CaseView view = await ViewAsync(separation, includeSettlement: true, cancellationToken);
        return view with { Settlement = view.Settlement?.Status == SettlementStatus.Draft ? null : view.Settlement };
    }

    public async Task<CaseView> ResignAsync(ResignBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        await EnsureNoOpenCaseAsync(me, cancellationToken);
        int noticeDays = await policy.NoticeDaysForAsync(me, cancellationToken);
        var separation = SeparationCase.Open(me, SeparationType.Resignation, body.Reason, today, noticeDays, body.RequestedLastDay ?? today.AddDays(noticeDays), body.ReasonCode);
        dbContext.SeparationCases.Add(separation);
        Note(separation, "submitted", "Resignation submitted", $"Notice {noticeDays} days; last day asked for {Format(separation.RequestedLastDay)}.");
        await dbContext.SaveChangesAsync(cancellationToken);

        EmployeeSummary? person = await employees.GetAsync(me, cancellationToken);
        List<Guid> told = [.. new[] { person?.ManagerEmployeeId, person?.HrbpEmployeeId }.Where(id => id != null).Select(id => id!.Value).Distinct()];
        if (told.Count > 0)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ResignationSubmitted,
                    [.. told.Select(id => new NotificationRecipient(id))],
                    new Dictionary<string, string> { ["employeeName"] = person?.DisplayName ?? string.Empty, ["lastDay"] = Format(separation.RequestedLastDay) },
                    $"/separation/cases/{separation.Id}"),
                cancellationToken);
        }

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = separation.Id,
                Title = $"Resignation: {person?.DisplayName}, last day {Format(separation.RequestedLastDay)}",
                RequesterEmployeeId = me,
                Link = $"/separation/cases/{separation.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["noticeDays"] = (separation.RequestedLastDay.DayNumber - separation.NoticeDate.DayNumber).ToString(CultureInfo.InvariantCulture),
                    ["shortfallDays"] = separation.ShortfallDays.ToString(CultureInfo.InvariantCulture),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            separation.AwaitApproval(started.InstanceId!.Value);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await ApproveAsync(separation, null, cancellationToken);
        }

        return await ViewAsync(separation, includeSettlement: false, cancellationToken);
    }

    /// <summary>HR starts a separation (termination, retirement, contract end...); it takes effect at once.</summary>
    public async Task<CaseView> InitiateAsync(InitiateBody body, CancellationToken cancellationToken)
        => await ViewAsync(await StartAsync(body, Permissions.Separation.CaseManage, cancellationToken), includeSettlement: true, cancellationToken);

    /// <summary>
    /// Opens and approves a separation the company starts, for someone in the caller's scope under
    /// <paramref name="permission"/> - HR's own start, a termination after show-cause, a retrenchment
    /// or an absconding.
    /// </summary>
    internal async Task<SeparationCase> StartAsync(InitiateBody body, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await employees.IsActiveAsync(body.EmployeeId, cancellationToken) || !await employees.IsInScopeAsync(body.EmployeeId, permission, cancellationToken))
        {
            throw new DomainRuleException("SEPARATION_EMPLOYEE", "Choose an active employee you manage separations for.");
        }

        await EnsureNoOpenCaseAsync(body.EmployeeId, cancellationToken);
        var separation = SeparationCase.Open(body.EmployeeId, body.Type, body.Reason, body.NoticeDate, body.NoticePeriodDays ?? (body.LastWorkingDay.DayNumber - body.NoticeDate.DayNumber), body.LastWorkingDay, body.ReasonCode);
        separation.SetPayInLieu(body.PayInLieuDays ?? 0);
        separation.SetNominee(body.NomineeName, body.NomineeRelation, body.NomineeContact);
        dbContext.SeparationCases.Add(separation);
        Note(separation, "initiated", $"{TypeLabel(body.Type)} started by HR", $"Last working day {Format(separation.LastWorkingDay)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        await ApproveAsync(separation, null, cancellationToken);
        return separation;
    }

    /// <summary>
    /// Opens a retirement or contract end ahead of its date for HR to confirm or decline. Nothing
    /// happens to the employee until it is confirmed.
    /// </summary>
    internal async Task<SeparationCase> ProposeAsync(Guid employeeId, SeparationType type, DateOnly lastDay, string reason, DateOnly today, CancellationToken cancellationToken)
    {
        DateOnly noticeDate = lastDay < today ? lastDay : today;
        var separation = SeparationCase.Open(employeeId, type, reason, noticeDate, Math.Min(180, lastDay.DayNumber - noticeDate.DayNumber), lastDay);
        separation.Propose();
        dbContext.SeparationCases.Add(separation);
        Note(separation, "proposed", $"{TypeLabel(type)} proposed", $"Due on {Format(lastDay)}; waiting for HR to confirm.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return separation;
    }

    /// <summary>HR confirms a proposed retirement or contract end: notice starts and offboarding begins.</summary>
    public async Task<CaseView> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(id, cancellationToken);
        if (!separation.AwaitingConfirmation)
        {
            throw new DomainRuleException("SEPARATION_NOT_PROPOSED", "This separation is not waiting for confirmation.");
        }

        Note(separation, "confirmed", "Confirmed by HR", null);
        await ApproveAsync(separation, null, cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    public async Task DecideAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        SeparationCase? separation = await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (separation?.Status != CaseStatus.PendingApproval)
        {
            return;
        }

        if (approved == true)
        {
            await ApproveAsync(separation, null, cancellationToken);
        }
        else
        {
            // A resignation sent back or rejected is closed; the employee can resign again.
            separation.Reject();
            Note(separation, "rejected", "Resignation not accepted", null);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<CaseView> WithdrawAsync(Guid? id, CancellationToken cancellationToken)
    {
        SeparationCase separation = id is { } caseId
            ? await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.Id == caseId, cancellationToken) ?? throw new NotFoundException(nameof(SeparationCase), caseId)
            : await dbContext.SeparationCases.Where(row => row.EmployeeId == currentUser.EmployeeId && (row.Status == CaseStatus.PendingApproval || row.Status == CaseStatus.Approved)).FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainRuleException("SEPARATION_NONE", "You have no separation in progress.");
        CaseStatus before = separation.Status;
        separation.Withdraw(await clock.TodayAsync(cancellationToken));
        Note(separation, "withdrawn", "Separation withdrawn", null);
        if (before == CaseStatus.PendingApproval)
        {
            await workflow.WithdrawAsync(SubjectType, separation.Id, cancellationToken);
        }
        else
        {
            await separations.WithdrawNoticeAsync(separation.EmployeeId, cancellationToken);
            await offboarding.CancelAsync(separation, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: false, cancellationToken);
    }

    public async Task<CaseView> SetTermsAsync(Guid id, TermsBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = await ManagedAsync(id, cancellationToken);
        DateOnly before = separation.LastWorkingDay;
        separation.SetLastDay(body.LastWorkingDay);
        separation.SetTerms(body.ShortfallWaived, body.EligibleForRehire);
        if (body.GardenLeave is { } gardenLeave && gardenLeave != separation.GardenLeave)
        {
            if (gardenLeave && !(await policy.PolicyAsync(cancellationToken)).GardenLeaveAllowed)
            {
                throw new DomainRuleException("SEPARATION_GARDEN_LEAVE", "The separation policy does not allow garden leave.");
            }

            separation.SetGardenLeave(gardenLeave);
            Note(separation, "garden-leave", gardenLeave ? "Placed on garden leave" : "Garden leave ended", null);
        }

        if (before != separation.LastWorkingDay)
        {
            await offboarding.FollowLastDayAsync(separation, cancellationToken);
        }

        Note(separation, "terms", "Terms updated", before == separation.LastWorkingDay
            ? $"Shortfall {(body.ShortfallWaived ? "waived" : "recovered")}; {(body.EligibleForRehire ? "eligible" : "not eligible")} for rehire."
            : $"Last day moved from {Format(before)} to {Format(separation.LastWorkingDay)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    /// <summary>The manager's or HR's reading of the exit: reasons, regretted or not, retention attempted.</summary>
    public async Task<CaseView> ClassifyAsync(Guid id, ClassificationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = await ManagedAsync(id, allowCompleted: true, cancellationToken);
        separation.Classify(body.ReasonCode, body.SecondaryReasonCode, body.Regretted, body.RetentionNote);
        string regret = body.Regretted switch { true => " (regretted)", false => " (not regretted)", _ => string.Empty };
        Note(separation, "classified", "Exit reason recorded", $"{SeparationReasons.LabelOf(body.ReasonCode) ?? "No reason"}{regret}");
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    /// <summary>The employee asks to leave before the notice ends, optionally buying out the remaining days.</summary>
    public async Task<CaseView> RequestEarlyReleaseAsync(EarlyReleaseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        SeparationCase separation = await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.EmployeeId == me && (row.Status == CaseStatus.PendingApproval || row.Status == CaseStatus.Approved), cancellationToken)
            ?? throw new DomainRuleException("SEPARATION_NONE", "You have no separation in progress.");
        if (body.WithBuyout && !(await policy.PolicyAsync(cancellationToken)).BuyoutAllowed)
        {
            throw new DomainRuleException("SEPARATION_BUYOUT", "The separation policy does not allow buying out the notice period.");
        }

        separation.RequestEarlyRelease(body.ReleaseDay, body.WithBuyout);
        Note(separation, "early-release-requested", "Early release asked for", $"Last day {Format(body.ReleaseDay)}{(body.WithBuyout ? ", buying out the remaining notice" : string.Empty)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: false, cancellationToken);
    }

    public async Task<CaseView> DecideEarlyReleaseAsync(Guid id, bool granted, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(id, cancellationToken);
        DateOnly? asked = separation.RequestedReleaseDay;
        bool buyout = separation.ReleaseWithBuyout;
        separation.DecideEarlyRelease(granted);
        if (granted)
        {
            await offboarding.FollowLastDayAsync(separation, cancellationToken);
        }
        Note(separation, granted ? "early-release-granted" : "early-release-declined", granted ? "Early release granted" : "Early release declined",
            granted && asked is { } day ? $"Last day {Format(day)}; shortfall {(buyout ? "recovered as a buyout" : "waived")}." : null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    // ----- Clearance ----------------------------------------------------------------------------

    /// <summary>Clearance items waiting on the caller - theirs to sign off, or any with clearance rights.</summary>
    public async Task<IReadOnlyList<ClearanceView>> MyClearancesAsync(CancellationToken cancellationToken)
    {
        bool clearer = await HasAsync(Permissions.Separation.ClearanceManage, cancellationToken);
        Guid? me = currentUser.EmployeeId;
        List<ClearanceItem> rows = await (
                from item in dbContext.ClearanceItems.AsNoTracking()
                join separation in dbContext.SeparationCases.AsNoTracking() on item.CaseId equals separation.Id
                where item.Status == ClearanceStatus.Pending && separation.Status == CaseStatus.Approved && (item.OwnerEmployeeId == me || (clearer && item.OwnerEmployeeId == null))
                select item)
            .ToListAsync(cancellationToken);
        return await ClearanceViewsAsync(rows, cancellationToken);
    }

    public async Task<CaseView> AddClearanceAsync(Guid id, ClearanceBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = await ManagedAsync(id, cancellationToken);
        dbContext.ClearanceItems.Add(ClearanceItem.Add(separation.Id, body.Area, body.Title, body.OwnerEmployeeId, body.RecoveryAmount));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    public async Task<ClearanceView> ResolveAsync(Guid itemId, ResolveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ClearanceItem item = await dbContext.ClearanceItems.FirstOrDefaultAsync(row => row.Id == itemId, cancellationToken) ?? throw new NotFoundException(nameof(ClearanceItem), itemId);
        if (!await CanResolveAsync(item, cancellationToken))
        {
            throw new ForbiddenException("CLEARANCE_OWNER", "This clearance is signed off by its owner or the clearance team.");
        }

        if (await dbContext.Settlements.AnyAsync(row => row.CaseId == item.CaseId && row.Status != SettlementStatus.Draft, cancellationToken))
        {
            throw new DomainRuleException("CLEARANCE_SETTLED", "The settlement is already approved; reopen it to change clearance.");
        }

        item.Resolve(body.Status, body.RecoveryAmount, body.Note, currentUser.EmployeeId, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ClearanceViewsAsync([item], cancellationToken))[0];
    }

    // ----- Exit interview -----------------------------------------------------------------------

    public async Task<CaseView> RecordInterviewAsync(Guid? id, InterviewBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationCase separation = id is { } caseId
            ? await ManagedAsync(caseId, cancellationToken)
            : await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.EmployeeId == currentUser.EmployeeId && row.Status == CaseStatus.Approved, cancellationToken)
                ?? throw new DomainRuleException("SEPARATION_NONE", "You have no approved separation to give an exit interview for.");
        DateOnly today = await clock.TodayAsync(cancellationToken);
        ExitInterview? interview = await dbContext.ExitInterviews.FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken);
        if (interview is null)
        {
            dbContext.ExitInterviews.Add(ExitInterview.Record(separation.Id, body.PrimaryReason, body.OverallRating, body.WouldRecommend, body.WouldReturn, body.WhatWentWell, body.WhatToImprove, currentUser.EmployeeId, today));
        }
        else
        {
            interview.Update(body.PrimaryReason, body.OverallRating, body.WouldRecommend, body.WouldReturn, body.WhatWentWell, body.WhatToImprove, currentUser.EmployeeId, today);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: id is not null, cancellationToken);
    }

    // ----- Settlement ---------------------------------------------------------------------------

    /// <summary>
    /// Works out (or re-works) the full and final settlement: salary for the last month's days,
    /// encashment of earned leave, gratuity when service qualifies, and recoveries for notice not
    /// served and for clearance items. Manual lines are kept.
    /// </summary>
    public async Task<CaseView> CalculateAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await SettlementCaseAsync(id, cancellationToken);
        Settlement? settlement = await dbContext.Settlements.FirstOrDefaultAsync(row => row.CaseId == id, cancellationToken);
        if (settlement is null)
        {
            settlement = Settlement.Start(id);
            dbContext.Settlements.Add(settlement);
        }

        settlement.EnsureDraft();
        List<SettlementLine> existing = await dbContext.SettlementLines.Where(row => row.SettlementId == settlement.Id).ToListAsync(cancellationToken);
        dbContext.SettlementLines.RemoveRange(existing.Where(line => !line.IsManual));
        List<SettlementLine> lines = [.. existing.Where(line => line.IsManual)];

        EmployeeSummary person = await employees.GetAsync(separation.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", separation.EmployeeId);
        SalaryInfo? salary = (await salaries.CurrentAsync([separation.EmployeeId], separation.LastWorkingDay, cancellationToken)).GetValueOrDefault(separation.EmployeeId);
        void Add(string code, string label, bool deduction, decimal amount, string basis)
        {
            if (amount > 0)
            {
                lines.Add(SettlementLine.Add(settlement.Id, code, label, deduction, amount, manual: false, basis));
            }
        }

        if (salary is not null)
        {
            Add("SALARY", $"Salary for {separation.LastWorkingDay:MMMM yyyy}", false, SettlementMath.LastMonthSalary(salary.MonthlyGross, separation.LastWorkingDay),
                $"{salary.MonthlyGross.ToString("N2", CultureInfo.InvariantCulture)} x {separation.LastWorkingDay.Day}/{DateTime.DaysInMonth(separation.LastWorkingDay.Year, separation.LastWorkingDay.Month)} days");

            // Only the leave types the policy encashes, up to its cap; tax-free up to the s.10(10AA) limit.
            SeparationPolicy terms = await policy.PolicyAsync(cancellationToken);
            TerminalExemptionConfig limits = await ExemptionsAsync(separation.LastWorkingDay, cancellationToken);
            HashSet<string> encashable = [.. terms.EncashableLeaveTypes];
            List<LeaveBalanceInfo> earned = [.. (await leave.BalancesAsync(separation.EmployeeId, cancellationToken)).Where(balance => encashable.Contains(balance.Code) && balance.Available > 0)];
            decimal available = earned.Sum(balance => balance.Available);
            decimal encashed = terms.EncashmentCapDays is { } cap ? Math.Min(available, cap) : available;
            if (encashed > 0)
            {
                Add("LEAVE_ENCASH", earned.Count == 1 ? $"{earned[0].Name} encashment" : "Leave encashment", false, SettlementMath.LeaveEncashment(salary.MonthlyBasic, encashed),
                    $"{encashed.ToString("0.##", CultureInfo.InvariantCulture)} days ({string.Join(", ", earned.Select(balance => balance.Code))}) x basic/26"
                    + (encashed < available ? $", capped at {terms.EncashmentCapDays} days" : string.Empty)
                    + $"; tax-free up to {Rupees(limits.LeaveEncashment)}");
            }

            if (person.JoiningDate is { } joined && await rules.ResolveAsync(StatutoryRuleType.Gratuity, null, separation.LastWorkingDay, cancellationToken) is { } rule)
            {
                // Death (and disablement) waives the five-year minimum (Payment of Gratuity Act s.4(1)).
                GratuityConfig config = rule.Read<GratuityConfig>();
                decimal minimumYears = separation.Type == SeparationType.Death ? 0 : config.MinimumYears;
                (decimal years, decimal amount, bool eligible) = SettlementMath.Gratuity(salary.MonthlyBasic, joined, separation.LastWorkingDay, config.DaysPerYear, config.DivisorDays, minimumYears, config.MaximumAmount);
                if (eligible)
                {
                    Add("GRATUITY", separation.Type == SeparationType.Death ? $"Gratuity (to {separation.NomineeName ?? "the nominee"})" : "Gratuity", false, amount,
                        $"{years.ToString("0.##", CultureInfo.InvariantCulture)} years of service, {config.DaysPerYear}/{config.DivisorDays} days' basic per year{(separation.Type == SeparationType.Death ? "; no minimum service on death" : years < config.MinimumYears ? "; 240 days worked in the final year count as a year" : string.Empty)}; tax-free up to {Rupees(limits.Gratuity)}");
                }
            }

            if (person.JoiningDate is { } since2 && await rules.ResolveAsync(StatutoryRuleType.Bonus, null, separation.LastWorkingDay, cancellationToken) is { } bonusRule)
            {
                BonusConfig bonus = bonusRule.Read<BonusConfig>();
                (decimal months, decimal amount, bool eligible) = SettlementMath.StatutoryBonus(
                    salary.MonthlyBasic, since2, separation.LastWorkingDay, bonus.EligibilityWageCeiling, bonus.CalculationWageCeiling, bonus.PayablePercent, bonus.MinimumWorkingDays);
                if (eligible)
                {
                    Add("BONUS", "Statutory bonus (pro rata)", false, amount,
                        $"{months.ToString("0.##", CultureInfo.InvariantCulture)} months x {bonus.PayablePercent.ToString("0.##", CultureInfo.InvariantCulture)}% of {Rupees(Math.Min(salary.MonthlyBasic, bonus.CalculationWageCeiling))} a month (Payment of Bonus Act)");
                }
            }

            if (separation.Type == SeparationType.Retrenchment && person.JoiningDate is { } since)
            {
                (int years, decimal amount, bool eligible) = SettlementMath.Retrenchment(salary.MonthlyBasic, since, separation.LastWorkingDay);
                if (eligible)
                {
                    Add("RETRENCHMENT", "Retrenchment compensation", false, amount, $"{years} year(s) x 15 days' basic (basic/26), IR Code s.70; tax-free up to {Rupees(limits.RetrenchmentCompensation)}");
                }
            }

            if (separation.PayInLieuDays > 0)
            {
                Add("NOTICE_PAY", "Pay in lieu of notice", false, SettlementMath.NoticePay(salary.MonthlyGross, separation.PayInLieuDays), $"{separation.PayInLieuDays} days x gross/30");
            }

            if (separation.Type == SeparationType.Resignation && !separation.ShortfallWaived && separation.ShortfallDays > 0)
            {
                Add("NOTICE_RECOVERY", "Notice period shortfall", true, SettlementMath.NoticeRecovery(salary.MonthlyGross, separation.ShortfallDays), $"{separation.ShortfallDays} days x gross/30");
            }
        }

        foreach (ClearanceItem item in await dbContext.ClearanceItems.AsNoTracking().Where(row => row.CaseId == id && row.Status != ClearanceStatus.Waived && row.RecoveryAmount > 0).ToListAsync(cancellationToken))
        {
            Add(item.Area == ClearanceArea.Finance ? "ADVANCE_RECOVERY" : "RECOVERY", $"Recovery: {item.Title}", true, item.RecoveryAmount, $"{item.Area} clearance");
        }

        dbContext.SettlementLines.AddRange(lines.Where(line => !line.IsManual));
        settlement.Total(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    public async Task<CaseView> AddLineAsync(Guid id, LineBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        (SeparationCase separation, Settlement settlement) = await DraftSettlementAsync(id, cancellationToken);
        dbContext.SettlementLines.Add(SettlementLine.Add(settlement.Id, body.Code, body.Label, body.IsDeduction, body.Amount, manual: true, "Added by HR"));
        await dbContext.SaveChangesAsync(cancellationToken);
        await RetotalAsync(settlement, cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    public async Task<CaseView> RemoveLineAsync(Guid id, Guid lineId, CancellationToken cancellationToken)
    {
        (SeparationCase separation, Settlement settlement) = await DraftSettlementAsync(id, cancellationToken);
        SettlementLine line = await dbContext.SettlementLines.FirstOrDefaultAsync(row => row.Id == lineId && row.SettlementId == settlement.Id, cancellationToken) ?? throw new NotFoundException(nameof(SettlementLine), lineId);
        dbContext.SettlementLines.Remove(line);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RetotalAsync(settlement, cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    public async Task<CaseView> SettlementActionAsync(Guid id, string action, string? reference, CancellationToken cancellationToken)
    {
        SeparationCase separation = await SettlementCaseAsync(id, cancellationToken);
        Settlement settlement = await dbContext.Settlements.FirstOrDefaultAsync(row => row.CaseId == id, cancellationToken) ?? throw new DomainRuleException("SETTLEMENT_NONE", "Calculate the settlement first.");
        switch (action)
        {
            case "approve":
                if (await dbContext.ClearanceItems.AnyAsync(row => row.CaseId == id && row.Status == ClearanceStatus.Pending, cancellationToken))
                {
                    throw new DomainRuleException("SETTLEMENT_CLEARANCE", "Every clearance item must be cleared or waived first.");
                }

                settlement.Approve();
                Note(separation, "settlement-approved", "Settlement approved", $"Net {Rupees(settlement.Net)}.");
                await dbContext.SaveChangesAsync(cancellationToken);
                await SendToPayrollAsync(separation, settlement, cancellationToken);
                break;
            case "reopen":
                // Its payroll run goes with it - unless that is already finalized, which refuses.
                await payrollSettlements.CancelAsync(settlement.Id, cancellationToken);
                settlement.Reopen();
                Note(separation, "settlement-reopened", "Settlement reopened", null);
                break;
            default:
                if (settlement.PayrollRunId is not null)
                {
                    throw new DomainRuleException("SETTLEMENT_IN_PAYROLL", "This settlement is paid through payroll; it is marked paid when its payroll run is paid.");
                }

                settlement.Pay(reference, await clock.TodayAsync(cancellationToken));
                Note(separation, "settlement-paid", "Settlement paid", reference);
                break;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    /// <summary>Adds HR's exit summary (written in the case or drafted by the assistant and approved) to the timeline.</summary>
    public async Task AddSummaryAsync(Guid id, string summary, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(id, allowCompleted: true, cancellationToken);
        string text = Gggs.BuildingBlocks.Domain.Primitives.Guard.MaxLength(Gggs.BuildingBlocks.Domain.Primitives.Guard.NotNullOrWhiteSpace(summary).Trim(), 1000);
        Note(separation, "exit-summary", "Exit summary", text);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A full and final payroll run was finalized: the leaver's payslip is out.</summary>
    public async Task SettlementPayslipIssuedAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        if (await dbContext.Settlements.AsNoTracking().FirstOrDefaultAsync(row => row.Id == settlementId, cancellationToken) is { } settlement)
        {
            dbContext.CaseEvents.Add(CaseEvent.Record(settlement.CaseId, "settlement-payslip", "Full and final payslip issued", "The off-cycle payroll run was finalized; the payslip is in My payslips.", null, timeProvider.GetUtcNow().UtcDateTime));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>A full and final payroll run was paid: so is the settlement.</summary>
    public async Task SettlementPaidByPayrollAsync(Guid settlementId, DateOnly paidOn, string? reference, CancellationToken cancellationToken)
    {
        Settlement? settlement = await dbContext.Settlements.FirstOrDefaultAsync(row => row.Id == settlementId, cancellationToken);
        if (settlement?.Status != SettlementStatus.Approved)
        {
            return;
        }

        settlement.Pay(reference ?? "Payroll", paidOn);
        dbContext.CaseEvents.Add(CaseEvent.Record(settlement.CaseId, "settlement-paid", "Settlement paid through payroll", reference, null, timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tells HR about settlements past their legal deadline (two working days after the last day, by
    /// policy), once each. Returns how many were reported.
    /// </summary>
    public async Task<int> SettlementDeadlinesAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly since = today.AddDays(-120);
        List<SeparationCase> candidates = await dbContext.SeparationCases
            .Where(row => (row.Status == CaseStatus.Approved || row.Status == CaseStatus.Completed) && row.LastWorkingDay >= since && row.LastWorkingDay < today && row.SettlementOverdueNotifiedOn == null)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        Dictionary<Guid, CaseItem> items = (await ItemsAsync(candidates, cancellationToken)).ToDictionary(item => item.Id);
        List<SeparationCase> overdue = [.. candidates.Where(row => items[row.Id].SettlementOverdue)];
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(overdue.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        foreach (SeparationCase separation in overdue)
        {
            separation.SettlementOverdueNotified(today);
            EmployeeSummary? person = people.GetValueOrDefault(separation.EmployeeId);
            if (person?.HrbpEmployeeId is { } hrbp)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(NotificationTemplates.SettlementOverdue, [new NotificationRecipient(hrbp)],
                        new Dictionary<string, string> { ["employeeName"] = person.DisplayName, ["dueOn"] = Format(items[separation.Id].SettlementDueOn!.Value) },
                        $"/separation/cases/{separation.Id}"),
                    cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return overdue.Count;
    }

    /// <summary>Issues the relieving letter (and an experience letter unless the person absconded or was terminated).</summary>
    public async Task<CaseView> IssueLettersAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await ManagedAsync(id, allowCompleted: true, cancellationToken);
        if (separation.Status is not (CaseStatus.Approved or CaseStatus.Completed))
        {
            throw new DomainRuleException("SEPARATION_NOT_APPROVED", "Letters are issued for an approved separation.");
        }

        var values = new Dictionary<string, string>
        {
            ["lastWorkingDay"] = Format(separation.LastWorkingDay),
            ["separationType"] = Label(separation.Type).ToLowerInvariant(),
        };
        Guid relieving = await letters.GenerateAsync(separation.EmployeeId, "RELIEVING_LETTER", values, cancellationToken);
        Guid? experience = separation.Type is SeparationType.Termination or SeparationType.Absconding
            ? null
            : await letters.GenerateAsync(separation.EmployeeId, "EXPERIENCE_LETTER", values, cancellationToken);
        separation.LettersIssued(relieving, experience);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(separation, includeSettlement: true, cancellationToken);
    }

    /// <summary>Exits everyone whose last working day has passed and adds them to the alumni network.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<SeparationCase> due = await dbContext.SeparationCases.Where(row => row.Status == CaseStatus.Approved && row.LastWorkingDay < today).ToListAsync(cancellationToken);
        foreach (SeparationCase separation in due)
        {
            await CompleteAsync(separation, cancellationToken);
        }

        return due.Count;
    }

    // ----- Alumni and dashboard -----------------------------------------------------------------

    public async Task<IReadOnlyList<AlumniItem>> AlumniAsync(string? search, CancellationToken cancellationToken)
    {
        IQueryable<AlumniProfile> rows = dbContext.Alumni.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            rows = rows.Where(row => row.Name.Contains(term) || (row.LastDesignation != null && row.LastDesignation.Contains(term)));
        }

        return [.. (await rows.OrderByDescending(row => row.ExitDate).Take(500).ToListAsync(cancellationToken)).Select(Alumni)];
    }

    public async Task<AlumniItem> UpdateAlumniAsync(Guid id, AlumniBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        AlumniProfile profile = await dbContext.Alumni.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(AlumniProfile), id);
        profile.Update(body.Email, body.EligibleForRehire, body.OptedIn, body.Notes, body.Mobile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Alumni(profile);
    }

    public async Task<SeparationDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        DateOnly monthEnd = monthStart.AddMonths(1).AddDays(-1);
        DateOnly yearAgo = today.AddYears(-1);
        List<SeparationCase> cases = await (await VisibleAsync(cancellationToken)).Where(row => row.Status != CaseStatus.Rejected && row.Status != CaseStatus.Withdrawn).ToListAsync(cancellationToken);
        List<Guid> open = [.. cases.Where(row => row.Status == CaseStatus.Approved).Select(row => row.Id)];
        int clearance = await dbContext.ClearanceItems.CountAsync(row => open.Contains(row.CaseId) && row.Status == ClearanceStatus.Pending, cancellationToken);
        List<Guid> closed = [.. cases.Where(row => row.Status is CaseStatus.Approved or CaseStatus.Completed).Select(row => row.Id)];
        HashSet<Guid> paid = [.. await dbContext.Settlements.Where(row => closed.Contains(row.CaseId) && row.Status == SettlementStatus.Paid).Select(row => row.CaseId).ToListAsync(cancellationToken)];
        int exits = cases.Count(row => row.Status == CaseStatus.Completed && row.LastWorkingDay >= yearAgo);
        int headcount = (await employees.ListActiveIdsAsync(null, null, cancellationToken)).Count;
        List<string> reasons = await dbContext.ExitInterviews.AsNoTracking().Where(row => row.ConductedOn >= yearAgo).Select(row => row.PrimaryReason).ToListAsync(cancellationToken);
        return new SeparationDashboard(
            cases.Count(row => row.InProgress),
            cases.Count(row => row.Status == CaseStatus.PendingApproval),
            cases.Count(row => row.InProgress && row.LastWorkingDay >= monthStart && row.LastWorkingDay <= monthEnd),
            clearance,
            cases.Count(row => row.Status == CaseStatus.Completed && !paid.Contains(row.Id)),
            headcount + exits == 0 ? 0 : Math.Round(100.0 * exits / (headcount + (exits / 2.0)), 1),
            cases.Where(row => row.LastWorkingDay >= yearAgo).GroupBy(row => Label(row.Type)).ToDictionary(group => group.Key, group => group.Count()),
            reasons.GroupBy(reason => reason).OrderByDescending(group => group.Count()).Take(6).ToDictionary(group => group.Key, group => group.Count()),
            (await ItemsAsync([.. cases.Where(row => row.Status is CaseStatus.Approved or CaseStatus.Completed && row.LastWorkingDay >= today.AddDays(-120) && row.LastWorkingDay < today)], cancellationToken))
                .Count(item => item.SettlementOverdue));
    }

    // ----- Internals ----------------------------------------------------------------------------

    public static string Label(SeparationType type) => type switch
    {
        SeparationType.ContractEnd => "End of contract",
        SeparationType.Mutual => "Mutual separation",
        _ => type.ToString(),
    };

    private static string Format(DateOnly day) => day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static string Rupees(decimal amount) => "Rs " + amount.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));

    /// <summary>The tax-free limits on terminal benefits in force on the day (defaults if no rule is published).</summary>
    private async Task<TerminalExemptionConfig> ExemptionsAsync(DateOnly day, CancellationToken cancellationToken)
        => (await rules.ResolveAsync(StatutoryRuleType.TerminalExemption, null, day, cancellationToken))?.Read<TerminalExemptionConfig>()
            ?? new TerminalExemptionConfig(2_000_000m, 2_500_000m, 500_000m);

    /// <summary>
    /// Sends an approved settlement to payroll as an off-cycle run: the exit month's salary is worked
    /// out there (with PF, ESI, PT and TDS), so only the other lines go across - terminal benefits split
    /// at their tax-free limits, other earnings as taxable, and every recovery. Someone without a
    /// payroll salary, or who has died (paid to the nominee), is paid outside payroll by reference.
    /// </summary>
    private async Task SendToPayrollAsync(SeparationCase separation, Settlement settlement, CancellationToken cancellationToken)
    {
        if (separation.Type == SeparationType.Death
            || !(await salaries.CurrentAsync([separation.EmployeeId], separation.LastWorkingDay, cancellationToken)).ContainsKey(separation.EmployeeId))
        {
            return;
        }

        TerminalExemptionConfig limits = await ExemptionsAsync(separation.LastWorkingDay, cancellationToken);
        List<FinalSettlementLine> lines = [];
        void Split(PayrollInputKind kind, decimal amount, decimal limit, string label)
        {
            (decimal exempt, decimal taxable) = SettlementMath.SplitExempt(amount, limit);
            if (exempt > 0)
            {
                lines.Add(new FinalSettlementLine(kind, exempt, label));
            }

            if (taxable > 0)
            {
                lines.Add(new FinalSettlementLine(PayrollInputKind.TerminalTaxable, taxable, $"{label} above the tax-free limit"));
            }
        }

        foreach (SettlementLine line in await dbContext.SettlementLines.AsNoTracking().Where(row => row.SettlementId == settlement.Id).ToListAsync(cancellationToken))
        {
            switch (line.Code)
            {
                case "SALARY":
                    break;
                case var _ when line.IsDeduction:
                    lines.Add(new FinalSettlementLine(PayrollInputKind.Deduction, line.Amount, line.Label));
                    break;
                case "LEAVE_ENCASH":
                    Split(PayrollInputKind.LeaveEncashment, line.Amount, limits.LeaveEncashment, line.Label);
                    break;
                case "GRATUITY":
                    Split(PayrollInputKind.Gratuity, line.Amount, limits.Gratuity, "Gratuity");
                    break;
                case "RETRENCHMENT":
                    Split(PayrollInputKind.RetrenchmentCompensation, line.Amount, limits.RetrenchmentCompensation, line.Label);
                    break;
                case "NOTICE_PAY":
                    lines.Add(new FinalSettlementLine(PayrollInputKind.NoticePay, line.Amount, line.Label));
                    break;
                case "BONUS":
                    lines.Add(new FinalSettlementLine(PayrollInputKind.Bonus, line.Amount, line.Label));
                    break;
                default:
                    lines.Add(new FinalSettlementLine(PayrollInputKind.TerminalTaxable, line.Amount, line.Label));
                    break;
            }
        }

        FinalSettlementRun run = await payrollSettlements.SubmitAsync(new FinalSettlementRequest(separation.EmployeeId, settlement.Id, separation.LastWorkingDay, lines), cancellationToken);
        settlement.SentToPayroll(run.RunId);
        Note(separation, "settlement-payroll", "Sent to payroll", $"Off-cycle run for {run.Period:MMMM yyyy}, net {Rupees(run.Net)}{(run.SalaryIncluded ? ", with the exit month's salary" : string.Empty)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AlumniItem Alumni(AlumniProfile row)
        => new(row.Id, row.EmployeeId, row.Name, row.Email, row.LastDesignation, row.ExitDate, row.SeparationType, row.EligibleForRehire, row.OptedIn, row.Notes, row.Mobile);

    /// <summary>Adds a step to the case's timeline; saved with the change it describes.</summary>
    private void Note(SeparationCase separation, string kind, string title, string? note)
        => dbContext.CaseEvents.Add(CaseEvent.Record(separation.Id, kind, title, note, currentUser.EmployeeId, timeProvider.GetUtcNow().UtcDateTime));

    private static string TypeLabel(SeparationType type) => type switch
    {
        SeparationType.ContractEnd => "Contract end",
        _ => type.ToString(),
    };

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("SEPARATION_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<bool> HasAsync(string permission, CancellationToken cancellationToken) => (await permissions.GetCurrentAsync(cancellationToken)).Has(permission);

    private async Task EnsureNoOpenCaseAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (await dbContext.SeparationCases.AnyAsync(row => row.EmployeeId == employeeId && (row.Status == CaseStatus.PendingApproval || row.Status == CaseStatus.Approved), cancellationToken))
        {
            throw new DomainRuleException("SEPARATION_OPEN", "A separation is already in progress for this employee.");
        }
    }

    /// <summary>Approves a case: starts notice, raises the clearance checklist and tells its owners.</summary>
    private async Task ApproveAsync(SeparationCase separation, DateOnly? lastWorkingDay, CancellationToken cancellationToken)
    {
        separation.Approve(lastWorkingDay);
        Note(separation, "approved", "Separation approved", $"Serving notice to {Format(separation.LastWorkingDay)}.");
        await separations.StartNoticeAsync(separation.EmployeeId, separation.NoticeDate, cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(separation.EmployeeId, cancellationToken);
        List<ClearanceItem> items =
        [
            ClearanceItem.Add(separation.Id, ClearanceArea.Manager, "Knowledge transfer and handover", person?.ManagerEmployeeId),
            ClearanceItem.Add(separation.Id, ClearanceArea.It, "Revoke system access and collect accounts", null),
            ClearanceItem.Add(separation.Id, ClearanceArea.Admin, "Return ID card and access card", null),
            ClearanceItem.Add(separation.Id, ClearanceArea.Finance, "No dues: loans, advances and reimbursements", null),
            ClearanceItem.Add(separation.Id, ClearanceArea.Hr, "Exit interview and documents", person?.HrbpEmployeeId),
        ];
        foreach (HeldAsset asset in await assets.HeldByAsync(separation.EmployeeId, cancellationToken))
        {
            items.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Assets, $"Return {asset.Name} ({asset.Tag})", null, asset.BookValue, "asset", asset.AssetId));
        }

        foreach (OpenAdvance advance in await expenses.OpenAdvancesAsync(separation.EmployeeId, cancellationToken))
        {
            items.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Finance, $"Settle travel advance {advance.Number}", null, advance.Amount, "travel-advance", advance.TravelRequestId));
        }

        dbContext.ClearanceItems.AddRange(items);
        await offboarding.StartAsync(separation, person, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (IGrouping<Guid, ClearanceItem> group in items.Where(item => item.OwnerEmployeeId != null && item.OwnerEmployeeId != separation.EmployeeId).GroupBy(item => item.OwnerEmployeeId!.Value))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ClearanceAssigned,
                    [new NotificationRecipient(group.Key)],
                    new Dictionary<string, string> { ["employeeName"] = person?.DisplayName ?? string.Empty, ["item"] = string.Join("; ", group.Select(item => item.Title)), ["lastDay"] = Format(separation.LastWorkingDay) },
                    "/separation/clearance"),
                cancellationToken);
        }
    }

    private async Task CompleteAsync(SeparationCase separation, CancellationToken cancellationToken)
    {
        await separations.ExitAsync(separation.EmployeeId, separation.LastWorkingDay, separation.ExitReasonCode, cancellationToken);
        separation.Complete(timeProvider.GetUtcNow().UtcDateTime);
        Note(separation, "completed", "Exit completed", $"Last working day {Format(separation.LastWorkingDay)}.");
        await offboarding.DeprovisionAsync(separation, cancellationToken);
        if (!await dbContext.Alumni.AnyAsync(row => row.EmployeeId == separation.EmployeeId, cancellationToken) && separation.Type != SeparationType.Death)
        {
            EmployeeSummary? person = await employees.GetAsync(separation.EmployeeId, cancellationToken);
            var alumnus = AlumniProfile.Create(separation.EmployeeId, person?.DisplayName ?? "Former employee", person?.OfficialEmail, person?.Designation, person?.DepartmentId, separation.LastWorkingDay, separation.Type, separation.EligibleForRehire);

            // Personal contact details, so the person is recognised if they apply again.
            PersonalContact? contact = (await separations.PersonalContactsAsync([separation.EmployeeId], cancellationToken)).GetValueOrDefault(separation.EmployeeId);
            alumnus.Contact(contact?.PersonalEmail, contact?.MobilePhone);
            dbContext.Alumni.Add(alumnus);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Cases the caller sees: everyone in their separation scope, plus their own.</summary>
    private async Task<IQueryable<SeparationCase>> VisibleAsync(CancellationToken cancellationToken)
    {
        IQueryable<SeparationCase> rows = dbContext.SeparationCases.AsNoTracking();
        Guid? me = currentUser.EmployeeId;
        if (!await HasAsync(Permissions.Separation.CaseView, cancellationToken))
        {
            return rows.Where(row => row.EmployeeId == me);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Separation.CaseView, cancellationToken);
        return scope is null ? rows : rows.Where(row => row.EmployeeId == me || scope.Contains(row.EmployeeId));
    }

    /// <summary>A clearance owner or offboarding task assignee may open the case their item belongs to.</summary>
    private async Task<SeparationCase?> ClearerCaseAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid? me = currentUser.EmployeeId;
        bool owns = await dbContext.ClearanceItems.AnyAsync(row => row.CaseId == id && row.OwnerEmployeeId == me, cancellationToken)
            || await dbContext.OffboardingTasks.AnyAsync(row => row.CaseId == id && row.AssigneeEmployeeId == me, cancellationToken)
            || await HasAsync(Permissions.Separation.ClearanceManage, cancellationToken);
        return owns ? await dbContext.SeparationCases.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) : null;
    }

    private Task<SeparationCase> ManagedAsync(Guid id, CancellationToken cancellationToken) => ManagedAsync(id, allowCompleted: false, cancellationToken);

    private async Task<SeparationCase> ManagedAsync(Guid id, bool allowCompleted, CancellationToken cancellationToken)
    {
        SeparationCase separation = await dbContext.SeparationCases.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(SeparationCase), id);
        if (!await employees.IsInScopeAsync(separation.EmployeeId, Permissions.Separation.CaseManage, cancellationToken))
        {
            throw new NotFoundException(nameof(SeparationCase), id);
        }

        return separation.InProgress || (allowCompleted && separation.Status == CaseStatus.Completed) ? separation : throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
    }

    private async Task<SeparationCase> SettlementCaseAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await dbContext.SeparationCases.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(SeparationCase), id);
        return separation.Status is CaseStatus.Approved or CaseStatus.Completed ? separation : throw new DomainRuleException("SEPARATION_NOT_APPROVED", "A settlement is for an approved separation.");
    }

    private async Task<(SeparationCase Case, Settlement Settlement)> DraftSettlementAsync(Guid id, CancellationToken cancellationToken)
    {
        SeparationCase separation = await SettlementCaseAsync(id, cancellationToken);
        Settlement settlement = await dbContext.Settlements.FirstOrDefaultAsync(row => row.CaseId == id, cancellationToken) ?? throw new DomainRuleException("SETTLEMENT_NONE", "Calculate the settlement first.");
        settlement.EnsureDraft();
        return (separation, settlement);
    }

    private async Task RetotalAsync(Settlement settlement, CancellationToken cancellationToken)
    {
        settlement.Total(await dbContext.SettlementLines.AsNoTracking().Where(row => row.SettlementId == settlement.Id).ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> CanResolveAsync(ClearanceItem item, CancellationToken cancellationToken)
        => item.OwnerEmployeeId == currentUser.EmployeeId && item.OwnerEmployeeId is not null || await HasAsync(Permissions.Separation.ClearanceManage, cancellationToken);

    private async Task<List<ClearanceView>> ClearanceViewsAsync(IReadOnlyList<ClearanceItem> items, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> names = (await employees.GetManyAsync(items.Where(row => row.OwnerEmployeeId != null).Select(row => row.OwnerEmployeeId!.Value).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        bool clearer = await HasAsync(Permissions.Separation.ClearanceManage, cancellationToken);
        Guid? me = currentUser.EmployeeId;
        return [.. items.OrderBy(row => row.Area).Select(row => new ClearanceView(row.Id, row.Area, row.Title, row.OwnerEmployeeId, row.OwnerEmployeeId is { } owner ? names.GetValueOrDefault(owner) : null,
            row.Status, row.RecoveryAmount, row.Note, row.ClearedOn, clearer || (row.OwnerEmployeeId is not null && row.OwnerEmployeeId == me)))];
    }

    private async Task<CaseView> ViewAsync(SeparationCase separation, bool includeSettlement, CancellationToken cancellationToken)
    {
        List<ClearanceItem> items = await dbContext.ClearanceItems.AsNoTracking().Where(row => row.CaseId == separation.Id).ToListAsync(cancellationToken);
        ExitInterview? interview = await dbContext.ExitInterviews.AsNoTracking().FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken);
        SettlementView? settlement = null;
        if (includeSettlement && await dbContext.Settlements.AsNoTracking().FirstOrDefaultAsync(row => row.CaseId == separation.Id, cancellationToken) is { } row)
        {
            List<SettlementLine> lines = await dbContext.SettlementLines.AsNoTracking().Where(line => line.SettlementId == row.Id).ToListAsync(cancellationToken);
            FinalSettlementRun? payroll = row.PayrollRunId is null ? null : await payrollSettlements.FindAsync(row.Id, cancellationToken);
            settlement = new SettlementView(row.Id, row.Status, row.Earnings, row.Deductions, row.Net, row.PaidOn, row.PaymentReference,
                [.. lines.OrderBy(line => line.IsDeduction).ThenBy(line => line.Code, StringComparer.Ordinal).Select(line => new SettlementLineView(line.Id, line.Code, line.Label, line.IsDeduction, line.Amount, line.IsManual, line.Basis))],
                payroll?.RunId ?? row.PayrollRunId, payroll?.Status, payroll?.Net, payroll?.SalaryIncluded, payroll?.Issues);
        }

        List<CaseEvent> events = await dbContext.CaseEvents.AsNoTracking().Where(row => row.CaseId == separation.Id).OrderBy(row => row.OccurredAt).ToListAsync(cancellationToken);
        Dictionary<Guid, string> actors = (await employees.GetManyAsync(events.Where(row => row.ActorEmployeeId != null).Select(row => row.ActorEmployeeId!.Value).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        return new CaseView(
            (await ItemsAsync([separation], cancellationToken))[0],
            await ClearanceViewsAsync(items, cancellationToken),
            interview is null ? null : new InterviewView(interview.PrimaryReason, interview.OverallRating, interview.WouldRecommend, interview.WouldReturn, interview.WhatWentWell, interview.WhatToImprove, interview.ConductedOn),
            settlement,
            [.. events.Select(row => new CaseEventView(row.Kind, row.Title, row.Note, row.ActorEmployeeId is { } actor ? actors.GetValueOrDefault(actor) : null, row.OccurredAt))],
            await offboarding.ViewAsync(separation, forHr: separation.EmployeeId != currentUser.EmployeeId && await HasAsync(Permissions.Separation.CaseView, cancellationToken), cancellationToken),
            await exitSurveys.ForCaseAsync(separation, cancellationToken));
    }

    private async Task<List<CaseItem>> ItemsAsync(IReadOnlyList<SeparationCase> cases, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. cases.Select(row => row.Id)];
        var clearance = await dbContext.ClearanceItems.AsNoTracking().Where(row => ids.Contains(row.CaseId))
            .GroupBy(row => row.CaseId).Select(group => new { group.Key, Total = group.Count(), Pending = group.Count(row => row.Status == ClearanceStatus.Pending) })
            .ToDictionaryAsync(row => row.Key, cancellationToken);
        HashSet<Guid> interviewed = [.. await dbContext.ExitInterviews.AsNoTracking().Where(row => ids.Contains(row.CaseId)).Select(row => row.CaseId).ToListAsync(cancellationToken)];
        Dictionary<Guid, Settlement> settlements = await dbContext.Settlements.AsNoTracking().Where(row => ids.Contains(row.CaseId)).ToDictionaryAsync(row => row.CaseId, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(cases.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id);

        // Settlement due two working days after the last day (by policy), gratuity thirty days after.
        List<SeparationCase> settling = [.. cases.Where(row => row.Status is CaseStatus.Approved or CaseStatus.Completed)];
        SeparationPolicy terms = await policy.PolicyAsync(cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = settling.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<HolidayEntry>>()
            : await calendar.GetHolidaysAsync([.. settling.Select(row => row.EmployeeId).Distinct()], settling.Min(row => row.LastWorkingDay), settling.Max(row => row.LastWorkingDay).AddDays(30), cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly? DueOn(SeparationCase row) => row.Status is CaseStatus.Approved or CaseStatus.Completed
            ? SettlementMath.WorkingDaysAfter(row.LastWorkingDay, terms.SettlementDueWorkingDays,
                holidays.TryGetValue(row.EmployeeId, out IReadOnlyList<HolidayEntry>? days) ? days.Where(day => !day.IsOptional).Select(day => day.Date).ToHashSet() : [])
            : null;
        return [.. cases.Select(row =>
        {
            DateOnly? due = DueOn(row);
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            Settlement? settlement = settlements.GetValueOrDefault(row.Id);
            return new CaseItem(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, person?.Designation, row.Type, row.Reason, row.NoticeDate, row.NoticePeriodDays, row.RequestedLastDay,
                row.LastWorkingDay, row.ShortfallDays, row.ShortfallWaived, row.EligibleForRehire, row.Status, row.WorkflowInstanceId,
                clearance.TryGetValue(row.Id, out var counts) ? counts.Pending : 0, counts?.Total ?? 0, interviewed.Contains(row.Id),
                settlement?.Status, settlement?.Net, row.RelievingLetterId, row.ExperienceLetterId,
                row.ReasonCode, SeparationReasons.LabelOf(row.ReasonCode), row.SecondaryReasonCode, row.Regretted, row.RetentionNote,
                row.RequestedReleaseDay, row.ReleaseWithBuyout, row.GardenLeave, row.PayInLieuDays, row.NomineeName, row.NomineeRelation, row.NomineeContact, row.AwaitingConfirmation,
                due, due is null ? null : row.LastWorkingDay.AddDays(terms.GratuityDueDays), due is { } by && today > by && settlement?.Status != SettlementStatus.Paid);
        })];
    }
}
