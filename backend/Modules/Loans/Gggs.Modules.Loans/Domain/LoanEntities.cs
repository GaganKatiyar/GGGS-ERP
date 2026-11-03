using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Loans.Domain;

public sealed record LoanTypeDetails(
    string Code,
    string Name,
    string? Description,
    decimal InterestRate,
    int MaxTenureMonths,
    decimal? MaxAmount,
    decimal? MaxGrossMultiple,
    int MinServiceMonths,
    int MaxActive,
    decimal MaxEmiPercentOfGross,
    bool PerquisiteExempt);

/// <summary>
/// A kind of loan the company offers - a salary advance, a personal or medical loan - and its rules:
/// how much (an absolute cap, a multiple of monthly gross, or the lower of both), over how long, at
/// what interest, after how much service, and how much of the monthly gross the instalment may take.
/// </summary>
public sealed class LoanType : TenantAggregateRoot
{
    private LoanType()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Yearly interest charged, in percent; 0 for an interest-free loan.</summary>
    public decimal InterestRate { get; private set; }

    public int MaxTenureMonths { get; private set; }

    public decimal? MaxAmount { get; private set; }

    /// <summary>The most one may borrow as a multiple of their monthly gross.</summary>
    public decimal? MaxGrossMultiple { get; private set; }

    public int MinServiceMonths { get; private set; }

    /// <summary>How many loans of this type one person may be repaying at once.</summary>
    public int MaxActive { get; private set; }

    /// <summary>The share of monthly gross all of a person's instalments together may take.</summary>
    public decimal MaxEmiPercentOfGross { get; private set; }

    /// <summary>Exempt from the interest perquisite - medical treatment of specified diseases (Rule 3(7)(i)).</summary>
    public bool PerquisiteExempt { get; private set; }

    public static LoanType Create(LoanTypeDetails details)
    {
        var type = new LoanType();
        type.Update(details, active: true);
        return type;
    }

    public void Update(LoanTypeDetails details, bool active)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.InterestRate is < 0 or > 30 || details.MaxTenureMonths is < 1 or > 120)
        {
            throw new DomainRuleException("LOAN_TYPE_TERMS", "Interest is between 0% and 30% a year and the tenure between 1 and 120 months.");
        }

        if (details.MaxAmount is null && details.MaxGrossMultiple is null || details.MaxAmount is <= 0 || details.MaxGrossMultiple is <= 0)
        {
            throw new DomainRuleException("LOAN_TYPE_LIMIT", "Set a maximum amount, a multiple of monthly gross, or both, above zero.");
        }

        if (details.MinServiceMonths < 0 || details.MaxActive < 1 || details.MaxEmiPercentOfGross is <= 0 or > 100)
        {
            throw new DomainRuleException("LOAN_TYPE_ELIGIBILITY", "Service is not negative, at least one loan may be active, and the instalment cap is a percentage above zero.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Code).Trim().ToUpperInvariant(), 30);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 100);
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 500);
        InterestRate = details.InterestRate;
        MaxTenureMonths = details.MaxTenureMonths;
        MaxAmount = details.MaxAmount;
        MaxGrossMultiple = details.MaxGrossMultiple;
        MinServiceMonths = details.MinServiceMonths;
        MaxActive = details.MaxActive;
        MaxEmiPercentOfGross = details.MaxEmiPercentOfGross;
        PerquisiteExempt = details.PerquisiteExempt;
        IsActive = active;
    }

    /// <summary>The most this person may borrow, given their monthly gross (null when they have no salary).</summary>
    public decimal Limit(decimal? monthlyGross)
    {
        decimal? byGross = MaxGrossMultiple is { } multiple ? LoanMath.Rupees((monthlyGross ?? 0) * multiple) : null;
        return (MaxAmount, byGross) switch
        {
            ({ } cap, { } gross) => Math.Min(cap, gross),
            ({ } cap, null) => cap,
            (null, { } gross) => gross,
            _ => 0,
        };
    }

    /// <summary>
    /// Why this application cannot be made, or null when it can: service, amount, tenure, how many
    /// loans of the type are open, and whether all instalments together stay within the cap.
    /// </summary>
    public string? Ineligibility(decimal amount, int tenureMonths, int serviceMonths, decimal? monthlyGross, int activeOfType, decimal otherEmis)
    {
        if (!IsActive)
        {
            return $"{Name} is not offered any more.";
        }

        if (serviceMonths < MinServiceMonths)
        {
            return $"{Name} needs {MinServiceMonths} month(s) of service.";
        }

        if (activeOfType >= MaxActive)
        {
            return $"You already have {activeOfType} {Name.ToLowerInvariant()}(s) open; the most is {MaxActive}.";
        }

        if (tenureMonths < 1 || tenureMonths > MaxTenureMonths)
        {
            return $"{Name} is repaid over 1 to {MaxTenureMonths} months.";
        }

        decimal limit = Limit(monthlyGross);
        if (amount <= 0 || amount > limit)
        {
            return limit <= 0 ? $"{Name} needs a salary on record to work out the limit." : $"You can borrow up to {limit:N0} as a {Name.ToLowerInvariant()}.";
        }

        if (monthlyGross is { } gross && gross > 0)
        {
            decimal cap = LoanMath.Rupees(gross * MaxEmiPercentOfGross / 100);
            decimal emi = LoanMath.Emi(amount, InterestRate, tenureMonths);
            if (otherEmis + emi > cap)
            {
                return $"The instalment of {emi:N0}, with {otherEmis:N0} already being repaid, is more than {MaxEmiPercentOfGross:0.##}% of your monthly gross ({cap:N0}). Borrow less or over longer.";
            }
        }

        return null;
    }
}

public enum LoanStatus
{
    PendingApproval = 1,
    Approved = 2,

    /// <summary>Disbursed and being repaid.</summary>
    Active = 3,
    Closed = 4,
    Rejected = 5,
    Cancelled = 6,
    WrittenOff = 7,
}

public enum DisbursementMode
{
    /// <summary>Paid with the next salary.</summary>
    Payroll = 1,

    /// <summary>Transferred on its own; the bank reference is recorded.</summary>
    Bank = 2,
}

/// <summary>
/// A loan or salary advance to one employee: applied for, approved through the workflow, disbursed,
/// then repaid in monthly instalments deducted by payroll - or prepaid, recovered in the final
/// settlement at exit, or written off.
/// </summary>
public sealed class Loan : TenantAggregateRoot
{
    private Loan()
    {
        Number = null!;
        Purpose = null!;
    }

    public string Number { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid LoanTypeId { get; private set; }

    public decimal Principal { get; private set; }

    /// <summary>Yearly interest, fixed from the type when applied.</summary>
    public decimal InterestRate { get; private set; }

    public int TenureMonths { get; private set; }

    /// <summary>The instalment the schedule was built with.</summary>
    public decimal Emi { get; private set; }

    public string Purpose { get; private set; }

    public bool PerquisiteExempt { get; private set; }

    public LoanStatus Status { get; private set; }

    public DateOnly AppliedOn { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public string? DecisionNote { get; private set; }

    public DisbursementMode? DisbursementMode { get; private set; }

    public string? DisbursementReference { get; private set; }

    public DateOnly? DisbursedOn { get; private set; }

    public DateOnly? FirstEmiMonth { get; private set; }

    /// <summary>Principal still to be repaid.</summary>
    public decimal Outstanding { get; private set; }

    /// <summary>The final settlement now recovering this loan instead of monthly payroll.</summary>
    public Guid? SettlementId { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public string? CloseNote { get; private set; }

    public bool IsOpen => Status is LoanStatus.PendingApproval or LoanStatus.Approved or LoanStatus.Active;

    public static Loan Apply(string number, Guid employeeId, LoanType type, decimal amount, int tenureMonths, string purpose, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new Loan
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number), 30),
            EmployeeId = Guard.NotEmpty(employeeId),
            LoanTypeId = type.Id,
            Principal = LoanMath.Rupees(amount),
            InterestRate = type.InterestRate,
            TenureMonths = tenureMonths,
            Emi = LoanMath.Emi(LoanMath.Rupees(amount), type.InterestRate, tenureMonths),
            Purpose = Guard.MaxLength(Guard.NotNullOrWhiteSpace(purpose).Trim(), 500),
            PerquisiteExempt = type.PerquisiteExempt,
            Status = LoanStatus.PendingApproval,
            AppliedOn = today,
        };
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Decide(bool approved, string? note, DateTime now)
    {
        Ensure(LoanStatus.PendingApproval, "decided");
        Status = approved ? LoanStatus.Approved : LoanStatus.Rejected;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 500);
        DecidedDate = now;
    }

    public void Cancel()
    {
        if (Status is not (LoanStatus.PendingApproval or LoanStatus.Approved))
        {
            throw new DomainRuleException("LOAN_STATE", "Only a loan not yet disbursed can be cancelled.");
        }

        Status = LoanStatus.Cancelled;
    }

    public void Disburse(DisbursementMode mode, string? reference, DateOnly on, DateOnly firstEmiMonth)
    {
        Ensure(LoanStatus.Approved, "disbursed");
        DateOnly first = new(firstEmiMonth.Year, firstEmiMonth.Month, 1);
        if (first < new DateOnly(on.Year, on.Month, 1))
        {
            throw new DomainRuleException("LOAN_FIRST_EMI", "Repayment cannot start before the month the loan is paid out.");
        }

        if (mode == Domain.DisbursementMode.Bank && string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainRuleException("LOAN_REFERENCE", "Record the bank reference of the transfer.");
        }

        Status = LoanStatus.Active;
        DisbursementMode = mode;
        DisbursementReference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100);
        DisbursedOn = on;
        FirstEmiMonth = first;
        Outstanding = Principal;
    }

    /// <summary>Principal repaid - by an instalment, a prepayment or the settlement. Returns what is left.</summary>
    public decimal Repay(decimal principal)
    {
        Ensure(LoanStatus.Active, "repaid");
        Outstanding = Math.Max(0, Outstanding - principal);
        return Outstanding;
    }

    /// <summary>A part prepayment changes the instalment of what is left.</summary>
    public void Reschedule(decimal emi) => Emi = emi;

    public void Close(DateOnly on, string note)
    {
        Ensure(LoanStatus.Active, "closed");
        Status = LoanStatus.Closed;
        Outstanding = 0;
        ClosedOn = on;
        CloseNote = Guard.MaxLength(note, 200);
    }

    public void WriteOff(DateOnly on, string reason)
    {
        Ensure(LoanStatus.Active, "written off");
        Status = LoanStatus.WrittenOff;
        Outstanding = 0;
        ClosedOn = on;
        CloseNote = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 200);
    }

    public void HandOver(Guid settlementId)
    {
        Ensure(LoanStatus.Active, "handed to a settlement");
        SettlementId = settlementId;
    }

    public void TakeBack() => SettlementId = null;

    private void Ensure(LoanStatus status, string action)
    {
        if (Status != status)
        {
            throw new DomainRuleException("LOAN_STATE", $"A loan that is {Label(Status)} cannot be {action}.");
        }
    }

    public static string Label(LoanStatus status) => status switch
    {
        LoanStatus.PendingApproval => "pending approval",
        LoanStatus.WrittenOff => "written off",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public enum InstalmentStatus
{
    Scheduled = 1,
    Deducted = 2,
    Prepaid = 3,
    RecoveredAtExit = 4,
    WrittenOff = 5,

    /// <summary>Replaced by a new schedule after a part prepayment.</summary>
    Superseded = 6,
}

/// <summary>One month of a loan's schedule, raised to payroll as a deduction when it falls due.</summary>
public sealed class LoanInstalment : TenantAggregateRoot
{
    private LoanInstalment()
    {
    }

    public Guid LoanId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>The first day of the payroll month it is recovered in.</summary>
    public DateOnly Month { get; private set; }

    public decimal Principal { get; private set; }

    public decimal Interest { get; private set; }

    public decimal Amount { get; private set; }

    public InstalmentStatus Status { get; private set; }

    /// <summary>Whether it has been sent to payroll.</summary>
    public bool Raised { get; private set; }

    public DateOnly? SettledOn { get; private set; }

    public static LoanInstalment From(Loan loan, ScheduledInstalment line)
    {
        ArgumentNullException.ThrowIfNull(loan);
        ArgumentNullException.ThrowIfNull(line);
        return new LoanInstalment
        {
            LoanId = loan.Id,
            EmployeeId = loan.EmployeeId,
            Sequence = line.Sequence,
            Month = line.Month,
            Principal = line.Principal,
            Interest = line.Interest,
            Amount = line.Amount,
            Status = InstalmentStatus.Scheduled,
        };
    }

    public void MarkRaised() => Raised = true;

    /// <summary>Payroll took it back (a prepayment, the settlement); it may be raised again if still due.</summary>
    public void MarkWithdrawn() => Raised = false;

    public void Settle(InstalmentStatus status, DateOnly on)
    {
        if (Status != InstalmentStatus.Scheduled)
        {
            return;
        }

        Status = status;
        SettledOn = on;
    }
}

public enum RepaymentKind
{
    Prepayment = 1,
    WriteOff = 2,
    Settlement = 3,
}

/// <summary>Money against a loan outside the monthly instalments: a prepayment, the exit settlement, or a write-off.</summary>
public sealed class LoanRepayment : TenantAggregateRoot
{
    private LoanRepayment()
    {
    }

    public Guid LoanId { get; private set; }

    public RepaymentKind Kind { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly Date { get; private set; }

    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    public static LoanRepayment Record(Guid loanId, RepaymentKind kind, decimal amount, DateOnly date, string? reference, string? note)
    {
        if (amount <= 0)
        {
            throw new DomainRuleException("LOAN_REPAYMENT", "A repayment is more than zero.");
        }

        return new LoanRepayment
        {
            LoanId = loanId,
            Kind = kind,
            Amount = LoanMath.Rupees(amount),
            Date = date,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100),
            Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 300),
        };
    }
}

/// <summary>
/// One employee's taxable interest benefit for one payroll month: their concessional loans'
/// outstanding balance at the gap between the benchmark rate and the rate charged. Raised to payroll
/// as a perquisite, so it is taxed with salary without being paid.
/// </summary>
public sealed class LoanPerquisite : TenantAggregateRoot
{
    private LoanPerquisite()
    {
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>The first day of the payroll month.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>What the employee owed on loans that attract the perquisite.</summary>
    public decimal Outstanding { get; private set; }

    public decimal BenchmarkRate { get; private set; }

    public decimal Amount { get; private set; }

    public static LoanPerquisite For(Guid employeeId, DateOnly month)
        => new() { EmployeeId = Guard.NotEmpty(employeeId), Month = new DateOnly(month.Year, month.Month, 1) };

    public void Value(decimal outstanding, decimal benchmarkRate, decimal amount)
    {
        Outstanding = outstanding;
        BenchmarkRate = benchmarkRate;
        Amount = LoanMath.Rupees(Math.Max(0, amount));
    }
}

/// <summary>The loan types a tenant starts with.</summary>
public static class LoanDefaults
{
    public static IReadOnlyList<LoanTypeDetails> Types { get; } =
    [
        new("SALARY_ADVANCE", "Salary advance", "Part of next month's pay, early, recovered over up to three months.", 0m, 3, null, 1m, 3, 1, 50m, false),
        new("PERSONAL", "Personal loan", "For a wedding, education, a move or other personal needs.", 8m, 24, 500_000m, 3m, 12, 1, 40m, false),
        new("MEDICAL", "Medical loan", "Interest-free, for treatment of yourself or a dependant.", 0m, 24, 300_000m, null, 0, 1, 40m, true),
    ];
}
