using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Leave.Domain;

/// <summary>A kind of leave: casual, sick, earned, loss of pay.</summary>
public sealed class LeaveType : TenantAggregateRoot
{
    private LeaveType()
    {
        Code = null!;
        Name = null!;
        Color = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsPaid { get; private set; }

    public bool AllowHalfDay { get; private set; }

    /// <summary>For calendars: a hex colour such as #2E7D32.</summary>
    public string Color { get; private set; }

    public bool IsSystem { get; private set; }

    public static LeaveType Create(string code, string name, bool isPaid, bool allowHalfDay, string? color, bool isSystem = false)
    {
        var type = new LeaveType { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 16), IsSystem = isSystem };
        type.Update(name, isPaid, allowHalfDay, color);
        return type;
    }

    public void Update(string name, bool isPaid, bool allowHalfDay, string? color)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        IsPaid = isPaid;
        AllowHalfDay = allowHalfDay;
        Color = string.IsNullOrWhiteSpace(color) ? "#546E7A" : Guard.MaxLength(color.Trim(), 9);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum AccrualFrequency
{
    /// <summary>Granted by hand (adjustments) - maternity leave, compensatory off.</summary>
    None = 0,

    /// <summary>A twelfth of the year's entitlement each month.</summary>
    Monthly = 1,

    /// <summary>The year's entitlement up front, prorated for someone joining mid-year.</summary>
    Yearly = 2,
}

/// <summary>
/// The rules one kind of leave follows, for everyone or for one employment type. The most specific
/// active policy applies: a contractor's casual-leave policy over the general one.
/// </summary>
public sealed class LeavePolicy : TenantAggregateRoot
{
    private LeavePolicy()
    {
    }

    public Guid LeaveTypeId { get; private set; }

    /// <summary>Null applies to every employment type without a policy of its own.</summary>
    public string? EmploymentTypeCode { get; private set; }

    public decimal AnnualEntitlement { get; private set; }

    public AccrualFrequency Accrual { get; private set; }

    /// <summary>Days kept into the next year; null keeps everything, 0 keeps nothing.</summary>
    public decimal? CarryForwardLimit { get; private set; }

    public bool AllowDuringProbation { get; private set; }

    /// <summary>How far below zero a balance may go; 0 allows none.</summary>
    public decimal MaxNegativeBalance { get; private set; }

    public int? MaxConsecutiveDays { get; private set; }

    /// <summary>Days of notice an employee must give.</summary>
    public int MinNoticeDays { get; private set; }

    /// <summary>
    /// The sandwich rule: weekly offs and holidays lying between two days of the same leave are
    /// counted as leave too.
    /// </summary>
    public bool CountSandwichedDays { get; private set; }

    public static LeavePolicy Create(Guid leaveTypeId, string? employmentTypeCode, PolicyRules rules)
    {
        var policy = new LeavePolicy
        {
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            EmploymentTypeCode = string.IsNullOrWhiteSpace(employmentTypeCode) ? null : Guard.MaxLength(employmentTypeCode.Trim().ToUpperInvariant(), 64),
        };
        policy.Update(rules);
        return policy;
    }

    public void Update(PolicyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.AnnualEntitlement is < 0 or > 366)
        {
            throw new DomainRuleException("LEAVE_POLICY_ENTITLEMENT", "The yearly entitlement is between 0 and 366 days.");
        }

        if (rules.CarryForwardLimit is < 0 || rules.MaxNegativeBalance is < 0 or > 366)
        {
            throw new DomainRuleException("LEAVE_POLICY_LIMITS", "Carry-forward and negative-balance limits cannot be negative.");
        }

        if (rules.MaxConsecutiveDays is < 1 || rules.MinNoticeDays is < 0 or > 365)
        {
            throw new DomainRuleException("LEAVE_POLICY_RULES", "Check the longest stretch (at least a day) and the notice period (0 to 365 days).");
        }

        if (!Enum.IsDefined(rules.Accrual))
        {
            throw new DomainRuleException("LEAVE_POLICY_ACCRUAL", "Unknown accrual frequency.");
        }

        AnnualEntitlement = rules.AnnualEntitlement;
        Accrual = rules.Accrual;
        CarryForwardLimit = rules.CarryForwardLimit;
        AllowDuringProbation = rules.AllowDuringProbation;
        MaxNegativeBalance = rules.MaxNegativeBalance;
        MaxConsecutiveDays = rules.MaxConsecutiveDays;
        MinNoticeDays = rules.MinNoticeDays;
        CountSandwichedDays = rules.CountSandwichedDays;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public sealed record PolicyRules(
    decimal AnnualEntitlement,
    AccrualFrequency Accrual,
    decimal? CarryForwardLimit,
    bool AllowDuringProbation,
    decimal MaxNegativeBalance,
    int? MaxConsecutiveDays,
    int MinNoticeDays,
    bool CountSandwichedDays);

public enum LedgerKind
{
    Accrual = 1,
    Grant = 2,
    Taken = 3,
    Restored = 4,
    Adjustment = 5,
    Lapse = 6,
}

/// <summary>
/// One movement of a leave balance. A balance is never stored - it is the sum of its entries - so
/// every day granted, taken, restored or lapsed can be traced.
/// </summary>
public sealed class LeaveLedgerEntry : TenantAggregateRoot
{
    private LeaveLedgerEntry()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public LedgerKind Kind { get; private set; }

    /// <summary>Positive adds to the balance, negative takes from it.</summary>
    public decimal Days { get; private set; }

    public Guid? RequestId { get; private set; }

    /// <summary>Identifies a scheduled entry ("accrual:2026-10") so running a job twice adds nothing.</summary>
    public string? PeriodKey { get; private set; }

    public string? Note { get; private set; }

    public static LeaveLedgerEntry Record(
        Guid employeeId,
        Guid leaveTypeId,
        DateOnly entryDate,
        LedgerKind kind,
        decimal days,
        Guid? requestId = null,
        string? periodKey = null,
        string? note = null) => new()
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            EntryDate = entryDate,
            Kind = kind,
            Days = decimal.Round(days, 2),
            RequestId = requestId,
            PeriodKey = periodKey is null ? null : Guard.MaxLength(periodKey, 32),
            Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 300),
        };
}

public enum LeaveRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>A request for leave. Its days come off the balance when it is approved.</summary>
public sealed class LeaveRequest : TenantAggregateRoot
{
    private LeaveRequest()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>Leave begins at the second half of the first day.</summary>
    public bool StartsSecondHalf { get; private set; }

    /// <summary>Leave ends after the first half of the last day.</summary>
    public bool EndsFirstHalf { get; private set; }

    public decimal Days { get; private set; }

    public string? Reason { get; private set; }

    public LeaveRequestStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>Set when someone applied on the employee's behalf.</summary>
    public Guid? AppliedByUserId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public string? CancellationReason { get; private set; }

    public static LeaveRequest Apply(
        Guid employeeId,
        Guid leaveTypeId,
        DateOnly start,
        DateOnly end,
        bool startsSecondHalf,
        bool endsFirstHalf,
        decimal days,
        string? reason,
        Guid? appliedByUserId) => new()
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            LeaveTypeId = Guard.NotEmpty(leaveTypeId),
            StartDate = start,
            EndDate = end,
            StartsSecondHalf = startsSecondHalf,
            EndsFirstHalf = endsFirstHalf,
            Days = days,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500),
            Status = LeaveRequestStatus.Pending,
            AppliedByUserId = appliedByUserId,
        };

    public void AwaitApproval(Guid instanceId) => WorkflowInstanceId = instanceId;

    public void Approve(DateTime now) => Move(LeaveRequestStatus.Approved, now);

    public void Reject(DateTime now) => Move(LeaveRequestStatus.Rejected, now);

    public void Cancel(DateTime now, string? reason)
    {
        if (Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved))
        {
            throw new DomainRuleException("LEAVE_REQUEST_CLOSED", $"This request is already {Status}.");
        }

        Status = LeaveRequestStatus.Cancelled;
        DecidedDate = now;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 300);
    }

    public bool Overlaps(DateOnly start, DateOnly end) => StartDate <= end && EndDate >= start;

    private void Move(LeaveRequestStatus status, DateTime now)
    {
        if (Status != LeaveRequestStatus.Pending)
        {
            throw new DomainRuleException("LEAVE_REQUEST_CLOSED", $"This request is already {Status}.");
        }

        Status = status;
        DecidedDate = now;
    }
}

/// <summary>A set of public holidays, for the locations listed or, as the default, for everyone else.</summary>
public sealed class HolidayCalendar : TenantAggregateRoot
{
    private HolidayCalendar()
    {
        Code = null!;
        Name = null!;
        LocationIdsJson = "[]";
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsDefault { get; private set; }

    public string LocationIdsJson { get; private set; }

    public IReadOnlyList<Guid> LocationIds => JsonSerializer.Deserialize<List<Guid>>(LocationIdsJson) ?? [];

    public static HolidayCalendar Create(string code, string name, bool isDefault, IReadOnlyCollection<Guid> locationIds)
    {
        var calendar = new HolidayCalendar { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32) };
        calendar.Update(name, isDefault, locationIds);
        return calendar;
    }

    public void Update(string name, bool isDefault, IReadOnlyCollection<Guid> locationIds)
    {
        ArgumentNullException.ThrowIfNull(locationIds);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        IsDefault = isDefault;
        LocationIdsJson = JsonSerializer.Serialize(locationIds.Distinct().ToList());
    }
}

public sealed class Holiday : TenantAggregateRoot
{
    private Holiday()
    {
        Name = null!;
    }

    public Guid CalendarId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Name { get; private set; }

    public bool IsOptional { get; private set; }

    public static Holiday Create(Guid calendarId, DateOnly date, string name, bool isOptional) => new()
    {
        CalendarId = Guard.NotEmpty(calendarId),
        Date = date,
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        IsOptional = isOptional,
    };
}

/// <summary>One day in a leave range, as the calculator sees it.</summary>
public sealed record LeaveCalendarDay(DateOnly Date, bool IsWorkingDay);

/// <summary>
/// How many days a request takes. Working days count; weekly offs and holidays do not - unless the
/// policy's sandwich rule applies and they lie between two leave days. Half days take half.
/// </summary>
public static class LeaveCalculator
{
    public static decimal CountDays(
        IReadOnlyList<LeaveCalendarDay> days,
        bool startsSecondHalf,
        bool endsFirstHalf,
        bool countSandwichedDays)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            return 0;
        }

        if (days.Count == 1 && startsSecondHalf && endsFirstHalf)
        {
            throw new DomainRuleException("LEAVE_HALF_DAYS", "A single day cannot both start at the second half and end after the first.");
        }

        int firstWorking = -1;
        int lastWorking = -1;
        for (int index = 0; index < days.Count; index++)
        {
            if (days[index].IsWorkingDay)
            {
                firstWorking = firstWorking < 0 ? index : firstWorking;
                lastWorking = index;
            }
        }

        if (firstWorking < 0)
        {
            return 0;
        }

        decimal total = 0;
        for (int index = 0; index < days.Count; index++)
        {
            bool sandwiched = countSandwichedDays && index > firstWorking && index < lastWorking;
            if (days[index].IsWorkingDay || sandwiched)
            {
                total += 1;
            }
        }

        if (startsSecondHalf && days[0].IsWorkingDay)
        {
            total -= 0.5m;
        }

        if (endsFirstHalf && days[^1].IsWorkingDay)
        {
            total -= 0.5m;
        }

        return total;
    }
}
