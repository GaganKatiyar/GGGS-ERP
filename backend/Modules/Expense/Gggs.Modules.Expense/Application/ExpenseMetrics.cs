using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Expense.Application;

/// <summary>Expenses for company-wide dashboards: what is waiting, what is owed and where the money goes.</summary>
internal sealed class ExpenseMetrics(ExpenseService expenses) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Experience, MetricAreas.Payroll];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        ExpenseDashboard dashboard = await expenses.DashboardAsync(cancellationToken);
        return
        [
            new Metric("experience.expenses", MetricAreas.Experience, "Expense claims awaiting approval", dashboard.PendingApproval, MetricUnit.Count, $"{dashboard.PendingAmount:N0} in all", HigherIsBetter: false),
            new Metric("payroll.reimbursements", MetricAreas.Payroll, "Reimbursements paid this month", dashboard.PaidThisMonth, MetricUnit.Currency, $"{dashboard.ApprovedUnpaidAmount:N0} approved and unpaid; {dashboard.OpenAdvances:N0} in open travel advances",
                [.. dashboard.ByCategoryThisMonth.Select(pair => new MetricPoint(pair.Key, pair.Value))]),
        ];
    }
}
