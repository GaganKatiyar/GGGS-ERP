using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Compensation.Domain;

/// <summary>
/// A salary band for a grade (spec §23): the annual minimum, midpoint and maximum. A new band from a
/// later date replaces the old one; compa-ratio measures each person against the midpoint.
/// </summary>
public sealed class SalaryBand : TenantAggregateRoot
{
    private SalaryBand()
    {
        Currency = null!;
    }

    public Guid GradeId { get; private set; }

    public string Currency { get; private set; }

    public decimal Minimum { get; private set; }

    public decimal Midpoint { get; private set; }

    public decimal Maximum { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public string? Notes { get; private set; }

    public static SalaryBand Create(Guid gradeId, string currency, decimal minimum, decimal midpoint, decimal maximum, DateOnly effectiveFrom, string? notes)
    {
        var band = new SalaryBand { GradeId = Guard.NotEmpty(gradeId) };
        band.Update(currency, minimum, midpoint, maximum, effectiveFrom, notes);
        return band;
    }

    public void Update(string currency, decimal minimum, decimal midpoint, decimal maximum, DateOnly effectiveFrom, string? notes)
    {
        if (minimum <= 0 || minimum > midpoint || midpoint > maximum)
        {
            throw new DomainRuleException("BAND_RANGE", "A band runs minimum ≤ midpoint ≤ maximum, all above zero.");
        }

        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3);
        Minimum = Math.Round(minimum, 2);
        Midpoint = Math.Round(midpoint, 2);
        Maximum = Math.Round(maximum, 2);
        EffectiveFrom = effectiveFrom;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 500);
    }

    /// <summary>CTC over the midpoint: 1.00 is on the midpoint.</summary>
    public decimal CompaRatio(decimal annualCtc) => Midpoint == 0 ? 0 : Math.Round(annualCtc / Midpoint, 2);

    /// <summary>Where a CTC sits in the band's range: 0% at the minimum, 100% at the maximum.</summary>
    public decimal Penetration(decimal annualCtc) => Maximum == Minimum ? 0 : Math.Round((annualCtc - Minimum) * 100 / (Maximum - Minimum), 1);

    public string Position(decimal annualCtc) => annualCtc < Minimum ? "Below" : annualCtc > Maximum ? "Above" : "Within";
}

public enum CycleStatus
{
    Planning = 1,
    Open = 2,
    Closed = 3,
}

/// <summary>A review cycle (spec §23 "merit"): a budget as a share of current pay, and the revisions raised in it.</summary>
public sealed class CompensationCycle : TenantAggregateRoot
{
    private CompensationCycle()
    {
        Name = null!;
    }

    public string Name { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public decimal BudgetPercent { get; private set; }

    public CycleStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public static CompensationCycle Plan(string name, DateOnly effectiveDate, decimal budgetPercent, string? notes)
    {
        var cycle = new CompensationCycle { Status = CycleStatus.Planning };
        cycle.Update(name, effectiveDate, budgetPercent, notes);
        return cycle;
    }

    public void Update(string name, DateOnly effectiveDate, decimal budgetPercent, string? notes)
    {
        if (budgetPercent is < 0 or > 100)
        {
            throw new DomainRuleException("CYCLE_BUDGET", "The budget is a percentage of current pay, 0-100.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        EffectiveDate = effectiveDate;
        BudgetPercent = budgetPercent;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }

    public void Open()
    {
        if (Status != CycleStatus.Planning)
        {
            throw new DomainRuleException("CYCLE_STATE", "Only a cycle being planned can open.");
        }

        Status = CycleStatus.Open;
    }

    public void Close() => Status = CycleStatus.Closed;
}

public enum RevisionType
{
    Merit = 1,
    Promotion = 2,
    MarketAdjustment = 3,
    Correction = 4,
    Retention = 5,
}

public enum RevisionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Applied = 4,
    Rejected = 5,
    Cancelled = 6,
}

/// <summary>
/// A salary revision (spec §23): a merit increase, promotion, market adjustment or correction, from a
/// date, approved through the workflow and then applied to payroll as the person's new CTC.
/// </summary>
public sealed class CompensationRevision : TenantAggregateRoot
{
    private CompensationRevision()
    {
        Currency = null!;
        Justification = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid? CycleId { get; private set; }

    public RevisionType Type { get; private set; }

    public decimal CurrentCtc { get; private set; }

    public decimal ProposedCtc { get; private set; }

    public string Currency { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public Guid? NewGradeId { get; private set; }

    public Guid? NewStructureId { get; private set; }

    public string? NewDesignation { get; private set; }

    public string Justification { get; private set; }

    public string? PerformanceRating { get; private set; }

    public RevisionStatus Status { get; private set; }

    public Guid? ProposedByEmployeeId { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public DateTime? AppliedDate { get; private set; }

    public Guid? PayrollSalaryId { get; private set; }

    /// <summary>Why applying to payroll failed (a date inside a finalized month), until it is retried.</summary>
    public string? ApplyError { get; private set; }

    public decimal IncreasePercent => CurrentCtc == 0 ? 0 : Math.Round((ProposedCtc - CurrentCtc) * 100 / CurrentCtc, 2);

    public static CompensationRevision Propose(Guid employeeId, decimal currentCtc, string currency, Guid? proposedBy, RevisionDetails details)
    {
        var revision = new CompensationRevision
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            CurrentCtc = currentCtc,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            ProposedByEmployeeId = proposedBy,
            Status = RevisionStatus.Draft,
        };
        revision.Update(details);
        return revision;
    }

    public void Update(RevisionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status != RevisionStatus.Draft)
        {
            throw new DomainRuleException("REVISION_STATE", "Only a draft revision can be changed.");
        }

        if (details.ProposedCtc <= 0)
        {
            throw new DomainRuleException("REVISION_CTC", "Propose a positive annual CTC.");
        }

        if (details.Type != RevisionType.Correction && details.ProposedCtc < CurrentCtc)
        {
            throw new DomainRuleException("REVISION_CTC", "Only a correction can lower pay.");
        }

        CycleId = details.CycleId;
        Type = details.Type;
        ProposedCtc = Math.Round(details.ProposedCtc, 2);
        EffectiveDate = details.EffectiveDate;
        NewGradeId = details.NewGradeId;
        NewStructureId = details.NewStructureId;
        NewDesignation = string.IsNullOrWhiteSpace(details.NewDesignation) ? null : Guard.MaxLength(details.NewDesignation.Trim(), 200);
        Justification = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Justification).Trim(), 2000);
        PerformanceRating = string.IsNullOrWhiteSpace(details.PerformanceRating) ? null : Guard.MaxLength(details.PerformanceRating.Trim(), 50);
    }

    public void Submit()
    {
        if (Status != RevisionStatus.Draft)
        {
            throw new DomainRuleException("REVISION_STATE", "Only a draft revision can be submitted.");
        }

        Status = RevisionStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool approved, DateTime now)
    {
        if (Status != RevisionStatus.PendingApproval)
        {
            return;
        }

        Status = approved ? RevisionStatus.Approved : RevisionStatus.Rejected;
        DecidedDate = now;
    }

    /// <summary>Sent back by an approver: a draft again, to rework.</summary>
    public void ReturnToDraft()
    {
        if (Status == RevisionStatus.PendingApproval)
        {
            Status = RevisionStatus.Draft;
        }
    }

    public void Applied(Guid salaryId, DateTime now)
    {
        Status = RevisionStatus.Applied;
        PayrollSalaryId = salaryId;
        AppliedDate = now;
        ApplyError = null;
    }

    public void ApplyFailed(string error) => ApplyError = Guard.MaxLength(error, 500);

    /// <summary>Moves an approved revision that payroll refused to a later date, to try again.</summary>
    public void Reschedule(DateOnly effectiveDate)
    {
        if (Status != RevisionStatus.Approved)
        {
            throw new DomainRuleException("REVISION_STATE", "Only an approved revision waiting to be applied can be rescheduled.");
        }

        EffectiveDate = effectiveDate;
    }

    public void Cancel()
    {
        if (Status is RevisionStatus.Applied or RevisionStatus.Rejected)
        {
            throw new DomainRuleException("REVISION_STATE", $"A {Status.ToString().ToLowerInvariant()} revision cannot be cancelled.");
        }

        Status = RevisionStatus.Cancelled;
    }
}

public sealed record RevisionDetails(
    Guid? CycleId,
    RevisionType Type,
    decimal ProposedCtc,
    DateOnly EffectiveDate,
    Guid? NewGradeId,
    Guid? NewStructureId,
    string? NewDesignation,
    string Justification,
    string? PerformanceRating);

public enum AwardType
{
    Bonus = 1,
    Commission = 2,
    Incentive = 3,
    PerformanceBonus = 4,
    ProjectIncentive = 5,
    Referral = 6,
    Retention = 7,
    SpotAward = 8,
}

public enum AwardStatus
{
    PendingApproval = 1,
    Approved = 2,
    Paid = 3,
    Rejected = 4,
    Cancelled = 5,
}

/// <summary>
/// Variable pay (spec §23, §94): a bonus, commission or incentive for one person, approved and then
/// paid through payroll in the chosen month.
/// </summary>
public sealed class VariablePayAward : TenantAggregateRoot
{
    private VariablePayAward()
    {
        Currency = null!;
        Reason = null!;
    }

    public Guid EmployeeId { get; private set; }

    public AwardType Type { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The first day of the month it is paid in.</summary>
    public DateOnly PayoutMonth { get; private set; }

    public string Reason { get; private set; }

    public Guid? ProjectId { get; private set; }

    public AwardStatus Status { get; private set; }

    public Guid? ProposedByEmployeeId { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public Guid? PayrollInputId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public static VariablePayAward Propose(Guid employeeId, AwardType type, decimal amount, string currency, DateOnly payoutMonth, string reason, Guid? projectId, Guid? proposedBy)
    {
        if (amount <= 0)
        {
            throw new DomainRuleException("AWARD_AMOUNT", "An award must be more than zero.");
        }

        return new VariablePayAward
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            Type = type,
            Amount = Math.Round(amount, 2),
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            PayoutMonth = new DateOnly(payoutMonth.Year, payoutMonth.Month, 1),
            Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500),
            ProjectId = projectId,
            ProposedByEmployeeId = proposedBy,
            Status = AwardStatus.PendingApproval,
        };
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool approved, DateTime now)
    {
        if (Status != AwardStatus.PendingApproval)
        {
            return;
        }

        Status = approved ? AwardStatus.Approved : AwardStatus.Rejected;
        DecidedDate = now;
    }

    public void SentToPayroll(Guid inputId) => PayrollInputId = inputId;

    public void MarkPaid() => Status = AwardStatus.Paid;

    public void Cancel()
    {
        if (Status is AwardStatus.Paid or AwardStatus.Rejected)
        {
            throw new DomainRuleException("AWARD_STATE", $"A {Status.ToString().ToLowerInvariant()} award cannot be cancelled.");
        }

        Status = AwardStatus.Cancelled;
    }
}

public enum GrantStatus
{
    Active = 1,
    Cancelled = 2,
    Exercised = 3,
}

/// <summary>One vesting event: on this date this many units vest.</summary>
public sealed record VestingEvent(DateOnly Date, decimal Units, decimal Cumulative);

/// <summary>
/// An equity grant (spec §23 "ESOP metadata"): options or units, the strike price, and a vesting
/// schedule with a cliff - recorded and tracked here, administered by the plan's trustee.
/// </summary>
public sealed class EquityGrant : TenantAggregateRoot
{
    private EquityGrant()
    {
        PlanName = null!;
        Currency = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string PlanName { get; private set; }

    public DateOnly GrantDate { get; private set; }

    public decimal Units { get; private set; }

    public decimal StrikePrice { get; private set; }

    public decimal? FairValuePerUnit { get; private set; }

    public string Currency { get; private set; }

    public DateOnly VestingStart { get; private set; }

    public int VestingMonths { get; private set; }

    public int CliffMonths { get; private set; }

    /// <summary>How often units vest after the cliff: 1 (monthly), 3 (quarterly) or 12 (yearly).</summary>
    public int VestingFrequencyMonths { get; private set; }

    public decimal ExercisedUnits { get; private set; }

    public GrantStatus Status { get; private set; }

    public DateOnly? CancelledOn { get; private set; }

    public string? Notes { get; private set; }

    public static EquityGrant Create(Guid employeeId, GrantDetails details)
    {
        var grant = new EquityGrant { EmployeeId = Guard.NotEmpty(employeeId), Status = GrantStatus.Active };
        grant.Update(details);
        return grant;
    }

    public void Update(GrantDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Units <= 0 || details.StrikePrice < 0 || details.FairValuePerUnit < 0)
        {
            throw new DomainRuleException("GRANT_UNITS", "A grant has units above zero and no negative prices.");
        }

        if (details.VestingMonths is < 1 or > 120 || details.CliffMonths < 0 || details.CliffMonths > details.VestingMonths || details.VestingFrequencyMonths is not (1 or 3 or 6 or 12))
        {
            throw new DomainRuleException("GRANT_VESTING", "Vesting runs 1-120 months, the cliff within it, vesting monthly, quarterly, half-yearly or yearly.");
        }

        if (details.Units < ExercisedUnits)
        {
            throw new DomainRuleException("GRANT_UNITS", "A grant cannot be smaller than what has been exercised.");
        }

        PlanName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.PlanName).Trim(), 200);
        GrantDate = details.GrantDate;
        Units = details.Units;
        StrikePrice = details.StrikePrice;
        FairValuePerUnit = details.FairValuePerUnit;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Currency ?? "INR").Trim().ToUpperInvariant(), 3);
        VestingStart = details.VestingStart ?? details.GrantDate;
        VestingMonths = details.VestingMonths;
        CliffMonths = details.CliffMonths;
        VestingFrequencyMonths = details.VestingFrequencyMonths;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Guard.MaxLength(details.Notes.Trim(), 1000);
    }

    /// <summary>
    /// The vesting schedule: nothing before the cliff, the cliff's share on it, then equal parts at each
    /// interval to the end. Unvested units lapse when the grant is cancelled.
    /// </summary>
    public IReadOnlyList<VestingEvent> Schedule()
    {
        // Vesting months: the cliff (or the first interval), each interval after it, and the last month.
        var months = new SortedSet<int>();
        for (int month = Math.Max(CliffMonths, VestingFrequencyMonths); month < VestingMonths; month += VestingFrequencyMonths)
        {
            months.Add(month);
        }

        months.Add(VestingMonths);

        var events = new List<VestingEvent>();
        decimal cumulative = 0;
        foreach (int month in months)
        {
            DateOnly date = VestingStart.AddMonths(month);
            if (CancelledOn is { } cancelled && date > cancelled)
            {
                break;
            }

            decimal target = month == VestingMonths ? Units : Math.Round(Units * month / VestingMonths, 4);
            events.Add(new VestingEvent(date, target - cumulative, target));
            cumulative = target;
        }

        return events;
    }

    public decimal VestedOn(DateOnly day) => Schedule().Where(vest => vest.Date <= day).Select(vest => vest.Cumulative).DefaultIfEmpty(0).Max();

    public void Exercise(decimal units, DateOnly day)
    {
        if (Status != GrantStatus.Active && !(Status == GrantStatus.Cancelled && ExercisedUnits + units <= VestedOn(day)))
        {
            throw new DomainRuleException("GRANT_STATE", "This grant cannot be exercised.");
        }

        if (units <= 0 || ExercisedUnits + units > VestedOn(day))
        {
            throw new DomainRuleException("GRANT_EXERCISE", $"Only vested units can be exercised: {VestedOn(day) - ExercisedUnits:0.####} available.");
        }

        ExercisedUnits += units;
        if (ExercisedUnits >= Units)
        {
            Status = GrantStatus.Exercised;
        }
    }

    public void Cancel(DateOnly on)
    {
        if (Status != GrantStatus.Active)
        {
            throw new DomainRuleException("GRANT_STATE", "Only an active grant can be cancelled.");
        }

        Status = GrantStatus.Cancelled;
        CancelledOn = on;
    }
}

public sealed record GrantDetails(
    string PlanName,
    DateOnly GrantDate,
    decimal Units,
    decimal StrikePrice,
    decimal? FairValuePerUnit,
    string? Currency,
    DateOnly? VestingStart,
    int VestingMonths,
    int CliffMonths,
    int VestingFrequencyMonths,
    string? Notes);
