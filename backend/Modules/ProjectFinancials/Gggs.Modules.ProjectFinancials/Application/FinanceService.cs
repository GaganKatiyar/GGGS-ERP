using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Contract.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.ProjectFinancials.Infrastructure;
using Gggs.Modules.Timesheets.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.ProjectFinancials.Application;

public sealed record ProjectFinanceRow(
    Guid ProjectId,
    string Code,
    string Name,
    string BillingType,
    string Status,
    string Currency,
    decimal ContractValue,
    decimal Revenue,
    decimal ActualCost,
    decimal Budget,
    decimal GrossMargin,
    decimal? MarginPercent,
    decimal? BudgetUsedPercent,
    decimal Billed,
    decimal Unbilled,
    decimal ForecastMargin,
    decimal? ForecastMarginPercent);

public sealed record PortfolioFinance(
    decimal ContractValue,
    decimal Revenue,
    decimal ActualCost,
    decimal GrossMargin,
    decimal? MarginPercent,
    decimal Billed,
    decimal Unbilled,
    decimal ForecastMargin,
    int OverBudget,
    int LossMaking,
    IReadOnlyList<ProjectFinanceRow> Projects);

public sealed record RateItem(Guid Id, Guid? ProjectRoleId, string? RoleName, Guid? EmployeeId, string? EmployeeName, decimal? BillingRate, decimal? CostRate, DateOnly EffectiveFrom);

public sealed record RateBody(Guid? ProjectRoleId, Guid? EmployeeId, decimal? BillingRate, decimal? CostRate, DateOnly EffectiveFrom);

public sealed record CostItem(Guid Id, CostCategory Category, DateOnly Date, decimal Amount, string? Vendor, string Description, string? Reference, bool Planned);

public sealed record InvoiceItem(
    Guid Id,
    string Number,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    decimal Amount,
    decimal TaxAmount,
    InvoiceStatus Status,
    DateOnly? PaidOn,
    Guid? MilestoneId,
    string? MilestoneName,
    string? Notes,
    bool IsOverdue);

public sealed record InvoiceBody(string? Number, InvoiceDetails Details);

public sealed record PaidOnBody(DateOnly PaidOn);

public sealed record BudgetLineItem(CostCategory Category, decimal Amount, string? Notes);

public sealed record BudgetBody(IReadOnlyList<BudgetLineItem> Lines);

public sealed record MilestoneBilling(Guid MilestoneId, string Name, DateOnly PlannedDate, DateOnly? ActualDate, string Status, bool IsBillable, decimal? Amount);

public sealed record ProfileBody(decimal? ContractValue, decimal? RetainerMonthlyFee, IReadOnlyDictionary<Guid, decimal>? MilestoneAmounts);

public sealed record TrendItem(DateOnly Month, decimal Revenue, decimal Cost, decimal Billed, decimal Collected, decimal ForecastRevenue, decimal ForecastCost);

public sealed record PersonLabourItem(Guid EmployeeId, string? EmployeeName, decimal Hours, decimal BillableHours, decimal Cost, decimal Revenue, decimal? CostRate, decimal? BillingRate);

/// <summary>One project's financial dashboard (spec §77) with everything behind it.</summary>
public sealed record ProjectFinance(
    Guid ProjectId,
    string Code,
    string Name,
    string BillingType,
    string Status,
    string Currency,
    decimal ProgressPercent,
    decimal? ContractValueFromContract,
    FinanceSummary Summary,
    IReadOnlyList<PersonLabourItem> People,
    IReadOnlyList<BudgetLineItem> Budget,
    IReadOnlyList<RateItem> Rates,
    IReadOnlyList<CostItem> Costs,
    IReadOnlyList<InvoiceItem> Invoices,
    decimal? ContractValueOverride,
    decimal? RetainerMonthlyFee,
    IReadOnlyList<MilestoneBilling> Milestones,
    IReadOnlyList<TrendItem> Trend,
    bool CanManage);

/// <summary>
/// Project financials (spec §74-77, §125): budget by category, rate cards, non-labour costs, invoices,
/// and the cost, revenue and margin they add up to - per project and across the portfolio.
/// </summary>
internal sealed class FinanceService(
    AppDbContext dbContext,
    IProjectDirectory projects,
    IProjectPermissions projectPermissions,
    IContractDirectory contracts,
    ITimesheetLedger timesheets,
    IEmployeeDirectory employees,
    INotifier notifier,
    ITenantClock clock)
{
    public async Task<PortfolioFinance> PortfolioAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? visible = await projectPermissions.VisibleIdsAsync(Permissions.Project.FinancialsView, cancellationToken);
        IReadOnlyList<ProjectFinancialFacts> facts = await projects.FinancialFactsAsync(visible, cancellationToken);
        facts = [.. facts.Where(fact => fact.Status != "Cancelled")];
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyDictionary<Guid, FinanceSummary> summaries = await SummariesAsync(facts, today, cancellationToken);

        List<ProjectFinanceRow> rows = [.. facts.Select(fact =>
        {
            FinanceSummary summary = summaries[fact.Id];
            return new ProjectFinanceRow(
                fact.Id, fact.Code, fact.Name, fact.BillingType, fact.Status, fact.Currency, summary.ContractValue, summary.Revenue, summary.ActualCost, summary.Budget,
                summary.GrossMargin, summary.MarginPercent, summary.BudgetUsedPercent, summary.Billed, summary.Unbilled, summary.ForecastMargin, summary.ForecastMarginPercent);
        }).OrderBy(row => row.MarginPercent ?? decimal.MaxValue)];

        decimal revenue = rows.Sum(row => row.Revenue);
        decimal margin = rows.Sum(row => row.GrossMargin);
        return new PortfolioFinance(
            rows.Sum(row => row.ContractValue), revenue, rows.Sum(row => row.ActualCost), margin, revenue == 0 ? null : Math.Round(margin * 100 / revenue, 1),
            rows.Sum(row => row.Billed), rows.Sum(row => row.Unbilled), rows.Sum(row => row.ForecastMargin),
            rows.Count(row => row.BudgetUsedPercent > 100), rows.Count(row => row.GrossMargin < 0 && row.Revenue > 0), rows);
    }

    public async Task<ProjectFinance> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsView, cancellationToken);
        ProjectFinancialFacts fact = await projects.FinancialFactsAsync([projectId], cancellationToken) is [var found, ..] ? found : throw new NotFoundException("Project", projectId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        FinanceSummary summary = (await SummariesAsync([fact], today, cancellationToken))[projectId];

        ProjectFinanceProfile? profile = await dbContext.ProjectFinanceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);
        decimal? fromContract = fact.ContractId is { } contractId ? (await contracts.GetManyAsync([contractId], cancellationToken)).GetValueOrDefault(contractId)?.Value : null;
        List<ProjectRate> rates = await dbContext.ProjectRates.AsNoTracking().Where(row => row.ProjectId == projectId).OrderBy(row => row.EffectiveFrom).ToListAsync(cancellationToken);
        Dictionary<Guid, string> roleNames = fact.Team.GroupBy(member => member.ProjectRoleId).ToDictionary(group => group.Key, group => group.First().RoleName);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(rates.Select(rate => rate.EmployeeId).OfType<Guid>().Concat(summary.People.Select(person => person.EmployeeId)), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        List<ProjectInvoice> invoices = await dbContext.ProjectInvoices.AsNoTracking().Where(row => row.ProjectId == projectId).OrderByDescending(row => row.InvoiceDate).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, decimal> milestoneAmounts = profile?.MilestoneAmounts ?? new Dictionary<Guid, decimal>();

        return new ProjectFinance(
            fact.Id, fact.Code, fact.Name, fact.BillingType, fact.Status, fact.Currency, fact.ProgressPercent, fromContract, summary,
            [.. summary.People.Select(person => new PersonLabourItem(person.EmployeeId, names.GetValueOrDefault(person.EmployeeId), person.Hours, person.BillableHours, person.Cost, person.Revenue, person.CostRate, person.BillingRate))],
            [.. (await dbContext.ProjectBudgetLines.AsNoTracking().Where(row => row.ProjectId == projectId).OrderBy(row => row.Category).ToListAsync(cancellationToken)).Select(row => new BudgetLineItem(row.Category, row.Amount, row.Notes))],
            [.. rates.Select(rate => new RateItem(
                rate.Id, rate.ProjectRoleId, rate.ProjectRoleId is { } role ? roleNames.GetValueOrDefault(role) : null, rate.EmployeeId,
                rate.EmployeeId is { } person ? names.GetValueOrDefault(person) : null, rate.BillingRate, rate.CostRate, rate.EffectiveFrom))],
            [.. (await dbContext.ProjectCosts.AsNoTracking().Where(row => row.ProjectId == projectId).OrderByDescending(row => row.Date).ToListAsync(cancellationToken)).Select(Cost)],
            [.. invoices.Select(invoice => Invoice(invoice, fact, today))],
            profile?.ContractValue, profile?.RetainerMonthlyFee,
            [.. fact.Milestones.Select(milestone => new MilestoneBilling(
                milestone.Id, milestone.Name, milestone.PlannedDate, milestone.ActualDate, milestone.Status, milestone.IsBillable, milestoneAmounts.TryGetValue(milestone.Id, out decimal amount) ? amount : null))],
            [.. (await dbContext.ProjectFinancialSnapshots.AsNoTracking().Where(row => row.ProjectId == projectId).OrderBy(row => row.Month).ToListAsync(cancellationToken))
                .Select(row => new TrendItem(row.Month, row.Revenue, row.Cost, row.Billed, row.Collected, row.ForecastRevenue, row.ForecastCost))],
            await projectPermissions.CanAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken));
    }

    // ----- Budget, profile, rates, costs, invoices ----------------------------------------------

    public async Task<ProjectFinance> SaveBudgetAsync(Guid projectId, BudgetBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        List<ProjectBudgetLine> existing = await dbContext.ProjectBudgetLines.Where(row => row.ProjectId == projectId).ToListAsync(cancellationToken);
        foreach (IGrouping<CostCategory, BudgetLineItem> line in (body.Lines ?? []).GroupBy(line => line.Category))
        {
            BudgetLineItem item = line.Last();
            ProjectBudgetLine? current = existing.Find(row => row.Category == line.Key);
            if (current is null)
            {
                dbContext.ProjectBudgetLines.Add(ProjectBudgetLine.For(projectId, line.Key, item.Amount, item.Notes));
            }
            else
            {
                current.Set(item.Amount, item.Notes);
            }
        }

        foreach (ProjectBudgetLine removed in existing.Where(row => (body.Lines ?? []).All(line => line.Category != row.Category)))
        {
            dbContext.ProjectBudgetLines.Remove(removed);
        }

        (await ProfileAsync(projectId, cancellationToken)).ResetAlerts();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ProjectAsync(projectId, cancellationToken);
    }

    public async Task<ProjectFinance> SaveProfileAsync(Guid projectId, ProfileBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectFinanceProfile profile = await ProfileAsync(projectId, cancellationToken);
        profile.Update(body.ContractValue, body.RetainerMonthlyFee, body.MilestoneAmounts);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ProjectAsync(projectId, cancellationToken);
    }

    public async Task<RateItem> SaveRateAsync(Guid projectId, Guid? rateId, RateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectRate rate;
        if (rateId is { } id)
        {
            rate = await dbContext.ProjectRates.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken) ?? throw new NotFoundException(nameof(ProjectRate), id);
            rate.Update(body.ProjectRoleId, body.EmployeeId, body.BillingRate, body.CostRate, body.EffectiveFrom);
        }
        else
        {
            rate = ProjectRate.Create(projectId, body.ProjectRoleId, body.EmployeeId, body.BillingRate, body.CostRate, body.EffectiveFrom);
            dbContext.ProjectRates.Add(rate);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        string? person = rate.EmployeeId is { } employeeId ? (await employees.GetAsync(employeeId, cancellationToken))?.DisplayName : null;
        return new RateItem(rate.Id, rate.ProjectRoleId, null, rate.EmployeeId, person, rate.BillingRate, rate.CostRate, rate.EffectiveFrom);
    }

    public async Task DeleteRateAsync(Guid projectId, Guid rateId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectRate rate = await dbContext.ProjectRates.FirstOrDefaultAsync(row => row.Id == rateId && row.ProjectId == projectId, cancellationToken) ?? throw new NotFoundException(nameof(ProjectRate), rateId);
        dbContext.ProjectRates.Remove(rate);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CostItem> SaveCostAsync(Guid projectId, Guid? costId, CostDetails body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectCost cost;
        if (costId is { } id)
        {
            cost = await dbContext.ProjectCosts.FirstOrDefaultAsync(row => row.Id == id && row.ProjectId == projectId, cancellationToken) ?? throw new NotFoundException(nameof(ProjectCost), id);
            cost.Update(body);
        }
        else
        {
            cost = ProjectCost.Record(projectId, body);
            dbContext.ProjectCosts.Add(cost);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Cost(cost);
    }

    public async Task DeleteCostAsync(Guid projectId, Guid costId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectCost cost = await dbContext.ProjectCosts.FirstOrDefaultAsync(row => row.Id == costId && row.ProjectId == projectId, cancellationToken) ?? throw new NotFoundException(nameof(ProjectCost), costId);
        dbContext.ProjectCosts.Remove(cost);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvoiceItem> SaveInvoiceAsync(Guid projectId, Guid? invoiceId, InvoiceBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice;
        if (invoiceId is { } id)
        {
            invoice = await InvoiceAsync(projectId, id, cancellationToken);
            invoice.Update(body.Details);
        }
        else
        {
            string number = string.IsNullOrWhiteSpace(body.Number) ? await NextNumberAsync(body.Details.InvoiceDate, cancellationToken) : body.Number.Trim();
            if (await dbContext.ProjectInvoices.AnyAsync(row => row.Number == number, cancellationToken))
            {
                throw new DomainRuleException("INVOICE_NUMBER", $"Invoice {number} already exists.");
            }

            invoice = ProjectInvoice.Raise(projectId, number, body.Details);
            dbContext.ProjectInvoices.Add(invoice);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await InvoiceItemAsync(invoice, cancellationToken);
    }

    public async Task<InvoiceItem> SendInvoiceAsync(Guid projectId, Guid invoiceId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice = await InvoiceAsync(projectId, invoiceId, cancellationToken);
        invoice.Send();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await InvoiceItemAsync(invoice, cancellationToken);
    }

    public async Task<InvoiceItem> InvoicePaidAsync(Guid projectId, Guid invoiceId, DateOnly paidOn, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice = await InvoiceAsync(projectId, invoiceId, cancellationToken);
        invoice.MarkPaid(paidOn);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await InvoiceItemAsync(invoice, cancellationToken);
    }

    public async Task<InvoiceItem> CancelInvoiceAsync(Guid projectId, Guid invoiceId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice = await InvoiceAsync(projectId, invoiceId, cancellationToken);
        invoice.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await InvoiceItemAsync(invoice, cancellationToken);
    }

    // ----- Nightly snapshot and budget alerts ---------------------------------------------------

    /// <summary>Records this month's figures for every live project and alerts managers at 90% and 100% of the cost budget. Returns how many projects were recorded.</summary>
    public async Task<int> SnapshotAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var month = new DateOnly(today.Year, today.Month, 1);
        List<ProjectFinancialFacts> facts = [.. (await projects.FinancialFactsAsync(null, cancellationToken)).Where(fact => fact.Status is "Active" or "OnHold" || fact.EndDate >= month.AddMonths(-1))];
        IReadOnlyDictionary<Guid, FinanceSummary> summaries = await SummariesAsync(facts, today, cancellationToken);
        List<Guid> ids = [.. facts.Select(fact => fact.Id)];
        Dictionary<Guid, ProjectFinancialSnapshot> existing = await dbContext.ProjectFinancialSnapshots.Where(row => ids.Contains(row.ProjectId) && row.Month == month).ToDictionaryAsync(row => row.ProjectId, cancellationToken);
        Dictionary<Guid, ProjectFinanceProfile> profiles = await dbContext.ProjectFinanceProfiles.Where(row => ids.Contains(row.ProjectId)).ToDictionaryAsync(row => row.ProjectId, cancellationToken);

        foreach (ProjectFinancialFacts fact in facts)
        {
            FinanceSummary summary = summaries[fact.Id];
            if (!existing.TryGetValue(fact.Id, out ProjectFinancialSnapshot? snapshot))
            {
                snapshot = ProjectFinancialSnapshot.For(fact.Id, month);
                dbContext.ProjectFinancialSnapshots.Add(snapshot);
            }

            snapshot.Record(summary.Revenue, summary.ActualCost, summary.Billed, summary.Collected, summary.ForecastRevenue, summary.ForecastCost);

            if (summary.Budget <= 0 || fact.ProjectManagerEmployeeId is not { } manager)
            {
                continue;
            }

            int used = (int)Math.Floor(summary.ActualCost * 100 / summary.Budget);
            int threshold = used >= 100 ? 100 : used >= 90 ? 90 : 0;
            if (!profiles.TryGetValue(fact.Id, out ProjectFinanceProfile? profile))
            {
                profile = ProjectFinanceProfile.For(fact.Id);
                dbContext.ProjectFinanceProfiles.Add(profile);
                profiles[fact.Id] = profile;
            }

            if (threshold > profile.AlertedPercent)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.ProjectBudgetAlert,
                        [new NotificationRecipient(manager)],
                        new Dictionary<string, string>
                        {
                            ["projectCode"] = fact.Code,
                            ["projectName"] = fact.Name,
                            ["percent"] = used.ToString(CultureInfo.InvariantCulture),
                            ["actualCost"] = summary.ActualCost.ToString("N0", CultureInfo.InvariantCulture),
                            ["costBudget"] = summary.Budget.ToString("N0", CultureInfo.InvariantCulture),
                        },
                        $"/projects/{fact.Id}?tab=financials"),
                    cancellationToken);
                profile.Alerted(threshold);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return facts.Count;
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<IReadOnlyDictionary<Guid, FinanceSummary>> SummariesAsync(IReadOnlyList<ProjectFinancialFacts> facts, DateOnly today, CancellationToken cancellationToken)
    {
        if (facts.Count == 0)
        {
            return new Dictionary<Guid, FinanceSummary>();
        }

        List<Guid> ids = [.. facts.Select(fact => fact.Id)];
        DateOnly from = facts.Min(fact => fact.StartDate).AddMonths(-1);
        ILookup<Guid?, LoggedHours> hours = (await timesheets.ProjectHoursAsync(ids, from, today, approvedOnly: true, cancellationToken)).ToLookup(row => row.ProjectId);
        ILookup<Guid, ProjectRate> rates = (await dbContext.ProjectRates.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToListAsync(cancellationToken)).ToLookup(row => row.ProjectId);
        ILookup<Guid, ProjectCost> costs = (await dbContext.ProjectCosts.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToListAsync(cancellationToken)).ToLookup(row => row.ProjectId);
        ILookup<Guid, ProjectInvoice> invoices = (await dbContext.ProjectInvoices.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToListAsync(cancellationToken)).ToLookup(row => row.ProjectId);
        ILookup<Guid, ProjectBudgetLine> budgets = (await dbContext.ProjectBudgetLines.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToListAsync(cancellationToken)).ToLookup(row => row.ProjectId);
        Dictionary<Guid, ProjectFinanceProfile> profiles = await dbContext.ProjectFinanceProfiles.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToDictionaryAsync(row => row.ProjectId, cancellationToken);
        IReadOnlyDictionary<Guid, ContractSummary> contractValues = await contracts.GetManyAsync(facts.Select(fact => fact.ContractId).OfType<Guid>(), cancellationToken);

        return facts.ToDictionary(fact => fact.Id, fact =>
        {
            ProjectFinanceProfile? profile = profiles.GetValueOrDefault(fact.Id);
            decimal? contract = profile?.ContractValue ?? (fact.ContractId is { } contractId ? contractValues.GetValueOrDefault(contractId)?.Value : null);
            return FinanceCalculator.Calculate(new FinanceInput(
                fact, contract, profile?.RetainerMonthlyFee, profile?.MilestoneAmounts ?? new Dictionary<Guid, decimal>(), [.. hours[fact.Id]], [.. rates[fact.Id]],
                [.. costs[fact.Id]], [.. invoices[fact.Id]], [.. budgets[fact.Id]], today));
        });
    }

    private async Task EnsureAsync(Guid projectId, string permission, CancellationToken cancellationToken)
    {
        if (!await projectPermissions.CanAsync(projectId, permission, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }
    }

    private async Task<ProjectFinanceProfile> ProfileAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ProjectFinanceProfile? profile = await dbContext.ProjectFinanceProfiles.FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);
        if (profile is null)
        {
            profile = ProjectFinanceProfile.For(projectId);
            dbContext.ProjectFinanceProfiles.Add(profile);
        }

        return profile;
    }

    private async Task<ProjectInvoice> InvoiceAsync(Guid projectId, Guid invoiceId, CancellationToken cancellationToken)
        => await dbContext.ProjectInvoices.FirstOrDefaultAsync(row => row.Id == invoiceId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectInvoice), invoiceId);

    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string prefix = $"INV-{date.Year}-";
        List<string> numbers = await dbContext.ProjectInvoices.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Where(row => row.Number.StartsWith(prefix)).Select(row => row.Number).ToListAsync(cancellationToken);
        int next = numbers.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    private async Task<InvoiceItem> InvoiceItemAsync(ProjectInvoice invoice, CancellationToken cancellationToken)
    {
        ProjectFinancialFacts? fact = await projects.FinancialFactsAsync([invoice.ProjectId], cancellationToken) is [var found, ..] ? found : null;
        return Invoice(invoice, fact, await clock.TodayAsync(cancellationToken));
    }

    private static InvoiceItem Invoice(ProjectInvoice invoice, ProjectFinancialFacts? fact, DateOnly today) => new(
        invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.DueDate, invoice.PeriodFrom, invoice.PeriodTo, invoice.Amount, invoice.TaxAmount, invoice.Status, invoice.PaidOn,
        invoice.MilestoneId, invoice.MilestoneId is { } milestone ? fact?.Milestones.FirstOrDefault(row => row.Id == milestone)?.Name : null, invoice.Notes, invoice.IsOverdue(today));

    private static CostItem Cost(ProjectCost cost) => new(cost.Id, cost.Category, cost.Date, cost.Amount, cost.Vendor, cost.Description, cost.Reference, cost.Planned);
}

/// <summary>Fans the nightly project financial snapshot out to every tenant.</summary>
internal sealed class FinanceSnapshotSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "project-financials";

    public Task RunAsync() => fanOut.FanOutAsync<FinanceSnapshotJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class FinanceSnapshotJob(FinanceService finance, ILogger<FinanceSnapshotJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int recorded = await finance.SnapshotAsync(CancellationToken.None);
        if (recorded > 0)
        {
            logger.LogInformation("Project financials for tenant {TenantId}: {Count} project(s) recorded.", tenantId, recorded);
        }
    }
}

internal sealed class FinanceJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // After the project health snapshot (02:45), so both describe the same day.
        recurringJobs.AddOrUpdate<FinanceSnapshotSweep>(FinanceSnapshotSweep.RecurringJobName, sweep => sweep.RunAsync(), "0 3 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
