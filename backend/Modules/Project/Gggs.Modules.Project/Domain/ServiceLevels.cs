using System.Globalization;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

/// <summary>What a priority promises: how fast it is answered and resolved, and what missing that costs.</summary>
public sealed record SlaTarget(WorkPriority Priority, int ResponseMinutes, int ResolutionMinutes, decimal CreditPercent);

/// <summary>When the SLA clock runs: all the time, or in working hours where the organisation is.</summary>
public sealed record SlaCalendar(bool AlwaysOn, TimeOnly DayStart, TimeOnly DayEnd, IReadOnlyList<DayOfWeek> WorkingDays)
{
    public static SlaCalendar AllTheTime { get; } = new(true, TimeOnly.MinValue, TimeOnly.MaxValue, []);
}

public sealed record SlaPolicyDetails(SlaCalendar Calendar, IReadOnlyList<SlaTarget> Targets, IReadOnlyList<Guid> PausedColumnIds, decimal MonthlyCreditCapPercent);

/// <summary>The starting point offered for a new policy. A contract's own numbers replace these.</summary>
public static class SlaDefaults
{
    public static SlaPolicyDetails Details { get; } = new(
        new SlaCalendar(false, new TimeOnly(9, 0), new TimeOnly(18, 0), [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]),
        [
            new SlaTarget(WorkPriority.Critical, 30, 240, 5),
            new SlaTarget(WorkPriority.High, 60, 480, 3),
            new SlaTarget(WorkPriority.Medium, 240, 1440, 2),
            new SlaTarget(WorkPriority.Low, 480, 2880, 1),
        ],
        [],
        15);
}

/// <summary>Counts SLA time. Everything is UTC outside and the organisation's local time inside.</summary>
public static class SlaClock
{
    private const int LongestSearchInDays = 3660;

    /// <summary>The instant <paramref name="minutes"/> of SLA time after the start.</summary>
    public static DateTime Add(DateTime startUtc, int minutes, SlaCalendar calendar, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(zone);
        if (calendar.AlwaysOn)
        {
            return startUtc.AddMinutes(minutes);
        }

        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone);
        TimeSpan left = TimeSpan.FromMinutes(minutes);
        for (int offset = 0; offset < LongestSearchInDays; offset++)
        {
            DateOnly day = DateOnly.FromDateTime(local).AddDays(offset);
            if (!calendar.WorkingDays.Contains(day.DayOfWeek))
            {
                continue;
            }

            DateTime open = day.ToDateTime(calendar.DayStart);
            DateTime from = offset == 0 && local > open ? local : open;
            TimeSpan available = day.ToDateTime(calendar.DayEnd) - from;
            if (available <= TimeSpan.Zero)
            {
                continue;
            }

            if (left <= available)
            {
                return ToUtc(from + left, zone);
            }

            left -= available;
        }

        throw new DomainRuleException("SLA_CALENDAR", "The working calendar leaves no time to meet that target.");
    }

    /// <summary>The whole minutes of SLA time between two instants.</summary>
    public static int Between(DateTime fromUtc, DateTime toUtc, SlaCalendar calendar, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(zone);
        if (toUtc <= fromUtc)
        {
            return 0;
        }

        if (calendar.AlwaysOn)
        {
            return (int)(toUtc - fromUtc).TotalMinutes;
        }

        DateTime from = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone);
        DateTime to = TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone);
        TimeSpan total = TimeSpan.Zero;
        for (DateOnly day = DateOnly.FromDateTime(from); day <= DateOnly.FromDateTime(to); day = day.AddDays(1))
        {
            if (calendar.WorkingDays.Contains(day.DayOfWeek))
            {
                DateTime open = day.ToDateTime(calendar.DayStart);
                DateTime close = day.ToDateTime(calendar.DayEnd);
                DateTime start = from > open ? from : open;
                DateTime end = to < close ? to : close;
                if (end > start)
                {
                    total += end - start;
                }
            }
        }

        return (int)total.TotalMinutes;
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        // A time the clocks skip over: the first moment after the change.
        DateTime unspecified = DateTime.SpecifyKind(zone.IsInvalidTime(local) ? local.AddHours(1) : local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }
}

/// <summary>A project's service levels: its calendar, its targets by priority, and the most credits a month can cost.</summary>
public sealed class ProjectSlaPolicy : TenantAggregateRoot
{
    private const int LongestTargetMinutes = 90 * 24 * 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ProjectSlaPolicy()
    {
        WorkingDaysJson = "[]";
        TargetsJson = "[]";
        PausedColumnIdsJson = "[]";
    }

    public Guid ProjectId { get; private set; }

    public bool AlwaysOn { get; private set; }

    public TimeOnly DayStart { get; private set; }

    public TimeOnly DayEnd { get; private set; }

    public string WorkingDaysJson { get; private set; }

    public string TargetsJson { get; private set; }

    /// <summary>Board columns where the ticket is with the client, which stop the resolution clock.</summary>
    public string PausedColumnIdsJson { get; private set; }

    public decimal MonthlyCreditCapPercent { get; private set; }

    public SlaCalendar Calendar => new(AlwaysOn, DayStart, DayEnd, JsonSerializer.Deserialize<List<DayOfWeek>>(WorkingDaysJson, Json) ?? []);

    public IReadOnlyList<SlaTarget> Targets => JsonSerializer.Deserialize<List<SlaTarget>>(TargetsJson, Json) ?? [];

    public IReadOnlyList<Guid> PausedColumnIds => JsonSerializer.Deserialize<List<Guid>>(PausedColumnIdsJson, Json) ?? [];

    public SlaPolicyDetails Details => new(Calendar, Targets, PausedColumnIds, MonthlyCreditCapPercent);

    public static ProjectSlaPolicy For(Guid projectId, SlaPolicyDetails details)
    {
        var policy = new ProjectSlaPolicy { ProjectId = Guard.NotEmpty(projectId) };
        policy.Update(details);
        return policy;
    }

    public void Update(SlaPolicyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        SlaCalendar calendar = details.Calendar ?? throw new DomainRuleException("SLA_CALENDAR", "Say when the clock runs.");
        if (!calendar.AlwaysOn && (calendar.DayEnd <= calendar.DayStart || calendar.WorkingDays is not { Count: > 0 }))
        {
            throw new DomainRuleException("SLA_CALENDAR", "Working hours need a start before their end, and at least one working day.");
        }

        IReadOnlyList<SlaTarget> targets = details.Targets ?? [];
        if (targets.Count == 0 || targets.Select(target => target.Priority).Distinct().Count() != targets.Count || targets.Any(target => !Enum.IsDefined(target.Priority)))
        {
            throw new DomainRuleException("SLA_TARGETS", "Set one target for each priority covered.");
        }

        foreach (SlaTarget target in targets)
        {
            if (target.ResponseMinutes < 1 || target.ResolutionMinutes < target.ResponseMinutes || target.ResolutionMinutes > LongestTargetMinutes)
            {
                throw new DomainRuleException("SLA_TARGETS", $"For {target.Priority}, response comes first, and resolution is within 90 days.");
            }

            if (target.CreditPercent is < 0 or > 100)
            {
                throw new DomainRuleException("SLA_CREDITS", "A credit is between 0 and 100 percent of the monthly fee.");
            }
        }

        if (details.MonthlyCreditCapPercent is < 0 or > 100)
        {
            throw new DomainRuleException("SLA_CREDITS", "The monthly cap is between 0 and 100 percent of the fee.");
        }

        AlwaysOn = calendar.AlwaysOn;
        DayStart = calendar.AlwaysOn ? TimeOnly.MinValue : calendar.DayStart;
        DayEnd = calendar.AlwaysOn ? TimeOnly.MinValue : calendar.DayEnd;
        WorkingDaysJson = JsonSerializer.Serialize(calendar.AlwaysOn ? [] : calendar.WorkingDays.Distinct().ToList(), Json);
        TargetsJson = JsonSerializer.Serialize(targets, Json);
        PausedColumnIdsJson = JsonSerializer.Serialize((details.PausedColumnIds ?? []).Distinct().ToList(), Json);
        MonthlyCreditCapPercent = details.MonthlyCreditCapPercent;
    }
}

/// <summary>
/// The clock on one ticket. It keeps the targets it was opened under, so changing a policy never moves the
/// goalposts on work already in hand. A breach, once recorded, stays: only an exclusion takes it out of credits.
/// </summary>
public sealed class TicketSla : TenantAggregateRoot
{
    private TicketSla()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public WorkPriority Priority { get; private set; }

    public int ResponseMinutes { get; private set; }

    public int ResolutionMinutes { get; private set; }

    public decimal CreditPercent { get; private set; }

    public DateTime OpenedAt { get; private set; }

    public DateTime ResponseDueAt { get; private set; }

    public DateTime ResolutionDueAt { get; private set; }

    public DateTime? RespondedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    /// <summary>With the client since then, so the resolution clock is stopped.</summary>
    public DateTime? PausedSince { get; private set; }

    /// <summary>SLA time spent stopped so far, which moves the resolution target out.</summary>
    public int PausedMinutes { get; private set; }

    public DateTime? ResponseBreachedAt { get; private set; }

    public DateTime? ResolutionBreachedAt { get; private set; }

    /// <summary>Why a breach does not count against the provider, as agreed with the client.</summary>
    public string? ExcludedReason { get; private set; }

    public Guid? ExcludedByEmployeeId { get; private set; }

    public bool IsBreached => ResponseBreachedAt is not null || ResolutionBreachedAt is not null;

    public bool CountsAgainst => IsBreached && ExcludedReason is null;

    /// <summary>When it first went wrong, which is the month its credit belongs to.</summary>
    public DateTime? FirstBreachAt => (ResponseBreachedAt, ResolutionBreachedAt) switch
    {
        ({ } response, { } resolution) => response < resolution ? response : resolution,
        ({ } response, null) => response,
        (null, { } resolution) => resolution,
        _ => null,
    };

    public static TicketSla Open(Guid projectId, Guid taskId, SlaTarget target, DateTime now, SlaCalendar calendar, TimeZoneInfo zone)
    {
        var sla = new TicketSla { ProjectId = Guard.NotEmpty(projectId), TaskId = Guard.NotEmpty(taskId), OpenedAt = now };
        sla.Retarget(target, calendar, zone);
        return sla;
    }

    /// <summary>The priority changed: new targets, counted from when the ticket opened.</summary>
    public void Retarget(SlaTarget target, SlaCalendar calendar, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(target);
        Priority = target.Priority;
        ResponseMinutes = target.ResponseMinutes;
        ResolutionMinutes = target.ResolutionMinutes;
        CreditPercent = target.CreditPercent;
        Recalculate(calendar, zone);
    }

    public void Respond(DateTime now)
    {
        if (RespondedAt is null)
        {
            RespondedAt = now;
            if (now > ResponseDueAt)
            {
                ResponseBreachedAt ??= ResponseDueAt;
            }
        }
    }

    public void Resolve(DateTime now, SlaCalendar calendar, TimeZoneInfo zone)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        Respond(now);
        Resume(now, calendar, zone);
        ResolvedAt = now;
        if (now > ResolutionDueAt)
        {
            ResolutionBreachedAt ??= ResolutionDueAt;
        }
    }

    /// <summary>Back open. The time it spent resolved does not count against the target.</summary>
    public void Reopen(DateTime now, SlaCalendar calendar, TimeZoneInfo zone)
    {
        if (ResolvedAt is { } resolved)
        {
            PausedMinutes += SlaClock.Between(resolved, now, calendar, zone);
            ResolvedAt = null;
            Recalculate(calendar, zone);
        }
    }

    public void Pause(DateTime now) => PausedSince ??= now;

    public void Resume(DateTime now, SlaCalendar calendar, TimeZoneInfo zone)
    {
        if (PausedSince is { } since)
        {
            PausedMinutes += SlaClock.Between(since, now, calendar, zone);
            PausedSince = null;
            Recalculate(calendar, zone);
        }
    }

    /// <summary>Records targets missed by now, and says which were newly missed.</summary>
    public (bool Response, bool Resolution) CheckBreach(DateTime now)
    {
        bool response = ResponseBreachedAt is null && RespondedAt is null && now > ResponseDueAt;
        bool resolution = ResolutionBreachedAt is null && ResolvedAt is null && PausedSince is null && now > ResolutionDueAt;
        if (response)
        {
            ResponseBreachedAt = ResponseDueAt;
        }

        if (resolution)
        {
            ResolutionBreachedAt = ResolutionDueAt;
        }

        return (response, resolution);
    }

    public void Exclude(string reason, Guid? byEmployeeId)
    {
        if (!IsBreached)
        {
            throw new DomainRuleException("SLA_NOT_BREACHED", "Only a breach can be excluded.");
        }

        ExcludedReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500);
        ExcludedByEmployeeId = byEmployeeId;
    }

    public void Reinstate()
    {
        ExcludedReason = null;
        ExcludedByEmployeeId = null;
    }

    private void Recalculate(SlaCalendar calendar, TimeZoneInfo zone)
    {
        ResponseDueAt = SlaClock.Add(OpenedAt, ResponseMinutes, calendar, zone);
        ResolutionDueAt = SlaClock.Add(OpenedAt, ResolutionMinutes + PausedMinutes, calendar, zone);
    }
}

/// <summary>Which task types run on an SLA.</summary>
public static class ServiceTickets
{
    public static bool IsTicket(TaskType type) => type is TaskType.Incident or TaskType.ServiceRequest;
}

public sealed record SlaPriorityLine(WorkPriority Priority, int Opened, int Resolved, int ResponseMeasured, int ResponseMet, int ResolutionMeasured, int ResolutionMet);

public sealed record SlaMonthReport(
    DateOnly Month,
    int Opened,
    int Resolved,
    int ResponseMeasured,
    int ResponseMet,
    int ResolutionMeasured,
    int ResolutionMet,
    int Breaches,
    int Excluded,
    decimal CreditPercent,
    IReadOnlyList<SlaPriorityLine> Priorities)
{
    public decimal? ResponseMetPercent => ResponseMeasured == 0 ? null : Math.Round(100m * ResponseMet / ResponseMeasured, 1);

    public decimal? ResolutionMetPercent => ResolutionMeasured == 0 ? null : Math.Round(100m * ResolutionMet / ResolutionMeasured, 1);
}

public sealed record SlaCreditMonth(DateOnly Month, decimal Percent, int Breaches);

/// <summary>A month's service levels, and what they cost. Pure, so the report and the invoice cannot disagree.</summary>
public static class SlaReporting
{
    private static DateOnly MonthOf(DateTime utc, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return new DateOnly(local.Year, local.Month, 1);
    }

    /// <summary>
    /// A ticket earns one credit however many of its targets it missed, in the month it first went wrong.
    /// Excluded tickets earn none. The cap limits a month's total.
    /// </summary>
    public static IReadOnlyList<SlaCreditMonth> Credits(IEnumerable<TicketSla> tickets, TimeZoneInfo zone, decimal capPercent)
        => [.. tickets
            .Where(ticket => ticket.CountsAgainst)
            .GroupBy(ticket => MonthOf(ticket.FirstBreachAt!.Value, zone))
            .OrderBy(group => group.Key)
            .Select(group => new SlaCreditMonth(group.Key, Math.Min(group.Sum(ticket => ticket.CreditPercent), capPercent), group.Count()))];

    public static SlaMonthReport Month(IReadOnlyList<TicketSla> tickets, DateOnly month, TimeZoneInfo zone, decimal capPercent)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        var first = new DateOnly(month.Year, month.Month, 1);
        bool In(DateTime? utc) => utc is { } value && MonthOf(value, zone) == first;

        // Measured when the outcome is known in the month: answered or missed, resolved or missed.
        bool ResponseIn(TicketSla ticket) => In(ticket.RespondedAt ?? ticket.ResponseBreachedAt);
        bool ResolutionIn(TicketSla ticket) => In(ticket.ResolvedAt ?? ticket.ResolutionBreachedAt);
        bool ResponseOk(TicketSla ticket) => ticket.ResponseBreachedAt is null || ticket.ExcludedReason is not null;
        bool ResolutionOk(TicketSla ticket) => ticket.ResolutionBreachedAt is null || ticket.ExcludedReason is not null;

        SlaCreditMonth? credit = Credits(tickets, zone, capPercent).FirstOrDefault(row => row.Month == first);
        return new SlaMonthReport(
            first,
            tickets.Count(ticket => In(ticket.OpenedAt)),
            tickets.Count(ticket => In(ticket.ResolvedAt)),
            tickets.Count(ResponseIn),
            tickets.Count(ticket => ResponseIn(ticket) && ResponseOk(ticket)),
            tickets.Count(ResolutionIn),
            tickets.Count(ticket => ResolutionIn(ticket) && ResolutionOk(ticket)),
            credit?.Breaches ?? 0,
            tickets.Count(ticket => ticket.IsBreached && !ticket.CountsAgainst && In(ticket.FirstBreachAt)),
            credit?.Percent ?? 0,
            [.. tickets
                .Where(ticket => In(ticket.OpenedAt) || ResponseIn(ticket) || ResolutionIn(ticket))
                .GroupBy(ticket => ticket.Priority)
                .OrderByDescending(group => group.Key)
                .Select(group => new SlaPriorityLine(
                    group.Key,
                    group.Count(ticket => In(ticket.OpenedAt)),
                    group.Count(ticket => In(ticket.ResolvedAt)),
                    group.Count(ResponseIn),
                    group.Count(ticket => ResponseIn(ticket) && ResponseOk(ticket)),
                    group.Count(ResolutionIn),
                    group.Count(ticket => ResolutionIn(ticket) && ResolutionOk(ticket))))]);
    }
}

/// <summary>One project's month, for a report per client.</summary>
public sealed record ServiceLevelRow(Guid ProjectId, string Code, string Name, Guid? ClientId, string? ClientName, SlaMonthReport Report);

/// <summary>The monthly report as a spreadsheet to send on.</summary>
public static class ServiceLevelCsv
{
    public static string Write(IEnumerable<ServiceLevelRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var csv = new StringBuilder("Client,Project,Month,Opened,Resolved,Response met %,Resolution met %,Breaches,Excluded,Service credit %\r\n");
        foreach (ServiceLevelRow row in rows)
        {
            SlaMonthReport report = row.Report;
            csv.AppendJoin(
                ',',
                Field(row.ClientName ?? string.Empty),
                Field($"{row.Code} {row.Name}"),
                report.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                report.Opened.ToString(CultureInfo.InvariantCulture),
                report.Resolved.ToString(CultureInfo.InvariantCulture),
                report.ResponseMetPercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                report.ResolutionMetPercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                report.Breaches.ToString(CultureInfo.InvariantCulture),
                report.Excluded.ToString(CultureInfo.InvariantCulture),
                report.CreditPercent.ToString("0.##", CultureInfo.InvariantCulture));
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>Quotes a field, and defuses anything a spreadsheet would run as a formula.</summary>
    private static string Field(string value)
    {
        string safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }
}
