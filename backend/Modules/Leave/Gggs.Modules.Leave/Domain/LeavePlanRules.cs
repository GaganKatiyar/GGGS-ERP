using System.Globalization;

namespace Gggs.Modules.Leave.Domain;

/// <summary>
/// A policy's leave year: January to December, April to March, or any other twelve months. The key
/// names the year in ledger period keys, and a January year keeps the plain calendar year so ledgers
/// written before policies had their own years still match.
/// </summary>
public readonly record struct LeaveYear(DateOnly Start, DateOnly End, string Key)
{
    public static LeaveYear For(int startMonth, DateOnly day)
    {
        int startYear = day.Month >= startMonth ? day.Year : day.Year - 1;
        var start = new DateOnly(startYear, startMonth, 1);
        string key = startMonth == 1 ? startYear.ToString(CultureInfo.InvariantCulture) : "FY" + startYear.ToString(CultureInfo.InvariantCulture);
        return new LeaveYear(start, start.AddYears(1).AddDays(-1), key);
    }

    /// <summary>"Jan–Dec", "Apr–Mar".</summary>
    public static string Label(int startMonth)
    {
        string first = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(startMonth);
        string last = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(startMonth == 1 ? 12 : startMonth - 1);
        return $"{first}–{last}";
    }

    /// <summary>Whole months from <paramref name="from"/>'s month to this year's end, counting both: what a mid-year joiner is due.</summary>
    public int MonthsFrom(DateOnly from)
    {
        DateOnly first = from < Start ? Start : from;
        return Math.Max(0, ((End.Year - first.Year) * 12) + End.Month - first.Month + 1);
    }
}

/// <summary>Why an employee follows the policy they do.</summary>
public enum PlanSource
{
    /// <summary>The policy assigned to their engagement type.</summary>
    EngagementType = 1,

    /// <summary>A policy HR gave this employee on their own, for a time.</summary>
    Exception = 2,
}

public sealed record PlanChoice(Guid PlanId, PlanSource Source);

/// <summary>Which policy applies to an employee on a day: their own exception first, then their engagement type's.</summary>
public static class LeavePlanResolver
{
    public static PlanChoice? Resolve(
        IEnumerable<EmployeeLeavePlan> employeeExceptions,
        IReadOnlyDictionary<string, Guid> assignments,
        string? employmentTypeCode,
        DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(employeeExceptions);
        ArgumentNullException.ThrowIfNull(assignments);

        EmployeeLeavePlan? exception = employeeExceptions
            .Where(candidate => candidate.IsActive && candidate.Covers(day))
            .OrderByDescending(candidate => candidate.EffectiveFrom)
            .FirstOrDefault();
        if (exception is not null)
        {
            return new PlanChoice(exception.PlanId, PlanSource.Exception);
        }

        return !string.IsNullOrWhiteSpace(employmentTypeCode) && assignments.TryGetValue(employmentTypeCode.Trim().ToUpperInvariant(), out Guid planId)
            ? new PlanChoice(planId, PlanSource.EngagementType)
            : null;
    }
}

/// <summary>What the eligibility rules need to know about the person applying.</summary>
public sealed record LeaveApplicant(string? GenderCode, DateOnly? JoiningDate, DateOnly? ProbationEndDate, DateOnly? ConfirmationDate, bool OnNotice);

/// <summary>What the request rules need to know about the request itself.</summary>
public sealed record LeaveAsk(DateOnly StartDate, DateOnly EndDate, decimal Days, DateOnly Today, bool OnBehalf, int RequestsThisLeaveYear, int OccurrencesSoFar = 0);

/// <summary>
/// The policy's "who can take it" and "requests" rules, as plain-language problems. Balance,
/// overlaps and other modules' rules are checked by the caller; HR applying on someone's behalf is
/// held to eligibility but not to notice, backdating or advance limits.
/// </summary>
public static class LeaveEligibility
{
    public static IReadOnlyList<string> Check(string typeName, PlanItemRules rules, LeaveApplicant person, LeaveAsk ask)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(ask);
        var problems = new List<string>();

        if (rules.EligibleGenderCodes.Count > 0
            && (person.GenderCode is null || !rules.EligibleGenderCodes.Contains(person.GenderCode, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"{typeName} is not available to you under your leave policy.");
        }

        if (rules.MinServiceMonths > 0 && (person.JoiningDate is not { } joined || joined.AddMonths(rules.MinServiceMonths) > ask.StartDate))
        {
            problems.Add($"{typeName} can be taken after {rules.MinServiceMonths} month(s) of service.");
        }

        bool onProbation = person.ConfirmationDate is null && person.ProbationEndDate is { } probationEnd && probationEnd >= ask.StartDate;
        if (onProbation && !rules.AllowDuringProbation)
        {
            problems.Add($"{typeName} cannot be taken during probation.");
        }

        if (person.OnNotice && !rules.AllowDuringNotice)
        {
            problems.Add($"{typeName} cannot be taken during the notice period.");
        }

        if (rules.MinDaysPerRequest is { } shortest && ask.Days < shortest)
        {
            problems.Add($"{typeName} is taken for at least {Days(shortest)} day(s) at a time.");
        }

        if (rules.MaxConsecutiveDays is { } longest && ask.Days > longest)
        {
            problems.Add($"{typeName} can be taken for at most {longest} day(s) at a time.");
        }

        if (rules.GrantPerOccurrence is { } grant && ask.Days > grant)
        {
            problems.Add($"{typeName} is granted for at most {Days(grant)} day(s) each time.");
        }

        if (rules.MaxOccurrencesInEmployment is { } times && ask.OccurrencesSoFar >= times)
        {
            problems.Add($"{typeName} can be taken at most {times} time(s) in your employment.");
        }

        if (rules.MaxRequestsPerYear is { } most && ask.RequestsThisLeaveYear >= most)
        {
            problems.Add($"{typeName} can be requested at most {most} time(s) a leave year.");
        }

        if (!ask.OnBehalf)
        {
            if (rules.MinNoticeDays > 0 && ask.StartDate < ask.Today.AddDays(rules.MinNoticeDays))
            {
                problems.Add($"{typeName} needs {rules.MinNoticeDays} day(s) of notice.");
            }

            if (rules.MaxBackdatedDays is { } back && ask.StartDate < ask.Today.AddDays(-back))
            {
                problems.Add(back == 0 ? $"{typeName} cannot be applied for after the day." : $"{typeName} can be applied for at most {back} day(s) after the day.");
            }

            if (rules.MaxAdvanceDays is { } ahead && ask.StartDate > ask.Today.AddDays(ahead))
            {
                problems.Add($"{typeName} can be applied for at most {ahead} day(s) ahead.");
            }
        }

        return problems;
    }

    private static string Days(decimal days) => days.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>One period's days to credit, with the key that stops it being credited twice.</summary>
public sealed record AccrualCredit(string PeriodKey, DateOnly EntryDate, decimal Days, string Note);

/// <summary>
/// What an accrual run for one month credits under one leave type's rules: the period it falls in
/// (a month, quarter, half or the leave year), on its first day or once it has ended, prorated for a
/// joiner when the policy says so. Monthly crediting at the start with a cut-off of the 15th is the
/// rule Nuts and Bolts always had, so ledgers written before policies keep their keys.
/// </summary>
public static class AccrualSchedule
{
    public static AccrualCredit? For(PlanItemRules rules, int leaveYearStartMonth, DateOnly joined, DateOnly monthStart)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.AnnualEntitlement <= 0 || rules.Accrual == AccrualFrequency.None || rules.IsPerOccurrence)
        {
            return null;
        }

        int periodMonths = rules.Accrual switch
        {
            AccrualFrequency.Monthly => 1,
            AccrualFrequency.Quarterly => 3,
            AccrualFrequency.HalfYearly => 6,
            _ => 12,
        };

        // Crediting at the end pays the period that has just finished; at the start, the one now beginning.
        bool atEnd = rules.CreditTiming == CreditTiming.EndOfPeriod;
        DateOnly day = atEnd ? monthStart.AddDays(-1) : monthStart;
        LeaveYear year = LeaveYear.For(leaveYearStartMonth, day);
        int monthsIn = ((day.Year - year.Start.Year) * 12) + day.Month - year.Start.Month;
        int index = monthsIn / periodMonths;
        DateOnly start = year.Start.AddMonths(index * periodMonths);
        DateOnly end = start.AddMonths(periodMonths).AddDays(-1);
        if (atEnd && end != day)
        {
            return null;
        }

        // The first month a joiner earns: theirs if they joined by the cut-off, else the next.
        int cutoff = Math.Min(rules.JoiningCutoffDay, DateTime.DaysInMonth(joined.Year, joined.Month));
        DateOnly firstMonth = new DateOnly(joined.Year, joined.Month, 1).AddMonths(joined.Day <= cutoff ? 0 : 1);
        DateOnly from = firstMonth > start ? firstMonth : start;
        int earned = from > end ? 0 : ((end.Year - from.Year) * 12) + end.Month - from.Month + 1;
        if (earned <= 0)
        {
            return null;
        }

        decimal share = rules.AnnualEntitlement * periodMonths / 12;
        decimal days = decimal.Round(rules.ProrateOnJoining ? share * earned / periodMonths : share, 2, MidpointRounding.AwayFromZero);

        (string key, string note) = rules.Accrual switch
        {
            AccrualFrequency.Monthly => ($"accrual:{start:yyyy-MM}", $"Accrual {start:yyyy-MM}"),
            AccrualFrequency.Quarterly => ($"accrual:{year.Key}-Q{index + 1}", $"Entitlement {year.Key} Q{index + 1}"),
            AccrualFrequency.HalfYearly => ($"accrual:{year.Key}-H{index + 1}", $"Entitlement {year.Key} H{index + 1}"),
            _ => ($"accrual:{year.Key}", $"Entitlement {year.Key}"),
        };

        return days <= 0 ? null : new AccrualCredit(key, atEnd ? end : from, days, note);
    }
}

/// <summary>What happens to a balance when its leave year closes: paid out, lapsed, and what is carried.</summary>
public sealed record YearEndOutcome(decimal Encashed, decimal Lapsed, decimal Carried);

public static class YearEnd
{
    /// <summary>
    /// The balance above the carry-forward limit is encashed (up to the encashment limit, when the
    /// policy pays out) and the rest of the excess lapses; what is left is carried into the new year.
    /// </summary>
    public static YearEndOutcome Close(PlanItemRules rules, decimal balance)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (balance <= 0 || rules.CarryForwardLimit is not { } limit || balance <= limit)
        {
            return new YearEndOutcome(0, 0, Math.Max(0, balance));
        }

        decimal excess = balance - limit;
        decimal encashed = rules.EncashAtYearEnd ? Math.Min(excess, rules.MaxEncashDays ?? excess) : 0;
        return new YearEndOutcome(encashed, excess - encashed, limit);
    }

    /// <summary>Carried days still unused when they expire: what was carried less what has been taken since.</summary>
    public static decimal Expiring(decimal carried, decimal takenSinceYearStart, decimal currentBalance)
        => Math.Max(0, Math.Min(carried - takenSinceYearStart, currentBalance));
}
