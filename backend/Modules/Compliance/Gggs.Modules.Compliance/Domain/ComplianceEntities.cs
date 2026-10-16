using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Compliance.Contracts;

namespace Gggs.Modules.Compliance.Domain;

public enum RuleStatus
{
    Draft = 1,
    Active = 2,
    Retired = 3,
}

/// <summary>
/// One version of a statutory rule (spec §22): EPF for the country, professional tax for a state.
/// A draft can be edited; once published it never changes, so payroll calculated with it can always
/// be explained. A change is a new version from a later date - the old one is closed the day before.
/// </summary>
public sealed class StatutoryRule : TenantAggregateRoot
{
    private StatutoryRule()
    {
        Code = null!;
        Name = null!;
        CountryCode = null!;
        Configuration = null!;
    }

    /// <summary>Shared by every version of the same rule, e.g. <c>IN-KA-PT</c>.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public StatutoryRuleType Type { get; private set; }

    public string CountryCode { get; private set; }

    public string? StateCode { get; private set; }

    public int Version { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public RuleStatus Status { get; private set; }

    /// <summary>The scheme's parameters as JSON; its shape follows <see cref="Type"/>.</summary>
    public string Configuration { get; private set; }

    /// <summary>Who it applies to, in words - for the reader, not the calculation.</summary>
    public string? Eligibility { get; private set; }

    public string? Notes { get; private set; }

    public DateTime? PublishedDate { get; private set; }

    public static StatutoryRule Draft(string code, string name, StatutoryRuleType type, string countryCode, string? stateCode, int version, DateOnly effectiveFrom, string configuration, string? eligibility, string? notes)
    {
        if (type is StatutoryRuleType.ProfessionalTax or StatutoryRuleType.LabourWelfareFund && string.IsNullOrWhiteSpace(stateCode))
        {
            throw new DomainRuleException("RULE_STATE_REQUIRED", "Professional tax and labour welfare rules belong to a state.");
        }

        var rule = new StatutoryRule
        {
            Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 40),
            Type = type,
            CountryCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant(), 8),
            StateCode = string.IsNullOrWhiteSpace(stateCode) ? null : Guard.MaxLength(stateCode.Trim().ToUpperInvariant(), 16),
            Version = version,
            Status = RuleStatus.Draft,
        };
        rule.Edit(name, effectiveFrom, configuration, eligibility, notes);
        return rule;
    }

    public void Edit(string name, DateOnly effectiveFrom, string configuration, string? eligibility, string? notes)
    {
        if (Status != RuleStatus.Draft)
        {
            throw new DomainRuleException("RULE_PUBLISHED", "A published rule cannot change. Draft a new version from a later date instead.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        EffectiveFrom = effectiveFrom;
        Configuration = Guard.NotNullOrWhiteSpace(configuration);
        Eligibility = string.IsNullOrWhiteSpace(eligibility) ? null : Guard.MaxLength(eligibility.Trim(), 1000);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.MaxLength(notes.Trim(), 2000);
    }

    public void Publish(DateTime now)
    {
        if (Status != RuleStatus.Draft)
        {
            throw new DomainRuleException("RULE_STATE", "Only a draft can be published.");
        }

        Status = RuleStatus.Active;
        PublishedDate = now;
    }

    /// <summary>A newer version takes over from <paramref name="nextStarts"/>: this one ends the day before.</summary>
    public void SupersedeFrom(DateOnly nextStarts)
    {
        if (nextStarts <= EffectiveFrom)
        {
            throw new DomainRuleException("RULE_VERSION_DATE", $"A new version must start after {EffectiveFrom:d MMM yyyy}, when version {Version} began.");
        }

        EffectiveTo = nextStarts.AddDays(-1);
    }

    /// <summary>The scheme no longer applies (a state abolishes a levy): it stops after <paramref name="lastDay"/>.</summary>
    public void Retire(DateOnly lastDay)
    {
        if (Status != RuleStatus.Active)
        {
            throw new DomainRuleException("RULE_STATE", "Only a published rule can be retired.");
        }

        if (lastDay < EffectiveFrom)
        {
            throw new DomainRuleException("RULE_VERSION_DATE", "A rule cannot end before it starts.");
        }

        EffectiveTo = EffectiveTo is { } current && current < lastDay ? current : lastDay;
        Status = RuleStatus.Retired;
    }

    public bool InForceOn(DateOnly day) => Status != RuleStatus.Draft && EffectiveFrom <= day && (EffectiveTo is null || EffectiveTo >= day);
}

public enum FilingKind
{
    ProvidentFundReturn = 1,
    StateInsuranceReturn = 2,
    ProfessionalTaxReturn = 3,
    TaxDeposit = 4,
    TaxReturn = 5,
    TaxCertificate = 6,
    LabourWelfareReturn = 7,
    Other = 99,
}

public enum FilingFrequency
{
    Monthly = 1,
    Quarterly = 2,
    HalfYearly = 3,
    Annual = 4,
}

/// <summary>A period of an obligation: its dates and how it reads.</summary>
public sealed record FilingPeriod(DateOnly Start, DateOnly End, string Label);

/// <summary>
/// Something the company must file or pay on a schedule (spec §107): the EPF return by the 15th,
/// TDS returns each quarter, Form 16 every June. Periods follow the Indian financial year.
/// </summary>
public sealed class ComplianceObligation : TenantAggregateRoot
{
    private ComplianceObligation()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public FilingKind Kind { get; private set; }

    public FilingFrequency Frequency { get; private set; }

    /// <summary>Day of the month it falls due (the month's last day when the month is shorter).</summary>
    public int DueDay { get; private set; }

    /// <summary>Months after the period's last month that it falls due: 1 for "the 15th of next month".</summary>
    public int DueMonthOffset { get; private set; }

    public string? StateCode { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Periods due before this day are not generated - an obligation set up today has no history.</summary>
    public DateOnly StartsFrom { get; private set; }

    public static ComplianceObligation Create(string code, ObligationDetails details, DateOnly startsFrom)
    {
        var obligation = new ComplianceObligation { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 40), StartsFrom = startsFrom };
        obligation.Update(details);
        return obligation;
    }

    public void Update(ObligationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.DueDay is < 1 or > 31 || details.DueMonthOffset is < 0 or > 12)
        {
            throw new DomainRuleException("OBLIGATION_DUE", "The due day is 1-31 and the month offset 0-12.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Kind = details.Kind;
        Frequency = details.Frequency;
        DueDay = details.DueDay;
        DueMonthOffset = details.DueMonthOffset;
        StateCode = string.IsNullOrWhiteSpace(details.StateCode) ? null : Guard.MaxLength(details.StateCode.Trim().ToUpperInvariant(), 16);
        OwnerEmployeeId = details.OwnerEmployeeId;
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : Guard.MaxLength(details.Description.Trim(), 1000);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>The period containing <paramref name="day"/>.</summary>
    public FilingPeriod PeriodOf(DateOnly day) => FilingCalendar.PeriodOf(Frequency, day);

    public DateOnly DueDateOf(FilingPeriod period) => FilingCalendar.DueDate(period, DueMonthOffset, DueDay);
}

public sealed record ObligationDetails(string Name, FilingKind Kind, FilingFrequency Frequency, int DueDay, int DueMonthOffset, string? StateCode, Guid? OwnerEmployeeId, string? Description);

/// <summary>Financial-year periods and due dates.</summary>
public static class FilingCalendar
{
    /// <summary>The financial year (April-March) a day falls in, as its first year: 2026 for Apr 2026-Mar 2027.</summary>
    public static int FinancialYearOf(DateOnly day) => day.Month >= 4 ? day.Year : day.Year - 1;

    public static string FinancialYearLabel(int startYear) => $"FY{startYear}-{(startYear + 1) % 100:00}";

    public static FilingPeriod PeriodOf(FilingFrequency frequency, DateOnly day)
    {
        int fy = FinancialYearOf(day);
        var fyStart = new DateOnly(fy, 4, 1);
        int monthIndex = ((day.Year - fy) * 12) + day.Month - 4;
        switch (frequency)
        {
            case FilingFrequency.Monthly:
                {
                    var start = new DateOnly(day.Year, day.Month, 1);
                    return new FilingPeriod(start, start.AddMonths(1).AddDays(-1), start.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
                }

            case FilingFrequency.Quarterly:
                {
                    int quarter = monthIndex / 3;
                    DateOnly start = fyStart.AddMonths(quarter * 3);
                    return new FilingPeriod(start, start.AddMonths(3).AddDays(-1), $"Q{quarter + 1} {FinancialYearLabel(fy)}");
                }

            case FilingFrequency.HalfYearly:
                {
                    int half = monthIndex / 6;
                    DateOnly start = fyStart.AddMonths(half * 6);
                    return new FilingPeriod(start, start.AddMonths(6).AddDays(-1), $"H{half + 1} {FinancialYearLabel(fy)}");
                }

            default:
                return new FilingPeriod(fyStart, fyStart.AddYears(1).AddDays(-1), FinancialYearLabel(fy));
        }
    }

    public static DateOnly DueDate(FilingPeriod period, int monthOffset, int dueDay)
    {
        ArgumentNullException.ThrowIfNull(period);
        DateOnly month = new DateOnly(period.End.Year, period.End.Month, 1).AddMonths(monthOffset);
        return new DateOnly(month.Year, month.Month, Math.Min(dueDay, DateTime.DaysInMonth(month.Year, month.Month)));
    }
}

public enum FilingStatus
{
    Pending = 1,
    Filed = 2,
    Waived = 3,
}

/// <summary>One period of an obligation: due, filed (with the challan or acknowledgement), or waived.</summary>
public sealed class ComplianceFiling : TenantAggregateRoot
{
    private ComplianceFiling()
    {
        Name = null!;
        PeriodLabel = null!;
    }

    public Guid ObligationId { get; private set; }

    public string Name { get; private set; }

    public FilingKind Kind { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public string PeriodLabel { get; private set; }

    public DateOnly DueDate { get; private set; }

    public FilingStatus Status { get; private set; }

    public DateOnly? FiledOn { get; private set; }

    public Guid? FiledByEmployeeId { get; private set; }

    /// <summary>Challan, acknowledgement or receipt number.</summary>
    public string? Reference { get; private set; }

    public decimal? Amount { get; private set; }

    public string? Notes { get; private set; }

    public Guid? DocumentId { get; private set; }

    /// <summary>When the last reminder went out, so the job reminds once per milestone.</summary>
    public DateOnly? RemindedOn { get; private set; }

    public static ComplianceFiling For(ComplianceObligation obligation, FilingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        ArgumentNullException.ThrowIfNull(period);
        return new ComplianceFiling
        {
            ObligationId = obligation.Id,
            Name = obligation.Name,
            Kind = obligation.Kind,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            PeriodLabel = period.Label,
            DueDate = obligation.DueDateOf(period),
            Status = FilingStatus.Pending,
        };
    }

    public bool IsOverdue(DateOnly today) => Status == FilingStatus.Pending && DueDate < today;

    public void MarkFiled(DateOnly filedOn, Guid? filedBy, string? reference, decimal? amount, string? notes, Guid? documentId)
    {
        if (Status == FilingStatus.Filed)
        {
            throw new DomainRuleException("FILING_STATE", "This filing is already recorded as filed.");
        }

        Status = FilingStatus.Filed;
        FiledOn = filedOn;
        FiledByEmployeeId = filedBy;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : Guard.MaxLength(reference.Trim(), 100);
        Amount = amount is < 0 ? throw new DomainRuleException("FILING_AMOUNT", "The amount cannot be negative.") : amount;
        Notes = string.IsNullOrWhiteSpace(notes) ? Notes : Guard.MaxLength(notes.Trim(), 1000);
        DocumentId = documentId ?? DocumentId;
    }

    public void Waive(string reason)
    {
        if (Status != FilingStatus.Pending)
        {
            throw new DomainRuleException("FILING_STATE", "Only a pending filing can be waived.");
        }

        Status = FilingStatus.Waived;
        Notes = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 1000);
    }

    public void Reopen()
    {
        Status = FilingStatus.Pending;
        FiledOn = null;
        FiledByEmployeeId = null;
    }

    public void Reschedule(DateOnly dueDate)
    {
        if (Status != FilingStatus.Pending)
        {
            throw new DomainRuleException("FILING_STATE", "Only a pending filing can be rescheduled.");
        }

        DueDate = dueDate;
        RemindedOn = null;
    }

    public void Reminded(DateOnly today) => RemindedOn = today;
}

public enum RegistrationKind
{
    License = 1,
    Registration = 2,
    Certificate = 3,
    Audit = 4,
    Inspection = 5,
}

/// <summary>
/// A licence, registration, certificate or audit the company holds or has undergone (spec §107):
/// shops and establishments, PF and ESI codes, a factory licence, an ISO certificate, a statutory audit.
/// </summary>
public sealed class ComplianceRegistration : TenantAggregateRoot
{
    private ComplianceRegistration()
    {
        Name = null!;
    }

    public RegistrationKind Kind { get; private set; }

    public string Name { get; private set; }

    public string? Number { get; private set; }

    public string? Authority { get; private set; }

    public string? StateCode { get; private set; }

    public Guid? LegalEntityId { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>How long before expiry renewal should start.</summary>
    public int RenewalLeadDays { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    /// <summary>For an audit or inspection: what it found.</summary>
    public string? Notes { get; private set; }

    public Guid? DocumentId { get; private set; }

    public DateOnly? RemindedOn { get; private set; }

    public static ComplianceRegistration Create(RegistrationDetails details)
    {
        var registration = new ComplianceRegistration();
        registration.Update(details);
        return registration;
    }

    public void Update(RegistrationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.IssuedOn is { } issued && details.ExpiresOn is { } expires && expires < issued)
        {
            throw new DomainRuleException("REGISTRATION_DATES", "It cannot expire before it was issued.");
        }

        Kind = details.Kind;
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Number = string.IsNullOrWhiteSpace(details.Number) ? null : Guard.MaxLength(details.Number.Trim(), 100);
        Authority = string.IsNullOrWhiteSpace(details.Authority) ? null : Guard.MaxLength(details.Authority.Trim(), 200);
        StateCode = string.IsNullOrWhiteSpace(details.StateCode) ? null : Guard.MaxLength(details.StateCode.Trim().ToUpperInvariant(), 16);
        LegalEntityId = details.LegalEntityId;
        if (details.ExpiresOn != ExpiresOn)
        {
            RemindedOn = null;
        }

        IssuedOn = details.IssuedOn;
        ExpiresOn = details.ExpiresOn;
        RenewalLeadDays = details.RenewalLeadDays is >= 0 and <= 365 ? details.RenewalLeadDays : 30;
        OwnerEmployeeId = details.OwnerEmployeeId;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Guard.MaxLength(details.Notes.Trim(), 2000);
        DocumentId = details.DocumentId;
    }

    public string Validity(DateOnly today) => ExpiresOn switch
    {
        null => "NoExpiry",
        { } expires when expires < today => "Expired",
        { } expires when expires <= today.AddDays(RenewalLeadDays) => "ExpiringSoon",
        _ => "Valid",
    };

    public void Reminded(DateOnly today) => RemindedOn = today;
}

public sealed record RegistrationDetails(
    RegistrationKind Kind,
    string Name,
    string? Number,
    string? Authority,
    string? StateCode,
    Guid? LegalEntityId,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    int RenewalLeadDays,
    Guid? OwnerEmployeeId,
    string? Notes,
    Guid? DocumentId);
