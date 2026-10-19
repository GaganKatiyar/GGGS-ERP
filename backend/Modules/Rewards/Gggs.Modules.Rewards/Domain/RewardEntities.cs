using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Rewards.Domain;

/// <summary>A tenant's reward programme settings: what a point is worth, when points lapse, and how many each person may give peers a month.</summary>
public sealed class RewardSettings : TenantAggregateRoot
{
    private RewardSettings()
    {
        Currency = "INR";
    }

    /// <summary>Money one point is worth, for budgets and reporting.</summary>
    public decimal PointValue { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Months after which earned points lapse; zero means never.</summary>
    public int ExpiryMonths { get; private set; }

    public int MonthlyPeerAllowance { get; private set; }

    public static RewardSettings Default() => new() { PointValue = 1, ExpiryMonths = 12, MonthlyPeerAllowance = 500 };

    public void Update(decimal pointValue, string currency, int expiryMonths, int monthlyPeerAllowance)
    {
        if (pointValue <= 0 || expiryMonths is < 0 or > 60 || monthlyPeerAllowance is < 0 or > 100_000)
        {
            throw new DomainRuleException("REWARD_SETTINGS", "A point is worth more than zero, expiry runs 0 to 60 months and the peer allowance 0 to 100,000.");
        }

        PointValue = pointValue;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3);
        ExpiryMonths = expiryMonths;
        MonthlyPeerAllowance = monthlyPeerAllowance;
    }

    public DateOnly? ExpiryFrom(DateOnly earnedOn) => ExpiryMonths == 0 ? null : earnedOn.AddMonths(ExpiryMonths);
}

public enum RecognitionKind
{
    /// <summary>Anyone to anyone, from their monthly allowance.</summary>
    Peer = 1,

    /// <summary>A manager to their team, from the department's budget.</summary>
    Manager = 2,

    /// <summary>A formal award given by the programme team, from the budget.</summary>
    Award = 3,
}

/// <summary>A badge or award people can give - its points, who gives it, whether it needs approval and any cash with it.</summary>
public sealed class RecognitionType : TenantAggregateRoot
{
    private RecognitionType()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public RecognitionKind Kind { get; private set; }

    public int Points { get; private set; }

    public bool RequiresApproval { get; private set; }

    /// <summary>Cash paid through payroll as a bonus when the recognition is approved.</summary>
    public decimal? CashAmount { get; private set; }

    public static RecognitionType Create(string code, string name, string? description, RecognitionKind kind, int points, bool requiresApproval, decimal? cashAmount)
    {
        var type = new RecognitionType();
        type.Update(code, name, description, kind, points, requiresApproval, cashAmount, active: true);
        return type;
    }

    public void Update(string code, string name, string? description, RecognitionKind kind, int points, bool requiresApproval, decimal? cashAmount, bool active)
    {
        if (!Enum.IsDefined(kind) || points is < 0 or > 1_000_000 || cashAmount is < 0)
        {
            throw new DomainRuleException("RECOGNITION_TYPE", "Choose a kind, points from 0 to 1,000,000 and no negative cash.");
        }

        if (kind == RecognitionKind.Peer && cashAmount > 0)
        {
            throw new DomainRuleException("RECOGNITION_TYPE_CASH", "Peer recognition carries points only, not cash.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
        Kind = kind;
        Points = points;
        RequiresApproval = requiresApproval;
        CashAmount = cashAmount is > 0 ? cashAmount : null;
        IsActive = active;
    }
}

public enum RuleTrigger
{
    /// <summary>On each work anniversary; the threshold is the minimum years.</summary>
    WorkAnniversary = 1,

    /// <summary>When a review is released with at least the threshold rating.</summary>
    ReviewRating = 2,

    /// <summary>When a course is completed.</summary>
    CourseCompleted = 3,
}

/// <summary>Points the system awards on its own when something happens.</summary>
public sealed class RewardRule : TenantAggregateRoot
{
    private RewardRule()
    {
        Name = null!;
        Message = null!;
    }

    public string Name { get; private set; }

    public RuleTrigger Trigger { get; private set; }

    public int Points { get; private set; }

    /// <summary>Minimum years (anniversary) or rating (review); ignored for courses.</summary>
    public int Threshold { get; private set; }

    /// <summary>Anniversary points multiply by the years served.</summary>
    public bool PerYear { get; private set; }

    /// <summary>The message on the feed; <c>{years}</c>, <c>{rating}</c> and <c>{course}</c> fill in.</summary>
    public string Message { get; private set; }

    public static RewardRule Create(string name, RuleTrigger trigger, int points, int threshold, bool perYear, string message)
    {
        var rule = new RewardRule();
        rule.Update(name, trigger, points, threshold, perYear, message, active: true);
        return rule;
    }

    public void Update(string name, RuleTrigger trigger, int points, int threshold, bool perYear, string message, bool active)
    {
        if (!Enum.IsDefined(trigger) || points is < 1 or > 1_000_000 || threshold is < 0 or > 100)
        {
            throw new DomainRuleException("REWARD_RULE", "Choose a trigger, points from 1 to 1,000,000 and a threshold from 0 to 100.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Trigger = trigger;
        Points = points;
        Threshold = threshold;
        PerYear = perYear && trigger == RuleTrigger.WorkAnniversary;
        Message = Guard.MaxLength(Guard.NotNullOrWhiteSpace(message).Trim(), 300);
        IsActive = active;
    }

    public bool Applies(int value) => IsActive && value >= Threshold;

    public int PointsFor(int years) => PerYear ? Points * Math.Max(1, years) : Points;
}

public enum RecognitionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// One recognition on the feed (spec: rewards): from a colleague, a manager, the programme team or a
/// rule, with its points credited to the recipient once approved.
/// </summary>
public sealed class Recognition : TenantAggregateRoot
{
    private Recognition()
    {
        Message = null!;
    }

    /// <summary>Who gave it; empty for a rule.</summary>
    public Guid? GiverEmployeeId { get; private set; }

    public Guid RecipientEmployeeId { get; private set; }

    public Guid? TypeId { get; private set; }

    public Guid? RuleId { get; private set; }

    public RecognitionKind? Kind { get; private set; }

    public string Message { get; private set; }

    public int Points { get; private set; }

    public decimal? CashAmount { get; private set; }

    public bool IsPublic { get; private set; }

    public RecognitionStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public Guid? BudgetId { get; private set; }

    /// <summary>What a rule fired on (e.g. "anniversary:2026"), so it fires once.</summary>
    public string? SourceKey { get; private set; }

    public DateOnly GivenOn { get; private set; }

    public int Cheers { get; private set; }

    public static Recognition Give(Guid giverEmployeeId, Guid recipientEmployeeId, RecognitionType type, string message, bool isPublic, Guid? budgetId, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (giverEmployeeId == recipientEmployeeId)
        {
            throw new DomainRuleException("RECOGNITION_SELF", "You cannot recognise yourself.");
        }

        return new Recognition
        {
            GiverEmployeeId = giverEmployeeId,
            RecipientEmployeeId = Guard.NotEmpty(recipientEmployeeId),
            TypeId = type.Id,
            Kind = type.Kind,
            Message = Guard.MaxLength(Guard.NotNullOrWhiteSpace(message).Trim(), 1000),
            Points = type.Points,
            CashAmount = type.CashAmount,
            IsPublic = isPublic,
            Status = RecognitionStatus.Pending,
            BudgetId = budgetId,
            GivenOn = today,
        };
    }

    public static Recognition FromRule(RewardRule rule, Guid recipientEmployeeId, int points, string message, string sourceKey, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new Recognition
        {
            RecipientEmployeeId = recipientEmployeeId,
            RuleId = rule.Id,
            Message = Guard.MaxLength(message, 1000),
            Points = points,
            IsPublic = true,
            Status = RecognitionStatus.Approved,
            SourceKey = Guard.MaxLength(sourceKey, 100),
            GivenOn = today,
        };
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool approved)
    {
        if (Status != RecognitionStatus.Pending)
        {
            throw new DomainRuleException("RECOGNITION_DECIDED", "The recognition has already been decided.");
        }

        Status = approved ? RecognitionStatus.Approved : RecognitionStatus.Rejected;
    }

    public void Cheered() => Cheers++;
}

/// <summary>A colleague's cheer on a recognition - once each.</summary>
public sealed class RecognitionCheer : TenantAggregateRoot
{
    public Guid RecognitionId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public static RecognitionCheer Add(Guid recognitionId, Guid employeeId) => new() { RecognitionId = recognitionId, EmployeeId = employeeId };
}

public enum PointEntryKind
{
    Earned = 1,
    Redeemed = 2,
    Expired = 3,
    Refunded = 4,
    Adjusted = 5,
}

/// <summary>
/// One movement in an employee's points wallet. Credits carry what is left of them and when they
/// lapse, so debits spend the soonest-expiring points first and expiry takes only what is unspent.
/// </summary>
public sealed class PointEntry : TenantAggregateRoot
{
    private PointEntry()
    {
        Note = null!;
    }

    public Guid EmployeeId { get; private set; }

    public PointEntryKind Kind { get; private set; }

    /// <summary>Signed: positive credits, negative debits.</summary>
    public int Points { get; private set; }

    /// <summary>Unspent part of a credit; zero for debits.</summary>
    public int RemainingPoints { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public string Note { get; private set; }

    /// <summary>Set once the 30-day expiry warning has gone out.</summary>
    public bool ExpiryWarned { get; private set; }

    public bool IsCredit => Points > 0;

    public static PointEntry Credit(Guid employeeId, PointEntryKind kind, int points, DateOnly on, DateOnly? expiresOn, string? sourceType, Guid? sourceId, string note)
    {
        if (points <= 0)
        {
            throw new DomainRuleException("POINTS_CREDIT", "A credit adds points.");
        }

        return new PointEntry
        {
            EmployeeId = employeeId,
            Kind = kind,
            Points = points,
            RemainingPoints = points,
            OccurredOn = on,
            ExpiresOn = expiresOn,
            SourceType = sourceType,
            SourceId = sourceId,
            Note = Guard.MaxLength(note, 300),
        };
    }

    public static PointEntry Debit(Guid employeeId, PointEntryKind kind, int points, DateOnly on, string? sourceType, Guid? sourceId, string note)
    {
        if (points <= 0)
        {
            throw new DomainRuleException("POINTS_DEBIT", "A debit takes points.");
        }

        return new PointEntry
        {
            EmployeeId = employeeId,
            Kind = kind,
            Points = -points,
            OccurredOn = on,
            SourceType = sourceType,
            SourceId = sourceId,
            Note = Guard.MaxLength(note, 300),
        };
    }

    /// <summary>Spends up to <paramref name="points"/> from this credit; returns how many it covered.</summary>
    public int Consume(int points)
    {
        int taken = Math.Min(points, RemainingPoints);
        RemainingPoints -= taken;
        return taken;
    }

    public void Warned() => ExpiryWarned = true;

    /// <summary>
    /// Spends points across credits, soonest-expiring first (never-expiring last), oldest first
    /// among equals. Throws if the credits do not cover it.
    /// </summary>
    public static void SpendFifo(IReadOnlyList<PointEntry> credits, int points)
    {
        ArgumentNullException.ThrowIfNull(credits);
        if (credits.Sum(credit => credit.RemainingPoints) < points)
        {
            throw new DomainRuleException("POINTS_INSUFFICIENT", "Not enough points.");
        }

        int left = points;
        foreach (PointEntry credit in credits.Where(credit => credit.RemainingPoints > 0).OrderBy(credit => credit.ExpiresOn ?? DateOnly.MaxValue).ThenBy(credit => credit.OccurredOn))
        {
            left -= credit.Consume(left);
            if (left == 0)
            {
                break;
            }
        }
    }
}

/// <summary>Something points can buy.</summary>
public sealed class CatalogItem : TenantAggregateRoot
{
    private CatalogItem()
    {
        Name = null!;
        Category = null!;
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string Category { get; private set; }

    public int PointsCost { get; private set; }

    /// <summary>Units left; empty for unlimited (e.g. vouchers issued on demand).</summary>
    public int? Stock { get; private set; }

    public static CatalogItem Create(string name, string? description, string category, int pointsCost, int? stock)
    {
        var item = new CatalogItem();
        item.Update(name, description, category, pointsCost, stock, active: true);
        return item;
    }

    public void Update(string name, string? description, string category, int pointsCost, int? stock, bool active)
    {
        if (pointsCost is < 1 or > 10_000_000 || stock is < 0)
        {
            throw new DomainRuleException("CATALOG_ITEM", "An item costs at least one point and stock cannot be negative.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 150);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 1000);
        Category = Guard.MaxLength(Guard.NotNullOrWhiteSpace(category).Trim(), 60);
        PointsCost = pointsCost;
        Stock = stock;
        IsActive = active;
    }

    public void Take()
    {
        if (!IsActive)
        {
            throw new DomainRuleException("CATALOG_ITEM_INACTIVE", "The item is no longer available.");
        }

        if (Stock is { } stock)
        {
            if (stock == 0)
            {
                throw new DomainRuleException("CATALOG_OUT_OF_STOCK", "The item is out of stock.");
            }

            Stock = stock - 1;
        }
    }

    public void PutBack()
    {
        if (Stock is { } stock)
        {
            Stock = stock + 1;
        }
    }
}

public enum RedemptionStatus
{
    Requested = 1,
    Fulfilled = 2,
    Cancelled = 3,
}

/// <summary>Points exchanged for a catalogue item, fulfilled by the programme team or cancelled with a refund.</summary>
public sealed class Redemption : TenantAggregateRoot
{
    private Redemption()
    {
        ItemName = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid ItemId { get; private set; }

    public string ItemName { get; private set; }

    public int Points { get; private set; }

    public RedemptionStatus Status { get; private set; }

    public string? Note { get; private set; }

    public DateOnly RequestedOn { get; private set; }

    public DateOnly? FulfilledOn { get; private set; }

    public static Redemption Request(Guid employeeId, CatalogItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new Redemption { EmployeeId = employeeId, ItemId = item.Id, ItemName = item.Name, Points = item.PointsCost, Status = RedemptionStatus.Requested, RequestedOn = today };
    }

    public void Fulfil(string? note, DateOnly today)
    {
        EnsureRequested();
        Status = RedemptionStatus.Fulfilled;
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 500);
        FulfilledOn = today;
    }

    public void Cancel(string? note)
    {
        EnsureRequested();
        Status = RedemptionStatus.Cancelled;
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 500);
    }

    private void EnsureRequested()
    {
        if (Status != RedemptionStatus.Requested)
        {
            throw new DomainRuleException("REDEMPTION_DONE", "The redemption has already been fulfilled or cancelled.");
        }
    }
}

/// <summary>Points a department (or the whole company) may award through manager recognition and awards in a year.</summary>
public sealed class RewardBudget : TenantAggregateRoot
{
    public int Year { get; private set; }

    /// <summary>Empty for the company-wide budget.</summary>
    public Guid? DepartmentId { get; private set; }

    public int Points { get; private set; }

    public static RewardBudget Create(int year, Guid? departmentId, int points)
    {
        var budget = new RewardBudget { Year = year, DepartmentId = departmentId };
        budget.Update(points);
        return budget;
    }

    public void Update(int points)
    {
        if (points is < 0 or > 100_000_000)
        {
            throw new DomainRuleException("REWARD_BUDGET", "A budget runs 0 to 100,000,000 points.");
        }

        Points = points;
    }
}

internal static class RewardDefaults
{
    public static readonly (string Code, string Name, string Description, RecognitionKind Kind, int Points, bool Approval, decimal? Cash)[] Types =
    [
        ("THANKS", "Thank you", "A quick thanks to a colleague who helped.", RecognitionKind.Peer, 50, false, null),
        ("TEAM_PLAYER", "Team player", "Went out of their way for the team.", RecognitionKind.Peer, 100, false, null),
        ("CUSTOMER_HERO", "Customer hero", "Delighted a customer or client.", RecognitionKind.Manager, 300, false, null),
        ("SPOT_AWARD", "Spot award", "Outstanding work deserving an on-the-spot award.", RecognitionKind.Manager, 1000, true, 2000m),
        ("STAR_QUARTER", "Star of the quarter", "The quarter's standout contributor.", RecognitionKind.Award, 5000, true, 10000m),
    ];

    public static readonly (string Name, RuleTrigger Trigger, int Points, int Threshold, bool PerYear, string Message)[] Rules =
    [
        ("Work anniversary", RuleTrigger.WorkAnniversary, 200, 1, true, "Happy {years}-year work anniversary!"),
        ("Exceptional review", RuleTrigger.ReviewRating, 1000, 4, false, "Recognised for an exceptional review (rating {rating})."),
        ("Course completed", RuleTrigger.CourseCompleted, 100, 0, false, "Completed the course \"{course}\"."),
    ];

    public static readonly (string Name, string Description, string Category, int Cost, int? Stock)[] Catalog =
    [
        ("Amazon voucher 500", "An Amazon gift voucher worth 500, emailed within two working days.", "Vouchers", 500, null),
        ("Amazon voucher 2000", "An Amazon gift voucher worth 2,000, emailed within two working days.", "Vouchers", 2000, null),
        ("Company hoodie", "A branded hoodie in your size.", "Merchandise", 1200, 50),
        ("Noise-cancelling headphones", "Over-ear wireless headphones.", "Electronics", 8000, 10),
        ("Extra day off", "One extra paid day off, to be planned with your manager.", "Experiences", 3000, null),
    ];
}
