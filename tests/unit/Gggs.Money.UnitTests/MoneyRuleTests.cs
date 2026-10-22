using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Compensation.Domain;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Compliance.Domain;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Shouldly;

namespace Gggs.Money.UnitTests;

public sealed class FormulaTests
{
    private static readonly Dictionary<string, decimal> Values = new() { ["BASIC"] = 120000m, ["GROSS"] = 18000m, ["CTC"] = 50000m };

    [Theory]
    [InlineData("MIN(BASIC * 0.5, 50000)", 50000)]
    [InlineData("BASIC * 0.4 + 100", 48100)]
    [InlineData("IF(GROSS <= 21000, GROSS * 0.0075, 0)", 135)]
    [InlineData("ROUND(CTC / 3, 2)", 16666.67)]
    [InlineData("-(BASIC - CTC) / 2", -35000)]
    [InlineData("BASIC / 0", 0)]
    public void Evaluates(string formula, double expected) => Formula.Evaluate(formula, Values).ShouldBe((decimal)expected);

    [Theory]
    [InlineData("BASIC * ")]
    [InlineData("BASIC; DROP TABLE")]
    [InlineData("SQRT(BASIC)")]
    [InlineData("UNKNOWN * 2")]
    public void Rejects(string formula) => Should.Throw<DomainRuleException>(() => Formula.Evaluate(formula, Values));

    [Fact]
    public void Validate_NamesTheUnknownCode()
        => Should.Throw<DomainRuleException>(() => Formula.Validate("BASIC + DA", ["BASIC", "CTC"])).Message.ShouldContain("DA");

    [Fact]
    public void References_AreTheVariablesNotTheFunctions()
        => Formula.References("MIN(basic, CTC * 0.1)").ShouldBe(["BASIC", "CTC"], ignoreOrder: true);
}

public sealed class SalaryBreakdownTests
{
    internal static ComponentRule Rule(string code, ComponentKind kind, CalculationType type, decimal? value = null, string? percentOf = null, bool pf = false, bool esi = true, int order = 0, bool taxable = true)
        => new(Guid.NewGuid(), code, code, kind, type, value, percentOf, null, null, taxable, pf, esi, kind == ComponentKind.Earning, true, true, order);

    internal static IReadOnlyList<ComponentRule> Standard() =>
    [
        Rule("BASIC", ComponentKind.Earning, CalculationType.Percentage, 40m, "CTC", pf: true, order: 1),
        Rule("HRA", ComponentKind.Earning, CalculationType.Percentage, 50m, "BASIC", order: 2),
        Rule("SPECIAL", ComponentKind.Earning, CalculationType.Balancing, order: 3),
        Rule(StatutoryKeys.ProvidentFundEmployer, ComponentKind.EmployerContribution, CalculationType.Statutory, esi: false, order: 4, taxable: false),
        Rule(StatutoryKeys.StateInsuranceEmployer, ComponentKind.EmployerContribution, CalculationType.Statutory, esi: false, order: 5, taxable: false),
        Rule(StatutoryKeys.Gratuity, ComponentKind.EmployerContribution, CalculationType.Statutory, esi: false, order: 6, taxable: false),
    ];

    internal static readonly ProvidentFundConfig Pf = new(12m, 8.33m, 3.67m, 15000m, true, true, 0.5m, 0.5m, 58);
    internal static readonly StateInsuranceConfig Esi = new(0.75m, 3.25m, 21000m);
    internal static readonly GratuityConfig Gratuity = new(15m, 26m, 5m, 2000000m, 4.81m);

    [Fact]
    public void AboveTheEsiThreshold_TheSpecialAllowanceTakesTheRest()
    {
        Breakdown breakdown = SalaryBreakdown.Calculate(600_000m, Standard(), new CtcRules(Pf, Esi, Gratuity, true));
        breakdown.Amount("BASIC").ShouldBe(20000m);
        breakdown.Amount("PF_ER").ShouldBe(1800m);
        breakdown.Amount("GRATUITY").ShouldBe(962m);
        breakdown.EsiApplies.ShouldBeFalse();
        breakdown.Amount("SPECIAL").ShouldBe(17238m);
        breakdown.Lines.Sum(line => line.Monthly).ShouldBe(50000m, "the breakdown adds up to the CTC");
    }

    [Fact]
    public void WithinTheEsiThreshold_EmployerEsiIsCarvedOutOfTheCtc()
    {
        Breakdown breakdown = SalaryBreakdown.Calculate(240_000m, Standard(), new CtcRules(Pf, Esi, Gratuity, true));
        breakdown.EsiApplies.ShouldBeTrue();
        breakdown.Amount("PF_ER").ShouldBe(960m);
        breakdown.Amount("GRATUITY").ShouldBe(385m);
        breakdown.Amount("ESI_ER").ShouldBe(588m);
        breakdown.Amount("SPECIAL").ShouldBe(6067m);
        breakdown.Lines.Sum(line => line.Monthly).ShouldBe(20000m);
    }

    [Fact]
    public void ComponentsOverTheCtc_AreAnError()
    {
        IReadOnlyList<ComponentRule> greedy = [.. Standard().Select(rule => rule.Code == "BASIC" ? rule with { Value = 80m } : rule)];
        SalaryBreakdown.Calculate(600_000m, greedy, new CtcRules(Pf, Esi, Gratuity, true)).Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void ComponentsThatDependOnEachOther_AreAnError()
    {
        IReadOnlyList<ComponentRule> loop =
        [
            Rule("A", ComponentKind.Earning, CalculationType.Percentage, 10m, "B"),
            Rule("B", ComponentKind.Earning, CalculationType.Percentage, 10m, "A"),
        ];
        SalaryBreakdown.Calculate(600_000m, loop, new CtcRules(null, null, null, false)).Errors.ShouldHaveSingleItem();
    }
}

public sealed class StatutoryMathTests
{
    [Fact]
    public void ProvidentFund_IsCappedAtTheWageCeiling_AndSplitIntoPension()
    {
        ProvidentFundFigures pf = ProvidentFundMath.Calculate(20000m, SalaryBreakdownTests.Pf, age: 30);
        pf.EmployeeShare.ShouldBe(1800m);
        pf.PensionShare.ShouldBe(1250m);
        pf.EmployerShare.ShouldBe(550m);
        pf.EdliCharge.ShouldBe(75m);
        pf.AdminCharge.ShouldBe(75m);
    }

    [Fact]
    public void ProvidentFund_AbovePensionAge_HasNoPensionShare()
    {
        ProvidentFundFigures pf = ProvidentFundMath.Calculate(20000m, SalaryBreakdownTests.Pf, age: 59);
        pf.PensionShare.ShouldBe(0m);
        pf.EmployerShare.ShouldBe(1800m);
    }

    [Theory]
    [InlineData(30000, 10, 200)]
    [InlineData(30000, 2, 300)]
    [InlineData(20000, 10, 0)]
    [InlineData(0, 10, 0)]
    public void ProfessionalTax_FollowsTheStateSlabs(double gross, int month, double expected)
        => StatutoryMath.ProfessionalTax((decimal)gross, new ProfessionalTaxConfig([new(0m, 0m), new(25000m, 200m, 300m)]), month).ShouldBe((decimal)expected);

    [Fact]
    public void Gratuity_CountsAPartYearOverSixMonths()
    {
        (decimal years, decimal amount, bool eligible) = StatutoryMath.Gratuity(30000m, new DateOnly(2019, 1, 10), new DateOnly(2026, 10, 16), SalaryBreakdownTests.Gratuity);
        eligible.ShouldBeTrue();
        years.ShouldBe(7.75m);
        amount.ShouldBe(138462m, "8 years × 30,000 × 15 / 26");
    }

    [Fact]
    public void Gratuity_IsNothingBeforeFiveYears()
        => StatutoryMath.Gratuity(30000m, new DateOnly(2023, 1, 1), new DateOnly(2026, 10, 16), SalaryBreakdownTests.Gratuity).Amount.ShouldBe(0m);
}

public sealed class TaxMathTests
{
    private static readonly IncomeTaxConfig Tax = new(
        "2025-26",
        new IncomeTaxRegime(75000m, [new(0m, 0m), new(400000m, 5m), new(800000m, 10m), new(1200000m, 15m), new(1600000m, 20m), new(2000000m, 25m), new(2400000m, 30m)], 1200000m, 60000m, true, false, 25m),
        new IncomeTaxRegime(50000m, [new(0m, 0m), new(250000m, 5m), new(500000m, 20m), new(1000000m, 30m)], 500000m, 12500m, false, true, 37m),
        4m,
        [new(5000000m, 10m), new(10000000m, 15m)],
        new Dictionary<string, decimal> { ["80C"] = 150000m, ["80D"] = 25000m });

    private static TaxInput Year(TaxRegime regime, decimal monthlyTaxable, DeclarationFacts? declaration = null, decimal basic = 0, decimal hra = 0, decimal pf = 0)
        => new(regime, 12, 11, monthlyTaxable, monthlyTaxable, basic, basic, hra, hra, 0, 0, pf, pf, YearToDate.None, declaration);

    [Fact]
    public void SlabTax_IsProgressive() => TaxMath.SlabTax(1_000_000m, Tax.NewRegime.Slabs).ShouldBe(40000m);

    [Fact]
    public void NewRegime_UpToTwelveLakhTaxable_PaysNothing()
        => TaxMath.Project(Year(TaxRegime.New, 1_275_000m / 12), Tax).AnnualTax.ShouldBe(0m);

    [Fact]
    public void NewRegime_JustAboveTheRebate_GetsMarginalRelief()
    {
        TaxComputation tax = TaxMath.Project(Year(TaxRegime.New, 1_285_000m / 12), Tax);
        tax.TaxableIncome.ShouldBe(1_210_000m);
        tax.TaxBeforeRebate.ShouldBe(61500m);
        tax.AnnualTax.ShouldBe(10400m, "tax is capped at the 10,000 above the limit, plus cess");
    }

    [Fact]
    public void MonthlyTds_SpreadsTheYearsTax()
    {
        TaxComputation tax = TaxMath.Project(Year(TaxRegime.New, 150000m), Tax);
        tax.AnnualTax.ShouldBe(150800m);
        tax.MonthlyTds.ShouldBe(12567m);
    }

    [Fact]
    public void OldRegime_CountsHraChapterViaAndPf()
    {
        var declaration = new DeclarationFacts(TaxRegime.Old, [("80C", 100000m), ("80D", 40000m)], 25000m, true, 0, 0, 0);
        TaxComputation tax = TaxMath.Project(Year(TaxRegime.Old, 100000m, declaration, basic: 40000m, hra: 20000m, pf: 1800m), Tax);
        tax.HraExemption.ShouldBe(240000m, "the least of HRA received, rent less 10% of basic, and half of basic");
        tax.ChapterVIA.ShouldBe(100000m + 21600m + 25000m, "80C counts the year's PF; 80D is capped at 25,000");
        tax.StandardDeduction.ShouldBe(50000m);
    }

    [Theory]
    [InlineData(4, 12)]
    [InlineData(10, 6)]
    [InlineData(3, 1)]
    public void MonthsRemaining_RunToMarch(int month, int expected) => TaxMath.MonthsRemaining(new DateOnly(2026, month, 1)).ShouldBe(expected);
}

public sealed class PayCalculatorTests
{
    private static PayContext Context(IReadOnlyList<SalarySegment> segments, DateOnly? joined = null, decimal lop = 0)
        => new(
            Guid.NewGuid(), "E1", "Test", null, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ProrationBasis.CalendarDays, joined, null,
            segments, lop, [], [], new RulesInForce(null, null, null, null, null, null), 30, "IN-KA", null, TaxRegime.New, YearToDate.None, true, true, true);

    private static decimal Basic(PayResult result) => result.Lines.Single(line => line.Code == "BASIC").Amount;

    private static ComponentRule Terminal(string code, PayrollInputKind kind, bool taxable, ComponentKind componentKind = ComponentKind.Earning)
        => new(Guid.NewGuid(), code, code, componentKind, CalculationType.Input, null, null, null, kind, taxable, false, false, false, false, true, 45);

    [Fact]
    public void A_settlement_with_no_salary_left_pays_its_lines_and_recoveries_only()
    {
        PayContext context = Context([]) with
        {
            Inputs =
            [
                new InputLine(Terminal("GRATUITY_PAY", PayrollInputKind.Gratuity, taxable: false), 120_000m, "Gratuity"),
                new InputLine(Terminal("NOTICE_PAY", PayrollInputKind.NoticePay, taxable: true), 30_000m, "Pay in lieu of notice"),
                new InputLine(Terminal("RECOVERY", PayrollInputKind.Deduction, taxable: false, ComponentKind.Deduction), 5_000m, "Laptop not returned"),
            ],
            InputsOnly = true,
        };

        PayResult result = PayCalculator.Calculate(context);
        result.Status.ShouldBe(ItemStatus.Ok);
        result.Gross.ShouldBe(150_000m);
        result.TaxableEarnings.ShouldBe(30_000m, "gratuity within its limit is tax-free");
        result.Deductions.ShouldBe(5_000m, "no income-tax rule in force here, so recoveries only");
        result.Net.ShouldBe(145_000m);
        result.Lines.ShouldNotContain(line => line.Code == "BASIC");
    }

    [Fact]
    public void Without_the_settlement_flag_a_month_with_no_salary_is_an_error()
        => PayCalculator.Calculate(Context([])).Status.ShouldBe(ItemStatus.Error);

    [Fact]
    public void JoiningMidMonth_PaysTheDaysEmployed()
    {
        PayResult result = PayCalculator.Calculate(Context([new SalarySegment(Guid.NewGuid(), 600_000m, SalaryBreakdownTests.Standard(), true, new DateOnly(2026, 10, 16), null)], joined: new DateOnly(2026, 10, 16)));
        result.PaidDays.ShouldBe(16m);
        Basic(result).ShouldBe(10323m, "20,000 × 16 / 31");
    }

    [Fact]
    public void LossOfPay_ReducesProratedComponents()
    {
        PayResult result = PayCalculator.Calculate(Context([new SalarySegment(Guid.NewGuid(), 600_000m, SalaryBreakdownTests.Standard(), true, new DateOnly(2024, 1, 1), null)], lop: 2));
        result.PaidDays.ShouldBe(29m);
        result.LopDays.ShouldBe(2m);
        Basic(result).ShouldBe(18710m, "20,000 × 29 / 31");
    }

    [Fact]
    public void ARevisionMidMonth_PaysEachSalaryForItsDays()
    {
        PayResult result = PayCalculator.Calculate(Context(
        [
            new SalarySegment(Guid.NewGuid(), 600_000m, SalaryBreakdownTests.Standard(), true, new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15)),
            new SalarySegment(Guid.NewGuid(), 1_200_000m, SalaryBreakdownTests.Standard(), true, new DateOnly(2026, 10, 16), null),
        ]));
        Basic(result).ShouldBe(30323m, "20,000 × 15/31 + 40,000 × 16/31");
        result.AnnualCtc.ShouldBe(1_200_000m);
    }

    [Fact]
    public void NoSalary_IsAnError()
    {
        PayResult result = PayCalculator.Calculate(Context([]));
        result.Status.ShouldBe(ItemStatus.Error);
        result.Net.ShouldBe(0m);
    }

    [Fact]
    public void AmountInWords_UsesLakhAndCrore()
        => Gggs.Modules.Payroll.Application.AmountInWords.Of(12_45_678.50m).ShouldBe("Rupees Twelve Lakh Forty-Five Thousand Six Hundred Seventy-Eight and Fifty Paise only");
}

public sealed class ComplianceRuleTests
{
    [Fact]
    public void Configurations_AreCheckedAgainstTheirScheme()
    {
        Should.Throw<DomainRuleException>(() => StatutoryConfigValidator.Normalise(StatutoryRuleType.StateInsurance, """{"employeeRate":140,"employerRate":3.25,"wageThreshold":21000}"""));
        Should.Throw<DomainRuleException>(() => StatutoryConfigValidator.Normalise(StatutoryRuleType.ProfessionalTax, """{"slabs":[{"from":5000,"amount":200}]}"""));
        Should.Throw<DomainRuleException>(() => StatutoryConfigValidator.Normalise(StatutoryRuleType.ProvidentFund, "not json"));

        string normalised = StatutoryConfigValidator.Normalise(StatutoryRuleType.ProfessionalTax, """{"slabs":[{"from":25000,"amount":200},{"from":0,"amount":0}]}""");
        JsonDocument.Parse(normalised).RootElement.GetProperty("slabs")[0].GetProperty("from").GetDecimal().ShouldBe(0m, "slabs are stored in order");
    }

    [Fact]
    public void EverySeededRule_IsValid()
    {
        foreach (RuleSeed seed in IndiaStatutoryDefaults.Rules)
        {
            Should.NotThrow(() => StatutoryConfigValidator.Normalise(seed.Type, JsonSerializer.Serialize(seed.Configuration, seed.Configuration.GetType(), StatutoryJson.Options)), seed.Code);
        }
    }

    [Fact]
    public void APublishedRule_CannotBeEdited_AndANewVersionClosesTheOld()
    {
        StatutoryRule rule = StatutoryRule.Draft("IN-ESI", "ESI", StatutoryRuleType.StateInsurance, "IN", null, 1, new DateOnly(2019, 7, 1), "{}", null, null);
        rule.Publish(DateTime.UtcNow);
        Should.Throw<DomainRuleException>(() => rule.Edit("ESI", new DateOnly(2019, 7, 1), "{}", null, null));
        Should.Throw<DomainRuleException>(() => rule.SupersedeFrom(new DateOnly(2019, 7, 1)));
        rule.SupersedeFrom(new DateOnly(2027, 4, 1));
        rule.EffectiveTo.ShouldBe(new DateOnly(2027, 3, 31));
        rule.InForceOn(new DateOnly(2027, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void StateRules_NeedAState()
        => Should.Throw<DomainRuleException>(() => StatutoryRule.Draft("PT", "PT", StatutoryRuleType.ProfessionalTax, "IN", null, 1, new DateOnly(2025, 4, 1), "{}", null, null));

    [Fact]
    public void FilingPeriods_FollowTheFinancialYear()
    {
        FilingPeriod quarter = FilingCalendar.PeriodOf(FilingFrequency.Quarterly, new DateOnly(2026, 10, 16));
        quarter.Start.ShouldBe(new DateOnly(2026, 10, 1));
        quarter.End.ShouldBe(new DateOnly(2026, 12, 31));
        quarter.Label.ShouldBe("Q3 FY2026-27");
        FilingCalendar.DueDate(quarter, 1, 31).ShouldBe(new DateOnly(2027, 1, 31));

        FilingPeriod year = FilingCalendar.PeriodOf(FilingFrequency.Annual, new DateOnly(2027, 2, 1));
        year.Start.ShouldBe(new DateOnly(2026, 4, 1));
        FilingCalendar.DueDate(year, 3, 15).ShouldBe(new DateOnly(2027, 6, 15));

        FilingPeriod january = FilingCalendar.PeriodOf(FilingFrequency.Monthly, new DateOnly(2027, 1, 20));
        FilingCalendar.DueDate(january, 1, 30).ShouldBe(new DateOnly(2027, 2, 28), "a due day past the month's end falls on its last day");
    }
}

public sealed class CompensationRuleTests
{
    private static EquityGrant Grant(int frequency) => EquityGrant.Create(Guid.NewGuid(), new GrantDetails("ESOP", new DateOnly(2024, 1, 1), 4800m, 10m, 60m, null, null, 48, 12, frequency, null));

    [Fact]
    public void Vesting_StartsAtTheCliff_AndEndsWithEveryUnit()
    {
        EquityGrant grant = Grant(1);
        IReadOnlyList<VestingEvent> schedule = grant.Schedule();
        schedule.Count.ShouldBe(37);
        schedule[0].ShouldBe(new VestingEvent(new DateOnly(2025, 1, 1), 1200m, 1200m));
        schedule[^1].Cumulative.ShouldBe(4800m);
        grant.VestedOn(new DateOnly(2024, 12, 31)).ShouldBe(0m);
        grant.VestedOn(new DateOnly(2026, 1, 15)).ShouldBe(2400m);
    }

    [Fact]
    public void YearlyVesting_HasFourEvents() => Grant(12).Schedule().Select(vest => vest.Units).ShouldBe([1200m, 1200m, 1200m, 1200m]);

    [Fact]
    public void OnlyVestedUnitsCanBeExercised()
    {
        EquityGrant grant = Grant(1);
        Should.Throw<DomainRuleException>(() => grant.Exercise(2500m, new DateOnly(2026, 1, 15)));
        grant.Exercise(2400m, new DateOnly(2026, 1, 15));
        grant.ExercisedUnits.ShouldBe(2400m);
    }

    [Fact]
    public void Bands_MeasureCompaRatioAndPosition()
    {
        var band = SalaryBand.Create(Guid.NewGuid(), "INR", 900_000m, 1_200_000m, 1_500_000m, new DateOnly(2024, 1, 1), null);
        band.CompaRatio(1_000_000m).ShouldBe(0.83m);
        band.Penetration(1_200_000m).ShouldBe(50m);
        band.Position(800_000m).ShouldBe("Below");
        Should.Throw<DomainRuleException>(() => SalaryBand.Create(Guid.NewGuid(), "INR", 1_300_000m, 1_200_000m, 1_500_000m, new DateOnly(2024, 1, 1), null));
    }

    [Fact]
    public void OnlyACorrectionCanLowerPay()
    {
        Should.Throw<DomainRuleException>(() => CompensationRevision.Propose(Guid.NewGuid(), 1_000_000m, "INR", null,
            new RevisionDetails(null, RevisionType.Merit, 900_000m, new DateOnly(2026, 4, 1), null, null, null, "Cut", null)));
        CompensationRevision.Propose(Guid.NewGuid(), 1_000_000m, "INR", null,
            new RevisionDetails(null, RevisionType.Correction, 900_000m, new DateOnly(2026, 4, 1), null, null, null, "Keyed wrongly", null)).IncreasePercent.ShouldBe(-10m);
    }

    [Fact]
    public void TheNewRegime_RefusesDeductions()
    {
        TaxDeclaration declaration = TaxDeclaration.Start(Guid.NewGuid(), 2026, TaxRegime.New);
        Should.Throw<DomainRuleException>(() => declaration.Declare(new DeclarationValues(TaxRegime.New, [new DeclarationItem("80C", "PPF", 150000m, null)], null, false, null, null, null, null)));
    }
}

public sealed class FinanceCalculatorTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Role = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static ProjectFinancialFacts Project(string billing, decimal progress = 0, IReadOnlyList<MilestoneFact>? milestones = null) => new(
        Guid.NewGuid(), "P1", "Project", "Active", billing, "INR", 500000m, 400000m, 300000m, null, null, null, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16),
        progress, milestones ?? [],
        [new ProjectAllocation(Guid.NewGuid(), Guid.NewGuid(), "P1", "Project", "Active", Person, Role, "Developer", 100, new DateOnly(2026, 9, 1), null, true, 2500m, 1200m, true)]);

    private static FinanceInput Input(ProjectFinancialFacts project, IReadOnlyList<ProjectRate>? rates = null, IReadOnlyDictionary<Guid, decimal>? milestones = null) => new(
        project, null, null, milestones ?? new Dictionary<Guid, decimal>(),
        [new LoggedHours(Person, project.Id, null, new DateOnly(2026, 10, 5), HourCategory.Billable, 40m, true), new LoggedHours(Person, project.Id, null, new DateOnly(2026, 10, 6), HourCategory.NonBillable, 8m, true)],
        rates ?? [], [ProjectCost.Record(project.Id, new CostDetails(CostCategory.Vendor, new DateOnly(2026, 10, 1), 5000m, null, "Licences", null, false))], [], [], Today);

    [Fact]
    public void TimeAndMaterial_BillsBillableHoursAndCostsAllHours()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(Project("TimeAndMaterial")));
        summary.LabourCost.ShouldBe(48m * 1200m);
        summary.Revenue.ShouldBe(40m * 2500m);
        summary.ActualCost.ShouldBe((48m * 1200m) + 5000m);
        summary.Budget.ShouldBe(300000m, "the project's cost budget, with no budget lines");
    }

    [Fact]
    public void APersonalRateCard_BeatsTheTeamRate()
    {
        ProjectFinancialFacts project = Project("TimeAndMaterial");
        FinanceSummary summary = FinanceCalculator.Calculate(Input(project, [ProjectRate.Create(project.Id, null, Person, 2000m, 1000m, new DateOnly(2026, 1, 1))]));
        summary.LabourCost.ShouldBe(48000m);
        summary.Revenue.ShouldBe(80000m);
    }

    [Fact]
    public void FixedPrice_RecognisesTheContractValueByProgress()
        => FinanceCalculator.Calculate(Input(Project("FixedPrice", progress: 25m))).Revenue.ShouldBe(100000m, "25% of the 400,000 revenue target");

    [Fact]
    public void MilestoneBilling_CountsAchievedMilestones()
    {
        var done = new MilestoneFact(Guid.NewGuid(), "Design", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 28), "Achieved", true);
        var open = new MilestoneFact(Guid.NewGuid(), "Build", new DateOnly(2026, 12, 31), null, "Planned", true);
        FinanceSummary summary = FinanceCalculator.Calculate(Input(Project("Milestone", milestones: [done, open]), milestones: new Dictionary<Guid, decimal> { [done.Id] = 150000m, [open.Id] = 250000m }));
        summary.Revenue.ShouldBe(150000m);
        summary.ForecastRevenue.ShouldBe(400000m);
    }
}
