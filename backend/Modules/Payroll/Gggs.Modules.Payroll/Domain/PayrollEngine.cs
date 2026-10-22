using Gggs.Modules.Benefits.Contracts;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Payroll.Contracts;

namespace Gggs.Modules.Payroll.Domain;

/// <summary>A component as it applies on a structure: the component's defaults with the structure's overrides.</summary>
public sealed record ComponentRule(
    Guid Id,
    string Code,
    string Name,
    ComponentKind Kind,
    CalculationType Type,
    decimal? Value,
    string? PercentOf,
    string? Formula,
    PayrollInputKind? InputKind,
    bool Taxable,
    bool Pf,
    bool Esi,
    bool Pt,
    bool Prorated,
    bool ShowOnPayslip,
    int Order)
{
    public static ComponentRule From(SalaryComponent component, StructureLine? line = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        CalculationType type = line?.CalculationType ?? component.CalculationType;
        bool overridden = line?.CalculationType is not null;
        return new ComponentRule(
            component.Id, component.Code, component.Name, component.Kind, type,
            overridden ? line!.Value : line?.Value ?? component.Value,
            overridden ? line!.PercentOf?.ToUpperInvariant() : line?.PercentOf?.ToUpperInvariant() ?? component.PercentOf,
            overridden ? line!.Formula : line?.Formula ?? component.Formula,
            component.InputKind, component.IsTaxable, component.PfApplicable, component.EsiApplicable, component.PtApplicable, component.IsProrated,
            component.ShowOnPayslip, line?.Order ?? component.DisplayOrder);
    }
}

/// <summary>A component's monthly amount in a CTC breakdown.</summary>
public sealed record BreakdownLine(ComponentRule Component, decimal Monthly)
{
    public decimal Annual => Monthly * 12;
}

/// <summary>A CTC laid out by a structure for a full month.</summary>
public sealed record Breakdown(IReadOnlyList<BreakdownLine> Lines, decimal MonthlyCtc, bool EsiApplies, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public decimal Gross => Lines.Where(line => line.Component.Kind == ComponentKind.Earning).Sum(line => line.Monthly);

    public decimal Amount(string code) => Lines.FirstOrDefault(line => line.Component.Code == code)?.Monthly ?? 0;
}

/// <summary>The schemes that shape a CTC: employer PF and ESI and gratuity sit inside it.</summary>
public sealed record CtcRules(ProvidentFundConfig? ProvidentFund, StateInsuranceConfig? StateInsurance, GratuityConfig? Gratuity, bool PfEnabled);

/// <summary>Rounds as Indian payroll does: to the nearest rupee, halves up.</summary>
public static class Money
{
    public static decimal Rupees(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    public static decimal UpToRupee(decimal value) => Math.Ceiling(value);
}

/// <summary>
/// Lays an annual CTC out over a structure for one month (spec §21, §23): fixed, percentage and
/// formula components in dependency order, employer PF and gratuity on top, and the balancing
/// component taking what is left - net of employer ESI when the gross is low enough to attract it.
/// </summary>
public static class SalaryBreakdown
{
    public static Breakdown Calculate(decimal annualCtc, IReadOnlyList<ComponentRule> structure, CtcRules rules)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(rules);
        decimal ctc = Math.Round(annualCtc / 12, 2);
        var errors = new List<string>();
        var warnings = new List<string>();
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["CTC"] = ctc };
        var amounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        List<ComponentRule> calculated = [.. structure.Where(rule =>
            rule.Kind is ComponentKind.Earning or ComponentKind.EmployerContribution && rule.Type is CalculationType.Fixed or CalculationType.Percentage or CalculationType.Formula)];
        List<ComponentRule> balancing = [.. structure.Where(rule => rule.Type == CalculationType.Balancing)];
        if (balancing.Count > 1)
        {
            errors.Add("A structure can have only one balancing component.");
        }

        // Resolve in dependency order: each pass computes whatever has all its inputs.
        var pending = new List<ComponentRule>(calculated);
        while (pending.Count > 0)
        {
            ComponentRule? ready = pending.FirstOrDefault(rule => Dependencies(rule).All(values.ContainsKey));
            if (ready is null)
            {
                errors.Add($"{string.Join(", ", pending.Select(rule => rule.Code))} depend on each other or on something not in the structure.");
                break;
            }

            decimal amount = Money.Rupees(Evaluate(ready, values));
            values[ready.Code] = amount;
            amounts[ready.Code] = amount;
            pending.Remove(ready);
        }

        decimal earnings = calculated.Where(rule => rule.Kind == ComponentKind.Earning).Sum(rule => amounts.GetValueOrDefault(rule.Code));
        decimal pfWages = calculated.Where(rule => rule.Kind == ComponentKind.Earning && rule.Pf).Sum(rule => amounts.GetValueOrDefault(rule.Code));

        decimal employerPf = 0;
        if (Has(structure, StatutoryKeys.ProvidentFundEmployer) && rules.PfEnabled && rules.ProvidentFund is { } pf)
        {
            decimal basis = pf.CapEmployerAtCeiling ? Math.Min(pfWages, pf.WageCeiling) : pfWages;
            employerPf = Money.Rupees(basis * (pf.EmployerPensionRate + pf.EmployerProvidentRate) / 100);
        }

        decimal gratuity = 0;
        if (Has(structure, StatutoryKeys.Gratuity) && rules.Gratuity is { } gratuityRule)
        {
            if (!values.TryGetValue("BASIC", out decimal basic))
            {
                warnings.Add("Gratuity accrues on BASIC, which this structure does not have.");
            }

            gratuity = Money.Rupees(basic * gratuityRule.MonthlyAccrualRate / 100);
        }

        decimal otherEmployer = calculated.Where(rule => rule.Kind == ComponentKind.EmployerContribution).Sum(rule => amounts.GetValueOrDefault(rule.Code));
        decimal fixedCost = earnings + employerPf + gratuity + otherEmployer;

        decimal balance = 0;
        decimal employerEsi = 0;
        bool esiApplies = false;
        bool wantsEsi = Has(structure, StatutoryKeys.StateInsuranceEmployer) && rules.StateInsurance is not null;
        ComponentRule? balancer = balancing.FirstOrDefault();
        if (balancer is not null)
        {
            balance = ctc - fixedCost;
            if (wantsEsi && rules.StateInsurance is { } esi)
            {
                decimal rate = esi.EmployerRate / 100;
                decimal candidate = ((ctc - employerPf - gratuity - otherEmployer) / (1 + rate)) - earnings;
                if (earnings + candidate <= esi.WageThreshold)
                {
                    decimal esiGross = calculated.Where(rule => rule.Kind == ComponentKind.Earning && rule.Esi).Sum(rule => amounts.GetValueOrDefault(rule.Code))
                        + (balancer.Esi ? candidate : 0);
                    employerEsi = Money.UpToRupee(esiGross * rate);
                    balance = ctc - fixedCost - employerEsi;
                    esiApplies = true;
                }
            }

            balance = Math.Round(balance, 2);
            if (balance < 0)
            {
                errors.Add($"The components come to more than the CTC by {-balance:0.##} a month; lower a percentage or raise the CTC.");
            }
        }
        else
        {
            if (wantsEsi && rules.StateInsurance is { } esi && earnings <= esi.WageThreshold)
            {
                decimal esiGross = calculated.Where(rule => rule.Kind == ComponentKind.Earning && rule.Esi).Sum(rule => amounts.GetValueOrDefault(rule.Code));
                employerEsi = Money.UpToRupee(esiGross * esi.EmployerRate / 100);
                esiApplies = true;
            }

            decimal gap = Math.Round(ctc - fixedCost - employerEsi, 2);
            if (Math.Abs(gap) >= 1)
            {
                warnings.Add($"The structure does not add up to the CTC (a difference of {gap:0.##} a month); add a balancing component.");
            }
        }

        var lines = new List<BreakdownLine>();
        foreach (ComponentRule rule in structure.OrderBy(rule => rule.Order))
        {
            decimal? monthly = rule.Code switch
            {
                StatutoryKeys.ProvidentFundEmployer => rules.PfEnabled && rules.ProvidentFund is not null ? employerPf : null,
                StatutoryKeys.StateInsuranceEmployer => esiApplies ? employerEsi : null,
                StatutoryKeys.Gratuity => rules.Gratuity is not null ? gratuity : null,
                _ when rule.Type == CalculationType.Balancing => Math.Max(0, balance),
                _ when amounts.TryGetValue(rule.Code, out decimal amount) => amount,
                _ => null,
            };

            if (monthly is { } value)
            {
                lines.Add(new BreakdownLine(rule, value));
            }
        }

        return new Breakdown(lines, ctc, esiApplies, errors, warnings);
    }

    private static bool Has(IReadOnlyList<ComponentRule> structure, string code) => structure.Any(rule => rule.Code == code);

    private static IEnumerable<string> Dependencies(ComponentRule rule) => rule.Type switch
    {
        CalculationType.Percentage => [rule.PercentOf ?? "CTC"],
        CalculationType.Formula => Formula.References(rule.Formula ?? "0"),
        _ => [],
    };

    private static decimal Evaluate(ComponentRule rule, IReadOnlyDictionary<string, decimal> values) => rule.Type switch
    {
        CalculationType.Fixed => rule.Value ?? 0,
        CalculationType.Percentage => values.GetValueOrDefault(rule.PercentOf ?? "CTC") * (rule.Value ?? 0) / 100,
        CalculationType.Formula => Formula.Evaluate(rule.Formula ?? "0", values),
        _ => 0,
    };
}

/// <summary>What PF works out to on a month's PF wages (spec §22).</summary>
public static class ProvidentFundMath
{
    public static ProvidentFundFigures Calculate(decimal pfWages, ProvidentFundConfig config, int? age)
    {
        ArgumentNullException.ThrowIfNull(config);
        decimal capped = Math.Min(pfWages, config.WageCeiling);
        decimal employeeBasis = config.CapEmployeeAtCeiling ? capped : pfWages;
        decimal employerBasis = config.CapEmployerAtCeiling ? capped : pfWages;
        decimal employee = Money.Rupees(employeeBasis * config.EmployeeRate / 100);
        decimal employerTotal = Money.Rupees(employerBasis * (config.EmployerPensionRate + config.EmployerProvidentRate) / 100);
        decimal pension = age >= config.PensionAgeLimit ? 0 : Math.Min(employerTotal, Money.Rupees(capped * config.EmployerPensionRate / 100));
        return new ProvidentFundFigures(
            employeeBasis, pension > 0 ? capped : 0, capped, employee, pension, employerTotal - pension,
            Money.Rupees(capped * config.EdliRate / 100), Money.Rupees(employerBasis * config.AdminChargeRate / 100));
    }
}

/// <summary>Professional tax, labour welfare and gratuity (spec §22).</summary>
public static class StatutoryMath
{
    public static decimal ProfessionalTax(decimal monthlyGross, ProfessionalTaxConfig config, int month)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (monthlyGross <= 0)
        {
            return 0;
        }

        ProfessionalTaxSlab? slab = config.Slabs.Where(candidate => candidate.From <= monthlyGross).MaxBy(candidate => candidate.From);
        return slab is null ? 0 : month == 2 && slab.FebruaryAmount is { } february ? february : slab.Amount;
    }

    /// <summary>
    /// Gratuity payable after <paramref name="joined"/> to <paramref name="asOf"/>: completed years (a final
    /// part year over six months counts as a year), once the minimum service is reached, up to the maximum.
    /// </summary>
    public static (decimal Years, decimal Amount, bool Eligible) Gratuity(decimal monthlyBasic, DateOnly joined, DateOnly asOf, GratuityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (asOf < joined)
        {
            return (0, 0, false);
        }

        int months = ((asOf.Year - joined.Year) * 12) + asOf.Month - joined.Month - (asOf.Day < joined.Day ? 1 : 0);
        decimal exactYears = months / 12m;
        int completed = months / 12;
        int counted = months % 12 > 6 ? completed + 1 : completed;
        bool eligible = exactYears >= config.MinimumYears;
        decimal amount = Math.Min(config.MaximumAmount, Money.Rupees(monthlyBasic * config.DaysPerYear / config.DivisorDays * counted));
        return (Math.Round(exactYears, 2), eligible ? amount : 0, eligible);
    }
}

/// <summary>Income-tax figures for the year to date, from finalized payroll this financial year.</summary>
public sealed record YearToDate(decimal TaxableEarnings, decimal Tds, decimal ProfessionalTax, decimal ProvidentFundEmployee, decimal Basic, decimal Hra, int MonthsPaid)
{
    public static YearToDate None { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>A declaration as TDS reads it.</summary>
public sealed record DeclarationFacts(TaxRegime Regime, IReadOnlyList<(string Section, decimal Amount)> Claims, decimal MonthlyRent, bool MetroCity, decimal OtherIncome, decimal PreviousEmployerIncome, decimal PreviousEmployerTds);

/// <summary>Everything the annual TDS projection needs for one month.</summary>
public sealed record TaxInput(
    TaxRegime Regime,
    int MonthsRemaining,
    int ProjectionMonths,
    decimal TaxableThisMonth,
    decimal RecurringTaxableMonthly,
    decimal BasicThisMonth,
    decimal BasicMonthly,
    decimal HraThisMonth,
    decimal HraMonthly,
    decimal PtThisMonth,
    decimal PtMonthly,
    decimal PfThisMonth,
    decimal PfMonthly,
    YearToDate YearToDate,
    DeclarationFacts? Declaration);

/// <summary>
/// Salary TDS (spec §22): the year's taxable salary projected from what has been paid and what will
/// be, less the regime's deductions; tax by slab, rebate (with marginal relief where the regime has
/// it), surcharge and cess; and what is left to deduct spread over the remaining months.
/// </summary>
public static class TaxMath
{
    public static TaxComputation Project(TaxInput input, IncomeTaxConfig config)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);
        IncomeTaxRegime regime = input.Regime == TaxRegime.Old ? config.OldRegime : config.NewRegime;
        DeclarationFacts? declaration = input.Declaration;
        YearToDate ytd = input.YearToDate;
        int projection = Math.Max(0, input.ProjectionMonths);

        decimal gross = ytd.TaxableEarnings + input.TaxableThisMonth + (input.RecurringTaxableMonthly * projection)
            + (declaration?.OtherIncome ?? 0) + (declaration?.PreviousEmployerIncome ?? 0);
        decimal standard = Math.Min(regime.StandardDeduction, gross);

        decimal hra = 0;
        decimal professionalTax = 0;
        decimal chapterVia = 0;
        if (regime.AllowsDeductions)
        {
            professionalTax = Math.Min(2500, ytd.ProfessionalTax + input.PtThisMonth + (input.PtMonthly * projection));
            if (declaration is { MonthlyRent: > 0 })
            {
                decimal hraYear = ytd.Hra + input.HraThisMonth + (input.HraMonthly * projection);
                decimal basicYear = ytd.Basic + input.BasicThisMonth + (input.BasicMonthly * projection);
                decimal rentYear = declaration.MonthlyRent * (ytd.MonthsPaid + 1 + projection);
                hra = Math.Max(0, Math.Min(hraYear, Math.Min(rentYear - (basicYear * 0.1m), basicYear * (declaration.MetroCity ? 0.5m : 0.4m))));
            }

            decimal pfYear = ytd.ProvidentFundEmployee + input.PfThisMonth + (input.PfMonthly * projection);
            var claims = (declaration?.Claims ?? [])
                .GroupBy(claim => claim.Section, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key.ToUpperInvariant(), group => group.Sum(claim => claim.Amount));
            claims["80C"] = claims.GetValueOrDefault("80C") + pfYear;
            foreach ((string section, decimal amount) in claims)
            {
                chapterVia += config.SectionLimits.TryGetValue(section, out decimal limit) ? Math.Min(amount, limit) : amount;
            }
        }

        decimal taxable = Math.Max(0, Math.Round((gross - standard - hra - professionalTax - chapterVia) / 10, 0, MidpointRounding.AwayFromZero) * 10);
        decimal tax = SlabTax(taxable, regime.Slabs);
        decimal rebate = 0;
        if (taxable <= regime.RebateIncomeLimit)
        {
            rebate = Math.Min(tax, regime.RebateMaximum);
        }
        else if (regime.RebateMarginalRelief && tax > taxable - regime.RebateIncomeLimit)
        {
            rebate = tax - (taxable - regime.RebateIncomeLimit);
        }

        decimal afterRebate = tax - rebate;
        decimal surchargeRate = Math.Min(regime.SurchargeCap, config.Surcharge.Where(slab => taxable > slab.From).Select(slab => slab.Rate).DefaultIfEmpty(0).Max());
        decimal surcharge = Money.Rupees(afterRebate * surchargeRate / 100);
        decimal cess = Money.Rupees((afterRebate + surcharge) * config.CessRate / 100);
        decimal annual = Money.Rupees(afterRebate + surcharge + cess);
        decimal already = ytd.Tds + (declaration?.PreviousEmployerTds ?? 0);
        decimal monthly = Math.Max(0, Money.Rupees((annual - already) / Math.Max(1, projection + 1)));

        return new TaxComputation(
            input.Regime.ToString(), input.MonthsRemaining, Math.Round(gross, 2), standard, Math.Round(hra, 2), professionalTax, Math.Round(chapterVia, 2),
            taxable, Money.Rupees(tax), Money.Rupees(rebate), surcharge, cess, annual, already, monthly);
    }

    /// <summary>Tax on an income by progressive slabs.</summary>
    public static decimal SlabTax(decimal income, IReadOnlyList<TaxSlab> slabs)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        List<TaxSlab> ordered = [.. slabs.OrderBy(slab => slab.From)];
        decimal tax = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            decimal from = ordered[i].From;
            decimal to = i + 1 < ordered.Count ? ordered[i + 1].From : decimal.MaxValue;
            if (income > from)
            {
                tax += (Math.Min(income, to) - from) * ordered[i].Rate / 100;
            }
        }

        return tax;
    }

    /// <summary>Months left in the financial year, the given month included: April is 12, March is 1.</summary>
    public static int MonthsRemaining(DateOnly period) => 12 - ((period.Month + 8) % 12);
}

/// <summary>A salary in force for part (or all) of the month.</summary>
public sealed record SalarySegment(Guid SalaryId, decimal AnnualCtc, IReadOnlyList<ComponentRule> Structure, bool PfEnabled, DateOnly From, DateOnly? To);

/// <summary>A one-off amount for the month, against its component.</summary>
public sealed record InputLine(ComponentRule Component, decimal Amount, string Description);

/// <summary>The rule versions in force for one employee's month.</summary>
public sealed record RulesInForce(
    StatutoryRuleVersion? ProvidentFund,
    StatutoryRuleVersion? StateInsurance,
    StatutoryRuleVersion? ProfessionalTax,
    StatutoryRuleVersion? LabourWelfare,
    StatutoryRuleVersion? IncomeTax,
    StatutoryRuleVersion? Gratuity);

/// <summary>Everything needed to pay one employee for one month.</summary>
public sealed record PayContext(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid? DepartmentId,
    string? Designation,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    ProrationBasis Basis,
    DateOnly? JoiningDate,
    DateOnly? ExitDate,
    IReadOnlyList<SalarySegment> Segments,
    decimal LopDays,
    IReadOnlyList<InputLine> Inputs,
    IReadOnlyList<BenefitCharge> Benefits,
    RulesInForce Rules,
    int? Age,
    string? StateCode,
    DeclarationFacts? Declaration,
    TaxRegime DefaultRegime,
    YearToDate YearToDate,
    bool HasBankAccount,
    bool HasPan,
    bool HasUan,
    bool InputsOnly = false);

/// <summary>One employee's calculated month.</summary>
public sealed record PayResult(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid? DepartmentId,
    string? Designation,
    Guid? SalaryId,
    ItemStatus Status,
    decimal PeriodDays,
    decimal PaidDays,
    decimal LopDays,
    decimal AnnualCtc,
    decimal Gross,
    decimal Reimbursements,
    decimal Deductions,
    decimal Net,
    decimal EmployerContributions,
    decimal TaxableEarnings,
    IReadOnlyList<PayLine> Lines,
    IReadOnlyList<AppliedRule> Rules,
    TaxComputation? Tax,
    ProvidentFundFigures? ProvidentFund,
    IReadOnlyList<string> Issues,
    string? StateCode);

/// <summary>
/// Pays one employee for one month (spec §21): fixed components prorated for days employed and loss
/// of pay (across a mid-month revision too), inputs, PF, ESI, professional tax, labour welfare,
/// benefit shares and TDS - each statutory amount from the rule version in force, which is recorded.
/// </summary>
public static class PayCalculator
{
    public static PayResult Calculate(PayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<string>();
        var warnings = new List<string>();
        var applied = new List<AppliedRule>();

        int actualDays = context.PeriodEnd.DayNumber - context.PeriodStart.DayNumber + 1;
        decimal basisDays = context.Basis == ProrationBasis.ThirtyDays ? 30 : actualDays;
        DateOnly employedFrom = Max(context.PeriodStart, context.JoiningDate ?? context.PeriodStart);
        DateOnly employedTo = Min(context.PeriodEnd, context.ExitDate ?? context.PeriodEnd);

        ProvidentFundConfig? pfConfig = Use(context.Rules.ProvidentFund, applied)?.Read<ProvidentFundConfig>();
        StateInsuranceConfig? esiConfig = Use(context.Rules.StateInsurance, applied)?.Read<StateInsuranceConfig>();
        GratuityConfig? gratuityConfig = context.Rules.Gratuity?.Read<GratuityConfig>();

        // Each salary's share of the month, in basis days.
        var pieces = new List<(SalarySegment Segment, Breakdown Breakdown, decimal Days)>();
        foreach (SalarySegment segment in context.Segments.OrderBy(segment => segment.From))
        {
            DateOnly first = Max(employedFrom, segment.From);
            DateOnly last = Min(employedTo, segment.To ?? employedTo);
            if (last < first)
            {
                continue;
            }

            decimal days = (last.DayNumber - first.DayNumber + 1) * basisDays / actualDays;
            Breakdown breakdown = SalaryBreakdown.Calculate(segment.AnnualCtc, segment.Structure, new CtcRules(pfConfig, esiConfig, gratuityConfig, segment.PfEnabled));
            errors.AddRange(breakdown.Errors);
            warnings.AddRange(breakdown.Warnings);
            pieces.Add((segment, breakdown, days));
        }

        if (pieces.Count == 0)
        {
            return context.InputsOnly ? InputsOnly(context, applied) : Empty(context, ["No salary is assigned for this month."]);
        }

        decimal employedDays = pieces.Sum(piece => piece.Days);
        decimal lop = Math.Min(context.LopDays, employedDays);
        decimal paidDays = Math.Round(employedDays - lop, 2);
        (SalarySegment latestSegment, Breakdown latest, _) = pieces[^1];
        bool gratuityInCtc = latestSegment.Structure.Any(rule => rule.Code == StatutoryKeys.Gratuity);
        if (gratuityInCtc)
        {
            Use(context.Rules.Gratuity, applied);
        }

        // Fixed earnings and employer contributions (not the statutory ones, worked out below on actual wages).
        var lines = new Dictionary<string, (ComponentRule Rule, decimal Amount, decimal Full)>(StringComparer.OrdinalIgnoreCase);
        foreach ((SalarySegment segment, Breakdown breakdown, decimal days) in pieces)
        {
            decimal share = employedDays == 0 ? 0 : days / employedDays;
            decimal paidShare = (days - (lop * share)) / basisDays;
            foreach (BreakdownLine line in breakdown.Lines.Where(line => !IsStatutory(line.Component.Code)))
            {
                decimal amount = line.Component.Prorated ? line.Monthly * paidShare : line.Monthly * share;
                decimal full = segment == latestSegment ? line.Monthly : 0;
                lines[line.Component.Code] = lines.TryGetValue(line.Component.Code, out var existing)
                    ? (line.Component, existing.Amount + amount, existing.Full + full)
                    : (line.Component, amount, full);
            }
        }

        var pay = lines.Values.Select(value => new PayLine(value.Rule.Code, value.Rule.Name, value.Rule.Kind, Money.Rupees(value.Amount), value.Rule.Taxable, value.Full)).ToList();
        var rules = lines.Values.ToDictionary(value => value.Rule.Code, value => value.Rule, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, InputLine> input in context.Inputs.GroupBy(input => input.Component.Code))
        {
            ComponentRule rule = input.First().Component;
            rules.TryAdd(rule.Code, rule);
            pay.Add(new PayLine(rule.Code, rule.Name, rule.Kind, Money.Rupees(input.Sum(line => line.Amount)), rule.Taxable, Note: string.Join("; ", input.Select(line => line.Description).Distinct())));
        }

        decimal Sum(Func<ComponentRule, bool> flag) => pay.Where(line => line.Kind == ComponentKind.Earning && rules.TryGetValue(line.Code, out ComponentRule? rule) && flag(rule)).Sum(line => line.Amount);
        decimal FullSum(Func<ComponentRule, bool> flag) => latest.Lines.Where(line => line.Component.Kind == ComponentKind.Earning && flag(line.Component) && !IsStatutory(line.Component.Code)).Sum(line => line.Monthly);

        // Provident fund on actual PF wages.
        ProvidentFundFigures? providentFund = null;
        decimal pfMonthly = 0;
        if (latestSegment.PfEnabled && pfConfig is not null)
        {
            providentFund = ProvidentFundMath.Calculate(Sum(rule => rule.Pf), pfConfig, context.Age);
            pfMonthly = ProvidentFundMath.Calculate(FullSum(rule => rule.Pf), pfConfig, context.Age).EmployeeShare;
            if (providentFund.EmployeeShare > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.ProvidentFundEmployee, "Provident fund", ComponentKind.Deduction, providentFund.EmployeeShare, false));
            }

            if (providentFund.PensionShare + providentFund.EmployerShare > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.ProvidentFundEmployer, "Employer PF", ComponentKind.EmployerContribution, providentFund.PensionShare + providentFund.EmployerShare, false));
            }

            if (!context.HasUan)
            {
                warnings.Add("No UAN on record; the ECR needs one.");
            }
        }

        // ESI while the full-month gross is within the threshold.
        if (esiConfig is not null && FullSum(rule => rule.Esi) <= esiConfig.WageThreshold)
        {
            decimal esiGross = Sum(rule => rule.Esi);
            if (esiGross > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.StateInsuranceEmployee, "ESI", ComponentKind.Deduction, Money.UpToRupee(esiGross * esiConfig.EmployeeRate / 100), false));
                pay.Add(new PayLine(StatutoryKeys.StateInsuranceEmployer, "Employer ESI", ComponentKind.EmployerContribution, Money.UpToRupee(esiGross * esiConfig.EmployerRate / 100), false));
            }
        }

        // Professional tax by state.
        decimal ptThisMonth = 0;
        decimal ptMonthly = 0;
        if (Use(context.Rules.ProfessionalTax, applied)?.Read<ProfessionalTaxConfig>() is { } ptConfig)
        {
            ptThisMonth = StatutoryMath.ProfessionalTax(Sum(rule => rule.Pt), ptConfig, context.PeriodStart.Month);
            ptMonthly = StatutoryMath.ProfessionalTax(FullSum(rule => rule.Pt), ptConfig, 1);
            if (ptThisMonth > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.ProfessionalTax, "Professional tax", ComponentKind.Deduction, ptThisMonth, false));
            }
        }

        // Labour welfare in the months it is due.
        if (context.Rules.LabourWelfare?.Read<LabourWelfareConfig>() is { } lwf && lwf.Months.Contains(context.PeriodStart.Month) && paidDays > 0)
        {
            Use(context.Rules.LabourWelfare, applied);
            if (lwf.EmployeeAmount > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.LabourWelfareEmployee, "Labour welfare fund", ComponentKind.Deduction, lwf.EmployeeAmount, false));
            }

            if (lwf.EmployerAmount > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.LabourWelfareEmployer, "Employer labour welfare", ComponentKind.EmployerContribution, lwf.EmployerAmount, false));
            }
        }

        // Gratuity accrual, when the structure carries it inside the CTC.
        if (gratuityInCtc && gratuityConfig is not null)
        {
            decimal basic = pay.Where(line => line.Code == "BASIC").Sum(line => line.Amount);
            decimal accrual = Money.Rupees(basic * gratuityConfig.MonthlyAccrualRate / 100);
            if (accrual > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.Gratuity, "Gratuity (accrued)", ComponentKind.EmployerContribution, accrual, false));
            }
        }

        // Benefit shares.
        foreach (BenefitCharge charge in context.Benefits)
        {
            if (charge.EmployeeAmount > 0)
            {
                pay.Add(new PayLine($"BEN_{charge.PlanCode}", $"{charge.PlanName} (your share)", ComponentKind.Deduction, Money.Rupees(charge.EmployeeAmount), false));
            }

            if (charge.EmployerAmount > 0)
            {
                pay.Add(new PayLine($"BENER_{charge.PlanCode}", $"{charge.PlanName} (employer)", ComponentKind.EmployerContribution, Money.Rupees(charge.EmployerAmount), false));
            }
        }

        // TDS on the projected year.
        decimal taxable = pay.Where(line => line.Kind == ComponentKind.Earning && line.Taxable).Sum(line => line.Amount);
        TaxComputation? tax = null;
        if (Use(context.Rules.IncomeTax, applied)?.Read<IncomeTaxConfig>() is { } taxConfig)
        {
            int remaining = TaxMath.MonthsRemaining(context.PeriodStart);
            int projection = remaining - 1;
            if (context.ExitDate is { } exit)
            {
                projection = exit <= context.PeriodEnd ? 0 : Math.Min(projection, ((exit.Year - context.PeriodStart.Year) * 12) + exit.Month - context.PeriodStart.Month);
            }

            tax = TaxMath.Project(
                new TaxInput(
                    context.Declaration?.Regime ?? context.DefaultRegime,
                    remaining,
                    projection,
                    taxable,
                    FullSum(rule => rule.Taxable),
                    pay.Where(line => line.Code == "BASIC").Sum(line => line.Amount),
                    latest.Amount("BASIC"),
                    pay.Where(line => line.Code == "HRA").Sum(line => line.Amount),
                    latest.Amount("HRA"),
                    ptThisMonth,
                    ptMonthly,
                    providentFund?.EmployeeShare ?? 0,
                    pfMonthly,
                    context.YearToDate,
                    context.Declaration),
                taxConfig);
            if (tax.MonthlyTds > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.IncomeTax, "Income tax (TDS)", ComponentKind.Deduction, tax.MonthlyTds, false));
                if (!context.HasPan)
                {
                    warnings.Add("No PAN on record: TDS must be deducted at not less than 20% (section 206AA).");
                }
            }
        }

        decimal gross = pay.Where(line => line.Kind == ComponentKind.Earning).Sum(line => line.Amount);
        decimal reimbursements = pay.Where(line => line.Kind == ComponentKind.Reimbursement).Sum(line => line.Amount);
        decimal deductions = pay.Where(line => line.Kind == ComponentKind.Deduction).Sum(line => line.Amount);
        decimal net = Money.Rupees(gross + reimbursements - deductions);
        decimal employer = pay.Where(line => line.Kind == ComponentKind.EmployerContribution).Sum(line => line.Amount)
            + (providentFund is null ? 0 : providentFund.EdliCharge + providentFund.AdminCharge);

        if (net < 0)
        {
            errors.Add($"Deductions exceed pay by {-net:0.##}; recover the balance next month instead.");
        }

        if (paidDays <= 0)
        {
            warnings.Add("No paid days this month.");
        }

        if (!context.HasBankAccount && net > 0)
        {
            warnings.Add("No bank account on record; this person will be missing from the bank file.");
        }

        return new PayResult(
            context.EmployeeId, context.EmployeeCode, context.EmployeeName, context.DepartmentId, context.Designation, latestSegment.SalaryId,
            errors.Count > 0 ? ItemStatus.Error : warnings.Count > 0 ? ItemStatus.Warning : ItemStatus.Ok,
            basisDays, paidDays, lop, latestSegment.AnnualCtc, gross, reimbursements, deductions, net, employer, taxable,
            [.. pay.OrderBy(line => line.Kind).ThenBy(line => rules.TryGetValue(line.Code, out ComponentRule? rule) ? rule.Order : 1000).ThenBy(line => line.Code, StringComparer.Ordinal)],
            applied, tax, providentFund, [.. errors.Concat(warnings).Distinct()], context.StateCode);
    }

    /// <summary>
    /// A full and final run with no salary left to pay (the exit month was paid already): just the
    /// settlement's lines, with TDS on them worked out against the year so far and nothing projected.
    /// </summary>
    private static PayResult InputsOnly(PayContext context, List<AppliedRule> applied)
    {
        List<PayLine> pay = [.. context.Inputs.GroupBy(input => input.Component.Code).Select(input =>
        {
            ComponentRule rule = input.First().Component;
            return new PayLine(rule.Code, rule.Name, rule.Kind, Money.Rupees(input.Sum(line => line.Amount)), rule.Taxable, Note: string.Join("; ", input.Select(line => line.Description).Distinct()));
        })];
        Dictionary<string, int> order = context.Inputs.GroupBy(input => input.Component.Code).ToDictionary(group => group.Key, group => group.First().Component.Order, StringComparer.OrdinalIgnoreCase);
        decimal taxable = pay.Where(line => line.Kind == ComponentKind.Earning && line.Taxable).Sum(line => line.Amount);
        TaxComputation? tax = null;
        if (taxable > 0 && Use(context.Rules.IncomeTax, applied)?.Read<IncomeTaxConfig>() is { } taxConfig)
        {
            tax = TaxMath.Project(
                new TaxInput(context.Declaration?.Regime ?? context.DefaultRegime, TaxMath.MonthsRemaining(context.PeriodStart), 0, taxable, 0, 0, 0, 0, 0, 0, 0, 0, 0, context.YearToDate, context.Declaration),
                taxConfig);
            if (tax.MonthlyTds > 0)
            {
                pay.Add(new PayLine(StatutoryKeys.IncomeTax, "Income tax (TDS)", ComponentKind.Deduction, tax.MonthlyTds, false));
            }
        }

        decimal gross = pay.Where(line => line.Kind == ComponentKind.Earning).Sum(line => line.Amount);
        decimal reimbursements = pay.Where(line => line.Kind == ComponentKind.Reimbursement).Sum(line => line.Amount);
        decimal deductions = pay.Where(line => line.Kind == ComponentKind.Deduction).Sum(line => line.Amount);
        decimal net = Money.Rupees(gross + reimbursements - deductions);
        List<string> issues = [];
        if (net < 0)
        {
            issues.Add($"Recoveries exceed the settlement by {-net:0.##}; collect the balance from the employee.");
        }

        if (!context.HasBankAccount && net > 0)
        {
            issues.Add("No bank account on record; this person will be missing from the bank file.");
        }

        return new PayResult(
            context.EmployeeId, context.EmployeeCode, context.EmployeeName, context.DepartmentId, context.Designation, null,
            net < 0 ? ItemStatus.Error : issues.Count > 0 ? ItemStatus.Warning : ItemStatus.Ok,
            0, 0, 0, 0, gross, reimbursements, deductions, net, 0, taxable,
            [.. pay.OrderBy(line => line.Kind).ThenBy(line => order.GetValueOrDefault(line.Code, 1000)).ThenBy(line => line.Code, StringComparer.Ordinal)],
            applied, tax, null, issues, context.StateCode);
    }

    private static bool IsStatutory(string code) => code is StatutoryKeys.ProvidentFundEmployer or StatutoryKeys.StateInsuranceEmployer or StatutoryKeys.Gratuity;

    private static StatutoryRuleVersion? Use(StatutoryRuleVersion? rule, List<AppliedRule> applied)
    {
        if (rule is not null && applied.TrueForAll(existing => existing.RuleId != rule.Id))
        {
            applied.Add(new AppliedRule(rule.Code, rule.Id, rule.Version, rule.Type.ToString()));
        }

        return rule;
    }

    private static PayResult Empty(PayContext context, IReadOnlyList<string> errors)
        => new(
            context.EmployeeId, context.EmployeeCode, context.EmployeeName, context.DepartmentId, context.Designation, null, ItemStatus.Error,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], null, null, errors, context.StateCode);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}

/// <summary>The Indian financial year, April to March, named by the year it starts in.</summary>
public static class FinancialYear
{
    public static int Of(DateOnly day) => day.Month >= 4 ? day.Year : day.Year - 1;

    public static DateOnly Start(int year) => new(year, 4, 1);

    public static DateOnly End(int year) => new(year + 1, 3, 31);

    public static string Label(int year) => $"{year}-{(year + 1) % 100:00}";
}
