using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gggs.Modules.Compliance.Contracts;

/// <summary>The statutory schemes a rule can configure (spec §22).</summary>
public enum StatutoryRuleType
{
    ProvidentFund = 1,
    StateInsurance = 2,
    ProfessionalTax = 3,
    IncomeTax = 4,
    Gratuity = 5,
    LabourWelfareFund = 6,
}

/// <summary>
/// EPF and EPS (spec §22). Rates are percentages. With a cap, contributions are worked out on wages
/// up to the ceiling; above the pension age no pension share is paid and the employer's whole
/// contribution goes to the provident fund.
/// </summary>
public sealed record ProvidentFundConfig(
    decimal EmployeeRate,
    decimal EmployerPensionRate,
    decimal EmployerProvidentRate,
    decimal WageCeiling,
    bool CapEmployeeAtCeiling,
    bool CapEmployerAtCeiling,
    decimal EdliRate,
    decimal AdminChargeRate,
    int PensionAgeLimit);

/// <summary>ESI: both shares apply only while monthly gross is at or below the threshold.</summary>
public sealed record StateInsuranceConfig(decimal EmployeeRate, decimal EmployerRate, decimal WageThreshold);

/// <summary>A professional-tax step: monthly gross from <see cref="From"/> up to the next step pays <see cref="Amount"/>.</summary>
public sealed record ProfessionalTaxSlab(decimal From, decimal Amount, decimal? FebruaryAmount = null);

public sealed record ProfessionalTaxConfig(IReadOnlyList<ProfessionalTaxSlab> Slabs);

/// <summary>A progressive tax step: income above <see cref="From"/>, up to the next step, is taxed at <see cref="Rate"/> percent.</summary>
public sealed record TaxSlab(decimal From, decimal Rate);

/// <summary>Surcharge on tax once taxable income passes <see cref="From"/>.</summary>
public sealed record SurchargeSlab(decimal From, decimal Rate);

/// <summary>
/// One income-tax regime. The rebate wipes out tax up to <see cref="RebateMaximum"/> while taxable
/// income stays within <see cref="RebateIncomeLimit"/>; with marginal relief, income just above the
/// limit never pays more tax than the income above it.
/// </summary>
public sealed record IncomeTaxRegime(
    decimal StandardDeduction,
    IReadOnlyList<TaxSlab> Slabs,
    decimal RebateIncomeLimit,
    decimal RebateMaximum,
    bool RebateMarginalRelief,
    bool AllowsDeductions,
    decimal SurchargeCap);

/// <summary>Salary TDS for a financial year: both regimes, cess, surcharge and Chapter VI-A limits.</summary>
public sealed record IncomeTaxConfig(
    string FinancialYear,
    IncomeTaxRegime NewRegime,
    IncomeTaxRegime OldRegime,
    decimal CessRate,
    IReadOnlyList<SurchargeSlab> Surcharge,
    IReadOnlyDictionary<string, decimal> SectionLimits);

/// <summary>Gratuity: (last basic × days per year ÷ divisor) per completed year, after the minimum service, up to the maximum.</summary>
public sealed record GratuityConfig(decimal DaysPerYear, decimal DivisorDays, decimal MinimumYears, decimal MaximumAmount, decimal MonthlyAccrualRate);

/// <summary>Labour welfare fund: flat amounts deducted in the listed months (1-12).</summary>
public sealed record LabourWelfareConfig(decimal EmployeeAmount, decimal EmployerAmount, IReadOnlyList<int> Months);

/// <summary>
/// One published version of a statutory rule. Payroll records the id and version it used, so a
/// historic payslip can always say which rule produced it.
/// </summary>
public sealed record StatutoryRuleVersion(
    Guid Id,
    string Code,
    StatutoryRuleType Type,
    string CountryCode,
    string? StateCode,
    int Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Name,
    string Configuration)
{
    public T Read<T>() => JsonSerializer.Deserialize<T>(Configuration, StatutoryJson.Options)
        ?? throw new InvalidOperationException($"Statutory rule {Code} v{Version} has no configuration.");
}

/// <summary>How rule configurations are written: camelCase, as the API and the screens see them.</summary>
public static class StatutoryJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

/// <summary>The rule versions in force (ADR-12). Payroll asks; it never keeps its own copy.</summary>
public interface IStatutoryRuleBook
{
    /// <summary>
    /// The version of a scheme in force on a day: the state's own rule when <paramref name="stateCode"/>
    /// is given and one exists, otherwise the country-wide rule. Null when there is none.
    /// </summary>
    Task<StatutoryRuleVersion?> ResolveAsync(StatutoryRuleType type, string? stateCode, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Every published version in force on a day - one read for a whole payroll run.</summary>
    Task<IReadOnlyList<StatutoryRuleVersion>> InForceAsync(DateOnly day, CancellationToken cancellationToken = default);
}
