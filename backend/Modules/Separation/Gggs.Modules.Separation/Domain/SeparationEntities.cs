using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Separation.Domain;

public enum SeparationType
{
    Resignation = 1,
    Termination = 2,
    Retirement = 3,
    ContractEnd = 4,
    Mutual = 5,
    Absconding = 6,
    Death = 7,

    /// <summary>Positions cut for business reasons (Industrial Relations Code, 2020 s.70): compensation is due.</summary>
    Retrenchment = 8,
}

public enum CaseStatus
{
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    Completed = 5,
}

/// <summary>
/// One person leaving (spec: separation): a resignation approved through the workflow, or an exit
/// HR starts; notice served to an agreed last working day, clearance, exit interview, full and
/// final settlement and letters, then the exit itself on the last day.
/// </summary>
public sealed class SeparationCase : TenantAggregateRoot
{
    private SeparationCase()
    {
        Reason = null!;
    }

    public Guid EmployeeId { get; private set; }

    public SeparationType Type { get; private set; }

    public string Reason { get; private set; }

    /// <summary>The day notice was given.</summary>
    public DateOnly NoticeDate { get; private set; }

    public int NoticePeriodDays { get; private set; }

    public DateOnly RequestedLastDay { get; private set; }

    public DateOnly LastWorkingDay { get; private set; }

    public CaseStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>A notice shortfall is not recovered.</summary>
    public bool ShortfallWaived { get; private set; }

    public bool EligibleForRehire { get; private set; }

    public Guid? RelievingLetterId { get; private set; }

    public Guid? ExperienceLetterId { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    /// <summary>Primary reason from <see cref="SeparationReasons"/>.</summary>
    public string? ReasonCode { get; private set; }

    public string? SecondaryReasonCode { get; private set; }

    /// <summary>Whether the organisation regrets losing this person - set by the manager or HR.</summary>
    public bool? Regretted { get; private set; }

    /// <summary>What was tried to keep them (a conversation, a counter-offer) and how it went.</summary>
    public string? RetentionNote { get; private set; }

    /// <summary>An earlier last day the employee asked for, pending a decision.</summary>
    public DateOnly? RequestedReleaseDay { get; private set; }

    /// <summary>With the early release, the employee pays for the notice not served.</summary>
    public bool ReleaseWithBuyout { get; private set; }

    /// <summary>Paid to the last day but not working it.</summary>
    public bool GardenLeave { get; private set; }

    /// <summary>Who takes over the leaver's work; offboarding tasks for the successor go to them.</summary>
    public Guid? SuccessorEmployeeId { get; private set; }

    public string? HandoverNote { get; private set; }

    /// <summary>When the leaver's sign-in was switched off.</summary>
    public DateTime? AccessDisabledAt { get; private set; }

    /// <summary>Notice days the company pays instead of the employee working them (termination, retrenchment).</summary>
    public int PayInLieuDays { get; private set; }

    /// <summary>Who receives the settlement after a death.</summary>
    public string? NomineeName { get; private set; }

    public string? NomineeRelation { get; private set; }

    public string? NomineeContact { get; private set; }

    /// <summary>Opened by the system ahead of a retirement or contract end, waiting for HR to confirm.</summary>
    public bool Proposed { get; private set; }

    public string ExitReasonCode => Type switch
    {
        SeparationType.Resignation => "RESIGNATION",
        SeparationType.Termination => "TERMINATION",
        SeparationType.Retirement => "RETIREMENT",
        SeparationType.ContractEnd => "CONTRACT_END",
        SeparationType.Absconding => "ABSCONDING",
        SeparationType.Death => "DEATH",
        SeparationType.Retrenchment => "RETRENCHMENT",
        _ => "MUTUAL",
    };

    /// <summary>Days of notice not served, against the notice period.</summary>
    public int ShortfallDays => Math.Max(0, NoticePeriodDays - (LastWorkingDay.DayNumber - NoticeDate.DayNumber));

    public bool InProgress => Status is CaseStatus.PendingApproval or CaseStatus.Approved;

    public static SeparationCase Open(Guid employeeId, SeparationType type, string reason, DateOnly noticeDate, int noticePeriodDays, DateOnly requestedLastDay, string? reasonCode = null)
    {
        if (!SeparationReasons.IsKnown(reasonCode))
        {
            throw new DomainRuleException("SEPARATION_REASON", "Choose a reason from the list.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainRuleException("SEPARATION_TYPE", "Choose a separation type.");
        }

        if (noticePeriodDays is < 0 or > 180)
        {
            throw new DomainRuleException("SEPARATION_NOTICE", "The notice period runs 0 to 180 days.");
        }

        if (requestedLastDay < noticeDate)
        {
            throw new DomainRuleException("SEPARATION_LAST_DAY", "The last working day cannot be before notice is given.");
        }

        return new SeparationCase
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            Type = type,
            Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 1000),
            NoticeDate = noticeDate,
            NoticePeriodDays = noticePeriodDays,
            RequestedLastDay = requestedLastDay,
            LastWorkingDay = requestedLastDay,
            Status = CaseStatus.PendingApproval,
            EligibleForRehire = type is not (SeparationType.Termination or SeparationType.Absconding),
            ReasonCode = reasonCode ?? type switch
            {
                SeparationType.Retirement => "RETIREMENT",
                SeparationType.ContractEnd => "CONTRACT_END",
                SeparationType.Death => "DEATH",
                SeparationType.Absconding => "ABSCONDING",
                SeparationType.Retrenchment => "RESTRUCTURING",
                _ => null,
            },
        };
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Approve(DateOnly? lastWorkingDay)
    {
        if (Status != CaseStatus.PendingApproval)
        {
            throw new DomainRuleException("SEPARATION_NOT_PENDING", "The separation is not awaiting approval.");
        }

        Status = CaseStatus.Approved;
        if (lastWorkingDay is { } day)
        {
            SetLastDay(day);
        }
    }

    public void Reject()
    {
        if (Status != CaseStatus.PendingApproval)
        {
            throw new DomainRuleException("SEPARATION_NOT_PENDING", "The separation is not awaiting approval.");
        }

        Status = CaseStatus.Rejected;
    }

    public void Withdraw(DateOnly today)
    {
        if (!InProgress || today > LastWorkingDay)
        {
            throw new DomainRuleException("SEPARATION_WITHDRAW", "Only a separation still in progress, before the last day, can be withdrawn.");
        }

        Status = CaseStatus.Withdrawn;
    }

    /// <summary>HR agrees a different last day - an early release or an extension.</summary>
    public void SetLastDay(DateOnly lastWorkingDay)
    {
        if (!InProgress)
        {
            throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
        }

        if (lastWorkingDay < NoticeDate)
        {
            throw new DomainRuleException("SEPARATION_LAST_DAY", "The last working day cannot be before notice is given.");
        }

        LastWorkingDay = lastWorkingDay;
    }

    public void SetTerms(bool shortfallWaived, bool eligibleForRehire)
    {
        ShortfallWaived = shortfallWaived;
        EligibleForRehire = eligibleForRehire;
    }

    /// <summary>The manager's or HR's reading of the exit: why, whether it is a loss, what was tried.</summary>
    public void Classify(string? reasonCode, string? secondaryReasonCode, bool? regretted, string? retentionNote)
    {
        if (!SeparationReasons.IsKnown(reasonCode) || !SeparationReasons.IsKnown(secondaryReasonCode))
        {
            throw new DomainRuleException("SEPARATION_REASON", "Choose a reason from the list.");
        }

        ReasonCode = reasonCode;
        SecondaryReasonCode = secondaryReasonCode == reasonCode ? null : secondaryReasonCode;
        Regretted = regretted;
        RetentionNote = string.IsNullOrWhiteSpace(retentionNote) ? null : Guard.MaxLength(retentionNote.Trim(), 1000);
    }

    /// <summary>The employee asks to leave before the notice ends - optionally paying for the days not served.</summary>
    public void RequestEarlyRelease(DateOnly releaseDay, bool withBuyout)
    {
        if (!InProgress)
        {
            throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
        }

        if (releaseDay < NoticeDate || releaseDay >= LastWorkingDay)
        {
            throw new DomainRuleException("SEPARATION_RELEASE_DAY", "An early release day falls between the notice date and the current last day.");
        }

        RequestedReleaseDay = releaseDay;
        ReleaseWithBuyout = withBuyout;
    }

    /// <summary>
    /// HR decides an early release. Granted with a buyout, the shortfall is recovered in the
    /// settlement; granted without, the shortfall is waived.
    /// </summary>
    public void DecideEarlyRelease(bool granted)
    {
        if (RequestedReleaseDay is not { } day)
        {
            throw new DomainRuleException("SEPARATION_RELEASE_NONE", "No early release was asked for.");
        }

        if (granted)
        {
            SetLastDay(day);
            ShortfallWaived = !ReleaseWithBuyout;
        }

        RequestedReleaseDay = null;
    }

    public void SetGardenLeave(bool gardenLeave)
    {
        if (!InProgress)
        {
            throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
        }

        GardenLeave = gardenLeave;
    }

    public void SetHandover(Guid? successorEmployeeId, string? note)
    {
        if (!InProgress)
        {
            throw new DomainRuleException("SEPARATION_CLOSED", "The separation is closed.");
        }

        if (successorEmployeeId == EmployeeId)
        {
            throw new DomainRuleException("SEPARATION_SUCCESSOR", "A leaver cannot be their own successor.");
        }

        SuccessorEmployeeId = successorEmployeeId;
        HandoverNote = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 2000);
    }

    public void AccessDisabled(DateTime now) => AccessDisabledAt ??= now;

    public void SetPayInLieu(int days)
    {
        if (days is < 0 or > 180)
        {
            throw new DomainRuleException("SEPARATION_PAY_IN_LIEU", "Pay in lieu of notice runs 0 to 180 days.");
        }

        if (days > 0 && Type is SeparationType.Resignation or SeparationType.Absconding or SeparationType.Death)
        {
            throw new DomainRuleException("SEPARATION_PAY_IN_LIEU", "Pay in lieu of notice is for exits the company starts.");
        }

        PayInLieuDays = days;
    }

    public void SetNominee(string? name, string? relation, string? contact)
    {
        if (Type != SeparationType.Death && !string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("SEPARATION_NOMINEE", "A nominee is recorded when an employee has died.");
        }

        NomineeName = string.IsNullOrWhiteSpace(name) ? null : Guard.MaxLength(name.Trim(), 200);
        NomineeRelation = string.IsNullOrWhiteSpace(relation) ? null : Guard.MaxLength(relation.Trim(), 60);
        NomineeContact = string.IsNullOrWhiteSpace(contact) ? null : Guard.MaxLength(contact.Trim(), 200);
    }

    /// <summary>Marks a case the system opened for HR to confirm (a retirement or contract end coming up).</summary>
    public void Propose()
    {
        if (Status != CaseStatus.PendingApproval || Type is not (SeparationType.Retirement or SeparationType.ContractEnd))
        {
            throw new DomainRuleException("SEPARATION_PROPOSE", "Only a pending retirement or contract end can be proposed.");
        }

        Proposed = true;
    }

    /// <summary>A proposed exit HR has not yet confirmed or declined.</summary>
    public bool AwaitingConfirmation => Proposed && Status == CaseStatus.PendingApproval;

    public void LettersIssued(Guid relievingLetterId, Guid? experienceLetterId)
    {
        RelievingLetterId = relievingLetterId;
        ExperienceLetterId = experienceLetterId;
    }

    public void Complete(DateTime now)
    {
        if (Status != CaseStatus.Approved)
        {
            throw new DomainRuleException("SEPARATION_NOT_APPROVED", "Only an approved separation completes.");
        }

        Status = CaseStatus.Completed;
        CompletedAt = now;
    }
}

public enum ClearanceArea
{
    Manager = 1,
    It = 2,
    Admin = 3,
    Finance = 4,
    Assets = 5,
    Hr = 6,
}

public enum ClearanceStatus
{
    Pending = 1,
    Cleared = 2,
    Waived = 3,
}

/// <summary>One sign-off before someone leaves - a handover, an asset back, an advance settled - with anything to recover.</summary>
public sealed class ClearanceItem : TenantAggregateRoot
{
    private ClearanceItem()
    {
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    public ClearanceArea Area { get; private set; }

    public string Title { get; private set; }

    /// <summary>Who signs it off; anyone with clearance rights when empty.</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    public ClearanceStatus Status { get; private set; }

    /// <summary>Money recovered in the settlement (an unreturned asset, an unsettled advance).</summary>
    public decimal RecoveryAmount { get; private set; }

    public string? Note { get; private set; }

    public DateOnly? ClearedOn { get; private set; }

    public Guid? ClearedBy { get; private set; }

    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public static ClearanceItem Add(Guid caseId, ClearanceArea area, string title, Guid? ownerEmployeeId, decimal recoveryAmount = 0, string? sourceType = null, Guid? sourceId = null) => new()
    {
        CaseId = caseId,
        Area = area,
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200),
        OwnerEmployeeId = ownerEmployeeId,
        Status = ClearanceStatus.Pending,
        RecoveryAmount = Math.Max(0, recoveryAmount),
        SourceType = sourceType,
        SourceId = sourceId,
    };

    public void Resolve(ClearanceStatus status, decimal? recoveryAmount, string? note, Guid? by, DateOnly today)
    {
        if (status == ClearanceStatus.Pending)
        {
            ClearedOn = null;
            ClearedBy = null;
        }
        else
        {
            ClearedOn = today;
            ClearedBy = by;
        }

        if (recoveryAmount is < 0)
        {
            throw new DomainRuleException("CLEARANCE_RECOVERY", "A recovery cannot be negative.");
        }

        Status = status;
        RecoveryAmount = status == ClearanceStatus.Waived ? 0 : recoveryAmount ?? RecoveryAmount;
        Note = string.IsNullOrWhiteSpace(note) ? Note : Guard.MaxLength(note.Trim(), 500);
    }
}

/// <summary>The exit interview: why they are leaving and what would have kept them.</summary>
public sealed class ExitInterview : TenantAggregateRoot
{
    private ExitInterview()
    {
        PrimaryReason = null!;
    }

    public Guid CaseId { get; private set; }

    public string PrimaryReason { get; private set; }

    /// <summary>Overall experience, 1-5.</summary>
    public int OverallRating { get; private set; }

    public bool WouldRecommend { get; private set; }

    public bool WouldReturn { get; private set; }

    public string? WhatWentWell { get; private set; }

    public string? WhatToImprove { get; private set; }

    public Guid? ConductedBy { get; private set; }

    public DateOnly ConductedOn { get; private set; }

    public static ExitInterview Record(Guid caseId, string primaryReason, int overallRating, bool wouldRecommend, bool wouldReturn, string? wentWell, string? improve, Guid? conductedBy, DateOnly on)
    {
        var interview = new ExitInterview { CaseId = caseId };
        interview.Update(primaryReason, overallRating, wouldRecommend, wouldReturn, wentWell, improve, conductedBy, on);
        return interview;
    }

    public void Update(string primaryReason, int overallRating, bool wouldRecommend, bool wouldReturn, string? wentWell, string? improve, Guid? conductedBy, DateOnly on)
    {
        if (overallRating is < 1 or > 5)
        {
            throw new DomainRuleException("EXIT_INTERVIEW_RATING", "Rate the overall experience from 1 to 5.");
        }

        PrimaryReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(primaryReason).Trim(), 100);
        OverallRating = overallRating;
        WouldRecommend = wouldRecommend;
        WouldReturn = wouldReturn;
        WhatWentWell = string.IsNullOrWhiteSpace(wentWell) ? null : Guard.MaxLength(wentWell.Trim(), 2000);
        WhatToImprove = string.IsNullOrWhiteSpace(improve) ? null : Guard.MaxLength(improve.Trim(), 2000);
        ConductedBy = conductedBy;
        ConductedOn = on;
    }
}

public enum SettlementStatus
{
    Draft = 1,
    Approved = 2,
    Paid = 3,
}

/// <summary>The full and final settlement: what is paid out and recovered when someone leaves.</summary>
public sealed class Settlement : TenantAggregateRoot
{
    public Guid CaseId { get; private set; }

    public SettlementStatus Status { get; private set; }

    public decimal Earnings { get; private set; }

    public decimal Deductions { get; private set; }

    public decimal Net { get; private set; }

    public DateOnly? PaidOn { get; private set; }

    public string? PaymentReference { get; private set; }

    public static Settlement Start(Guid caseId) => new() { CaseId = caseId, Status = SettlementStatus.Draft };

    public void Total(IEnumerable<SettlementLine> lines)
    {
        EnsureDraft();
        List<SettlementLine> list = [.. lines];
        Earnings = list.Where(line => !line.IsDeduction).Sum(line => line.Amount);
        Deductions = list.Where(line => line.IsDeduction).Sum(line => line.Amount);
        Net = Earnings - Deductions;
    }

    public void Approve()
    {
        EnsureDraft();
        Status = SettlementStatus.Approved;
    }

    public void Reopen()
    {
        if (Status != SettlementStatus.Approved)
        {
            throw new DomainRuleException("SETTLEMENT_REOPEN", "Only an approved, unpaid settlement can be reopened.");
        }

        Status = SettlementStatus.Draft;
    }

    public void Pay(string? reference, DateOnly paidOn)
    {
        if (Status != SettlementStatus.Approved)
        {
            throw new DomainRuleException("SETTLEMENT_NOT_APPROVED", "Approve the settlement before paying it.");
        }

        Status = SettlementStatus.Paid;
        PaidOn = paidOn;
        PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100);
    }

    public void EnsureDraft()
    {
        if (Status != SettlementStatus.Draft)
        {
            throw new DomainRuleException("SETTLEMENT_NOT_DRAFT", "The settlement is approved; reopen it to change it.");
        }
    }
}

/// <summary>One line of a settlement; calculated lines are rebuilt on recalculation, manual lines kept.</summary>
public sealed class SettlementLine : TenantAggregateRoot
{
    private SettlementLine()
    {
        Code = null!;
        Label = null!;
    }

    public Guid SettlementId { get; private set; }

    public string Code { get; private set; }

    public string Label { get; private set; }

    public bool IsDeduction { get; private set; }

    public decimal Amount { get; private set; }

    public bool IsManual { get; private set; }

    /// <summary>How the figure was reached, for the employee and auditor.</summary>
    public string? Basis { get; private set; }

    public static SettlementLine Add(Guid settlementId, string code, string label, bool isDeduction, decimal amount, bool manual, string? basis)
    {
        if (amount < 0)
        {
            throw new DomainRuleException("SETTLEMENT_LINE", "A settlement line amount cannot be negative; mark it a deduction instead.");
        }

        return new SettlementLine
        {
            SettlementId = settlementId,
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
            Label = Guard.MaxLength(Guard.NotNullOrWhiteSpace(label).Trim(), 200),
            IsDeduction = isDeduction,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            IsManual = manual,
            Basis = basis is null ? null : Guard.MaxLength(basis, 300),
        };
    }
}

/// <summary>A former employee in the alumni network, for rehire and referrals.</summary>
public sealed class AlumniProfile : TenantAggregateRoot
{
    private AlumniProfile()
    {
        Name = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string Name { get; private set; }

    public string? Email { get; private set; }

    public string? LastDesignation { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public DateOnly ExitDate { get; private set; }

    public SeparationType SeparationType { get; private set; }

    public bool EligibleForRehire { get; private set; }

    /// <summary>Agreed to hear about openings and events.</summary>
    public bool OptedIn { get; private set; }

    public string? Notes { get; private set; }

    public static AlumniProfile Create(Guid employeeId, string name, string? email, string? designation, Guid? departmentId, DateOnly exitDate, SeparationType type, bool eligibleForRehire) => new()
    {
        EmployeeId = employeeId,
        Name = Guard.MaxLength(name, 200),
        Email = email is null ? null : Guard.MaxLength(email, 256),
        LastDesignation = designation is null ? null : Guard.MaxLength(designation, 150),
        DepartmentId = departmentId,
        ExitDate = exitDate,
        SeparationType = type,
        EligibleForRehire = eligibleForRehire,
        OptedIn = true,
    };

    public void Update(string? email, bool eligibleForRehire, bool optedIn, string? notes)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : Guard.MaxLength(email.Trim(), 256);
        EligibleForRehire = eligibleForRehire;
        OptedIn = optedIn;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }
}

/// <summary>The settlement arithmetic, kept pure for testing.</summary>
public static class SettlementMath
{
    /// <summary>Salary for the days worked in the last month: gross over calendar days.</summary>
    public static decimal LastMonthSalary(decimal monthlyGross, DateOnly lastWorkingDay)
        => Math.Round(monthlyGross * lastWorkingDay.Day / DateTime.DaysInMonth(lastWorkingDay.Year, lastWorkingDay.Month), 2, MidpointRounding.AwayFromZero);

    /// <summary>Leave encashment: basic over 26 working days, per day of leave.</summary>
    public static decimal LeaveEncashment(decimal monthlyBasic, decimal days)
        => Math.Round(monthlyBasic / 26m * Math.Max(0, days), 2, MidpointRounding.AwayFromZero);

    /// <summary>Notice shortfall recovered at gross over 30 per day.</summary>
    public static decimal NoticeRecovery(decimal monthlyGross, int shortfallDays)
        => Math.Round(monthlyGross / 30m * Math.Max(0, shortfallDays), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gratuity under the Payment of Gratuity Act: days-per-year of basic over the divisor for each
    /// year of service (a final part-year over six months counts as a year), once the minimum service
    /// is met, capped at the maximum.
    /// </summary>
    public static (decimal Years, decimal Amount, bool Eligible) Gratuity(decimal monthlyBasic, DateOnly joined, DateOnly lastDay, decimal daysPerYear, decimal divisorDays, decimal minimumYears, decimal maximumAmount)
    {
        if (lastDay < joined || divisorDays <= 0)
        {
            return (0, 0, false);
        }

        int months = ((lastDay.Year - joined.Year) * 12) + lastDay.Month - joined.Month - (lastDay.Day < joined.Day ? 1 : 0);
        decimal exactYears = months / 12m;
        int completed = months / 12;
        int counted = months % 12 > 6 ? completed + 1 : completed;
        bool eligible = exactYears >= minimumYears;
        decimal amount = Math.Min(maximumAmount, Math.Round(monthlyBasic * daysPerYear / divisorDays * counted, 0, MidpointRounding.AwayFromZero));
        return (Math.Round(exactYears, 2), eligible ? amount : 0, eligible);
    }

    /// <summary>
    /// Retrenchment compensation (Industrial Relations Code, 2020 s.70(b)): fifteen days' average pay
    /// (basic over 26) for every completed year of continuous service or part over six months, for
    /// someone with at least a year of service.
    /// </summary>
    public static (int Years, decimal Amount, bool Eligible) Retrenchment(decimal monthlyBasic, DateOnly joined, DateOnly lastDay)
    {
        if (lastDay < joined)
        {
            return (0, 0, false);
        }

        int months = ((lastDay.Year - joined.Year) * 12) + lastDay.Month - joined.Month - (lastDay.Day < joined.Day ? 1 : 0);
        if (months < 12)
        {
            return (0, 0, false);
        }

        int counted = (months / 12) + (months % 12 > 6 ? 1 : 0);
        return (counted, Math.Round(monthlyBasic * 15m / 26m * counted, 0, MidpointRounding.AwayFromZero), true);
    }

    /// <summary>Pay in lieu of notice the company owes: gross over 30 per day not given.</summary>
    public static decimal NoticePay(decimal monthlyGross, int days)
        => Math.Round(monthlyGross / 30m * Math.Max(0, days), 2, MidpointRounding.AwayFromZero);
}
