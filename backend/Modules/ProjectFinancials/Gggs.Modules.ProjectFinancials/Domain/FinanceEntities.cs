using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>Where project money goes (spec §74).</summary>
public enum CostCategory
{
    Labour = 1,
    Vendor = 2,
    Expense = 3,
    Infrastructure = 4,
    Other = 5,

    /// <summary>LLM APIs, GPUs and AI tool seats: on AI-based work, cost that hours do not show.</summary>
    AiUsage = 6,
}

/// <summary>A project's budget for one kind of cost (spec §125 ProjectBudget).</summary>
public sealed class ProjectBudgetLine : TenantAggregateRoot
{
    private ProjectBudgetLine()
    {
    }

    public Guid ProjectId { get; private set; }

    public CostCategory Category { get; private set; }

    public decimal Amount { get; private set; }

    public string? Notes { get; private set; }

    public static ProjectBudgetLine For(Guid projectId, CostCategory category, decimal amount, string? notes)
    {
        var line = new ProjectBudgetLine { ProjectId = Guard.NotEmpty(projectId), Category = category };
        line.Set(amount, notes);
        return line;
    }

    public void Set(decimal amount, string? notes)
    {
        if (amount < 0)
        {
            throw new DomainRuleException("BUDGET_AMOUNT", "A budget cannot be negative.");
        }

        Amount = Math.Round(amount, 2);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 500);
    }
}

/// <summary>
/// A rate card line (spec §125 ProjectBillingRate, ProjectCostRate): what an hour of a role - or of one
/// named person - is billed at and costs on this project, from a date.
/// </summary>
public sealed class ProjectRate : TenantAggregateRoot
{
    private ProjectRate()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid? ProjectRoleId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public decimal? BillingRate { get; private set; }

    public decimal? CostRate { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public static ProjectRate Create(Guid projectId, Guid? projectRoleId, Guid? employeeId, decimal? billingRate, decimal? costRate, DateOnly effectiveFrom)
    {
        var rate = new ProjectRate { ProjectId = Guard.NotEmpty(projectId) };
        rate.Update(projectRoleId, employeeId, billingRate, costRate, effectiveFrom);
        return rate;
    }

    public void Update(Guid? projectRoleId, Guid? employeeId, decimal? billingRate, decimal? costRate, DateOnly effectiveFrom)
    {
        if ((projectRoleId is null) == (employeeId is null))
        {
            throw new DomainRuleException("RATE_TARGET", "A rate is for a role or for a person - choose one.");
        }

        if (billingRate is null && costRate is null || billingRate < 0 || costRate < 0)
        {
            throw new DomainRuleException("RATE_VALUE", "Give a billing rate, a cost rate or both - neither negative.");
        }

        ProjectRoleId = projectRoleId;
        EmployeeId = employeeId;
        BillingRate = billingRate;
        CostRate = costRate;
        EffectiveFrom = effectiveFrom;
    }
}

/// <summary>A non-labour cost (spec §74): a vendor bill, an expense, infrastructure. Planned costs count in the forecast only.</summary>
public sealed class ProjectCost : TenantAggregateRoot
{
    private ProjectCost()
    {
        Description = null!;
    }

    public Guid ProjectId { get; private set; }

    public CostCategory Category { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public string? Vendor { get; private set; }

    public string Description { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>Committed but not yet incurred.</summary>
    public bool Planned { get; private set; }

    public static ProjectCost Record(Guid projectId, CostDetails details)
    {
        var cost = new ProjectCost { ProjectId = Guard.NotEmpty(projectId) };
        cost.Update(details);
        return cost;
    }

    public void Update(CostDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Category == CostCategory.Labour)
        {
            throw new DomainRuleException("COST_CATEGORY", "Labour cost comes from timesheets; record vendor, expense, infrastructure or other costs here.");
        }

        if (details.Amount <= 0)
        {
            throw new DomainRuleException("COST_AMOUNT", "A cost must be more than zero.");
        }

        Category = details.Category;
        Date = details.Date;
        Amount = Math.Round(details.Amount, 2);
        Vendor = string.IsNullOrWhiteSpace(details.Vendor) ? null : Guard.MaxLength(details.Vendor.Trim(), 200);
        Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Description).Trim(), 300);
        Reference = string.IsNullOrWhiteSpace(details.Reference) ? null : Guard.MaxLength(details.Reference.Trim(), 100);
        Planned = details.Planned;
    }
}

public sealed record CostDetails(CostCategory Category, DateOnly Date, decimal Amount, string? Vendor, string Description, string? Reference, bool Planned);

public enum InvoiceStatus
{
    Draft = 1,
    Sent = 2,
    Paid = 3,
    Cancelled = 4,
}

/// <summary>An invoice raised on the client for the project (spec §75, §125 ProjectInvoice): billed once sent, collected once paid.</summary>
public sealed class ProjectInvoice : TenantAggregateRoot
{
    private ProjectInvoice()
    {
        Number = null!;
    }

    public Guid ProjectId { get; private set; }

    public string Number { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public DateOnly? PeriodFrom { get; private set; }

    public DateOnly? PeriodTo { get; private set; }

    public decimal Amount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateOnly? PaidOn { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public string? Notes { get; private set; }

    public static ProjectInvoice Raise(Guid projectId, string number, InvoiceDetails details)
    {
        var invoice = new ProjectInvoice { ProjectId = Guard.NotEmpty(projectId), Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number).Trim(), 40), Status = InvoiceStatus.Draft };
        invoice.Update(details);
        return invoice;
    }

    public void Update(InvoiceDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
        {
            throw new DomainRuleException("INVOICE_STATE", $"A {Status.ToString().ToLowerInvariant()} invoice cannot change.");
        }

        if (details.Amount <= 0 || details.TaxAmount < 0)
        {
            throw new DomainRuleException("INVOICE_AMOUNT", "An invoice is for more than zero, with no negative tax.");
        }

        if (details.PeriodFrom is { } from && details.PeriodTo is { } to && to < from)
        {
            throw new DomainRuleException("INVOICE_PERIOD", "The billing period ends after it starts.");
        }

        InvoiceDate = details.InvoiceDate;
        DueDate = details.DueDate;
        PeriodFrom = details.PeriodFrom;
        PeriodTo = details.PeriodTo;
        Amount = Math.Round(details.Amount, 2);
        TaxAmount = Math.Round(details.TaxAmount, 2);
        MilestoneId = details.MilestoneId;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Guard.MaxLength(details.Notes.Trim(), 1000);
    }

    public void Send()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new DomainRuleException("INVOICE_STATE", "Only a draft invoice can be sent.");
        }

        Status = InvoiceStatus.Sent;
    }

    public void MarkPaid(DateOnly paidOn)
    {
        if (Status != InvoiceStatus.Sent)
        {
            throw new DomainRuleException("INVOICE_STATE", "Only a sent invoice can be marked paid.");
        }

        Status = InvoiceStatus.Paid;
        PaidOn = paidOn;
    }

    public void Cancel()
    {
        if (Status == InvoiceStatus.Paid)
        {
            throw new DomainRuleException("INVOICE_STATE", "A paid invoice cannot be cancelled; issue a credit note instead.");
        }

        Status = InvoiceStatus.Cancelled;
    }

    public bool IsOverdue(DateOnly today) => Status == InvoiceStatus.Sent && DueDate is { } due && due < today;
}

public sealed record InvoiceDetails(DateOnly InvoiceDate, DateOnly? DueDate, DateOnly? PeriodFrom, DateOnly? PeriodTo, decimal Amount, decimal TaxAmount, Guid? MilestoneId, string? Notes);

/// <summary>
/// A project's commercial terms beyond the project record: a contract value that overrides the
/// contract's, the monthly fee of a retainer, how a milestone is billed, and which budget alert has gone out.
/// </summary>
public sealed class ProjectFinanceProfile : TenantAggregateRoot
{
    private ProjectFinanceProfile()
    {
        MilestoneAmountsJson = "{}";
    }

    public Guid ProjectId { get; private set; }

    public decimal? ContractValue { get; private set; }

    public decimal? RetainerMonthlyFee { get; private set; }

    /// <summary>Milestone id → amount billed when it is achieved (milestone billing).</summary>
    public string MilestoneAmountsJson { get; private set; }

    /// <summary>The highest budget threshold (90, 100) already alerted, so each goes out once.</summary>
    public int AlertedPercent { get; private set; }

    /// <summary>The most a T&amp;M engagement may bill; revenue stops counting past it.</summary>
    public decimal? NotToExceed { get; private set; }

    public string? PoNumber { get; private set; }

    public decimal? PoValue { get; private set; }

    /// <summary>Hours a month's fee covers, on a retainer or managed service.</summary>
    public decimal? IncludedHoursPerMonth { get; private set; }

    /// <summary>The share of a month's unused hours that rolls into the next: 0 none, 100 all.</summary>
    public int RolloverPercent { get; private set; }

    /// <summary>What an hour beyond the bucket is billed at.</summary>
    public decimal? OverageRate { get; private set; }

    /// <summary>The highest cap threshold (75, 90, 100) already alerted, so each goes out once.</summary>
    public int CapAlertedPercent { get; private set; }

    public CommercialTerms Terms => new(NotToExceed, PoNumber, PoValue, IncludedHoursPerMonth, RolloverPercent, OverageRate);

    public IReadOnlyDictionary<Guid, decimal> MilestoneAmounts
        => System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, decimal>>(MilestoneAmountsJson) ?? [];

    public static ProjectFinanceProfile For(Guid projectId) => new() { ProjectId = Guard.NotEmpty(projectId) };

    public void Update(decimal? contractValue, decimal? retainerMonthlyFee, IReadOnlyDictionary<Guid, decimal>? milestoneAmounts, CommercialTerms? terms = null)
    {
        CommercialTerms next = terms ?? new CommercialTerms();
        if (contractValue < 0 || retainerMonthlyFee < 0 || (milestoneAmounts?.Values.Any(amount => amount < 0) ?? false)
            || next.NotToExceed < 0 || next.PoValue < 0 || next.IncludedHoursPerMonth < 0 || next.OverageRate < 0)
        {
            throw new DomainRuleException("PROFILE_AMOUNTS", "Amounts cannot be negative.");
        }

        if (next.RolloverPercent is < 0 or > 100)
        {
            throw new DomainRuleException("PROFILE_ROLLOVER", "Rollover is a share of the unused hours, from 0 to 100 percent.");
        }

        if (next.NotToExceed != NotToExceed)
        {
            CapAlertedPercent = 0;
        }

        NotToExceed = next.NotToExceed;
        PoNumber = string.IsNullOrWhiteSpace(next.PoNumber) ? null : Guard.MaxLength(next.PoNumber.Trim(), 100);
        PoValue = next.PoValue;
        IncludedHoursPerMonth = next.IncludedHoursPerMonth;
        RolloverPercent = next.RolloverPercent;
        OverageRate = next.OverageRate;

        ContractValue = contractValue;
        RetainerMonthlyFee = retainerMonthlyFee;
        MilestoneAmountsJson = System.Text.Json.JsonSerializer.Serialize(milestoneAmounts?.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => pair.Value) ?? []);
    }

    public void Alerted(int percent) => AlertedPercent = Math.Max(AlertedPercent, percent);

    public void ResetAlerts() => AlertedPercent = 0;

    public void CapAlerted(int percent) => CapAlertedPercent = Math.Max(CapAlertedPercent, percent);
}

/// <summary>A month of a project's money, kept nightly so the financial dashboard can show a trend (spec §125 ProjectProfitability).</summary>
public sealed class ProjectFinancialSnapshot : TenantAggregateRoot
{
    private ProjectFinancialSnapshot()
    {
    }

    public Guid ProjectId { get; private set; }

    public DateOnly Month { get; private set; }

    /// <summary>Cumulative to the end of the month.</summary>
    public decimal Revenue { get; private set; }

    public decimal Cost { get; private set; }

    public decimal Billed { get; private set; }

    public decimal Collected { get; private set; }

    public decimal ForecastCost { get; private set; }

    public decimal ForecastRevenue { get; private set; }

    public static ProjectFinancialSnapshot For(Guid projectId, DateOnly month) => new() { ProjectId = Guard.NotEmpty(projectId), Month = new DateOnly(month.Year, month.Month, 1) };

    public void Record(decimal revenue, decimal cost, decimal billed, decimal collected, decimal forecastRevenue, decimal forecastCost)
    {
        Revenue = revenue;
        Cost = cost;
        Billed = billed;
        Collected = collected;
        ForecastRevenue = forecastRevenue;
        ForecastCost = forecastCost;
    }
}
