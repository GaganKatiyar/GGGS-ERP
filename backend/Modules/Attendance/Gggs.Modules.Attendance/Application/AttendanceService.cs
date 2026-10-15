using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Attendance.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Shift.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Attendance.Application;

public sealed record AttendanceDayItem(
    Guid EmployeeId,
    string? EmployeeName,
    DateOnly Date,
    string ShiftName,
    TimeOnly ScheduledStart,
    TimeOnly ScheduledEnd,
    TimeOnly? FirstIn,
    TimeOnly? LastOut,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    AttendanceStatus Status,
    int PunchCount,
    bool WorkedOnDayOff,
    bool IsRegularized,
    bool IsToday,
    string? Note);

public sealed record PunchItem(DateTime At, TimeOnly LocalTime, PunchSource Source);

public sealed record TodayView(AttendanceDayItem? Day, IReadOnlyList<PunchItem> Punches, bool CanPunch);

public sealed record AttendanceSummary(DateOnly Date, int People, IReadOnlyDictionary<AttendanceStatus, int> ByStatus, int Late);

public sealed record RegularizationItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    DateOnly Date,
    TimeOnly? CheckIn,
    TimeOnly? CheckOut,
    string Reason,
    RegularizationStatus Status,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate);

public sealed record RegularizationBody(DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut, string Reason);

public sealed record DeviceItem(Guid Id, string Code, string Name, Guid? LocationId, DateTime? LastSeenDate, bool IsActive);

public sealed record DeviceRequest(string Code, string Name, Guid? LocationId);

public sealed record DevicePunch(string EmployeeCode, DateTimeOffset PunchedAt);

public sealed record PunchImport(string DeviceCode, IReadOnlyList<DevicePunch> Punches);

public sealed record PunchImportResult(int Imported, int Duplicates, IReadOnlyList<string> UnknownEmployeeCodes);

/// <summary>
/// Attendance (spec §18): punches in, judged days out. Reads always evaluate from the source data
/// - punches, roster, leave, holidays - so a day is never stale; the stored days that reports and
/// payroll read are written for closed days, by the nightly job and whenever a closed day's inputs
/// change (a regularization, a device import, leave approved or cancelled).
/// </summary>
internal sealed class AttendanceService(
    AppDbContext dbContext,
    IShiftSchedule schedule,
    ILeaveCalendar leave,
    IEmployeeDirectory employees,
    IWorkflowStarter workflow,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string RegularizationSubjectType = "attendance.regularization";

    public static WorkflowSubject RegularizationSubject { get; } = new(RegularizationSubjectType, "Attendance regularization", "attendance", []);

    /// <summary>Punches this long before a shift starts, or after it ends, still belong to it.</summary>
    private static readonly TimeSpan EarlyWindow = TimeSpan.FromHours(4);
    private static readonly TimeSpan LateWindow = TimeSpan.FromHours(6);

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Evaluating --------------------------------------------------------------------------

    /// <summary>Judges every day in the range up to today for each employee. Future days are left out.</summary>
    public async Task<IReadOnlyList<DayEvaluation>> EvaluateAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly last = to > today ? today : to;
        List<Guid> ids = [.. employeeIds.Distinct()];
        if (ids.Count == 0 || last < from)
        {
            return [];
        }

        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);
        IReadOnlyList<ScheduledDay> days = await schedule.GetAsync(ids, from, last, cancellationToken);
        IReadOnlyList<LeaveDay> leaveDays = await leave.GetApprovedLeaveAsync(ids, from, last, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<HolidayEntry>> holidays = await leave.GetHolidaysAsync(ids, from, last, cancellationToken);

        DateTime windowStart = await clock.ToUtcAsync(from.AddDays(-1), TimeOnly.MinValue, cancellationToken);
        DateTime windowEnd = await clock.ToUtcAsync(last.AddDays(2), TimeOnly.MinValue, cancellationToken);
        List<AttendancePunch> punches = await dbContext.AttendancePunches.AsNoTracking()
            .Where(punch => ids.Contains(punch.EmployeeId) && punch.PunchedAt >= windowStart && punch.PunchedAt < windowEnd)
            .OrderBy(punch => punch.PunchedAt)
            .ToListAsync(cancellationToken);

        var results = new List<DayEvaluation>(days.Count);
        foreach (IGrouping<Guid, ScheduledDay> person in days.GroupBy(day => day.EmployeeId))
        {
            List<LocalPunch> unassigned = [.. punches
                .Where(punch => punch.EmployeeId == person.Key)
                .Select(punch => new LocalPunch(TimeZoneInfo.ConvertTimeFromUtc(punch.PunchedAt, zone), punch.PunchedAt, punch.Source == PunchSource.Regularization))];

            foreach (ScheduledDay day in person.OrderBy(day => day.Date))
            {
                DaySchedule plan = ToSchedule(day);
                DateTime opens = plan.ScheduledStart - EarlyWindow;
                DateTime closes = plan.ScheduledEnd + LateWindow;
                List<LocalPunch> mine = [.. unassigned.Where(punch => punch.Local >= opens && punch.Local <= closes)];
                unassigned.RemoveAll(mine.Contains);

                List<LeaveDay> onLeave = [.. leaveDays.Where(entry => entry.EmployeeId == day.EmployeeId && entry.Date == day.Date)];
                string? holiday = holidays.TryGetValue(day.EmployeeId, out IReadOnlyList<HolidayEntry>? list)
                    ? list.FirstOrDefault(entry => entry.Date == day.Date && !entry.IsOptional)?.Name
                    : null;

                DayEvaluation evaluation = AttendanceEvaluator.Evaluate(
                    day.EmployeeId, day.Date, plan, mine,
                    fullDayLeave: onLeave.Any(entry => !entry.IsHalfDay),
                    halfDayLeave: onLeave.Any(entry => entry.IsHalfDay),
                    holiday);

                if (day.Date == today && evaluation.Status == AttendanceStatus.Absent && evaluation.PunchCount == 0)
                {
                    evaluation = evaluation with { Note = "Not punched in yet" };
                }
                else if (day.Date == today && evaluation.Status == AttendanceStatus.MissingPunch)
                {
                    // The day is still open: one punch is someone at work, not a missing punch yet.
                    evaluation = evaluation with { Note = "Punched in, day in progress" };
                }

                if (onLeave.Count > 0 && evaluation.Status == AttendanceStatus.OnLeave)
                {
                    evaluation = evaluation with { Note = onLeave[0].LeaveTypeName };
                }

                results.Add(evaluation);
            }
        }

        return results;
    }

    /// <summary>Writes the judged days for closed days (before today) in the range.</summary>
    public async Task<int> PersistAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        DateOnly yesterday = (await clock.TodayAsync(cancellationToken)).AddDays(-1);
        DateOnly last = to > yesterday ? yesterday : to;
        if (last < from)
        {
            return 0;
        }

        int written = 0;
        foreach (Guid[] chunk in employeeIds.Distinct().Chunk(200))
        {
            IReadOnlyList<DayEvaluation> evaluations = await EvaluateAsync(chunk, from, last, cancellationToken);
            Dictionary<(Guid, DateOnly), AttendanceDay> stored = await dbContext.AttendanceDays
                .Where(day => chunk.Contains(day.EmployeeId) && day.Date >= from && day.Date <= last)
                .ToDictionaryAsync(day => (day.EmployeeId, day.Date), cancellationToken);

            foreach (DayEvaluation evaluation in evaluations)
            {
                if (!stored.TryGetValue((evaluation.EmployeeId, evaluation.Date), out AttendanceDay? day))
                {
                    day = AttendanceDay.For(evaluation.EmployeeId, evaluation.Date);
                    dbContext.AttendanceDays.Add(day);
                }

                day.Apply(evaluation);
                written++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return written;
    }

    public async Task<IReadOnlyList<AttendanceDayItem>> DaysAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            throw new DomainRuleException("ATTENDANCE_RANGE", "Show at most two months at a time.");
        }

        List<Guid> ids = [.. employeeIds.Take(500)];
        IReadOnlyList<DayEvaluation> evaluations = await EvaluateAsync(ids, from, to, cancellationToken);
        IReadOnlyList<ScheduledDay> plans = await schedule.GetAsync(ids, from, to, cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(ids, cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<(Guid, DateOnly), ScheduledDay> planned = plans.ToDictionary(day => (day.EmployeeId, day.Date));

        return [.. evaluations
            .OrderBy(evaluation => names.GetValueOrDefault(evaluation.EmployeeId), StringComparer.CurrentCulture)
            .ThenByDescending(evaluation => evaluation.Date)
            .Select(evaluation =>
            {
                ScheduledDay plan = planned[(evaluation.EmployeeId, evaluation.Date)];
                return new AttendanceDayItem(
                    evaluation.EmployeeId,
                    names.GetValueOrDefault(evaluation.EmployeeId),
                    evaluation.Date,
                    evaluation.ShiftName,
                    plan.Start,
                    plan.End,
                    Local(evaluation.FirstIn, zone),
                    Local(evaluation.LastOut, zone),
                    evaluation.WorkedMinutes,
                    evaluation.LateMinutes,
                    evaluation.EarlyLeaveMinutes,
                    evaluation.Status,
                    evaluation.PunchCount,
                    evaluation.WorkedOnDayOff,
                    evaluation.IsRegularized,
                    evaluation.Date == today,
                    evaluation.Note);
            })];
    }

    public async Task<AttendanceSummary> SummaryAsync(DateOnly date, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. employeeIds.Take(1000)];
        IReadOnlyList<DayEvaluation> evaluations = await EvaluateAsync(ids, date, date, cancellationToken);
        Dictionary<AttendanceStatus, int> byStatus = evaluations.GroupBy(evaluation => evaluation.Status).ToDictionary(group => group.Key, group => group.Count());
        return new AttendanceSummary(date, evaluations.Count, byStatus, evaluations.Count(evaluation => evaluation.LateMinutes > 0));
    }

    /// <summary>Who the caller may see under a permission, narrowed to a department or person when asked.</summary>
    public async Task<IReadOnlyList<Guid>> PeopleAsync(string permission, Guid? departmentId, Guid? employeeId, CancellationToken cancellationToken)
    {
        if (employeeId is { } single)
        {
            return await employees.IsInScopeAsync(single, permission, cancellationToken) ? [single] : throw new NotFoundException("Employee", single);
        }

        IReadOnlyList<Guid> candidates = await employees.ListActiveIdsAsync(departmentId is { } department ? [department] : null, null, cancellationToken);
        IReadOnlyCollection<Guid>? visible = await employees.InScopeIdsAsync(permission, cancellationToken);
        return visible is null ? candidates : [.. candidates.Where(visible.Contains)];
    }

    // ----- Self-service ------------------------------------------------------------------------

    public async Task<TodayView> PunchAsync(CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        DateTime now = UtcNow;

        bool justPunched = await dbContext.AttendancePunches.AnyAsync(punch => punch.EmployeeId == me && punch.PunchedAt > now.AddMinutes(-1), cancellationToken);
        if (justPunched)
        {
            throw new DomainRuleException("ATTENDANCE_PUNCH_TOO_SOON", "You punched less than a minute ago.");
        }

        dbContext.AttendancePunches.Add(AttendancePunch.Record(me, now, PunchSource.Web, ipAddress: currentUser.IpAddress));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await TodayAsync(cancellationToken);
    }

    public async Task<TodayView> TodayAsync(CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyList<AttendanceDayItem> days = await DaysAsync([me], today, today, cancellationToken);
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);

        DateTime start = await clock.ToUtcAsync(today, TimeOnly.MinValue, cancellationToken);
        List<PunchItem> punches = await dbContext.AttendancePunches.AsNoTracking()
            .Where(punch => punch.EmployeeId == me && punch.PunchedAt >= start)
            .OrderBy(punch => punch.PunchedAt)
            .Select(punch => new PunchItem(punch.PunchedAt, TimeOnly.MinValue, punch.Source))
            .ToListAsync(cancellationToken);

        return new TodayView(
            days.Count > 0 ? days[0] : null,
            [.. punches.Select(punch => punch with { LocalTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(punch.At, zone)) })],
            CanPunch: true);
    }

    // ----- Regularization ----------------------------------------------------------------------

    public async Task<RegularizationItem> RequestRegularizationAsync(RegularizationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = RequireEmployee();
        DateOnly today = await clock.TodayAsync(cancellationToken);

        if (body.Date > today || body.Date < today.AddDays(-31))
        {
            throw new DomainRuleException("REGULARIZATION_DATE", "Regularize a day from the last month, not a future one.");
        }

        if (await dbContext.Regularizations.AnyAsync(request => request.EmployeeId == me && request.Date == body.Date && request.Status == RegularizationStatus.Pending, cancellationToken))
        {
            throw new DomainRuleException("REGULARIZATION_PENDING", "You already have a request waiting for that day.");
        }

        var request = RegularizationRequest.Create(me, body.Date, body.CheckIn, body.CheckOut, body.Reason);
        dbContext.Regularizations.Add(request);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = RegularizationSubjectType,
                SubjectId = request.Id,
                Title = $"Attendance correction for {body.Date:d MMM yyyy}",
                RequesterEmployeeId = me,
                Link = "/team/attendance",
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            request.AwaitApproval(started.InstanceId!.Value);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await ApplyRegularizationAsync(request, cancellationToken);
        }

        return (await ToItemsAsync([request], cancellationToken))[0];
    }

    public async Task CompleteRegularizationAsync(Guid requestId, bool approved, CancellationToken cancellationToken)
    {
        RegularizationRequest? request = await dbContext.Regularizations.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        if (request is null || request.Status != RegularizationStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            await ApplyRegularizationAsync(request, cancellationToken);
        }
        else
        {
            request.Reject();
        }
    }

    public async Task CancelRegularizationAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid me = RequireEmployee();
        RegularizationRequest request = await dbContext.Regularizations.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(RegularizationRequest), id);

        request.Cancel();
        await workflow.WithdrawAsync(RegularizationSubjectType, request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegularizationItem>> RegularizationsAsync(bool mine, RegularizationStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<RegularizationRequest> requests = dbContext.Regularizations.AsNoTracking();
        if (mine)
        {
            Guid me = currentUser.EmployeeId ?? Guid.Empty;
            requests = requests.Where(request => request.EmployeeId == me);
        }
        else
        {
            IReadOnlyCollection<Guid>? visible = await employees.InScopeIdsAsync(Permissions.Attendance.RegularizationView, cancellationToken);
            if (visible is not null)
            {
                List<Guid> ids = [.. visible];
                requests = requests.Where(request => ids.Contains(request.EmployeeId));
            }
        }

        if (status is { } wanted)
        {
            requests = requests.Where(request => request.Status == wanted);
        }

        return await ToItemsAsync(await requests.OrderByDescending(request => request.Date).Take(300).ToListAsync(cancellationToken), cancellationToken);
    }

    private async Task ApplyRegularizationAsync(RegularizationRequest request, CancellationToken cancellationToken)
    {
        if (request.CheckIn is { } checkIn)
        {
            dbContext.AttendancePunches.Add(AttendancePunch.Record(
                request.EmployeeId, await clock.ToUtcAsync(request.Date, checkIn, cancellationToken), PunchSource.Regularization, note: "Regularized"));
        }

        if (request.CheckOut is { } checkOut)
        {
            // A check-out earlier than the check-in is after midnight: the night shift's next morning.
            DateOnly outDate = request.CheckIn is { } start && checkOut < start ? request.Date.AddDays(1) : request.Date;
            dbContext.AttendancePunches.Add(AttendancePunch.Record(
                request.EmployeeId, await clock.ToUtcAsync(outDate, checkOut, cancellationToken), PunchSource.Regularization, note: "Regularized"));
        }

        request.Approve();
        await dbContext.SaveChangesAsync(cancellationToken);
        await PersistAsync([request.EmployeeId], request.Date, request.Date, cancellationToken);
    }

    // ----- Devices -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<DeviceItem>> DevicesAsync(CancellationToken cancellationToken)
        => await dbContext.AttendanceDevices.AsNoTracking()
            .OrderBy(device => device.Name)
            .Select(device => new DeviceItem(device.Id, device.Code, device.Name, device.LocationId, device.LastSeenDate, device.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<DeviceItem> SaveDeviceAsync(Guid? id, DeviceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AttendanceDevice device;
        if (id is { } existing)
        {
            device = await dbContext.AttendanceDevices.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(AttendanceDevice), existing);
            device.Update(request.Name, request.LocationId);
        }
        else
        {
            string code = request.Code.Trim().ToUpperInvariant();
            if (await dbContext.AttendanceDevices.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("ATTENDANCE_DEVICE_CODE_IN_USE", $"A device with code '{code}' already exists.");
            }

            device = AttendanceDevice.Create(code, request.Name, request.LocationId);
            dbContext.AttendanceDevices.Add(device);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DeviceItem(device.Id, device.Code, device.Name, device.LocationId, device.LastSeenDate, device.IsActive);
    }

    public async Task SetDeviceActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        AttendanceDevice device = await dbContext.AttendanceDevices.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(AttendanceDevice), id);
        if (active)
        {
            device.Activate();
        }
        else
        {
            device.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Takes a batch of punches a device (or its integration) reports, by employee code. Repeats of
    /// a punch already held are skipped, so a device can safely resend its buffer.
    /// </summary>
    public async Task<PunchImportResult> ImportAsync(PunchImport import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);
        if (import.Punches is null || import.Punches.Count is 0 or > 5000)
        {
            throw new DomainRuleException("ATTENDANCE_IMPORT_SIZE", "Send between one and five thousand punches at a time.");
        }

        string code = import.DeviceCode.Trim().ToUpperInvariant();
        AttendanceDevice device = await dbContext.AttendanceDevices.FirstOrDefaultAsync(row => row.Code == code && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("ATTENDANCE_DEVICE_UNKNOWN", $"No active device with code '{code}'.");

        IReadOnlyDictionary<string, Guid> codes = await employees.ResolveCodesAsync(import.Punches.Select(punch => punch.EmployeeCode), cancellationToken);
        List<string> unknown = [.. import.Punches.Select(punch => punch.EmployeeCode.Trim()).Where(employeeCode => !codes.ContainsKey(employeeCode)).Distinct(StringComparer.OrdinalIgnoreCase)];

        var incoming = import.Punches
            .Where(punch => codes.ContainsKey(punch.EmployeeCode.Trim()))
            .Select(punch => (EmployeeId: codes[punch.EmployeeCode.Trim()], At: punch.PunchedAt.UtcDateTime))
            .Distinct()
            .ToList();

        List<Guid> people = [.. incoming.Select(punch => punch.EmployeeId).Distinct()];
        DateTime earliest = incoming.Count > 0 ? incoming.Min(punch => punch.At) : UtcNow;
        DateTime latest = incoming.Count > 0 ? incoming.Max(punch => punch.At) : UtcNow;

        HashSet<(Guid, DateTime)> held = [.. (await dbContext.AttendancePunches.AsNoTracking()
                .Where(punch => people.Contains(punch.EmployeeId) && punch.PunchedAt >= earliest && punch.PunchedAt <= latest)
                .Select(punch => new { punch.EmployeeId, punch.PunchedAt })
                .ToListAsync(cancellationToken))
            .Select(punch => (punch.EmployeeId, punch.PunchedAt))];

        int imported = 0;
        foreach ((Guid employeeId, DateTime at) in incoming)
        {
            if (held.Add((employeeId, at)))
            {
                dbContext.AttendancePunches.Add(AttendancePunch.Record(employeeId, at, PunchSource.Device, device.Id));
                imported++;
            }
        }

        device.Seen(UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (imported > 0)
        {
            TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);
            DateOnly first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earliest, zone)).AddDays(-1);
            DateOnly last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(latest, zone));
            await PersistAsync(people, first, last, cancellationToken);
        }

        return new PunchImportResult(imported, incoming.Count - imported, unknown);
    }

    // ----- Internals ---------------------------------------------------------------------------

    private Guid RequireEmployee()
        => currentUser.EmployeeId ?? throw new DomainRuleException("ATTENDANCE_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private static DaySchedule ToSchedule(ScheduledDay day)
    {
        DateTime start = day.Date.ToDateTime(day.Start);
        DateTime end = (day.IsOvernight ? day.Date.AddDays(1) : day.Date).ToDateTime(day.End);
        return new DaySchedule(day.ShiftName, start, end, day.BreakMinutes, day.GraceMinutes, day.HalfDayMinutes, day.FullDayMinutes, day.IsWeeklyOff);
    }

    private static TimeOnly? Local(DateTime? utc, TimeZoneInfo zone)
        => utc is { } instant ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone)) : null;

    private async Task<List<RegularizationItem>> ToItemsAsync(List<RegularizationRequest> requests, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> names = (await employees.GetManyAsync(requests.Select(request => request.EmployeeId), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return [.. requests.Select(request => new RegularizationItem(
            request.Id, request.EmployeeId, names.GetValueOrDefault(request.EmployeeId), request.Date, request.CheckIn, request.CheckOut,
            request.Reason, request.Status, request.WorkflowInstanceId, request.CreatedDate == default ? UtcNow : request.CreatedDate))];
    }
}
