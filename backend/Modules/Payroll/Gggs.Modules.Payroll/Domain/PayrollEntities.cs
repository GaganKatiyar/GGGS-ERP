using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Payroll.Contracts;

namespace Gggs.Modules.Payroll.Domain;

public enum ComponentKind
{
    Earning = 1,
    Deduction = 2,
    EmployerContribution = 3,
    Reimbursement = 4,
}

/// <summary>How a component's amount is found (spec §21).</summary>
public enum CalculationType
{
    /// <summary>A fixed monthly amount.</summary>
    Fixed = 1,

    /// <summary>A percentage of another component, of the monthly CTC or of gross.</summary>
    Percentage = 2,

    /// <summary>A formula over component codes.</summary>
    Formula = 3,

    /// <summary>Whatever is left of the CTC once everything else is laid out - the special allowance.</summary>
    Balancing = 4,

    /// <summary>Entered for a month: overtime, bonus, incentive, commission, reimbursement.</summary>
    Input = 5,

    /// <summary>Worked out by a statutory rule: PF, ESI, professional tax, TDS, labour welfare, gratuity.</summary>
    Statutory = 6,
}

/// <summary>The statutory amounts payroll works out, and the component each is shown as.</summary>
public static class StatutoryKeys
{
    public const string ProvidentFundEmployee = "PF_EE";
    public const string ProvidentFundEmployer = "PF_ER";
    public const string StateInsuranceEmployee = "ESI_EE";
    public const string StateInsuranceEmployer = "ESI_ER";
    public const string ProfessionalTax = "PT";
    public const string IncomeTax = "TDS";
    public const string LabourWelfareEmployee = "LWF_EE";
    public const string LabourWelfareEmployer = "LWF_ER";
    public const string Gratuity = "GRATUITY";

    public static IReadOnlyList<string> All { get; } =
        [ProvidentFundEmployee, ProvidentFundEmployer, StateInsuranceEmployee, StateInsuranceEmployer, ProfessionalTax, IncomeTax, LabourWelfareEmployee, LabourWelfareEmployer, Gratuity];
}

/// <summary>A salary component (spec §21): an earning, deduction, employer contribution or reimbursement, and how it is calculated.</summary>
public sealed class SalaryComponent : TenantAggregateRoot
{
    private SalaryComponent()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public ComponentKind Kind { get; private set; }

    public CalculationType CalculationType { get; private set; }

    /// <summary>The amount (Fixed) or the percentage (Percentage).</summary>
    public decimal? Value { get; private set; }

    /// <summary>What a percentage is of: a component code, <c>CTC</c> (monthly) or <c>GROSS</c>.</summary>
    public string? PercentOf { get; private set; }

    public string? Formula { get; private set; }

    /// <summary>For an input component: which kind of input lands on it.</summary>
    public PayrollInputKind? InputKind { get; private set; }

    public bool IsTaxable { get; private set; }

    /// <summary>Counts towards PF wages.</summary>
    public bool PfApplicable { get; private set; }

    /// <summary>Counts towards ESI gross.</summary>
    public bool EsiApplicable { get; private set; }

    /// <summary>Counts towards the gross professional tax is judged on.</summary>
    public bool PtApplicable { get; private set; }

    /// <summary>Reduced for days not paid: joining or leaving mid-month, loss of pay.</summary>
    public bool IsProrated { get; private set; }

    public bool ShowOnPayslip { get; private set; }

    public int DisplayOrder { get; private set; }

    /// <summary>Seeded components that payroll relies on - their code and calculation are fixed.</summary>
    public bool IsSystem { get; private set; }

    public string? Description { get; private set; }

    public static SalaryComponent Create(string code, ComponentDefinition definition, bool isSystem = false)
    {
        var component = new SalaryComponent { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30), IsSystem = isSystem };
        if (!System.Text.RegularExpressions.Regex.IsMatch(component.Code, "^[A-Z][A-Z0-9_]*$"))
        {
            throw new DomainRuleException("COMPONENT_CODE", "A component code is letters, digits and underscores, starting with a letter - formulas refer to it.");
        }

        if (component.Code is "CTC" or "GROSS" or "PAID_DAYS" or "PERIOD_DAYS" or "LOP_DAYS")
        {
            throw new DomainRuleException("COMPONENT_CODE", $"{component.Code} is reserved for formulas.");
        }

        component.Apply(definition);
        return component;
    }

    public void Update(ComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (IsSystem && (definition.Kind != Kind || definition.CalculationType != CalculationType))
        {
            throw new DomainRuleException("COMPONENT_SYSTEM", $"{Code} is a system component; its kind and calculation cannot change.");
        }

        Apply(definition);
    }

    private void Apply(ComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        switch (definition.CalculationType)
        {
            case CalculationType.Fixed when definition.Value is null or < 0:
                throw new DomainRuleException("COMPONENT_VALUE", "A fixed component needs an amount of zero or more.");
            case CalculationType.Percentage when definition.Value is null or < 0 or > 100 || string.IsNullOrWhiteSpace(definition.PercentOf):
                throw new DomainRuleException("COMPONENT_VALUE", "A percentage component needs a percentage (0-100) and what it is a percentage of.");
            case CalculationType.Formula when string.IsNullOrWhiteSpace(definition.Formula):
                throw new DomainRuleException("COMPONENT_VALUE", "A formula component needs its formula.");
            case CalculationType.Input when definition.InputKind is null:
                throw new DomainRuleException("COMPONENT_VALUE", "An input component says which kind of input it carries.");
            case CalculationType.Balancing when definition.Kind != ComponentKind.Earning:
                throw new DomainRuleException("COMPONENT_VALUE", "Only an earning can balance the CTC.");
            default:
                break;
        }

        if (definition.CalculationType == CalculationType.Formula)
        {
            Domain.Formula.References(definition.Formula!);
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(definition.Name).Trim(), 100);
        Kind = definition.Kind;
        CalculationType = definition.CalculationType;
        Value = definition.Value;
        PercentOf = string.IsNullOrWhiteSpace(definition.PercentOf) ? null : definition.PercentOf.Trim().ToUpperInvariant();
        Formula = string.IsNullOrWhiteSpace(definition.Formula) ? null : definition.Formula.Trim();
        InputKind = definition.InputKind;
        IsTaxable = definition.IsTaxable;
        PfApplicable = definition.PfApplicable;
        EsiApplicable = definition.EsiApplicable;
        PtApplicable = definition.PtApplicable;
        IsProrated = definition.IsProrated;
        ShowOnPayslip = definition.ShowOnPayslip;
        DisplayOrder = definition.DisplayOrder;
        Description = string.IsNullOrWhiteSpace(definition.Description) ? null : Guard.MaxLength(definition.Description.Trim(), 500);
    }

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new DomainRuleException("COMPONENT_SYSTEM", $"{Code} is a system component and stays active.");
        }

        IsActive = false;
    }
}

public sealed record ComponentDefinition(
    string Name,
    ComponentKind Kind,
    CalculationType CalculationType,
    decimal? Value,
    string? PercentOf,
    string? Formula,
    PayrollInputKind? InputKind,
    bool IsTaxable,
    bool PfApplicable,
    bool EsiApplicable,
    bool PtApplicable,
    bool IsProrated,
    bool ShowOnPayslip,
    int DisplayOrder,
    string? Description);

/// <summary>
/// A salary structure (spec §21, §23): which components make up a CTC and, where it differs from
/// the component's default, how each is calculated for people on this structure.
/// </summary>
public sealed class SalaryStructure : TenantAggregateRoot
{
    private SalaryStructure()
    {
        Code = null!;
        Name = null!;
        LinesJson = "[]";
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string LinesJson { get; private set; }

    public IReadOnlyList<StructureLine> Lines => JsonSerializer.Deserialize<List<StructureLine>>(LinesJson) ?? [];

    public static SalaryStructure Create(string code, string name, string? description, IReadOnlyList<StructureLine> lines)
    {
        var structure = new SalaryStructure { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30) };
        structure.Update(name, description, lines);
        return structure;
    }

    public void Update(string name, string? description, IReadOnlyList<StructureLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Select(line => line.ComponentId).Distinct().Count() != lines.Count)
        {
            throw new DomainRuleException("STRUCTURE_LINES", "A structure lists each component once, and needs at least one.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
        LinesJson = JsonSerializer.Serialize(lines.Select((line, index) => line with { Order = index + 1 }).ToList());
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>A component in a structure, optionally calculated differently from its default.</summary>
public sealed record StructureLine(Guid ComponentId, CalculationType? CalculationType, decimal? Value, string? PercentOf, string? Formula, int Order = 0);

public enum SalaryReason
{
    Hire = 1,
    Revision = 2,
    Promotion = 3,
    Correction = 4,
    Other = 5,
}

/// <summary>
/// What an employee is paid from a date (spec §21, §23): their annual CTC and the structure it is laid
/// out by. Never edited after it has been paid on - a change is a new salary from a later date.
/// </summary>
public sealed class EmployeeSalary : TenantAggregateRoot
{
    private EmployeeSalary()
    {
        Currency = null!;
        Source = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid StructureId { get; private set; }

    public decimal AnnualCtc { get; private set; }

    public string Currency { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public SalaryReason Reason { get; private set; }

    /// <summary>Where the change came from: <c>Manual</c>, or the module that decided it.</summary>
    public string Source { get; private set; }

    public Guid? SourceId { get; private set; }

    /// <summary>Whether PF is deducted - an employee above the wage ceiling may have opted out when joining.</summary>
    public bool PfEnabled { get; private set; }

    public string? Notes { get; private set; }

    public static EmployeeSalary Assign(Guid employeeId, Guid structureId, decimal annualCtc, string currency, DateOnly effectiveFrom, SalaryReason reason, string source, Guid? sourceId, bool pfEnabled, string? notes)
    {
        if (annualCtc <= 0 || annualCtc > 1_000_000_000m)
        {
            throw new DomainRuleException("SALARY_CTC", "The annual CTC must be a positive amount.");
        }

        return new EmployeeSalary
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            StructureId = Guard.NotEmpty(structureId),
            AnnualCtc = Math.Round(annualCtc, 2),
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            EffectiveFrom = effectiveFrom,
            Reason = reason,
            Source = Guard.MaxLength(Guard.NotNullOrWhiteSpace(source).Trim(), 40),
            SourceId = sourceId,
            PfEnabled = pfEnabled,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 500),
        };
    }

    /// <summary>The next salary starts <paramref name="nextStarts"/>; this one ends the day before.</summary>
    public void EndBefore(DateOnly nextStarts)
    {
        if (nextStarts <= EffectiveFrom)
        {
            throw new DomainRuleException("SALARY_DATE", $"The current salary started {EffectiveFrom:d MMM yyyy}; a new one must start after it.");
        }

        EffectiveTo = nextStarts.AddDays(-1);
    }

    public void Reopen() => EffectiveTo = null;

    public bool CoversAny(DateOnly first, DateOnly last) => EffectiveFrom <= last && (EffectiveTo is null || EffectiveTo >= first);
}

public enum ProrationBasis
{
    /// <summary>Days in the month (28-31).</summary>
    CalendarDays = 1,

    /// <summary>Every month counts as 30 days.</summary>
    ThirtyDays = 2,
}

public enum TaxRegime
{
    New = 1,
    Old = 2,
}

/// <summary>The tenant's payroll rules: pay day, proration, loss of pay, overtime, and the employer details on payslips and returns.</summary>
public sealed class PayrollSettings : TenantAggregateRoot
{
    private PayrollSettings()
    {
        DefaultCurrency = "INR";
    }

    public int PayDay { get; private set; }

    public ProrationBasis ProrationBasis { get; private set; }

    /// <summary>Count absences from attendance as loss of pay, on top of unpaid leave.</summary>
    public bool LopFromAttendance { get; private set; }

    /// <summary>Price approved overtime from timesheets into the month's payroll.</summary>
    public bool OvertimeFromTimesheets { get; private set; }

    public decimal OvertimeMultiplier { get; private set; }

    public decimal StandardHoursPerDay { get; private set; }

    public TaxRegime DefaultRegime { get; private set; }

    /// <summary>The professional-tax state for employees without one on their record.</summary>
    public string? DefaultStateCode { get; private set; }

    public string DefaultCurrency { get; private set; }

    public string? EmployerName { get; private set; }

    public string? EmployerAddress { get; private set; }

    public string? EmployerPan { get; private set; }

    public string? EmployerTan { get; private set; }

    public string? PfEstablishmentCode { get; private set; }

    public string? EsiEmployerCode { get; private set; }

    public static PayrollSettings Defaults() => new()
    {
        PayDay = 30,
        ProrationBasis = ProrationBasis.CalendarDays,
        OvertimeMultiplier = 2m,
        StandardHoursPerDay = 8m,
        DefaultRegime = TaxRegime.New,
        DefaultStateCode = "IN-KA",
    };

    public void Update(PayrollSettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.PayDay is < 1 or > 31 || values.OvertimeMultiplier is < 1 or > 5 || values.StandardHoursPerDay is <= 0 or > 24)
        {
            throw new DomainRuleException("PAYROLL_SETTINGS", "Pay day is 1-31, the overtime multiplier 1-5 and the standard day up to 24 hours.");
        }

        PayDay = values.PayDay;
        ProrationBasis = values.ProrationBasis;
        LopFromAttendance = values.LopFromAttendance;
        OvertimeFromTimesheets = values.OvertimeFromTimesheets;
        OvertimeMultiplier = values.OvertimeMultiplier;
        StandardHoursPerDay = values.StandardHoursPerDay;
        DefaultRegime = values.DefaultRegime;
        DefaultStateCode = Text(values.DefaultStateCode, 16)?.ToUpperInvariant();
        DefaultCurrency = Text(values.DefaultCurrency, 3)?.ToUpperInvariant() ?? "INR";
        EmployerName = Text(values.EmployerName, 200);
        EmployerAddress = Text(values.EmployerAddress, 500);
        EmployerPan = Text(values.EmployerPan, 10)?.ToUpperInvariant();
        EmployerTan = Text(values.EmployerTan, 10)?.ToUpperInvariant();
        PfEstablishmentCode = Text(values.PfEstablishmentCode, 30)?.ToUpperInvariant();
        EsiEmployerCode = Text(values.EsiEmployerCode, 30);
    }

    public PayrollSettingsValues ToValues() => new(
        PayDay, ProrationBasis, LopFromAttendance, OvertimeFromTimesheets, OvertimeMultiplier, StandardHoursPerDay, DefaultRegime, DefaultStateCode,
        DefaultCurrency, EmployerName, EmployerAddress, EmployerPan, EmployerTan, PfEstablishmentCode, EsiEmployerCode);

    private static string? Text(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

public sealed record PayrollSettingsValues(
    int PayDay,
    ProrationBasis ProrationBasis,
    bool LopFromAttendance,
    bool OvertimeFromTimesheets,
    decimal OvertimeMultiplier,
    decimal StandardHoursPerDay,
    TaxRegime DefaultRegime,
    string? DefaultStateCode,
    string? DefaultCurrency,
    string? EmployerName,
    string? EmployerAddress,
    string? EmployerPan,
    string? EmployerTan,
    string? PfEstablishmentCode,
    string? EsiEmployerCode);

public enum InputStatus
{
    Pending = 1,
    Included = 2,
    Paid = 3,
    Cancelled = 4,
}

/// <summary>A one-off amount for one person's month (spec §21 "variable inputs"): overtime, a bonus, an incentive, a recovery.</summary>
public sealed class PayrollInput : TenantAggregateRoot
{
    private PayrollInput()
    {
        Description = null!;
        Source = null!;
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>The first day of the month it is paid in.</summary>
    public DateOnly Period { get; private set; }

    public Guid ComponentId { get; private set; }

    public PayrollInputKind Kind { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Hours, for overtime.</summary>
    public decimal? Quantity { get; private set; }

    public string Description { get; private set; }

    /// <summary><c>Manual</c>, <c>Timesheets</c>, or the module that raised it.</summary>
    public string Source { get; private set; }

    public Guid? SourceId { get; private set; }

    public InputStatus Status { get; private set; }

    public Guid? RunId { get; private set; }

    public static PayrollInput Raise(Guid employeeId, DateOnly period, SalaryComponent component, PayrollInputKind kind, decimal amount, decimal? quantity, string description, string source, Guid? sourceId)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.CalculationType != CalculationType.Input)
        {
            throw new DomainRuleException("INPUT_COMPONENT", $"{component.Name} is not an input component.");
        }

        if (amount <= 0)
        {
            throw new DomainRuleException("INPUT_AMOUNT", "An input amount must be more than zero.");
        }

        return new PayrollInput
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            Period = new DateOnly(period.Year, period.Month, 1),
            ComponentId = component.Id,
            Kind = kind,
            Amount = Math.Round(amount, 2),
            Quantity = quantity,
            Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(description).Trim(), 200),
            Source = Guard.MaxLength(Guard.NotNullOrWhiteSpace(source).Trim(), 40),
            SourceId = sourceId,
            Status = InputStatus.Pending,
        };
    }

    public void Reprice(decimal amount, decimal? quantity)
    {
        EnsureOpen();
        Amount = Math.Round(amount, 2);
        Quantity = quantity;
    }

    public void IncludeIn(Guid runId)
    {
        EnsureOpen();
        Status = InputStatus.Included;
        RunId = runId;
    }

    public void Release()
    {
        if (Status == InputStatus.Included)
        {
            Status = InputStatus.Pending;
            RunId = null;
        }
    }

    /// <summary>Not paid this month (the person was held): moves to the next month's payroll.</summary>
    public void Defer(DateOnly nextPeriod)
    {
        EnsureOpen();
        Status = InputStatus.Pending;
        RunId = null;
        Period = new DateOnly(nextPeriod.Year, nextPeriod.Month, 1);
    }

    public void MarkPaid() => Status = InputStatus.Paid;

    public void Cancel()
    {
        EnsureOpen();
        Status = InputStatus.Cancelled;
    }

    private void EnsureOpen()
    {
        if (Status is InputStatus.Paid or InputStatus.Cancelled)
        {
            throw new DomainRuleException("INPUT_STATE", $"This input is {Status.ToString().ToLowerInvariant()} and cannot change.");
        }
    }
}

public enum DeclarationStatus
{
    Draft = 1,
    Submitted = 2,
    Verified = 3,
}

/// <summary>A claimed deduction (spec §22 "Form 12BB"): a section, what it is and how much.</summary>
public sealed record DeclarationItem(string Section, string Description, decimal Declared, decimal? Verified);

/// <summary>
/// An employee's income-tax declaration for a financial year (Form 12BB): the regime, investments
/// and other deductions, rent for HRA, and income from a previous employer. Payroll projects TDS from it.
/// </summary>
public sealed class TaxDeclaration : TenantAggregateRoot
{
    private TaxDeclaration()
    {
        ItemsJson = "[]";
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>The year the financial year starts in: 2026 for FY 2026-27.</summary>
    public int FinancialYear { get; private set; }

    public TaxRegime Regime { get; private set; }

    public DeclarationStatus Status { get; private set; }

    public string ItemsJson { get; private set; }

    public decimal? MonthlyRent { get; private set; }

    public bool MetroCity { get; private set; }

    public string? LandlordPan { get; private set; }

    public decimal? OtherIncome { get; private set; }

    public decimal? PreviousEmployerIncome { get; private set; }

    public decimal? PreviousEmployerTds { get; private set; }

    public DateTime? SubmittedDate { get; private set; }

    public DateTime? VerifiedDate { get; private set; }

    public Guid? VerifiedByEmployeeId { get; private set; }

    public string? VerifierNotes { get; private set; }

    public IReadOnlyList<DeclarationItem> Items => JsonSerializer.Deserialize<List<DeclarationItem>>(ItemsJson) ?? [];

    public static TaxDeclaration Start(Guid employeeId, int financialYear, TaxRegime regime) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        FinancialYear = financialYear,
        Regime = regime,
        Status = DeclarationStatus.Draft,
    };

    public void Declare(DeclarationValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (Status == DeclarationStatus.Verified)
        {
            throw new DomainRuleException("DECLARATION_VERIFIED", "This declaration has been verified; ask payroll to reopen it.");
        }

        List<DeclarationItem> items = [.. (values.Items ?? []).Where(item => item.Declared > 0)];
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Section) || item.Declared < 0))
        {
            throw new DomainRuleException("DECLARATION_ITEM", "Each item names its section and a positive amount.");
        }

        if (values.Regime == TaxRegime.New && items.Count > 0)
        {
            throw new DomainRuleException("DECLARATION_REGIME", "The new regime does not allow these deductions; switch to the old regime to claim them.");
        }

        Regime = values.Regime;
        ItemsJson = JsonSerializer.Serialize(items.Select(item => new DeclarationItem(
            Guard.MaxLength(item.Section.Trim().ToUpperInvariant(), 20), Guard.MaxLength((item.Description ?? string.Empty).Trim(), 200), Math.Round(item.Declared, 2), null)).ToList());
        MonthlyRent = values.MonthlyRent is > 0 ? values.MonthlyRent : null;
        MetroCity = values.MetroCity;
        LandlordPan = string.IsNullOrWhiteSpace(values.LandlordPan) ? null : Guard.MaxLength(values.LandlordPan.Trim().ToUpperInvariant(), 10);
        OtherIncome = values.OtherIncome is > 0 ? values.OtherIncome : null;
        PreviousEmployerIncome = values.PreviousEmployerIncome is > 0 ? values.PreviousEmployerIncome : null;
        PreviousEmployerTds = values.PreviousEmployerTds is > 0 ? values.PreviousEmployerTds : null;
        if (Status == DeclarationStatus.Submitted)
        {
            Status = DeclarationStatus.Draft;
        }
    }

    public void Submit(DateTime now)
    {
        if (Status == DeclarationStatus.Verified)
        {
            throw new DomainRuleException("DECLARATION_VERIFIED", "This declaration has already been verified.");
        }

        Status = DeclarationStatus.Submitted;
        SubmittedDate = now;
    }

    /// <summary>Payroll checks the proofs and records what it accepts for each item.</summary>
    public void Verify(IReadOnlyList<decimal> accepted, Guid? verifiedBy, string? notes, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        List<DeclarationItem> items = [.. Items];
        if (accepted.Count != items.Count)
        {
            throw new DomainRuleException("DECLARATION_VERIFY", "Give an accepted amount for each declared item.");
        }

        ItemsJson = JsonSerializer.Serialize(items.Select((item, index) => item with { Verified = Math.Clamp(accepted[index], 0, item.Declared) }).ToList());
        Status = DeclarationStatus.Verified;
        VerifiedDate = now;
        VerifiedByEmployeeId = verifiedBy;
        VerifierNotes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000);
    }

    public void Reopen() => Status = DeclarationStatus.Draft;

    /// <summary>What counts for TDS: verified amounts once verified, declared amounts until then.</summary>
    public IEnumerable<(string Section, decimal Amount)> Claims()
        => Items.Select(item => (item.Section, Status == DeclarationStatus.Verified ? item.Verified ?? 0 : item.Declared));
}

public sealed record DeclarationValues(
    TaxRegime Regime,
    IReadOnlyList<DeclarationItem>? Items,
    decimal? MonthlyRent,
    bool MetroCity,
    string? LandlordPan,
    decimal? OtherIncome,
    decimal? PreviousEmployerIncome,
    decimal? PreviousEmployerTds);

public enum RunStatus
{
    Draft = 1,
    Calculated = 2,
    PendingApproval = 3,
    Approved = 4,
    Finalized = 5,
    Paid = 6,
    Cancelled = 7,
}

/// <summary>
/// One month's payroll (spec §21 flow): inputs, calculation, review, approval, finalization (payslips),
/// payment (bank file) and accounting. Once finalized its numbers never change.
/// </summary>
public sealed class PayrollRun : TenantAggregateRoot
{
    private PayrollRun()
    {
        Currency = null!;
    }

    public DateOnly Period { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public DateOnly PayDate { get; private set; }

    public Guid? LegalEntityId { get; private set; }

    public RunStatus Status { get; private set; }

    public string Currency { get; private set; }

    public int EmployeeCount { get; private set; }

    public decimal Gross { get; private set; }

    public decimal Deductions { get; private set; }

    public decimal Net { get; private set; }

    public decimal EmployerCost { get; private set; }

    public int Warnings { get; private set; }

    public int Errors { get; private set; }

    public DateTime? CalculatedDate { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? ApprovedDate { get; private set; }

    public DateTime? FinalizedDate { get; private set; }

    public Guid? FinalizedBy { get; private set; }

    public DateTime? PaidDate { get; private set; }

    public string? PaymentReference { get; private set; }

    public string? Notes { get; private set; }

    public PayrollRunKind Kind { get; private set; } = PayrollRunKind.Regular;

    /// <summary>The leaver a full and final run pays.</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>The settlement a full and final run pays.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>The exit month's salary was already paid by that month's regular run, so this run pays the settlement only.</summary>
    public bool SalaryExcluded { get; private set; }

    /// <summary>The leaver's last working day: salary is paid up to it even before Core HR records the exit.</summary>
    public DateOnly? LastWorkingDay { get; private set; }

    /// <summary>An off-cycle run for one leaver's full and final settlement.</summary>
    public static PayrollRun OpenFinalSettlement(DateOnly period, DateOnly payDate, Guid? legalEntityId, string currency, Guid employeeId, Guid settlementId, DateOnly lastWorkingDay, bool salaryExcluded, string? notes)
    {
        PayrollRun run = Open(period, payDate, legalEntityId, currency, notes);
        run.LastWorkingDay = lastWorkingDay;
        run.Kind = PayrollRunKind.FullAndFinal;
        run.EmployeeId = Guard.NotEmpty(employeeId);
        run.SourceId = Guard.NotEmpty(settlementId);
        run.SalaryExcluded = salaryExcluded;
        return run;
    }

    public static PayrollRun Open(DateOnly period, DateOnly payDate, Guid? legalEntityId, string currency, string? notes)
    {
        var start = new DateOnly(period.Year, period.Month, 1);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        if (payDate < start)
        {
            throw new DomainRuleException("RUN_PAY_DATE", "Salaries cannot be paid before the month starts.");
        }

        return new PayrollRun
        {
            Period = start,
            PeriodEnd = end,
            PayDate = payDate,
            LegalEntityId = legalEntityId,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            Status = RunStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 1000),
        };
    }

    public bool IsLocked => Status is RunStatus.Finalized or RunStatus.Paid;

    public void EnsureCalculable()
    {
        if (Status is not (RunStatus.Draft or RunStatus.Calculated))
        {
            throw new DomainRuleException("RUN_STATE", $"A {Describe(Status)} run cannot be recalculated.");
        }
    }

    public void Calculated(int employees, decimal gross, decimal deductions, decimal net, decimal employerCost, int warnings, int errors, DateTime now)
    {
        EnsureCalculable();
        Totals(employees, gross, deductions, net, employerCost, warnings, errors);
        CalculatedDate = now;
        Status = RunStatus.Calculated;
    }

    /// <summary>Totals after someone is put on hold or released, without a full recalculation.</summary>
    public void Totals(int employees, decimal gross, decimal deductions, decimal net, decimal employerCost, int warnings, int errors)
    {
        EmployeeCount = employees;
        Gross = gross;
        Deductions = deductions;
        Net = net;
        EmployerCost = employerCost;
        Warnings = warnings;
        Errors = errors;
    }

    public void Submit()
    {
        if (Status != RunStatus.Calculated)
        {
            throw new DomainRuleException("RUN_STATE", "Calculate the run before sending it for approval.");
        }

        if (Errors > 0)
        {
            throw new DomainRuleException("RUN_ERRORS", $"{Errors} employee(s) have errors. Fix them or put them on hold first.");
        }

        Status = RunStatus.PendingApproval;
    }

    public void AwaitApproval(Guid workflowInstanceId) => WorkflowInstanceId = workflowInstanceId;

    public void Approve(DateTime now)
    {
        if (Status != RunStatus.PendingApproval)
        {
            throw new DomainRuleException("RUN_STATE", "Only a run waiting for approval can be approved.");
        }

        Status = RunStatus.Approved;
        ApprovedDate = now;
    }

    /// <summary>Rejected or sent back: back to the payroll team to rework.</summary>
    public void ReturnForRework()
    {
        if (Status is RunStatus.PendingApproval or RunStatus.Approved)
        {
            Status = RunStatus.Calculated;
            ApprovedDate = null;
        }
    }

    public void Finalize(Guid? by, DateTime now)
    {
        if (Status != RunStatus.Approved)
        {
            throw new DomainRuleException("RUN_STATE", "Only an approved run can be finalized.");
        }

        Status = RunStatus.Finalized;
        FinalizedDate = now;
        FinalizedBy = by;
    }

    public void MarkPaid(DateTime paidOn, string? reference)
    {
        if (Status != RunStatus.Finalized)
        {
            throw new DomainRuleException("RUN_STATE", "Only a finalized run can be marked paid.");
        }

        Status = RunStatus.Paid;
        PaidDate = paidOn;
        PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100);
    }

    public void Cancel()
    {
        if (IsLocked)
        {
            throw new DomainRuleException("RUN_STATE", "A finalized run is permanent and cannot be cancelled.");
        }

        Status = RunStatus.Cancelled;
    }

    private static string Describe(RunStatus status) => status switch
    {
        RunStatus.PendingApproval => "pending approval",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public enum ItemStatus
{
    Ok = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>A line of a payslip, as calculated.</summary>
public sealed record PayLine(string Code, string Name, ComponentKind Kind, decimal Amount, bool Taxable, decimal? FullAmount = null, string? Note = null);

/// <summary>A statutory rule version a calculation used (spec §22: historic payroll keeps its exact rule).</summary>
public sealed record AppliedRule(string Code, Guid RuleId, int Version, string Type);

/// <summary>The PF figures an ECR needs, kept with the result.</summary>
public sealed record ProvidentFundFigures(decimal EpfWages, decimal EpsWages, decimal EdliWages, decimal EmployeeShare, decimal PensionShare, decimal EmployerShare, decimal EdliCharge, decimal AdminCharge);

/// <summary>How the month's TDS was worked out: the annual projection behind it.</summary>
public sealed record TaxComputation(
    string Regime,
    int MonthsRemaining,
    decimal GrossTaxableYear,
    decimal StandardDeduction,
    decimal HraExemption,
    decimal ProfessionalTax,
    decimal ChapterVIA,
    decimal TaxableIncome,
    decimal TaxBeforeRebate,
    decimal Rebate,
    decimal Surcharge,
    decimal Cess,
    decimal AnnualTax,
    decimal AlreadyDeducted,
    decimal MonthlyTds);

/// <summary>One employee's result in a run (spec §21): days, every line, the rules used, the tax projection and any issues.</summary>
public sealed class PayrollRunItem : TenantAggregateRoot
{
    private PayrollRunItem()
    {
        EmployeeCode = null!;
        EmployeeName = null!;
        LinesJson = "[]";
        RulesJson = "[]";
        IssuesJson = "[]";
    }

    public Guid RunId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; }

    public string EmployeeName { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public string? Designation { get; private set; }

    public Guid? SalaryId { get; private set; }

    public ItemStatus Status { get; private set; }

    public decimal PeriodDays { get; private set; }

    public decimal PaidDays { get; private set; }

    public decimal LopDays { get; private set; }

    public decimal AnnualCtc { get; private set; }

    public decimal Gross { get; private set; }

    public decimal Reimbursements { get; private set; }

    public decimal Deductions { get; private set; }

    public decimal Net { get; private set; }

    public decimal EmployerContributions { get; private set; }

    public decimal TaxableEarnings { get; private set; }

    public string LinesJson { get; private set; }

    public string RulesJson { get; private set; }

    public string? TaxJson { get; private set; }

    public string? ProvidentFundJson { get; private set; }

    public string IssuesJson { get; private set; }

    public string? StateCode { get; private set; }

    public bool OnHold { get; private set; }

    public string? HoldReason { get; private set; }

    public IReadOnlyList<PayLine> Lines => JsonSerializer.Deserialize<List<PayLine>>(LinesJson) ?? [];

    public IReadOnlyList<AppliedRule> Rules => JsonSerializer.Deserialize<List<AppliedRule>>(RulesJson) ?? [];

    public IReadOnlyList<string> Issues => JsonSerializer.Deserialize<List<string>>(IssuesJson) ?? [];

    public TaxComputation? Tax => TaxJson is null ? null : JsonSerializer.Deserialize<TaxComputation>(TaxJson);

    public ProvidentFundFigures? ProvidentFund => ProvidentFundJson is null ? null : JsonSerializer.Deserialize<ProvidentFundFigures>(ProvidentFundJson);

    public static PayrollRunItem For(Guid runId, PayResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PayrollRunItem
        {
            RunId = Guard.NotEmpty(runId),
            EmployeeId = result.EmployeeId,
            EmployeeCode = Guard.MaxLength(result.EmployeeCode, 40),
            EmployeeName = Guard.MaxLength(result.EmployeeName, 200),
            DepartmentId = result.DepartmentId,
            Designation = result.Designation is null ? null : Guard.MaxLength(result.Designation, 200),
            SalaryId = result.SalaryId,
            Status = result.Status,
            PeriodDays = result.PeriodDays,
            PaidDays = result.PaidDays,
            LopDays = result.LopDays,
            AnnualCtc = result.AnnualCtc,
            Gross = result.Gross,
            Reimbursements = result.Reimbursements,
            Deductions = result.Deductions,
            Net = result.Net,
            EmployerContributions = result.EmployerContributions,
            TaxableEarnings = result.TaxableEarnings,
            LinesJson = JsonSerializer.Serialize(result.Lines),
            RulesJson = JsonSerializer.Serialize(result.Rules),
            TaxJson = result.Tax is null ? null : JsonSerializer.Serialize(result.Tax),
            ProvidentFundJson = result.ProvidentFund is null ? null : JsonSerializer.Serialize(result.ProvidentFund),
            IssuesJson = JsonSerializer.Serialize(result.Issues),
            StateCode = result.StateCode,
        };
    }

    public void Hold(string reason)
    {
        OnHold = true;
        HoldReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 300);
    }

    public void Release()
    {
        OnHold = false;
        HoldReason = null;
    }

    /// <summary>Paid in this run: not held, and calculated without errors.</summary>
    public bool IsPayable => !OnHold && Status != ItemStatus.Error;
}

/// <summary>
/// A published payslip (spec §21): a frozen copy of the employee's result, as it was when the run was
/// finalized, with the details the employee needs on it.
/// </summary>
public sealed class Payslip : TenantAggregateRoot
{
    private Payslip()
    {
        Currency = null!;
        DocumentJson = null!;
    }

    public Guid RunId { get; private set; }

    public Guid RunItemId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateOnly Period { get; private set; }

    public DateOnly PayDate { get; private set; }

    public string Currency { get; private set; }

    public decimal Gross { get; private set; }

    public decimal Deductions { get; private set; }

    public decimal Net { get; private set; }

    public DateTime PublishedDate { get; private set; }

    /// <summary>The payslip as shown: employer, employee, days, lines, year to date and tax.</summary>
    public string DocumentJson { get; private set; }

    public static Payslip Publish(PayrollRun run, PayrollRunItem item, string documentJson, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(item);
        return new Payslip
        {
            RunId = run.Id,
            RunItemId = item.Id,
            EmployeeId = item.EmployeeId,
            Period = run.Period,
            PayDate = run.PayDate,
            Currency = run.Currency,
            Gross = item.Gross,
            Deductions = item.Deductions,
            Net = item.Net,
            PublishedDate = now,
            DocumentJson = Guard.NotNullOrWhiteSpace(documentJson),
        };
    }
}
