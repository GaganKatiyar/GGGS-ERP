using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Leave.Domain;

/// <summary>
/// A leave policy as HR names it ("Full-time India", "Interns"): the leave types it grants, each
/// with its own rules, and the month its leave year starts. Engagement types are assigned a policy;
/// single employees can be given another one for a while.
/// </summary>
public sealed class LeavePlan : TenantAggregateRoot
{
    private readonly List<LeavePlanItem> _items = [];

    private LeavePlan()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>1 for January to December, 4 for April to March, and so on.</summary>
    public int LeaveYearStartMonth { get; private set; }

    public IReadOnlyList<LeavePlanItem> Items => _items;

    public static LeavePlan Create(string code, string name, string? description, int leaveYearStartMonth)
    {
        var plan = new LeavePlan { Code = NormalizeCode(code) };
        plan.Update(name, description, leaveYearStartMonth);
        return plan;
    }

    public static string NormalizeCode(string code) => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32);

    public void Update(string name, string? description, int leaveYearStartMonth)
    {
        Guard.Against(leaveYearStartMonth is < 1 or > 12, "LEAVE_PLAN_YEAR_START", "The leave year starts in a month from 1 (January) to 12 (December).");
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
        LeaveYearStartMonth = leaveYearStartMonth;
    }

    public LeavePlanItem? ItemFor(Guid leaveTypeId) => _items.FirstOrDefault(item => item.LeaveTypeId == leaveTypeId);

    /// <summary>
    /// Makes the policy grant exactly these leave types with these rules. Returns the items that
    /// were taken out, for the caller to delete.
    /// </summary>
    public IReadOnlyList<LeavePlanItem> SetItems(IReadOnlyCollection<(Guid LeaveTypeId, PlanItemRules Rules)> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        if (wanted.Select(entry => entry.LeaveTypeId).Distinct().Count() != wanted.Count)
        {
            throw new DomainRuleException("LEAVE_PLAN_DUPLICATE_TYPE", "A leave type appears in the policy only once.");
        }

        List<LeavePlanItem> removed = [.. _items.Where(item => wanted.All(entry => entry.LeaveTypeId != item.LeaveTypeId))];
        foreach (LeavePlanItem item in removed)
        {
            _items.Remove(item);
        }

        foreach ((Guid leaveTypeId, PlanItemRules rules) in wanted)
        {
            if (ItemFor(leaveTypeId) is { } existing)
            {
                existing.Apply(rules);
            }
            else
            {
                _items.Add(LeavePlanItem.Create(Id, leaveTypeId, rules));
            }
        }

        return removed;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>One leave type inside a policy and the rules it follows there.</summary>
public sealed class LeavePlanItem : TenantAggregateRoot
{
    private LeavePlanItem()
    {
        EligibleGenderCodes = string.Empty;
    }

    public Guid PlanId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    // ----- Entitlement and accrual -----

    public decimal AnnualEntitlement { get; private set; }

    public AccrualFrequency Accrual { get; private set; }

    /// <summary>Days kept into the next leave year; null keeps everything, 0 keeps nothing.</summary>
    public decimal? CarryForwardLimit { get; private set; }

    /// <summary>How far below zero a balance may go; 0 allows none.</summary>
    public decimal MaxNegativeBalance { get; private set; }

    /// <summary>Weekly offs and holidays between two days of the same leave count as leave too.</summary>
    public bool CountSandwichedDays { get; private set; }

    // ----- Who can take it -----

    /// <summary>Comma-separated gender lookup codes; empty means everyone.</summary>
    public string EligibleGenderCodes { get; private set; }

    /// <summary>Months of service before the leave can be taken; 0 means from the first day.</summary>
    public int MinServiceMonths { get; private set; }

    public bool AllowDuringProbation { get; private set; }

    public bool AllowDuringNotice { get; private set; }

    // ----- Requests -----

    public decimal? MinDaysPerRequest { get; private set; }

    public int? MaxConsecutiveDays { get; private set; }

    public int? MaxRequestsPerYear { get; private set; }

    /// <summary>Days of notice an employee must give.</summary>
    public int MinNoticeDays { get; private set; }

    /// <summary>How many days back an employee may apply for; null means no limit.</summary>
    public int? MaxBackdatedDays { get; private set; }

    /// <summary>How far ahead an employee may apply; null means no limit.</summary>
    public int? MaxAdvanceDays { get; private set; }

    /// <summary>A request longer than this needs a supporting document before it goes for approval.</summary>
    public decimal? DocumentRequiredAboveDays { get; private set; }

    // ----- Accrual options -----

    public CreditTiming CreditTiming { get; private set; }

    /// <summary>Joining on or before this day of a month earns that month.</summary>
    public int JoiningCutoffDay { get; private set; }

    /// <summary>A quarterly, half-yearly or yearly grant shrinks for someone joining part-way through.</summary>
    public bool ProrateOnJoining { get; private set; }

    // ----- Year end -----

    /// <summary>Carried-forward days still unused this many months into the new leave year lapse.</summary>
    public int? CarryForwardExpiryMonths { get; private set; }

    /// <summary>Days above the carry-forward limit are paid out (up to <see cref="MaxEncashDays"/>) instead of lapsing.</summary>
    public bool EncashAtYearEnd { get; private set; }

    public decimal? MaxEncashDays { get; private set; }

    // ----- Lifetime -----

    /// <summary>Days granted each time the leave is taken - maternity, say - instead of a balance.</summary>
    public decimal? GrantPerOccurrence { get; private set; }

    /// <summary>How many times the leave can be taken in the whole of the employment.</summary>
    public int? MaxOccurrencesInEmployment { get; private set; }

    public IReadOnlyList<string> Genders => PlanItemRules.SplitCodes(EligibleGenderCodes);

    public PlanItemRules Rules => new(
        AnnualEntitlement, Accrual, CarryForwardLimit, MaxNegativeBalance, CountSandwichedDays,
        Genders, MinServiceMonths, AllowDuringProbation, AllowDuringNotice,
        MinDaysPerRequest, MaxConsecutiveDays, MaxRequestsPerYear, MinNoticeDays, MaxBackdatedDays, MaxAdvanceDays,
        DocumentRequiredAboveDays, CreditTiming, JoiningCutoffDay, ProrateOnJoining, CarryForwardExpiryMonths, EncashAtYearEnd, MaxEncashDays,
        GrantPerOccurrence, MaxOccurrencesInEmployment);

    internal static LeavePlanItem Create(Guid planId, Guid leaveTypeId, PlanItemRules rules)
    {
        var item = new LeavePlanItem { PlanId = Guard.NotEmpty(planId), LeaveTypeId = Guard.NotEmpty(leaveTypeId) };
        item.Apply(rules);
        return item;
    }

    internal void Apply(PlanItemRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        AnnualEntitlement = rules.AnnualEntitlement;
        Accrual = rules.Accrual;
        CarryForwardLimit = rules.CarryForwardLimit;
        MaxNegativeBalance = rules.MaxNegativeBalance;
        CountSandwichedDays = rules.CountSandwichedDays;
        EligibleGenderCodes = string.Join(',', rules.EligibleGenderCodes.Select(code => code.Trim().ToUpperInvariant()).Where(code => code.Length > 0).Distinct());
        MinServiceMonths = rules.MinServiceMonths;
        AllowDuringProbation = rules.AllowDuringProbation;
        AllowDuringNotice = rules.AllowDuringNotice;
        MinDaysPerRequest = rules.MinDaysPerRequest;
        MaxConsecutiveDays = rules.MaxConsecutiveDays;
        MaxRequestsPerYear = rules.MaxRequestsPerYear;
        MinNoticeDays = rules.MinNoticeDays;
        MaxBackdatedDays = rules.MaxBackdatedDays;
        MaxAdvanceDays = rules.MaxAdvanceDays;
        DocumentRequiredAboveDays = rules.DocumentRequiredAboveDays;
        CreditTiming = rules.CreditTiming;
        JoiningCutoffDay = rules.JoiningCutoffDay;
        ProrateOnJoining = rules.ProrateOnJoining;
        CarryForwardExpiryMonths = rules.CarryForwardExpiryMonths;
        EncashAtYearEnd = rules.EncashAtYearEnd;
        MaxEncashDays = rules.EncashAtYearEnd ? rules.MaxEncashDays : null;
        GrantPerOccurrence = rules.GrantPerOccurrence;
        MaxOccurrencesInEmployment = rules.MaxOccurrencesInEmployment;
    }
}

/// <summary>The rules one leave type follows inside a policy, as HR sets them.</summary>
public sealed record PlanItemRules(
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    decimal MaxNegativeBalance,
    bool CountSandwichedDays,
    IReadOnlyList<string> EligibleGenderCodes,
    int MinServiceMonths,
    bool AllowDuringProbation,
    bool AllowDuringNotice,
    decimal? MinDaysPerRequest,
    int? MaxConsecutiveDays,
    int? MaxRequestsPerYear,
    int MinNoticeDays,
    int? MaxBackdatedDays,
    int? MaxAdvanceDays,
    decimal? DocumentRequiredAboveDays = null,
    CreditTiming CreditTiming = CreditTiming.StartOfPeriod,
    int JoiningCutoffDay = 15,
    bool ProrateOnJoining = true,
    int? CarryForwardExpiryMonths = null,
    bool EncashAtYearEnd = false,
    decimal? MaxEncashDays = null,
    decimal? GrantPerOccurrence = null,
    int? MaxOccurrencesInEmployment = null)
{
    /// <summary>The leave is granted afresh each time it is taken, not drawn from a balance.</summary>
    public bool IsPerOccurrence => GrantPerOccurrence is not null;

    public static IReadOnlyList<string> SplitCodes(string? codes)
        => string.IsNullOrWhiteSpace(codes) ? [] : [.. codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void Validate()
    {
        Rule(AnnualEntitlement is < 0 or > 366, "LEAVE_POLICY_ENTITLEMENT", "The yearly entitlement is between 0 and 366 days.");
        Rule(!Enum.IsDefined(Accrual), "LEAVE_POLICY_ACCRUAL", "Unknown accrual frequency.");
        Rule(CarryForwardLimit is < 0 || MaxNegativeBalance is < 0 or > 366, "LEAVE_POLICY_LIMITS", "Carry-forward and negative-balance limits cannot be negative.");
        Rule(MinServiceMonths is < 0 or > 600, "LEAVE_POLICY_SERVICE", "Months of service before the leave can be taken are between 0 and 600.");
        Rule(MinDaysPerRequest is <= 0 or > 366 || (MinDaysPerRequest is { } min && min * 2 != decimal.Truncate(min * 2)), "LEAVE_POLICY_MIN_DAYS", "The shortest request is a whole or half number of days.");
        Rule(MaxConsecutiveDays is < 1 or > 366, "LEAVE_POLICY_RULES", "The longest stretch is at least a day.");
        Rule(MinDaysPerRequest is { } shortest && MaxConsecutiveDays is { } longest && shortest > longest, "LEAVE_POLICY_MIN_DAYS", "The shortest request cannot be longer than the longest.");
        Rule(MaxRequestsPerYear is < 1 or > 366, "LEAVE_POLICY_REQUESTS", "The yearly request limit is at least one.");
        Rule(MinNoticeDays is < 0 or > 365, "LEAVE_POLICY_RULES", "The notice period is 0 to 365 days.");
        Rule(MaxBackdatedDays is < 0 or > 365 || MaxAdvanceDays is < 0 or > 730, "LEAVE_POLICY_WINDOW", "Backdated and advance limits are 0 to 365 and 0 to 730 days.");
        Rule(MaxAdvanceDays is { } advance && advance < MinNoticeDays, "LEAVE_POLICY_WINDOW", "The advance limit cannot be shorter than the notice period.");
        Rule(EligibleGenderCodes is null, "LEAVE_POLICY_GENDER", "Say which genders may take the leave, or none for everyone.");
        Rule(DocumentRequiredAboveDays is < 0 or > 366, "LEAVE_POLICY_DOCUMENT", "A document is required above 0 to 366 days.");
        Rule(!Enum.IsDefined(CreditTiming), "LEAVE_POLICY_ACCRUAL", "Unknown crediting time.");
        Rule(JoiningCutoffDay is < 1 or > 31, "LEAVE_POLICY_CUTOFF", "The joining cut-off is a day of the month, 1 to 31.");
        Rule(CarryForwardExpiryMonths is < 1 or > 11, "LEAVE_POLICY_EXPIRY", "Carried-forward days expire 1 to 11 months into the new leave year.");
        Rule(CarryForwardExpiryMonths is not null && CarryForwardLimit is 0, "LEAVE_POLICY_EXPIRY", "Nothing is carried forward, so nothing can expire.");
        Rule(MaxEncashDays is < 0 or > 366, "LEAVE_POLICY_ENCASH", "Encashment is limited to 0 to 366 days.");
        Rule(EncashAtYearEnd && CarryForwardLimit is null, "LEAVE_POLICY_ENCASH", "Encashment pays out days above the carry-forward limit; set a limit first.");
        Rule(GrantPerOccurrence is <= 0 or > 366, "LEAVE_POLICY_GRANT", "Each occurrence grants 0.5 to 366 days.");
        Rule(MaxOccurrencesInEmployment is < 1 or > 50, "LEAVE_POLICY_OCCURRENCES", "A lifetime limit is 1 to 50 times.");
        Rule(MaxOccurrencesInEmployment is not null && GrantPerOccurrence is null, "LEAVE_POLICY_OCCURRENCES", "A lifetime limit applies to leave granted per occurrence; set the days each occurrence grants.");
        Rule(GrantPerOccurrence is not null && Accrual != AccrualFrequency.None, "LEAVE_POLICY_GRANT", "Leave granted per occurrence does not accrue; choose 'Granted by HR' as the crediting.");
    }

    private static void Rule(bool broken, string code, string message)
    {
        if (broken)
        {
            throw new DomainRuleException(code, message);
        }
    }
}

/// <summary>When an accrual period's days are credited.</summary>
public enum CreditTiming
{
    /// <summary>On the first day of the period: the days can be used straight away.</summary>
    StartOfPeriod = 1,

    /// <summary>On the last day of the period: the days are earned by working it.</summary>
    EndOfPeriod = 2,
}

/// <summary>The policy every employee of one engagement type follows.</summary>
public sealed class LeavePlanAssignment : TenantAggregateRoot
{
    private LeavePlanAssignment()
    {
        EmploymentTypeCode = null!;
    }

    public string EmploymentTypeCode { get; private set; }

    public Guid PlanId { get; private set; }

    public static LeavePlanAssignment Create(string employmentTypeCode, Guid planId) => new()
    {
        EmploymentTypeCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(employmentTypeCode).Trim().ToUpperInvariant(), 64),
        PlanId = Guard.NotEmpty(planId),
    };

    public void Reassign(Guid planId) => PlanId = Guard.NotEmpty(planId);
}

/// <summary>A single employee on a policy other than their engagement type's, from a date and perhaps until one.</summary>
public sealed class EmployeeLeavePlan : TenantAggregateRoot
{
    private EmployeeLeavePlan()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public string? Reason { get; private set; }

    public static EmployeeLeavePlan Create(Guid employeeId, Guid planId, DateOnly effectiveFrom, DateOnly? effectiveTo, string? reason)
    {
        var exception = new EmployeeLeavePlan { EmployeeId = Guard.NotEmpty(employeeId) };
        exception.Update(planId, effectiveFrom, effectiveTo, reason);
        return exception;
    }

    public void Update(Guid planId, DateOnly effectiveFrom, DateOnly? effectiveTo, string? reason)
    {
        Guard.Against(effectiveTo is { } to && to < effectiveFrom, "LEAVE_EXCEPTION_DATES", "The exception cannot end before it starts.");
        PlanId = Guard.NotEmpty(planId);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300);
    }

    public bool Covers(DateOnly day) => EffectiveFrom <= day && (EffectiveTo is null || EffectiveTo >= day);

    public bool Overlaps(DateOnly from, DateOnly? to) => EffectiveFrom <= (to ?? DateOnly.MaxValue) && from <= (EffectiveTo ?? DateOnly.MaxValue);
}
