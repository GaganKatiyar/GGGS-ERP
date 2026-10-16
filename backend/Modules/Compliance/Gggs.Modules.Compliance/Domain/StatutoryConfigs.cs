using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Compliance.Contracts;

namespace Gggs.Modules.Compliance.Domain;

/// <summary>Checks a rule's configuration against its scheme before it is saved, so payroll never meets a malformed rule.</summary>
public static class StatutoryConfigValidator
{
    /// <summary>Returns the configuration normalised (camelCase, no unknown fields); throws when it does not fit the scheme.</summary>
    public static string Normalise(StatutoryRuleType type, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("The rule needs a configuration.");
        }

        try
        {
            return type switch
            {
                StatutoryRuleType.ProvidentFund => Write(Check(Read<ProvidentFundConfig>(json))),
                StatutoryRuleType.StateInsurance => Write(Check(Read<StateInsuranceConfig>(json))),
                StatutoryRuleType.ProfessionalTax => Write(Check(Read<ProfessionalTaxConfig>(json))),
                StatutoryRuleType.IncomeTax => Write(Check(Read<IncomeTaxConfig>(json))),
                StatutoryRuleType.Gratuity => Write(Check(Read<GratuityConfig>(json))),
                StatutoryRuleType.LabourWelfareFund => Write(Check(Read<LabourWelfareConfig>(json))),
                _ => throw Invalid("Unknown rule type."),
            };
        }
        catch (JsonException exception)
        {
            throw Invalid($"The configuration does not fit a {type} rule: {exception.Message}");
        }
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, StatutoryJson.Options) ?? throw Invalid("The configuration is empty.");

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, StatutoryJson.Options);

    private static ProvidentFundConfig Check(ProvidentFundConfig config)
    {
        Rates(config.EmployeeRate, config.EmployerPensionRate, config.EmployerProvidentRate, config.EdliRate, config.AdminChargeRate);
        Positive(config.WageCeiling, "wage ceiling");
        if (config.PensionAgeLimit is < 40 or > 80)
        {
            throw Invalid("The pension age limit should be between 40 and 80.");
        }

        return config;
    }

    private static StateInsuranceConfig Check(StateInsuranceConfig config)
    {
        Rates(config.EmployeeRate, config.EmployerRate);
        Positive(config.WageThreshold, "wage threshold");
        return config;
    }

    private static ProfessionalTaxConfig Check(ProfessionalTaxConfig config)
    {
        if (config.Slabs is null || config.Slabs.Count == 0)
        {
            throw Invalid("Professional tax needs at least one slab.");
        }

        Steps(config.Slabs.Select(slab => slab.From), "slab");
        if (config.Slabs.Any(slab => slab.Amount < 0 || slab.FebruaryAmount < 0))
        {
            throw Invalid("Professional tax amounts cannot be negative.");
        }

        return config with { Slabs = [.. config.Slabs.OrderBy(slab => slab.From)] };
    }

    private static IncomeTaxConfig Check(IncomeTaxConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.FinancialYear))
        {
            throw Invalid("Name the financial year, e.g. 2025-26.");
        }

        Regime(config.NewRegime, "new");
        Regime(config.OldRegime, "old");
        Rates(config.CessRate);
        Rates([.. (config.Surcharge ?? []).Select(slab => slab.Rate)]);
        return config with
        {
            Surcharge = [.. (config.Surcharge ?? []).OrderBy(slab => slab.From)],
            SectionLimits = config.SectionLimits ?? new Dictionary<string, decimal>(),
            NewRegime = config.NewRegime with { Slabs = [.. config.NewRegime.Slabs.OrderBy(slab => slab.From)] },
            OldRegime = config.OldRegime with { Slabs = [.. config.OldRegime.Slabs.OrderBy(slab => slab.From)] },
        };
    }

    private static void Regime(IncomeTaxRegime? regime, string name)
    {
        if (regime?.Slabs is null || regime.Slabs.Count == 0)
        {
            throw Invalid($"The {name} regime needs its slabs.");
        }

        Steps(regime.Slabs.Select(slab => slab.From), $"{name}-regime slab");
        Rates([.. regime.Slabs.Select(slab => slab.Rate)]);
        Rates(regime.SurchargeCap);
        if (regime.StandardDeduction < 0 || regime.RebateIncomeLimit < 0 || regime.RebateMaximum < 0)
        {
            throw Invalid($"The {name} regime's deduction and rebate cannot be negative.");
        }
    }

    private static GratuityConfig Check(GratuityConfig config)
    {
        Positive(config.DaysPerYear, "days per year");
        Positive(config.DivisorDays, "divisor");
        Positive(config.MaximumAmount, "maximum");
        Rates(config.MonthlyAccrualRate);
        if (config.MinimumYears < 0)
        {
            throw Invalid("The minimum service cannot be negative.");
        }

        return config;
    }

    private static LabourWelfareConfig Check(LabourWelfareConfig config)
    {
        if (config.EmployeeAmount < 0 || config.EmployerAmount < 0)
        {
            throw Invalid("Labour welfare amounts cannot be negative.");
        }

        if (config.Months is null || config.Months.Count == 0 || config.Months.Any(month => month is < 1 or > 12))
        {
            throw Invalid("List the months (1-12) the contribution is deducted in.");
        }

        return config with { Months = [.. config.Months.Distinct().Order()] };
    }

    private static void Steps(IEnumerable<decimal> froms, string what)
    {
        List<decimal> list = [.. froms];
        if (list.Any(from => from < 0) || list.Distinct().Count() != list.Count)
        {
            throw Invalid($"Each {what} needs its own starting amount, zero or more.");
        }

        if (list.Min() != 0)
        {
            throw Invalid($"The first {what} starts at zero, so every amount falls in one.");
        }
    }

    private static void Rates(params decimal[] rates)
    {
        if (rates.Any(rate => rate is < 0 or > 100))
        {
            throw Invalid("Rates are percentages between 0 and 100.");
        }
    }

    private static void Positive(decimal value, string what)
    {
        if (value <= 0)
        {
            throw Invalid($"The {what} must be more than zero.");
        }
    }

    private static DomainRuleException Invalid(string message) => new("RULE_CONFIGURATION", message);
}

/// <summary>A rule as the seeder writes it.</summary>
public sealed record RuleSeed(string Code, string Name, StatutoryRuleType Type, string? StateCode, DateOnly EffectiveFrom, object Configuration, string Eligibility);

/// <summary>An obligation as the seeder writes it.</summary>
public sealed record ObligationSeed(string Code, ObligationDetails Details);

/// <summary>
/// India's statutory schemes as they stood when this was written, seeded into every tenant as
/// published version 1 (spec §22: configurable, never hard-coded). When a rate changes, a new
/// version is published in the compliance centre - nothing here is read after seeding.
/// </summary>
public static class IndiaStatutoryDefaults
{
    public static IReadOnlyList<RuleSeed> Rules { get; } =
    [
        new("IN-EPF", "Employees' Provident Fund", StatutoryRuleType.ProvidentFund, null, new DateOnly(2014, 9, 1),
            new ProvidentFundConfig(12m, 8.33m, 3.67m, 15000m, CapEmployeeAtCeiling: true, CapEmployerAtCeiling: true, 0.5m, 0.5m, 58),
            "Employees of establishments covered by the EPF Act. Contributions on PF wages up to the ceiling."),
        new("IN-ESI", "Employees' State Insurance", StatutoryRuleType.StateInsurance, null, new DateOnly(2019, 7, 1),
            new StateInsuranceConfig(0.75m, 3.25m, 21000m),
            "Employees whose monthly gross is at or below the threshold."),
        new("IN-KA-PT", "Professional tax - Karnataka", StatutoryRuleType.ProfessionalTax, "IN-KA", new DateOnly(2025, 4, 1),
            new ProfessionalTaxConfig([new(0m, 0m), new(25000m, 200m, 300m)]),
            "Salaried employees working in Karnataka; ₹300 in February brings the year to ₹2,500."),
        new("IN-MH-PT", "Professional tax - Maharashtra", StatutoryRuleType.ProfessionalTax, "IN-MH", new DateOnly(2023, 4, 1),
            new ProfessionalTaxConfig([new(0m, 0m), new(7501m, 175m), new(10001m, 200m, 300m)]),
            "Salaried employees working in Maharashtra. Women earning up to ₹25,000 a month are exempt."),
        new("IN-TG-PT", "Professional tax - Telangana", StatutoryRuleType.ProfessionalTax, "IN-TG", new DateOnly(2023, 4, 1),
            new ProfessionalTaxConfig([new(0m, 0m), new(15001m, 150m), new(20001m, 200m)]),
            "Salaried employees working in Telangana."),
        new("IN-WB-PT", "Professional tax - West Bengal", StatutoryRuleType.ProfessionalTax, "IN-WB", new DateOnly(2023, 4, 1),
            new ProfessionalTaxConfig([new(0m, 0m), new(10001m, 110m), new(15001m, 130m), new(25001m, 150m), new(40001m, 200m)]),
            "Salaried employees working in West Bengal."),
        new("IN-TDS", "Income tax on salary (TDS)", StatutoryRuleType.IncomeTax, null, new DateOnly(2025, 4, 1),
            new IncomeTaxConfig(
                "2025-26",
                new IncomeTaxRegime(
                    75000m,
                    [new(0m, 0m), new(400000m, 5m), new(800000m, 10m), new(1200000m, 15m), new(1600000m, 20m), new(2000000m, 25m), new(2400000m, 30m)],
                    1200000m, 60000m, RebateMarginalRelief: true, AllowsDeductions: false, SurchargeCap: 25m),
                new IncomeTaxRegime(
                    50000m,
                    [new(0m, 0m), new(250000m, 5m), new(500000m, 20m), new(1000000m, 30m)],
                    500000m, 12500m, RebateMarginalRelief: false, AllowsDeductions: true, SurchargeCap: 37m),
                4m,
                [new(5000000m, 10m), new(10000000m, 15m), new(20000000m, 25m), new(50000000m, 37m)],
                new Dictionary<string, decimal>
                {
                    ["80C"] = 150000m,
                    ["80CCD(1B)"] = 50000m,
                    ["80D"] = 25000m,
                    ["80D-PARENTS"] = 50000m,
                    ["80TTA"] = 10000m,
                    ["24(b)"] = 200000m,
                }),
            "Resident salaried employees. The new regime is the default; the old regime allows Chapter VI-A deductions and HRA."),
        new("IN-GRATUITY", "Gratuity", StatutoryRuleType.Gratuity, null, new DateOnly(2018, 3, 29),
            new GratuityConfig(15m, 26m, 5m, 2000000m, 4.81m),
            "Employees with five or more years of continuous service; accrued monthly at the accrual rate of basic."),
        new("IN-KA-LWF", "Labour welfare fund - Karnataka", StatutoryRuleType.LabourWelfareFund, "IN-KA", new DateOnly(2021, 1, 1),
            new LabourWelfareConfig(50m, 100m, [12]),
            "Employees working in Karnataka; deducted once a year with December's salary."),
    ];

    public static IReadOnlyList<ObligationSeed> Obligations { get; } =
    [
        new("EPF-ECR", new ObligationDetails("EPF return (ECR) and payment", FilingKind.ProvidentFundReturn, FilingFrequency.Monthly, 15, 1, null, null,
            "Electronic challan-cum-return for the month's provident fund contributions.")),
        new("ESI-CONTRIBUTION", new ObligationDetails("ESI contribution", FilingKind.StateInsuranceReturn, FilingFrequency.Monthly, 15, 1, null, null,
            "The month's ESI contributions, paid on the ESIC portal.")),
        new("TDS-DEPOSIT", new ObligationDetails("TDS deposit (salary)", FilingKind.TaxDeposit, FilingFrequency.Monthly, 7, 1, null, null,
            "Tax deducted from salaries, deposited by the 7th of the next month (March's by 30 April).")),
        new("TDS-24Q", new ObligationDetails("TDS return (Form 24Q)", FilingKind.TaxReturn, FilingFrequency.Quarterly, 31, 1, null, null,
            "Quarterly salary TDS return. The fourth quarter's return is due by 31 May - reschedule it when it is generated.")),
        new("FORM-16", new ObligationDetails("Form 16 to employees", FilingKind.TaxCertificate, FilingFrequency.Annual, 15, 3, null, null,
            "Annual salary TDS certificates, issued by 15 June.")),
        new("PT-KA", new ObligationDetails("Professional tax return (Karnataka)", FilingKind.ProfessionalTaxReturn, FilingFrequency.Monthly, 20, 1, "IN-KA", null,
            "Monthly professional tax return and payment for employees in Karnataka.")),
    ];
}
