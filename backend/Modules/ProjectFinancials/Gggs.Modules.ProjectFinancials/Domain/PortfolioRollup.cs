namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>What the rollup needs of one project.</summary>
public sealed record PortfolioProject(
    Guid ProjectId,
    string Code,
    string Name,
    string Currency,
    string BillingType,
    string? Client,
    string? Department,
    string Health,
    decimal Revenue,
    decimal Cost,
    decimal ForecastRevenue,
    decimal Billed,
    decimal Unbilled,
    DateOnly EndDate);

public sealed record RollupRow(string Label, int Projects, decimal Revenue, decimal Cost, decimal Margin, decimal? MarginPercent, decimal ForecastRevenue, decimal Unbilled);

public sealed record ForecastMonth(DateOnly Month, decimal Revenue);

public sealed record WatchProject(Guid ProjectId, string Code, string Name, string Health, decimal Margin, decimal? MarginPercent, decimal Unbilled);

/// <summary>One currency's portfolio. Money in different currencies is never added together: the app holds no exchange rates.</summary>
public sealed record CurrencyPortfolio(
    string Currency,
    RollupRow Totals,
    IReadOnlyList<RollupRow> ByModel,
    IReadOnlyList<RollupRow> ByClient,
    IReadOnlyList<RollupRow> ByDepartment,
    IReadOnlyList<ForecastMonth> Forecast,
    decimal ForecastLater,
    IReadOnlyList<WatchProject> Watch);

public sealed record HealthCount(int Green, int Amber, int Red, int NotAssessed);

public sealed record PortfolioDashboard(HealthCount Health, IReadOnlyList<CurrencyPortfolio> Currencies);

/// <summary>Margin by model, client and department, delivery health, and revenue still to come. Pure, so its sums can be checked by hand.</summary>
public static class PortfolioRollup
{
    public const int ForecastMonths = 6;

    public static PortfolioDashboard Build(IReadOnlyList<PortfolioProject> projects, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(projects);
        return new PortfolioDashboard(
            new HealthCount(
                projects.Count(project => project.Health == "Green"),
                projects.Count(project => project.Health == "Amber"),
                projects.Count(project => project.Health == "Red"),
                projects.Count(project => project.Health is not ("Green" or "Amber" or "Red"))),
            [.. projects.GroupBy(project => project.Currency).OrderByDescending(group => group.Sum(project => project.Revenue)).Select(group => ForCurrency(group.Key, [.. group], today))]);
    }

    /// <summary>
    /// What is left to earn, spread evenly over the months each project has left. A straight line and no more:
    /// it is a guide to the shape of what is coming, not a commitment.
    /// </summary>
    public static (IReadOnlyList<ForecastMonth> Months, decimal Later) Forecast(IReadOnlyList<PortfolioProject> projects, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var first = new DateOnly(today.Year, today.Month, 1);
        var months = new decimal[ForecastMonths];
        decimal later = 0;
        foreach (PortfolioProject project in projects)
        {
            decimal left = Math.Max(0, project.ForecastRevenue - project.Revenue);
            int remaining = Math.Max(1, ((project.EndDate.Year - first.Year) * 12) + project.EndDate.Month - first.Month + 1);
            decimal share = left / remaining;
            for (int index = 0; index < remaining; index++)
            {
                if (index < ForecastMonths)
                {
                    months[index] += share;
                }
                else
                {
                    later += share;
                }
            }
        }

        return ([.. months.Select((amount, index) => new ForecastMonth(first.AddMonths(index), Math.Round(amount, 2)))], Math.Round(later, 2));
    }

    private static CurrencyPortfolio ForCurrency(string currency, IReadOnlyList<PortfolioProject> projects, DateOnly today)
    {
        (IReadOnlyList<ForecastMonth> months, decimal later) = Forecast(projects, today);
        return new CurrencyPortfolio(
            currency,
            Row("All projects", projects),
            GroupedBy(projects, project => project.BillingType),
            GroupedBy(projects, project => project.Client ?? "No client"),
            GroupedBy(projects, project => project.Department ?? "No department"),
            months,
            later,
            [.. projects
                .Where(project => project.Health == "Red" || project.Revenue - project.Cost < 0)
                .OrderBy(project => project.Revenue - project.Cost)
                .Take(10)
                .Select(project => new WatchProject(project.ProjectId, project.Code, project.Name, project.Health, Math.Round(project.Revenue - project.Cost, 2), Percent(project.Revenue - project.Cost, project.Revenue), project.Unbilled))]);
    }

    private static List<RollupRow> GroupedBy(IReadOnlyList<PortfolioProject> projects, Func<PortfolioProject, string> key)
        => [.. projects.GroupBy(key).Select(group => Row(group.Key, [.. group])).OrderByDescending(row => row.Revenue).ThenBy(row => row.Label, StringComparer.CurrentCulture)];

    private static RollupRow Row(string label, IReadOnlyList<PortfolioProject> projects)
    {
        decimal revenue = projects.Sum(project => project.Revenue);
        decimal cost = projects.Sum(project => project.Cost);
        return new RollupRow(
            label, projects.Count, Math.Round(revenue, 2), Math.Round(cost, 2), Math.Round(revenue - cost, 2), Percent(revenue - cost, revenue),
            Math.Round(projects.Sum(project => project.ForecastRevenue), 2), Math.Round(projects.Sum(project => project.Unbilled), 2));
    }

    private static decimal? Percent(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part * 100 / whole, 1);
}
