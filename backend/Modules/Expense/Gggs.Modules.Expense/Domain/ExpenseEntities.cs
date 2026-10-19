using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Expense.Domain;

/// <summary>A kind of expense and its policy: the most one line may claim, and above what a receipt is needed.</summary>
public sealed class ExpenseCategory : TenantAggregateRoot
{
    private ExpenseCategory()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal? LimitPerLine { get; private set; }

    public decimal ReceiptRequiredAbove { get; private set; }

    public static ExpenseCategory Create(string code, string name, decimal? limitPerLine, decimal receiptRequiredAbove)
    {
        var category = new ExpenseCategory();
        category.Update(code, name, limitPerLine, receiptRequiredAbove, active: true);
        return category;
    }

    public void Update(string code, string name, decimal? limitPerLine, decimal receiptRequiredAbove, bool active)
    {
        if (limitPerLine is <= 0 || receiptRequiredAbove < 0)
        {
            throw new DomainRuleException("EXPENSE_CATEGORY", "A limit is above zero and the receipt threshold is not negative.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        LimitPerLine = limitPerLine;
        ReceiptRequiredAbove = receiptRequiredAbove;
        IsActive = active;
    }

    /// <summary>What a line breaks of this category's policy, or null when it is within it.</summary>
    public string? PolicyCheck(decimal amount, bool hasReceipt)
    {
        var flags = new List<string>();
        if (LimitPerLine is { } limit && amount > limit)
        {
            flags.Add($"over the {limit:0.##} limit");
        }

        if (!hasReceipt && amount > ReceiptRequiredAbove)
        {
            flags.Add("receipt needed");
        }

        return flags.Count == 0 ? null : string.Join("; ", flags);
    }
}

public enum ClaimStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Paid = 5,
    Cancelled = 6,
}

public enum PaymentMode
{
    Payroll = 1,
    Direct = 2,
}

public sealed record ClaimLineInput(Guid CategoryId, DateOnly Date, string Description, decimal Amount, string? Merchant, bool HasReceipt, bool Billable, string? Justification);

/// <summary>An expense claim (spec: expense): lines checked against category policy, approved through the workflow, reimbursed.</summary>
public sealed class ExpenseClaim : TenantAggregateRoot
{
    private ExpenseClaim()
    {
        Number = null!;
        Title = null!;
        Currency = null!;
    }

    public string Number { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Title { get; private set; }

    public Guid? TravelRequestId { get; private set; }

    public Guid? ProjectId { get; private set; }

    public string Currency { get; private set; }

    public decimal Total { get; private set; }

    public ClaimStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? SubmittedDate { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public PaymentMode? PaymentMode { get; private set; }

    public string? PaymentReference { get; private set; }

    public DateOnly? PaidOn { get; private set; }

    public static ExpenseClaim Draft(string number, Guid employeeId, string title, Guid? travelRequestId, Guid? projectId, string currency)
    {
        var claim = new ExpenseClaim { Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30), EmployeeId = Guard.NotEmpty(employeeId), Status = ClaimStatus.Draft };
        claim.Update(title, travelRequestId, projectId, currency);
        return claim;
    }

    public void Update(string title, Guid? travelRequestId, Guid? projectId, string currency)
    {
        EnsureEditable();
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        TravelRequestId = travelRequestId;
        ProjectId = projectId;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3);
    }

    public void SetTotal(decimal total) => Total = Math.Round(total, 2);

    public void Submit(DateTime now)
    {
        EnsureEditable();
        if (Total <= 0)
        {
            throw new DomainRuleException("CLAIM_EMPTY", "Add the expenses before submitting the claim.");
        }

        Status = ClaimStatus.PendingApproval;
        SubmittedDate = now;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool? approved, DateTime now)
    {
        if (Status != ClaimStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => ClaimStatus.Approved,
            false => ClaimStatus.Rejected,
            null => ClaimStatus.Draft,
        };
        DecidedDate = now;
    }

    public void Pay(PaymentMode mode, string? reference, DateOnly on)
    {
        if (Status != ClaimStatus.Approved)
        {
            throw new DomainRuleException("CLAIM_STATE", "Only an approved claim can be paid.");
        }

        Status = ClaimStatus.Paid;
        PaymentMode = mode;
        PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100);
        PaidOn = on;
    }

    public void Cancel()
    {
        if (Status is not (ClaimStatus.Draft or ClaimStatus.Rejected or ClaimStatus.PendingApproval))
        {
            throw new DomainRuleException("CLAIM_STATE", $"A {Status.ToString().ToLowerInvariant()} claim cannot be cancelled.");
        }

        Status = ClaimStatus.Cancelled;
    }

    public void EnsureEditable()
    {
        if (Status is not (ClaimStatus.Draft or ClaimStatus.Rejected))
        {
            throw new DomainRuleException("CLAIM_STATE", $"A {Status.ToString().ToLowerInvariant()} claim cannot change.");
        }
    }
}

/// <summary>One expense on a claim.</summary>
public sealed class ExpenseLine : TenantAggregateRoot
{
    private ExpenseLine()
    {
        Description = null!;
    }

    public Guid ClaimId { get; private set; }

    public Guid CategoryId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Description { get; private set; }

    public decimal Amount { get; private set; }

    public string? Merchant { get; private set; }

    public bool HasReceipt { get; private set; }

    public bool Billable { get; private set; }

    /// <summary>What the line breaks of policy, if anything; the approver sees it.</summary>
    public string? PolicyFlag { get; private set; }

    public string? Justification { get; private set; }

    public static ExpenseLine Add(Guid claimId, ClaimLineInput input, string? policyFlag, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Amount <= 0)
        {
            throw new DomainRuleException("EXPENSE_AMOUNT", "An expense is above zero.");
        }

        if (input.Date > today)
        {
            throw new DomainRuleException("EXPENSE_DATE", "An expense cannot be in the future.");
        }

        if (policyFlag is not null && string.IsNullOrWhiteSpace(input.Justification))
        {
            throw new DomainRuleException("EXPENSE_POLICY", $"\"{input.Description}\" is outside policy ({policyFlag}); explain why.");
        }

        return new ExpenseLine
        {
            ClaimId = Guard.NotEmpty(claimId),
            CategoryId = Guard.NotEmpty(input.CategoryId),
            Date = input.Date,
            Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(input.Description).Trim(), 300),
            Amount = Math.Round(input.Amount, 2),
            Merchant = string.IsNullOrWhiteSpace(input.Merchant) ? null : Guard.MaxLength(input.Merchant.Trim(), 200),
            HasReceipt = input.HasReceipt,
            Billable = input.Billable,
            PolicyFlag = policyFlag,
            Justification = string.IsNullOrWhiteSpace(input.Justification) ? null : Guard.MaxLength(input.Justification.Trim(), 500),
        };
    }
}

public enum TravelMode
{
    Air = 1,
    Rail = 2,
    Road = 3,
    Other = 4,
}

public enum TravelStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Settled = 6,
}

public sealed record TravelDetails(
    string Purpose,
    string FromCity,
    string ToCity,
    DateOnly DepartOn,
    DateOnly ReturnOn,
    TravelMode Mode,
    decimal EstimatedCost,
    decimal AdvanceRequested,
    Guid? ProjectId);

/// <summary>A travel request (spec: travel): approved through the workflow, an advance paid, and settled against the trip's claims.</summary>
public sealed class TravelRequest : TenantAggregateRoot
{
    private TravelRequest()
    {
        Number = null!;
        Purpose = null!;
        FromCity = null!;
        ToCity = null!;
    }

    public string Number { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Purpose { get; private set; }

    public string FromCity { get; private set; }

    public string ToCity { get; private set; }

    public DateOnly DepartOn { get; private set; }

    public DateOnly ReturnOn { get; private set; }

    public TravelMode Mode { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public decimal AdvanceRequested { get; private set; }

    public Guid? ProjectId { get; private set; }

    public TravelStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public decimal? AdvancePaid { get; private set; }

    public DateOnly? AdvancePaidOn { get; private set; }

    /// <summary>Claims less the advance: positive is owed to the employee, negative is recovered.</summary>
    public decimal? SettlementAmount { get; private set; }

    public DateOnly? SettledOn { get; private set; }

    public static TravelRequest Draft(string number, Guid employeeId, TravelDetails details)
    {
        var request = new TravelRequest { Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30), EmployeeId = Guard.NotEmpty(employeeId), Status = TravelStatus.Draft };
        request.Update(details);
        return request;
    }

    public void Update(TravelDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status is not (TravelStatus.Draft or TravelStatus.Rejected))
        {
            throw new DomainRuleException("TRAVEL_STATE", "Only a draft or rejected request can change.");
        }

        if (details.ReturnOn < details.DepartOn)
        {
            throw new DomainRuleException("TRAVEL_DATES", "The return is before the departure.");
        }

        if (details.EstimatedCost < 0 || details.AdvanceRequested < 0 || details.AdvanceRequested > details.EstimatedCost)
        {
            throw new DomainRuleException("TRAVEL_COST", "The advance cannot be more than the estimated cost.");
        }

        Purpose = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Purpose).Trim(), 500);
        FromCity = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.FromCity).Trim(), 100);
        ToCity = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.ToCity).Trim(), 100);
        DepartOn = details.DepartOn;
        ReturnOn = details.ReturnOn;
        Mode = details.Mode;
        EstimatedCost = details.EstimatedCost;
        AdvanceRequested = details.AdvanceRequested;
        ProjectId = details.ProjectId;
        Status = TravelStatus.Draft;
    }

    public void Submit()
    {
        if (Status is not (TravelStatus.Draft or TravelStatus.Rejected))
        {
            throw new DomainRuleException("TRAVEL_STATE", "Only a draft request can be submitted.");
        }

        Status = TravelStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool? approved)
    {
        if (Status != TravelStatus.PendingApproval)
        {
            return;
        }

        Status = approved switch
        {
            true => TravelStatus.Approved,
            false => TravelStatus.Rejected,
            null => TravelStatus.Draft,
        };
    }

    public void PayAdvance(decimal amount, DateOnly on)
    {
        if (Status != TravelStatus.Approved || AdvancePaid is not null)
        {
            throw new DomainRuleException("TRAVEL_ADVANCE", "An advance is paid once, on an approved request.");
        }

        if (amount <= 0 || amount > AdvanceRequested)
        {
            throw new DomainRuleException("TRAVEL_ADVANCE", $"The advance is above zero and at most the {AdvanceRequested:0.##} requested.");
        }

        AdvancePaid = amount;
        AdvancePaidOn = on;
    }

    public void Settle(decimal claimed, DateOnly on)
    {
        if (Status != TravelStatus.Approved)
        {
            throw new DomainRuleException("TRAVEL_STATE", "Only an approved trip can be settled.");
        }

        SettlementAmount = Math.Round(claimed - (AdvancePaid ?? 0), 2);
        Status = TravelStatus.Settled;
        SettledOn = on;
    }

    public void Cancel()
    {
        if (Status is TravelStatus.Settled or TravelStatus.Cancelled || AdvancePaid is not null)
        {
            throw new DomainRuleException("TRAVEL_STATE", "A settled trip, or one with an advance paid, cannot be cancelled.");
        }

        Status = TravelStatus.Cancelled;
    }
}

/// <summary>The categories every tenant starts with.</summary>
public static class ExpenseDefaults
{
    public static IReadOnlyList<(string Code, string Name, decimal? Limit, decimal ReceiptAbove)> Categories { get; } =
    [
        ("TRAVEL", "Travel and fares", null, 500),
        ("HOTEL", "Hotel", 6000, 0),
        ("MEALS", "Meals", 1500, 500),
        ("LOCAL", "Local conveyance", 2000, 500),
        ("CLIENT", "Client entertainment", 10000, 0),
        ("INTERNET", "Internet and phone", 1500, 0),
        ("OTHER", "Other", 5000, 0),
    ];
}
