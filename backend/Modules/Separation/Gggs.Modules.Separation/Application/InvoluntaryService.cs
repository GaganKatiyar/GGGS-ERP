using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Contracts;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record ShowCauseView(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string Allegation,
    DateOnly IssuedOn,
    DateOnly ReplyDueOn,
    Guid? NoticeLetterId,
    string? Reply,
    DateOnly? RepliedOn,
    Guid? EnquiryOfficerEmployeeId,
    string? EnquiryOfficerName,
    string? EnquiryFindings,
    DateOnly? EnquiryCompletedOn,
    ShowCauseStatus Status,
    ShowCauseOutcome Outcome,
    string? DecisionNote,
    DateOnly? DecidedOn,
    Guid? SeparationCaseId,
    bool Heard);

public sealed record AbsenceAlertView(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    DateOnly FirstAbsentDay,
    DateOnly LastWorkedDay,
    int WorkingDaysAbsent,
    DateOnly DetectedOn,
    AbsenceAlertStatus Status,
    int NoticesSent,
    DateOnly? LastNoticeOn,
    DateOnly? RespondBy,
    Guid? LastLetterId,
    string? Note,
    Guid? SeparationCaseId,
    bool CanSendNotice,
    bool CanSeparate);

public sealed record RetrenchmentResult(Guid EmployeeId, string? EmployeeName, Guid? CaseId, int PayInLieuDays, string? Error);

public sealed record ShowCauseBody(Guid EmployeeId, string Allegation, int? ReplyDays);

public sealed record ShowCauseReplyBody(string Reply);

public sealed record EnquiryBody(Guid OfficerEmployeeId);

public sealed record FindingsBody(string Findings);

public sealed record ShowCauseDecisionBody(ShowCauseOutcome Outcome, string? Note, DateOnly? LastWorkingDay, int? PayInLieuDays, bool DisableAccessNow = false);

public sealed record RetrenchmentBody(IReadOnlyList<Guid> EmployeeIds, string Reason, DateOnly NoticeDate, DateOnly LastWorkingDay, bool PayInLieuOfNotice);

public sealed record AbsenceCloseBody(bool Returned, string? Note);

/// <summary>
/// Exits the company starts (Separation v2 phase C): termination after a show-cause and, when
/// needed, a domestic enquiry; retrenchment under the Industrial Relations Code; absconding found
/// from attendance and handled through notices; and retirements and contract ends proposed ahead
/// of their date for HR to confirm.
/// </summary>
internal sealed class InvoluntaryService(
    AppDbContext dbContext,
    SeparationService separations,
    SeparationPolicyService policy,
    OffboardingService offboarding,
    IEmployeeDirectory employees,
    IEmployeeSeparations employeeSeparations,
    IAttendanceLedger attendance,
    ILetterGenerator letters,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    /// <summary>Notice the Industrial Relations Code asks for before retrenchment (s.70(a)): one month.</summary>
    public const int RetrenchmentNoticeDays = 30;

    // ----- Show-cause and termination ------------------------------------------------------------

    public async Task<IReadOnlyList<ShowCauseView>> ShowCausesAsync(bool? open, CancellationToken cancellationToken)
    {
        IQueryable<ShowCause> rows = dbContext.ShowCauses.AsNoTracking();
        if (open is { } wanted)
        {
            rows = wanted ? rows.Where(row => row.Status != ShowCauseStatus.Closed) : rows.Where(row => row.Status == ShowCauseStatus.Closed);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Separation.TerminationManage, cancellationToken);
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        return await ViewsAsync(await rows.OrderByDescending(row => row.IssuedOn).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<ShowCauseView> ShowCauseAsync(Guid id, CancellationToken cancellationToken)
        => (await ViewsAsync([await ManagedShowCauseAsync(id, cancellationToken)], cancellationToken))[0];

    public async Task<ShowCauseView> IssueAsync(ShowCauseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureInScopeAsync(body.EmployeeId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        ShowCause notice = ShowCause.Issue(body.EmployeeId, body.Allegation, today, body.ReplyDays ?? (await policy.PolicyAsync(cancellationToken)).ShowCauseReplyDays, currentUser.EmployeeId);
        dbContext.ShowCauses.Add(notice);
        notice.NoticeIssued(await letters.GenerateAsync(body.EmployeeId, "SHOW_CAUSE_NOTICE", new Dictionary<string, string>
        {
            ["allegation"] = notice.Allegation,
            ["replyBy"] = Format(notice.ReplyDueOn),
        }, cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(NotificationTemplates.ShowCauseIssued, [new NotificationRecipient(body.EmployeeId)], new Dictionary<string, string> { ["replyBy"] = Format(notice.ReplyDueOn) }, "/me/separation"),
            cancellationToken);
        return await ShowCauseAsync(notice.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShowCauseView>> MyShowCausesAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await ViewsAsync(await dbContext.ShowCauses.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.IssuedOn).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<ShowCauseView> ReplyAsync(Guid id, ShowCauseReplyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        ShowCause notice = await dbContext.ShowCauses.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == me, cancellationToken) ?? throw new NotFoundException(nameof(ShowCause), id);
        notice.RecordReply(body.Reply, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);

        EmployeeSummary? person = await employees.GetAsync(me, cancellationToken);
        List<Guid> told = [.. new[] { notice.IssuedByEmployeeId, person?.HrbpEmployeeId }.Where(id => id is not null && id != me).Select(id => id!.Value).Distinct()];
        if (told.Count > 0)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(NotificationTemplates.ShowCauseReplied, [.. told.Select(id => new NotificationRecipient(id))],
                    new Dictionary<string, string> { ["employeeName"] = person?.DisplayName ?? string.Empty, ["issuedOn"] = Format(notice.IssuedOn) }, "/separation/disciplinary"),
                cancellationToken);
        }

        return (await ViewsAsync([notice], cancellationToken))[0];
    }

    public async Task<ShowCauseView> StartEnquiryAsync(Guid id, EnquiryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ShowCause notice = await ManagedShowCauseAsync(id, cancellationToken);
        if (!await employees.IsActiveAsync(body.OfficerEmployeeId, cancellationToken))
        {
            throw new DomainRuleException("SHOW_CAUSE_OFFICER", "Choose a current employee to hold the enquiry.");
        }

        notice.StartEnquiry(body.OfficerEmployeeId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ShowCauseAsync(id, cancellationToken);
    }

    public async Task<ShowCauseView> RecordFindingsAsync(Guid id, FindingsBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ShowCause notice = await ManagedShowCauseAsync(id, cancellationToken);
        notice.RecordFindings(body.Findings, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ShowCauseAsync(id, cancellationToken);
    }

    /// <summary>Warns, drops, or terminates - the last opening a termination with its letter.</summary>
    public async Task<ShowCauseView> DecideAsync(Guid id, ShowCauseDecisionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ShowCause notice = await ManagedShowCauseAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        notice.Decide(body.Outcome, body.Note, today);
        if (body.Outcome != ShowCauseOutcome.Termination)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return await ShowCauseAsync(id, cancellationToken);
        }

        DateOnly lastDay = body.LastWorkingDay ?? today;
        int payInLieu = body.PayInLieuDays ?? 0;
        SeparationCase separation = await separations.StartAsync(
            new InitiateBody(notice.EmployeeId, SeparationType.Termination, $"Terminated after the show-cause notice of {Format(notice.IssuedOn)}: {notice.Allegation}", today, lastDay,
                lastDay.DayNumber - today.DayNumber, "MISCONDUCT", payInLieu),
            Permissions.Separation.TerminationManage,
            cancellationToken);
        notice.LedTo(separation.Id);
        string process = notice.EnquiryCompletedOn is { } held
            ? $"the findings of the domestic enquiry completed on {Format(held)}"
            : notice.RepliedOn is { } replied ? $"your reply of {Format(replied)}" : "no reply having been received by the date given";
        await letters.GenerateAsync(notice.EmployeeId, "TERMINATION_LETTER", new Dictionary<string, string>
        {
            ["issuedOn"] = Format(notice.IssuedOn),
            ["process"] = process,
            ["lastWorkingDay"] = Format(lastDay),
            ["noticeTerms"] = payInLieu > 0 ? $"You will be paid {payInLieu} days' wages in lieu of notice." : string.Empty,
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.DisableAccessNow)
        {
            await offboarding.DisableAccessNowAsync(separation.Id, cancellationToken);
        }

        return await ShowCauseAsync(id, cancellationToken);
    }

    // ----- Retrenchment ---------------------------------------------------------------------------

    /// <summary>
    /// Retrenches several people at once. Each gets a month's notice or pay in lieu of the part not
    /// given, a retrenchment notice, and compensation in the settlement. One person's problem (an
    /// open separation, say) does not stop the rest.
    /// </summary>
    public async Task<IReadOnlyList<RetrenchmentResult>> RetrenchAsync(RetrenchmentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<Guid> ids = [.. (body.EmployeeIds ?? []).Distinct()];
        if (ids.Count is 0 or > 200)
        {
            throw new DomainRuleException("RETRENCHMENT_EMPLOYEES", "Choose between 1 and 200 employees.");
        }

        if (string.IsNullOrWhiteSpace(body.Reason))
        {
            throw new DomainRuleException("RETRENCHMENT_REASON", "Say why the positions are being cut.");
        }

        int given = body.LastWorkingDay.DayNumber - body.NoticeDate.DayNumber;
        if (given < 0)
        {
            throw new DomainRuleException("SEPARATION_LAST_DAY", "The last working day cannot be before notice is given.");
        }

        if (given < RetrenchmentNoticeDays && !body.PayInLieuOfNotice)
        {
            throw new DomainRuleException("RETRENCHMENT_NOTICE", "Give a month's notice, or pay wages in lieu of the part not given.");
        }

        int payInLieu = Math.Max(0, RetrenchmentNoticeDays - given);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(ids, cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        List<RetrenchmentResult> results = [];
        foreach (Guid id in ids)
        {
            try
            {
                SeparationCase separation = await separations.StartAsync(
                    new InitiateBody(id, SeparationType.Retrenchment, body.Reason, body.NoticeDate, body.LastWorkingDay, given, "RESTRUCTURING", payInLieu),
                    Permissions.Separation.TerminationManage,
                    cancellationToken);
                await letters.GenerateAsync(id, "RETRENCHMENT_NOTICE", new Dictionary<string, string>
                {
                    ["reason"] = body.Reason.Trim(),
                    ["lastWorkingDay"] = Format(body.LastWorkingDay),
                    ["noticeTerms"] = payInLieu > 0 ? $"You will be paid {payInLieu} days' wages in lieu of the notice not given." : "This letter is your one month's notice.",
                }, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                results.Add(new RetrenchmentResult(id, names.GetValueOrDefault(id), separation.Id, payInLieu, null));
            }
            catch (DomainRuleException failure)
            {
                dbContext.ChangeTracker.Clear();
                results.Add(new RetrenchmentResult(id, names.GetValueOrDefault(id), null, 0, failure.Message));
            }
        }

        return results;
    }

    // ----- Absconding -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<AbsenceAlertView>> AbsencesAsync(bool? open, CancellationToken cancellationToken)
    {
        IQueryable<AbsenceAlert> rows = dbContext.AbsenceAlerts.AsNoTracking();
        if (open is { } wanted)
        {
            rows = wanted ? rows.Where(row => row.Status == AbsenceAlertStatus.Open) : rows.Where(row => row.Status != AbsenceAlertStatus.Open);
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Separation.TerminationManage, cancellationToken);
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        return await AlertViewsAsync(await rows.OrderByDescending(row => row.DetectedOn).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Looks for runs of unexplained absence across everyone without a separation in progress.
    /// New runs raise an alert (and tell HR and the manager); runs that ended before any notice went
    /// out close themselves. Returns how many alerts were raised.
    /// </summary>
    public async Task<int> DetectAbsencesAsync(CancellationToken cancellationToken)
    {
        SeparationPolicy rules = await policy.PolicyAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        HashSet<Guid> leaving = [.. await dbContext.SeparationCases.Where(row => row.Status == CaseStatus.PendingApproval || row.Status == CaseStatus.Approved).Select(row => row.EmployeeId).ToListAsync(cancellationToken)];
        List<Guid> candidates = [.. (await employees.ListActiveIdsAsync(null, null, cancellationToken)).Where(id => !leaving.Contains(id))];
        Dictionary<Guid, AbsenceStreak> streaks = (await attendance.AbsenceStreaksAsync(candidates, rules.AbscondingDays, cancellationToken)).ToDictionary(streak => streak.EmployeeId);
        List<AbsenceAlert> alerts = await dbContext.AbsenceAlerts.Where(row => candidates.Contains(row.EmployeeId)).ToListAsync(cancellationToken);

        foreach (AbsenceAlert alert in alerts.Where(row => row.IsOpen))
        {
            if (streaks.TryGetValue(alert.EmployeeId, out AbsenceStreak? streak) && streak.FirstAbsentDay == alert.FirstAbsentDay)
            {
                alert.Extend(streak.WorkingDaysAbsent);
            }
            else if (alert.NoticesSent == 0)
            {
                alert.Close(AbsenceAlertStatus.Returned, "Attendance shows the absence has ended.");
            }
        }

        List<AbsenceAlert> raised = [];
        foreach (AbsenceStreak streak in streaks.Values)
        {
            if (alerts.Any(row => row.EmployeeId == streak.EmployeeId && (row.IsOpen || row.FirstAbsentDay == streak.FirstAbsentDay)))
            {
                continue;
            }

            AbsenceAlert alert = AbsenceAlert.Raise(streak.EmployeeId, streak.FirstAbsentDay, streak.LastWorkedDay, streak.WorkingDaysAbsent, today);
            dbContext.AbsenceAlerts.Add(alert);
            raised.Add(alert);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(raised.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        foreach (AbsenceAlert alert in raised)
        {
            EmployeeSummary? person = people.GetValueOrDefault(alert.EmployeeId);
            List<Guid> told = [.. new[] { person?.HrbpEmployeeId, person?.ManagerEmployeeId }.Where(id => id is not null).Select(id => id!.Value).Distinct()];
            if (told.Count > 0)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(NotificationTemplates.AbsenceAlert, [.. told.Select(id => new NotificationRecipient(id))],
                        new Dictionary<string, string>
                        {
                            ["employeeName"] = person?.DisplayName ?? string.Empty,
                            ["days"] = alert.WorkingDaysAbsent.ToString(CultureInfo.InvariantCulture),
                            ["absentSince"] = Format(alert.FirstAbsentDay),
                        },
                        "/separation/absconding"),
                    cancellationToken);
            }
        }

        return raised.Count;
    }

    public async Task<AbsenceAlertView> SendAbsenceNoticeAsync(Guid id, CancellationToken cancellationToken)
    {
        AbsenceAlert alert = await ManagedAlertAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        int days = (await policy.PolicyAsync(cancellationToken)).ShowCauseReplyDays;
        int number = alert.NoticesSent + 1;
        if (number > AbsenceAlert.MaximumNotices)
        {
            throw new DomainRuleException("ABSENCE_NOTICES", "The final notice has already been sent.");
        }

        string title = AbsenceAlert.NoticeTitle(number);
        DateOnly respondBy = today.AddDays(days);
        Guid letter = await letters.GenerateAsync(alert.EmployeeId, "ABSCONDING_NOTICE", new Dictionary<string, string>
        {
            ["noticeTitle"] = title,
            ["absentSince"] = Format(alert.FirstAbsentDay),
            ["respondBy"] = Format(respondBy),
            ["consequence"] = number == AbsenceAlert.MaximumNotices
                ? $"If we do not hear from you by then, we will treat you as having abandoned your employment, with effect from your last day at work, {Format(alert.LastWorkedDay)}."
                : "If you do not respond, the company may take further action, which can include treating you as having left.",
        }, cancellationToken);
        alert.SendNotice(today, days, letter);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(NotificationTemplates.AbsenceNotice, [new NotificationRecipient(alert.EmployeeId)],
                new Dictionary<string, string> { ["noticeTitle"] = title, ["absentSince"] = Format(alert.FirstAbsentDay), ["respondBy"] = Format(respondBy) }, "/me/documents"),
            cancellationToken);
        return (await AlertViewsAsync([alert], cancellationToken))[0];
    }

    public async Task<AbsenceAlertView> CloseAbsenceAsync(Guid id, AbsenceCloseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        AbsenceAlert alert = await ManagedAlertAsync(id, cancellationToken);
        alert.Close(body.Returned ? AbsenceAlertStatus.Returned : AbsenceAlertStatus.Dismissed, body.Note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await AlertViewsAsync([alert], cancellationToken))[0];
    }

    /// <summary>Treats the absence as absconding: a separation from the last day worked, completed by the exit sweep.</summary>
    public async Task<AbsenceAlertView> SeparateAbsenceAsync(Guid id, CancellationToken cancellationToken)
    {
        AbsenceAlert alert = await ManagedAlertAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (!alert.NoticeLapsed(today))
        {
            throw new DomainRuleException("ABSENCE_NOT_LAPSED", "Send a notice and let its date pass before treating the absence as absconding.");
        }

        SeparationCase separation = await separations.StartAsync(
            new InitiateBody(alert.EmployeeId, SeparationType.Absconding,
                $"Absent without leave since {Format(alert.FirstAbsentDay)}; no response to {alert.NoticesSent} notice(s).", alert.LastWorkedDay, alert.LastWorkedDay, 0),
            Permissions.Separation.TerminationManage,
            cancellationToken);
        alert.Separated(separation.Id, today);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await AlertViewsAsync([alert], cancellationToken))[0];
    }

    // ----- Retirement and contract end ------------------------------------------------------------

    /// <summary>
    /// Proposes a separation for everyone whose retirement or contract end falls within the policy's
    /// lead time and who has none in progress. A proposal HR declined is not raised again for the
    /// same date. Returns how many were proposed.
    /// </summary>
    public async Task<int> ProposeExitsAsync(CancellationToken cancellationToken)
    {
        SeparationPolicy rules = await policy.PolicyAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyList<UpcomingExit> upcoming = await employeeSeparations.UpcomingExitsAsync(rules.RetirementAge, today.AddDays(rules.ExitProposalLeadDays), cancellationToken);
        List<Guid> ids = [.. upcoming.Select(row => row.EmployeeId)];
        var cases = await dbContext.SeparationCases.AsNoTracking().Where(row => ids.Contains(row.EmployeeId))
            .Select(row => new { row.EmployeeId, row.Status, row.Type, row.RequestedLastDay }).ToListAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(ids, cancellationToken)).ToDictionary(person => person.Id);
        int proposed = 0;
        foreach (UpcomingExit exit in upcoming)
        {
            SeparationType type = exit.Kind == UpcomingExitKind.Retirement ? SeparationType.Retirement : SeparationType.ContractEnd;
            if (cases.Any(row => row.EmployeeId == exit.EmployeeId
                    && (row.Status is CaseStatus.PendingApproval or CaseStatus.Approved || (row.Type == type && row.RequestedLastDay == exit.Date))))
            {
                continue;
            }

            string reason = type == SeparationType.Retirement ? $"Superannuation at {rules.RetirementAge}" : "Fixed-term contract ends";
            await separations.ProposeAsync(exit.EmployeeId, type, exit.Date, reason, today, cancellationToken);
            proposed++;
            EmployeeSummary? person = people.GetValueOrDefault(exit.EmployeeId);
            if (person?.HrbpEmployeeId is { } hrbp)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(NotificationTemplates.ExitProposed, [new NotificationRecipient(hrbp)],
                        new Dictionary<string, string>
                        {
                            ["employeeName"] = person.DisplayName,
                            ["kind"] = type == SeparationType.Retirement ? "retirement" : "the end of their contract",
                            ["lastDay"] = Format(exit.Date),
                        },
                        "/separation"),
                    cancellationToken);
            }
        }

        return proposed;
    }

    // ----- Internals ------------------------------------------------------------------------------

    private static string Format(DateOnly day) => day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("SEPARATION_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task EnsureInScopeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await employees.IsActiveAsync(employeeId, cancellationToken) || !await employees.IsInScopeAsync(employeeId, Permissions.Separation.TerminationManage, cancellationToken))
        {
            throw new DomainRuleException("SEPARATION_EMPLOYEE", "Choose an active employee you manage separations for.");
        }
    }

    private async Task<ShowCause> ManagedShowCauseAsync(Guid id, CancellationToken cancellationToken)
    {
        ShowCause notice = await dbContext.ShowCauses.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ShowCause), id);
        return await employees.IsInScopeAsync(notice.EmployeeId, Permissions.Separation.TerminationManage, cancellationToken) ? notice : throw new NotFoundException(nameof(ShowCause), id);
    }

    private async Task<AbsenceAlert> ManagedAlertAsync(Guid id, CancellationToken cancellationToken)
    {
        AbsenceAlert alert = await dbContext.AbsenceAlerts.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(AbsenceAlert), id);
        return await employees.IsInScopeAsync(alert.EmployeeId, Permissions.Separation.TerminationManage, cancellationToken) ? alert : throw new NotFoundException(nameof(AbsenceAlert), id);
    }

    private async Task<List<ShowCauseView>> ViewsAsync(IReadOnlyList<ShowCause> rows, CancellationToken cancellationToken)
    {
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(
                rows.Select(row => row.EmployeeId).Concat(rows.Where(row => row.EnquiryOfficerEmployeeId != null).Select(row => row.EnquiryOfficerEmployeeId!.Value)).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        return [.. rows.Select(row =>
        {
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new ShowCauseView(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, row.Allegation, row.IssuedOn, row.ReplyDueOn, row.NoticeLetterId, row.Reply, row.RepliedOn,
                row.EnquiryOfficerEmployeeId, row.EnquiryOfficerEmployeeId is { } officer ? people.GetValueOrDefault(officer)?.DisplayName : null, row.EnquiryFindings, row.EnquiryCompletedOn,
                row.Status, row.Outcome, row.DecisionNote, row.DecidedOn, row.SeparationCaseId, row.Heard(today));
        })];
    }

    private async Task<List<AbsenceAlertView>> AlertViewsAsync(IReadOnlyList<AbsenceAlert> rows, CancellationToken cancellationToken)
    {
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(rows.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        return [.. rows.Select(row =>
        {
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new AbsenceAlertView(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, row.FirstAbsentDay, row.LastWorkedDay, row.WorkingDaysAbsent, row.DetectedOn, row.Status,
                row.NoticesSent, row.LastNoticeOn, row.RespondBy, row.LastLetterId, row.Note, row.SeparationCaseId,
                row.IsOpen && row.NoticesSent < AbsenceAlert.MaximumNotices, row.IsOpen && row.NoticeLapsed(today));
        })];
    }
}
