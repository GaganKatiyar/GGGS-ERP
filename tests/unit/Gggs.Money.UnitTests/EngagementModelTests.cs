using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Shouldly;

namespace Gggs.Money.UnitTests;

public sealed class EngagementModelTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Feb = new(2026, 2, 1);
    private static readonly DateOnly Mar = new(2026, 3, 1);

    private static ProjectAllocation Seat(DateOnly start, DateOnly? end, decimal? monthlyRate, bool billable = true, Guid? person = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), "P1", "Project", "Active", person ?? Person, Guid.NewGuid(), "Developer", 100, start, end, billable, 2000m, 1000m, true, monthlyRate);

    private static ProjectFinancialFacts Project(string billing, IReadOnlyList<ProjectAllocation>? team = null, int doneTasks = 0, int donePoints = 0) => new(
        Guid.NewGuid(), "P1", "Project", "Active", billing, "INR", null, null, null, null, null, null, null, Jan, new DateOnly(2026, 6, 30), 0, [], team ?? [], doneTasks, donePoints);

    private static LoggedHours Hours(DateOnly day, decimal hours, HourCategory category = HourCategory.Billable) => new(Person, null, null, day, category, hours, true);

    private static FinanceInput Input(ProjectFinancialFacts project, DateOnly today, decimal? fee = null, CommercialTerms? terms = null, IReadOnlyList<LoggedHours>? hours = null, IReadOnlyList<ProjectInvoice>? invoices = null) => new(
        project, null, fee, new Dictionary<Guid, decimal>(), hours ?? [], [], [], invoices ?? [], [], today) { Terms = terms ?? new CommercialTerms() };

    // ----- The bucket of hours --------------------------------------------------------------------

    private static IReadOnlyList<RetainerMonth> Months(CommercialTerms terms, params (DateOnly Month, decimal Used)[] used)
        => CommercialRules.RetainerMonths(Jan, used[^1].Month, 100000m, terms, used.ToDictionary(row => row.Month, row => row.Used));

    [Fact]
    public void WithinTheBucket_ThereIsOnlyTheFee()
    {
        RetainerMonth month = Months(new CommercialTerms(IncludedHoursPerMonth: 40, OverageRate: 2000), (Jan, 30m))[0];

        month.OverageHours.ShouldBe(0);
        month.Revenue.ShouldBe(100000m);
    }

    [Fact]
    public void BeyondTheBucket_HoursAreBilledAtTheOverageRate()
    {
        RetainerMonth month = Months(new CommercialTerms(IncludedHoursPerMonth: 40, OverageRate: 2000), (Jan, 45m))[0];

        month.OverageHours.ShouldBe(5);
        month.OverageAmount.ShouldBe(10000m);
        month.Revenue.ShouldBe(110000m);
    }

    [Fact]
    public void WithoutRollover_UnusedHoursAreLost()
    {
        IReadOnlyList<RetainerMonth> months = Months(new CommercialTerms(IncludedHoursPerMonth: 40, OverageRate: 2000), (Jan, 10m), (Feb, 45m));

        months[0].RollsForwardHours.ShouldBe(0);
        months[1].RolledInHours.ShouldBe(0);
        months[1].OverageHours.ShouldBe(5);
    }

    [Fact]
    public void RolloverIsAShareOfTheUnusedHours_AndCoversTheNextMonthsOverage()
    {
        IReadOnlyList<RetainerMonth> months = Months(new CommercialTerms(IncludedHoursPerMonth: 40, RolloverPercent: 50, OverageRate: 2000), (Jan, 20m), (Feb, 45m));

        months[0].RollsForwardHours.ShouldBe(10);
        months[1].RolledInHours.ShouldBe(10);
        months[1].OverageHours.ShouldBe(0, "five over the bucket, and ten rolled in");
    }

    [Fact]
    public void RolledHours_DoNotRollASecondTime()
    {
        IReadOnlyList<RetainerMonth> months = Months(new CommercialTerms(IncludedHoursPerMonth: 40, RolloverPercent: 100), (Jan, 0m), (Feb, 40m), (Mar, 40m));

        months[1].RolledInHours.ShouldBe(40);
        months[1].RollsForwardHours.ShouldBe(0, "February used its own bucket, and January's hours expire with it");
        months[2].RolledInHours.ShouldBe(0);
    }

    [Fact]
    public void WithoutABucket_ThereIsNeverOverage()
    {
        RetainerMonth month = Months(new CommercialTerms(OverageRate: 2000), (Jan, 500m))[0];

        month.OverageHours.ShouldBe(0);
        month.Revenue.ShouldBe(100000m);
    }

    [Fact]
    public void RetainerRevenue_IncludesOverage_AndOnlyBillableHoursBurnTheBucket()
    {
        FinanceInput input = Input(
            Project("Retainer"),
            new DateOnly(2026, 2, 15),
            100000m,
            new CommercialTerms(IncludedHoursPerMonth: 40, OverageRate: 2000),
            [Hours(new DateOnly(2026, 1, 10), 45m), Hours(new DateOnly(2026, 1, 11), 500m, HourCategory.NonBillable)]);

        FinanceSummary summary = FinanceCalculator.Calculate(input);

        summary.Revenue.ShouldBe(210000m, "two months' fees and five hours of overage");
        summary.RetainerMonths!.Count.ShouldBe(2);
        summary.RetainerMonths[0].UsedHours.ShouldBe(45m);
        summary.ForecastRevenue.ShouldBe(610000m, "six months' fees and the overage so far");
    }

    [Fact]
    public void ManagedService_EarnsLikeARetainer()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(Project("ManagedService"), new DateOnly(2026, 1, 20), 300000m));

        summary.Revenue.ShouldBe(300000m);
        summary.ForecastRevenue.ShouldBe(1800000m);
    }

    // ----- Dedicated teams ------------------------------------------------------------------------

    [Fact]
    public void AWholeMonthOfASeat_EarnsItsRate()
        => CommercialRules.SeatRevenueByMonth(Seat(Jan, null, 300000m), Jan, new DateOnly(2026, 1, 31))[Jan].ShouldBe(300000m);

    [Fact]
    public void APartMonth_EarnsItsShareOfTheMonthsDays()
        => CommercialRules.SeatRevenueByMonth(Seat(new DateOnly(2026, 2, 15), null, 280000m), Feb, new DateOnly(2026, 2, 28))[Feb].ShouldBe(140000m);

    [Fact]
    public void AReplacementMidMonth_SplitsTheFeeRatherThanDoublingIt()
    {
        decimal leaver = CommercialRules.SeatRevenueByMonth(Seat(Jan, new DateOnly(2026, 2, 14), 280000m), Feb, new DateOnly(2026, 2, 28))[Feb];
        decimal joiner = CommercialRules.SeatRevenueByMonth(Seat(new DateOnly(2026, 2, 15), null, 280000m), Feb, new DateOnly(2026, 2, 28))[Feb];

        (leaver + joiner).ShouldBe(280000m);
    }

    [Fact]
    public void ANonBillableSeat_OrOneWithNoRate_EarnsNothing()
    {
        CommercialRules.SeatRevenueByMonth(Seat(Jan, null, 300000m, billable: false), Jan, Feb).ShouldBeEmpty();
        CommercialRules.SeatRevenueByMonth(Seat(Jan, null, null), Jan, Feb).ShouldBeEmpty();
    }

    [Fact]
    public void ASeatOutsideTheWindow_EarnsNothing()
        => CommercialRules.SeatRevenueByMonth(Seat(Mar, null, 300000m), Jan, new DateOnly(2026, 1, 31)).ShouldBeEmpty();

    [Fact]
    public void DedicatedTeam_EarnsSeatsToDate_ForecastsToTheEnd_AndCountsUnpricedSeats()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(
            Project("DedicatedTeam", [Seat(Jan, null, 300000m), Seat(Jan, null, null, person: Guid.NewGuid())]),
            new DateOnly(2026, 2, 28)));

        summary.Revenue.ShouldBe(600000m, "two whole months");
        summary.ForecastRevenue.ShouldBe(1800000m, "six months to the end of June");
        summary.UnpricedSeats.ShouldBe(1);
        summary.Months.Single(month => month.Month == Feb).Revenue.ShouldBe(300000m);
    }

    // ----- Time and material ----------------------------------------------------------------------

    private static ProjectFinancialFacts TimeAndMaterial()
        => Project("TimeAndMaterial", [new ProjectAllocation(Guid.NewGuid(), Guid.NewGuid(), "P1", "Project", "Active", Person, Guid.NewGuid(), "Developer", 100, Jan, new DateOnly(2026, 2, 28), true, 2000m, 1000m, true)]);

    [Fact]
    public void ACap_HoldsRevenue_AndReportsWhatWentOver()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(
            TimeAndMaterial(),
            new DateOnly(2026, 3, 15),
            terms: new CommercialTerms(NotToExceed: 100000m),
            hours: [Hours(new DateOnly(2026, 1, 10), 40m), Hours(new DateOnly(2026, 2, 10), 30m)]));

        summary.Revenue.ShouldBe(100000m, "seventy hours would earn 140,000");
        summary.OverCapAmount.ShouldBe(40000m);
        summary.CapUsedPercent.ShouldBe(140m);
        summary.Months.Sum(month => month.Revenue).ShouldBe(100000m, "the monthly view adds up to the capped figure");
        summary.Months.Single(month => month.Month == Feb).Revenue.ShouldBe(20000m);
    }

    [Fact]
    public void UnderACap_NothingChanges()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(TimeAndMaterial(), new DateOnly(2026, 3, 15), terms: new CommercialTerms(NotToExceed: 100000m), hours: [Hours(new DateOnly(2026, 1, 10), 40m)]));

        summary.Revenue.ShouldBe(80000m);
        summary.OverCapAmount.ShouldBe(0);
        summary.CapUsedPercent.ShouldBe(80m);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(74d, 0)]
    [InlineData(75d, 75)]
    [InlineData(89.9, 75)]
    [InlineData(90d, 90)]
    [InlineData(120d, 100)]
    public void CapAlerts_GoOutAtThreeLevels(double? used, int expected)
        => CommercialRules.CapThreshold((decimal?)used).ShouldBe(expected);

    [Fact]
    public void ThePoBalance_IsWhatIsLeftAfterInvoices()
    {
        ProjectFinancialFacts project = TimeAndMaterial();
        ProjectInvoice sent = ProjectInvoice.Raise(project.Id, "INV-1", new InvoiceDetails(new DateOnly(2026, 2, 1), null, null, null, 60000m, 0, null, null));
        sent.Send();
        ProjectInvoice draft = ProjectInvoice.Raise(project.Id, "INV-2", new InvoiceDetails(new DateOnly(2026, 2, 1), null, null, null, 9000m, 0, null, null));

        FinanceSummary summary = FinanceCalculator.Calculate(Input(project, new DateOnly(2026, 3, 15), terms: new CommercialTerms(PoValue: 100000m), invoices: [sent, draft]));

        summary.PoBalance.ShouldBe(40000m, "a draft has not been billed");
    }

    // ----- Milestones -----------------------------------------------------------------------------

    [Fact]
    public void AMilestoneNeedingAcceptance_EarnsOnlyOnceAccepted_InTheMonthItWasAccepted()
    {
        var waiting = new MilestoneFact(Guid.NewGuid(), "Design", Jan, Jan, "Achieved", true, RequiresAcceptance: true);
        var accepted = new MilestoneFact(Guid.NewGuid(), "Build", Jan, Jan, "Achieved", true, RequiresAcceptance: true, AcceptedOn: Feb);
        var plain = new MilestoneFact(Guid.NewGuid(), "Test", Jan, Jan, "Achieved", true);
        ProjectFinancialFacts project = Project("Milestone") with { Milestones = [waiting, accepted, plain] };
        FinanceInput input = Input(project, new DateOnly(2026, 3, 1)) with
        {
            MilestoneAmounts = new Dictionary<Guid, decimal> { [waiting.Id] = 100m, [accepted.Id] = 200m, [plain.Id] = 400m },
        };

        FinanceSummary summary = FinanceCalculator.Calculate(input);

        summary.Revenue.ShouldBe(600m);
        summary.Months.Single(month => month.Month == Feb).Revenue.ShouldBe(200m);
    }

    // ----- Cost per deliverable -------------------------------------------------------------------

    [Fact]
    public void CostPerDeliverable_SharesActualCostOverWhatIsDone()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(
            Project("FixedPrice", [Seat(Jan, null, null)], doneTasks: 4, donePoints: 20),
            new DateOnly(2026, 3, 1),
            hours: [Hours(new DateOnly(2026, 1, 10), 40m)]));

        summary.ActualCost.ShouldBe(40000m);
        summary.CostPerDoneTask.ShouldBe(10000m);
        summary.CostPerStoryPoint.ShouldBe(2000m);
    }

    [Fact]
    public void WithNothingDone_CostPerDeliverableIsNotShown()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(Project("FixedPrice"), new DateOnly(2026, 3, 1)));

        summary.CostPerDoneTask.ShouldBeNull();
        summary.CostPerStoryPoint.ShouldBeNull();
    }
}
