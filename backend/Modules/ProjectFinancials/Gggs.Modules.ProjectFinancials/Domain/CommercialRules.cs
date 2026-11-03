using Gggs.Modules.Project.Contracts;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>What an engagement's contract says beyond its value: a T&amp;M cap and PO, and a monthly bucket of hours.</summary>
public sealed record CommercialTerms(
    decimal? NotToExceed = null,
    string? PoNumber = null,
    decimal? PoValue = null,
    decimal? IncludedHoursPerMonth = null,
    int RolloverPercent = 0,
    decimal? OverageRate = null);

/// <summary>One month of a retainer or managed service: what was included, what was used, and what ran over.</summary>
public sealed record RetainerMonth(
    DateOnly Month,
    decimal Fee,
    decimal IncludedHours,
    decimal RolledInHours,
    decimal UsedHours,
    decimal OverageHours,
    decimal OverageAmount,
    decimal RollsForwardHours)
{
    public decimal Revenue => Fee + OverageAmount;
}

/// <summary>The commercial rules that differ by engagement model, separate from the calculator so each can be checked alone.</summary>
public static class CommercialRules
{
    /// <summary>
    /// A monthly fee for a bucket of hours. Hours used take the month's own bucket first, then what rolled in;
    /// beyond both they are overage. Only the month's own unused hours roll forward, and only for one month, so
    /// a quiet quarter cannot build up a reserve. With no bucket there is only the fee.
    /// </summary>
    public static IReadOnlyList<RetainerMonth> RetainerMonths(
        DateOnly firstDay,
        DateOnly lastDay,
        decimal fee,
        CommercialTerms terms,
        IReadOnlyDictionary<DateOnly, decimal> usedHoursByMonth)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(usedHoursByMonth);

        List<RetainerMonth> months = [];
        decimal rolledIn = 0;
        decimal included = terms.IncludedHoursPerMonth ?? 0;
        bool hasBucket = terms.IncludedHoursPerMonth is not null;
        DateOnly last = new(lastDay.Year, lastDay.Month, 1);

        for (DateOnly month = new(firstDay.Year, firstDay.Month, 1); month <= last; month = month.AddMonths(1))
        {
            decimal used = usedHoursByMonth.GetValueOrDefault(month);
            decimal overage = hasBucket ? Math.Max(0, used - included - rolledIn) : 0;
            decimal rolls = hasBucket ? Math.Round(Math.Max(0, included - used) * terms.RolloverPercent / 100, 2) : 0;
            months.Add(new RetainerMonth(month, fee, included, rolledIn, used, overage, Math.Round(overage * (terms.OverageRate ?? 0), 2), rolls));
            rolledIn = rolls;
        }

        return months;
    }

    /// <summary>
    /// What a seat earns in the window at its monthly rate, month by month. A part month earns its share of
    /// that month's calendar days, so a replacement mid-month splits the fee rather than doubling it.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, decimal> SeatRevenueByMonth(ProjectAllocation seat, DateOnly firstDay, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var revenue = new Dictionary<DateOnly, decimal>();
        if (seat.MonthlyRate is not { } rate || !seat.Billable)
        {
            return revenue;
        }

        DateOnly start = seat.StartDate > firstDay ? seat.StartDate : firstDay;
        DateOnly end = seat.EndDate is { } until && until < lastDay ? until : lastDay;
        for (DateOnly month = new(start.Year, start.Month, 1); start <= end && month <= end; month = month.AddMonths(1))
        {
            DateOnly monthEnd = month.AddMonths(1).AddDays(-1);
            DateOnly from = start > month ? start : month;
            DateOnly to = end < monthEnd ? end : monthEnd;
            int covered = to.DayNumber - from.DayNumber + 1;
            revenue[month] = Math.Round(rate * covered / (monthEnd.DayNumber - month.DayNumber + 1), 2);
        }

        return revenue;
    }

    /// <summary>Holds revenue to a T&amp;M cap, trimming the months that went over it in order, so the monthly view still adds up.</summary>
    public static IReadOnlyDictionary<DateOnly, decimal> CapMonths(IReadOnlyDictionary<DateOnly, decimal> revenueByMonth, decimal cap)
    {
        ArgumentNullException.ThrowIfNull(revenueByMonth);
        var capped = new Dictionary<DateOnly, decimal>();
        decimal left = cap;
        foreach ((DateOnly month, decimal revenue) in revenueByMonth.OrderBy(pair => pair.Key))
        {
            decimal counted = Math.Min(revenue, Math.Max(0, left));
            capped[month] = counted;
            left -= counted;
        }

        return capped;
    }

    /// <summary>The highest of the cap alert levels reached.</summary>
    public static int CapThreshold(decimal? usedPercent) => usedPercent switch
    {
        >= 100 => 100,
        >= 90 => 90,
        >= 75 => 75,
        _ => 0,
    };
}
