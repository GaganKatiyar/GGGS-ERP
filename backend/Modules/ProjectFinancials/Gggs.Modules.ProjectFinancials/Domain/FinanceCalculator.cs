using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Timesheets.Contracts;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>Everything one project's financials are worked out from.</summary>
public sealed record FinanceInput(
    ProjectFinancialFacts Project,
    decimal? ContractValue,
    decimal? RetainerMonthlyFee,
    IReadOnlyDictionary<Guid, decimal> MilestoneAmounts,
    IReadOnlyList<LoggedHours> Hours,
    IReadOnlyList<ProjectRate> Rates,
    IReadOnlyList<ProjectCost> Costs,
    IReadOnlyList<ProjectInvoice> Invoices,
    IReadOnlyList<ProjectBudgetLine> Budget,
    DateOnly Today,
    decimal HoursPerDay = 8);

public sealed record PersonLabour(Guid EmployeeId, decimal Hours, decimal BillableHours, decimal Cost, decimal Revenue, decimal? CostRate, decimal? BillingRate);

public sealed record MonthFinance(DateOnly Month, decimal Hours, decimal LabourCost, decimal OtherCost, decimal Revenue, decimal Billed);

public sealed record CategoryFinance(CostCategory Category, decimal Budget, decimal Actual, decimal Planned);

/// <summary>A project's money (spec §74-77): contract value, revenue, billing, budget, cost, forecast and margin.</summary>
public sealed record FinanceSummary(
    decimal ContractValue,
    decimal Revenue,
    decimal Billed,
    decimal Collected,
    decimal Unbilled,
    decimal Budget,
    decimal LabourCost,
    decimal OtherCost,
    decimal ActualCost,
    decimal ForecastCost,
    decimal ForecastRevenue,
    decimal GrossMargin,
    decimal? MarginPercent,
    decimal ForecastMargin,
    decimal? ForecastMarginPercent,
    decimal CostVariance,
    decimal RevenueVariance,
    decimal? BudgetUsedPercent,
    decimal Hours,
    decimal BillableHours,
    int UnpricedHours,
    IReadOnlyList<PersonLabour> People,
    IReadOnlyList<MonthFinance> Months,
    IReadOnlyList<CategoryFinance> Categories);

/// <summary>
/// Project costing and revenue (spec §74-76). Labour costs the hours booked times the cost rate;
/// revenue follows the billing type - billable hours × billing rate for time and material, the
/// contract value × progress for fixed price, achieved milestones for milestone billing, the monthly
/// fee for a retainer. A rate card line for the person beats their team rate, which beats one for their role.
/// </summary>
public static class FinanceCalculator
{
    public static FinanceSummary Calculate(FinanceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ProjectFinancialFacts project = input.Project;
        DateOnly today = input.Today;

        var people = new Dictionary<Guid, (decimal Hours, decimal Billable, decimal Cost, decimal Revenue, decimal? CostRate, decimal? BillingRate)>();
        var months = new Dictionary<DateOnly, (decimal Hours, decimal Labour, decimal Other, decimal Revenue, decimal Billed)>();
        int unpriced = 0;
        bool timeAndMaterial = project.BillingType == "TimeAndMaterial";

        foreach (LoggedHours row in input.Hours.Where(row => row.Approved))
        {
            (decimal? costRate, decimal? billingRate) = RatesFor(input, row.EmployeeId, row.Date);
            bool billable = row.Category == HourCategory.Billable;
            decimal cost = row.Hours * (costRate ?? 0);
            decimal earned = timeAndMaterial && billable ? row.Hours * (billingRate ?? 0) : 0;
            if (costRate is null)
            {
                unpriced++;
            }

            people[row.EmployeeId] = people.TryGetValue(row.EmployeeId, out var person)
                ? (person.Hours + row.Hours, person.Billable + (billable ? row.Hours : 0), person.Cost + cost, person.Revenue + earned, costRate ?? person.CostRate, billingRate ?? person.BillingRate)
                : (row.Hours, billable ? row.Hours : 0, cost, earned, costRate, billingRate);

            DateOnly month = new(row.Date.Year, row.Date.Month, 1);
            var current = months.GetValueOrDefault(month);
            months[month] = (current.Hours + row.Hours, current.Labour + cost, current.Other, current.Revenue + earned, current.Billed);
        }

        List<ProjectCost> incurred = [.. input.Costs.Where(cost => !cost.Planned && cost.Date <= today)];
        foreach (ProjectCost cost in incurred)
        {
            DateOnly month = new(cost.Date.Year, cost.Date.Month, 1);
            var current = months.GetValueOrDefault(month);
            months[month] = (current.Hours, current.Labour, current.Other + cost.Amount, current.Revenue, current.Billed);
        }

        List<ProjectInvoice> billedInvoices = [.. input.Invoices.Where(invoice => invoice.Status is InvoiceStatus.Sent or InvoiceStatus.Paid)];
        foreach (ProjectInvoice invoice in billedInvoices)
        {
            DateOnly month = new(invoice.InvoiceDate.Year, invoice.InvoiceDate.Month, 1);
            var current = months.GetValueOrDefault(month);
            months[month] = (current.Hours, current.Labour, current.Other, current.Revenue, current.Billed + invoice.Amount);
        }

        decimal labour = people.Values.Sum(person => person.Cost);
        decimal other = incurred.Sum(cost => cost.Amount);
        decimal contract = input.ContractValue ?? project.RevenueTarget ?? project.Budget ?? 0;

        // Revenue by billing type, and the months it falls in where that is meaningful.
        decimal revenue;
        decimal forecastRevenue;
        switch (project.BillingType)
        {
            case "TimeAndMaterial":
                revenue = people.Values.Sum(person => person.Revenue);
                forecastRevenue = revenue + Projection(input, billableOnly: true, useBillingRate: true);
                break;

            case "FixedPrice":
                revenue = Math.Round(contract * Math.Clamp(project.ProgressPercent, 0, 100) / 100, 2);
                forecastRevenue = contract;
                break;

            case "Milestone":
                revenue = 0;
                foreach (MilestoneFact milestone in project.Milestones.Where(milestone => milestone.Status == "Achieved" && input.MilestoneAmounts.ContainsKey(milestone.Id)))
                {
                    decimal amount = input.MilestoneAmounts[milestone.Id];
                    revenue += amount;
                    DateOnly achieved = milestone.ActualDate ?? milestone.PlannedDate;
                    DateOnly month = new(achieved.Year, achieved.Month, 1);
                    var current = months.GetValueOrDefault(month);
                    months[month] = (current.Hours, current.Labour, current.Other, current.Revenue + amount, current.Billed);
                }

                forecastRevenue = project.Milestones.Where(milestone => input.MilestoneAmounts.ContainsKey(milestone.Id)).Sum(milestone => input.MilestoneAmounts[milestone.Id]);
                break;

            case "Retainer":
                decimal fee = input.RetainerMonthlyFee ?? 0;
                DateOnly last = today < project.EndDate ? today : project.EndDate;
                int elapsed = last < project.StartDate ? 0 : MonthsBetween(project.StartDate, last);
                revenue = fee * elapsed;
                forecastRevenue = fee * MonthsBetween(project.StartDate, project.EndDate);
                for (int i = 0; i < elapsed; i++)
                {
                    DateOnly month = new DateOnly(project.StartDate.Year, project.StartDate.Month, 1).AddMonths(i);
                    var current = months.GetValueOrDefault(month);
                    months[month] = (current.Hours, current.Labour, current.Other, current.Revenue + fee, current.Billed);
                }

                break;

            default:
                revenue = 0;
                forecastRevenue = 0;
                break;
        }

        decimal billed = billedInvoices.Sum(invoice => invoice.Amount);
        decimal collected = input.Invoices.Where(invoice => invoice.Status == InvoiceStatus.Paid).Sum(invoice => invoice.Amount);
        decimal budget = input.Budget.Count > 0 ? input.Budget.Sum(line => line.Amount) : project.CostBudget ?? project.Budget ?? 0;
        decimal actual = labour + other;
        decimal planned = input.Costs.Where(cost => cost.Planned || cost.Date > today).Sum(cost => cost.Amount);
        decimal forecastCost = actual + Projection(input, billableOnly: false, useBillingRate: false) + planned;
        decimal margin = revenue - actual;
        decimal forecastMargin = forecastRevenue - forecastCost;

        var categories = Enum.GetValues<CostCategory>().Select(category => new CategoryFinance(
            category,
            input.Budget.Where(line => line.Category == category).Sum(line => line.Amount),
            category == CostCategory.Labour ? labour : incurred.Where(cost => cost.Category == category).Sum(cost => cost.Amount),
            input.Costs.Where(cost => cost.Category == category && (cost.Planned || cost.Date > today)).Sum(cost => cost.Amount))).ToList();

        return new FinanceSummary(
            contract, Math.Round(revenue, 2), billed, collected, Math.Max(0, Math.Round(revenue - billed, 2)), budget, Math.Round(labour, 2), other, Math.Round(actual, 2),
            Math.Round(forecastCost, 2), Math.Round(forecastRevenue, 2), Math.Round(margin, 2), Percent(margin, revenue), Math.Round(forecastMargin, 2),
            Percent(forecastMargin, forecastRevenue), Math.Round(budget - forecastCost, 2), Math.Round(forecastRevenue - contract, 2), Percent(actual, budget),
            people.Values.Sum(person => person.Hours), people.Values.Sum(person => person.Billable), unpriced,
            [.. people.Select(pair => new PersonLabour(pair.Key, pair.Value.Hours, pair.Value.Billable, Math.Round(pair.Value.Cost, 2), Math.Round(pair.Value.Revenue, 2), pair.Value.CostRate, pair.Value.BillingRate))
                .OrderByDescending(person => person.Hours)],
            [.. months.OrderBy(pair => pair.Key).Select(pair => new MonthFinance(pair.Key, pair.Value.Hours, Math.Round(pair.Value.Labour, 2), pair.Value.Other, Math.Round(pair.Value.Revenue, 2), pair.Value.Billed))],
            categories);
    }

    /// <summary>The cost and billing rate of an hour by this person on this day.</summary>
    public static (decimal? CostRate, decimal? BillingRate) RatesFor(FinanceInput input, Guid employeeId, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(input);
        ProjectAllocation? member = input.Project.Team
            .Where(allocation => allocation.EmployeeId == employeeId && allocation.StartDate <= day && (allocation.EndDate is null || allocation.EndDate >= day))
            .MaxBy(allocation => allocation.StartDate)
            ?? input.Project.Team.Where(allocation => allocation.EmployeeId == employeeId).MaxBy(allocation => allocation.StartDate);
        ProjectRate? personal = input.Rates.Where(rate => rate.EmployeeId == employeeId && rate.EffectiveFrom <= day).MaxBy(rate => rate.EffectiveFrom);
        ProjectRate? role = member is null ? null : input.Rates.Where(rate => rate.ProjectRoleId == member.ProjectRoleId && rate.EffectiveFrom <= day).MaxBy(rate => rate.EffectiveFrom);
        return (personal?.CostRate ?? member?.CostRate ?? role?.CostRate, personal?.BillingRate ?? member?.BillingRate ?? role?.BillingRate);
    }

    /// <summary>What the team will cost (or bill) from tomorrow to the project's end, at their allocations, on working days.</summary>
    private static decimal Projection(FinanceInput input, bool billableOnly, bool useBillingRate)
    {
        DateOnly from = input.Today.AddDays(1);
        DateOnly projectEnd = input.Project.EndDate;
        if (projectEnd < from || input.Project.Status is "Completed" or "Cancelled")
        {
            return 0;
        }

        decimal total = 0;
        foreach (ProjectAllocation allocation in input.Project.Team.Where(allocation => allocation.IsActive && (!billableOnly || allocation.Billable)))
        {
            DateOnly start = allocation.StartDate > from ? allocation.StartDate : from;
            DateOnly end = allocation.EndDate is { } until && until < projectEnd ? until : projectEnd;
            if (end < start)
            {
                continue;
            }

            (decimal? cost, decimal? billing) = RatesFor(input, allocation.EmployeeId, start);
            decimal rate = (useBillingRate ? billing : cost) ?? 0;
            total += WorkingDays(start, end) * input.HoursPerDay * allocation.AllocationPercent / 100 * rate;
        }

        return total;
    }

    private static int WorkingDays(DateOnly first, DateOnly last)
    {
        int days = 0;
        for (DateOnly day = first; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days++;
            }
        }

        return days;
    }

    /// <summary>Months started from <paramref name="first"/> to <paramref name="last"/>, both months included.</summary>
    private static int MonthsBetween(DateOnly first, DateOnly last) => last < first ? 0 : ((last.Year - first.Year) * 12) + last.Month - first.Month + 1;

    private static decimal? Percent(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part * 100 / whole, 1);
}
