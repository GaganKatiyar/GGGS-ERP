using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Timesheets.Contracts;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>A line an invoice would carry, and the timesheet entries it would bill.</summary>
public sealed record PlannedLine(
    InvoiceLineKind Kind,
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Amount,
    DateOnly? Month = null,
    Guid? ReferenceId = null,
    IReadOnlyList<Guid>? EntryIds = null);

/// <summary>What can be invoiced for a project in a period, and what cannot, and why.</summary>
public sealed record BillingPlan(IReadOnlyList<PlannedLine> Lines, decimal HeldByCap, decimal UnpricedHours)
{
    public decimal Amount => Lines.Sum(line => line.Amount);

    /// <summary>Service credits owed that this invoice is too small to carry. They wait for the next one.</summary>
    public decimal CreditsWaiting { get; init; }
}

/// <summary>What has already gone on an invoice: entries, and the fees, seats and milestones lines cover.</summary>
public sealed record AlreadyBilled(IReadOnlySet<Guid> Entries, IReadOnlySet<(InvoiceLineKind Kind, DateOnly? Month, Guid? ReferenceId)> Lines, decimal Invoiced)
{
    public static AlreadyBilled Nothing { get; } = new(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)>(), 0);

    /// <summary>Overage invoiced so far for each month, because hours approved late can add to it.</summary>
    public IReadOnlyDictionary<DateOnly, decimal> Overage { get; init; } = new Dictionary<DateOnly, decimal>();

    /// <summary>Service credit already given for each month, as a positive amount.</summary>
    public IReadOnlyDictionary<DateOnly, decimal> ServiceCredits { get; init; } = new Dictionary<DateOnly, decimal>();
}

/// <summary>
/// What each engagement model has earned that has not been invoiced yet. Pure, so the same answer comes back
/// for a preview, for the invoice itself and for the monthly billing run.
/// </summary>
public static class BillingPlanner
{
    public static BillingPlan Plan(FinanceInput input, DateOnly from, DateOnly to, AlreadyBilled billed, Func<Guid, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(billed);
        ArgumentNullException.ThrowIfNull(nameOf);

        return input.Project.BillingType switch
        {
            "TimeAndMaterial" => Hours(input, from, to, billed, nameOf),
            "FixedPrice" => Progress(input, billed),
            "Milestone" => Milestones(input, billed),
            "Retainer" or "ManagedService" => MonthlyFees(input, from, to, billed),
            "DedicatedTeam" => Seats(input, from, to, billed, nameOf),
            "OutcomeBased" => Outcomes(input, to, billed),
            _ => new BillingPlan([], 0, 0),
        };
    }

    /// <summary>Approved billable hours not yet on an invoice, by person and rate. Whole entries go on until the cap is reached; the rest wait.</summary>
    private static BillingPlan Hours(FinanceInput input, DateOnly from, DateOnly to, AlreadyBilled billed, Func<Guid, string> nameOf)
    {
        decimal? room = input.Terms.NotToExceed is { } cap ? Math.Max(0, cap - billed.Invoiced) : null;
        decimal held = 0;
        decimal unpriced = 0;
        var people = new Dictionary<(Guid Employee, decimal Rate), (decimal Hours, List<Guid> Entries)>();

        foreach (LoggedHours row in input.Hours
            .Where(row => row.Approved && row.Category == HourCategory.Billable && row.Date >= from && row.Date <= to && row.EntryId is { } id && !billed.Entries.Contains(id))
            .OrderBy(row => row.Date))
        {
            if (FinanceCalculator.RatesFor(input, row.EmployeeId, row.Date).BillingRate is not { } rate)
            {
                unpriced += row.Hours;
                continue;
            }

            decimal amount = row.Hours * rate;
            if (room is { } left)
            {
                if (amount > left)
                {
                    held += amount;
                    continue;
                }

                room = left - amount;
            }

            (decimal hours, List<Guid> entries) = people.TryGetValue((row.EmployeeId, rate), out var person) ? person : (0m, []);
            entries.Add(row.EntryId!.Value);
            people[(row.EmployeeId, rate)] = (hours + row.Hours, entries);
        }

        return new BillingPlan(
            [.. people.OrderBy(pair => nameOf(pair.Key.Employee), StringComparer.CurrentCulture).Select(pair => new PlannedLine(
                InvoiceLineKind.Hours, $"{nameOf(pair.Key.Employee)}: {pair.Value.Hours:0.##} h at {pair.Key.Rate:0.##}", pair.Value.Hours, pair.Key.Rate, Math.Round(pair.Value.Hours * pair.Key.Rate, 2),
                EntryIds: pair.Value.Entries))],
            held,
            unpriced);
    }

    /// <summary>Accepted units not yet invoiced, up to the end of the period.</summary>
    private static BillingPlan Outcomes(FinanceInput input, DateOnly to, AlreadyBilled billed)
    {
        string unit = input.Terms.UnitName ?? "unit";
        return new(
            [.. input.OutcomeUnits
                .Where(units => units.AcceptedOn <= to && !billed.Lines.Contains((InvoiceLineKind.Outcome, null, units.Id)))
                .OrderBy(units => units.AcceptedOn)
                .Select(units => new PlannedLine(InvoiceLineKind.Outcome, $"{units.Reference}: {units.Quantity:0.##} {unit} at {units.UnitPrice:0.##}", units.Quantity, units.UnitPrice, units.Amount, ReferenceId: units.Id))],
            0,
            0);
    }

    /// <summary>The contract value earned so far by progress, less what has already been invoiced.</summary>
    private static BillingPlan Progress(FinanceInput input, AlreadyBilled billed)
    {
        ProjectFinancialFacts project = input.Project;
        decimal contract = input.ContractValue ?? project.RevenueTarget ?? project.Budget ?? 0;
        decimal percent = Math.Clamp(project.ProgressPercent, 0, 100);
        decimal due = Math.Round((contract * percent / 100) - billed.Invoiced, 2);
        return due <= 0
            ? new BillingPlan([], 0, 0)
            : new BillingPlan([new PlannedLine(InvoiceLineKind.Progress, $"Progress to {percent:0.#}% of the contract", percent, contract, due)], 0, 0);
    }

    /// <summary>Milestones that have earned their amount (achieved and, where required, accepted) and are not yet invoiced.</summary>
    private static BillingPlan Milestones(FinanceInput input, AlreadyBilled billed)
        => new(
            [.. input.Project.Milestones
                .Where(milestone => milestone.Status == "Achieved" && (!milestone.RequiresAcceptance || milestone.AcceptedOn is not null))
                .Where(milestone => input.MilestoneAmounts.TryGetValue(milestone.Id, out decimal amount) && amount > 0 && !billed.Lines.Contains((InvoiceLineKind.Milestone, null, milestone.Id)))
                .Select(milestone => new PlannedLine(InvoiceLineKind.Milestone, milestone.Name, 1, input.MilestoneAmounts[milestone.Id], input.MilestoneAmounts[milestone.Id], ReferenceId: milestone.Id))],
            0,
            0);

    /// <summary>
    /// The monthly fee for each month in the period, which can be billed in advance. Overage only once its
    /// month is over, because until then more hours can still land in it; hours approved after that are billed
    /// as the difference from what the month has already been charged. Service credits for any month that has
    /// ended come off, oldest first, even if the month itself was invoiced long ago; an invoice never goes to
    /// zero or below to carry one, so what does not fit waits.
    /// </summary>
    private static BillingPlan MonthlyFees(FinanceInput input, DateOnly from, DateOnly to, AlreadyBilled billed)
    {
        ProjectFinancialFacts project = input.Project;
        decimal fee = input.RetainerMonthlyFee ?? 0;
        DateOnly last = to < project.EndDate ? to : project.EndDate;
        if (last < project.StartDate)
        {
            return new BillingPlan([], 0, 0);
        }

        Dictionary<DateOnly, decimal> used = input.Hours.Where(row => row.Approved && row.Category == HourCategory.Billable)
            .GroupBy(row => FirstOf(row.Date))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Hours));

        List<PlannedLine> lines = [];
        foreach (RetainerMonth month in CommercialRules.RetainerMonths(project.StartDate, last, fee, input.Terms, used).Where(month => month.Month >= FirstOf(from)))
        {
            if (fee > 0 && !billed.Lines.Contains((InvoiceLineKind.MonthlyFee, month.Month, null)))
            {
                lines.Add(new PlannedLine(InvoiceLineKind.MonthlyFee, $"Monthly fee, {month.Month:MMM yyyy}", 1, fee, fee, month.Month));
            }

            bool over = month.Month.AddMonths(1) <= input.Today;
            decimal overage = month.OverageAmount - billed.Overage.GetValueOrDefault(month.Month);
            if (over && overage > 0)
            {
                decimal rate = input.Terms.OverageRate ?? 0;
                decimal hours = rate > 0 ? Math.Round(overage / rate, 2) : month.OverageHours;
                lines.Add(new PlannedLine(InvoiceLineKind.Overage, $"Hours over the bucket, {month.Month:MMM yyyy}: {hours:0.##} h", hours, rate, overage, month.Month));
            }
        }

        decimal total = lines.Sum(line => line.Amount);
        decimal waiting = 0;
        foreach ((DateOnly month, decimal percent) in input.ServiceCredits.OrderBy(pair => pair.Key))
        {
            decimal due = Math.Round(fee * percent / 100, 2) - billed.ServiceCredits.GetValueOrDefault(month);
            if (due <= 0 || month.AddMonths(1) > input.Today || month > FirstOf(last))
            {
                continue;
            }

            if (due >= total)
            {
                waiting += due;
                continue;
            }

            total -= due;
            lines.Add(new PlannedLine(InvoiceLineKind.ServiceCredit, $"Service credit, {month:MMM yyyy}: {percent:0.##}% of the fee", percent, fee, -due, month));
        }

        return new BillingPlan(lines, 0, 0) { CreditsWaiting = waiting };
    }

    /// <summary>Each seat's share of its monthly rate. A month is billed whole, up to the seat's end, so nothing is left to bill twice.</summary>
    private static BillingPlan Seats(FinanceInput input, DateOnly from, DateOnly to, AlreadyBilled billed, Func<Guid, string> nameOf)
    {
        ProjectFinancialFacts project = input.Project;
        DateOnly monthEnd = FirstOf(to).AddMonths(1).AddDays(-1);
        DateOnly last = monthEnd < project.EndDate ? monthEnd : project.EndDate;

        List<PlannedLine> lines = [];
        foreach (ProjectAllocation seat in project.Team.OrderBy(seat => nameOf(seat.EmployeeId), StringComparer.CurrentCulture))
        {
            foreach ((DateOnly month, decimal amount) in CommercialRules.SeatRevenueByMonth(seat, project.StartDate, last).OrderBy(pair => pair.Key))
            {
                if (month >= FirstOf(from) && amount > 0 && !billed.Lines.Contains((InvoiceLineKind.Seat, month, seat.MemberId)))
                {
                    lines.Add(new PlannedLine(InvoiceLineKind.Seat, $"{nameOf(seat.EmployeeId)}, {month:MMM yyyy}", 1, seat.MonthlyRate ?? 0, amount, month, seat.MemberId));
                }
            }
        }

        return new BillingPlan(lines, 0, 0);
    }

    private static DateOnly FirstOf(DateOnly day) => new(day.Year, day.Month, 1);
}
