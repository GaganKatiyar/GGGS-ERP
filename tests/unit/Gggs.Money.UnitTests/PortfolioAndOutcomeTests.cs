using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Shouldly;

namespace Gggs.Money.UnitTests;

public sealed class PortfolioAndOutcomeTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static PortfolioProject Row(
        string model = "TimeAndMaterial",
        string currency = "INR",
        string? client = "Acme",
        string? department = "Delivery",
        string health = "Green",
        decimal revenue = 100,
        decimal cost = 60,
        decimal forecast = 100,
        decimal unbilled = 0,
        DateOnly? end = null) => new(Guid.NewGuid(), "P1", "Project", currency, model, client, department, health, revenue, cost, forecast, 0, unbilled, end ?? new DateOnly(2027, 1, 31));

    // ----- Rollups --------------------------------------------------------------------------------

    [Fact]
    public void Money_InDifferentCurrencies_IsNeverAddedTogether()
    {
        PortfolioDashboard dashboard = PortfolioRollup.Build([Row(currency: "INR", revenue: 1000, cost: 400), Row(currency: "USD", revenue: 10, cost: 4)], Today);

        dashboard.Currencies.Select(row => row.Currency).ShouldBe(["INR", "USD"]);
        dashboard.Currencies[0].Totals.Revenue.ShouldBe(1000);
        dashboard.Currencies[1].Totals.Revenue.ShouldBe(10);
    }

    [Fact]
    public void Margin_AddsUpByModelClientAndDepartment_LargestFirst()
    {
        CurrencyPortfolio inr = PortfolioRollup.Build(
            [
                Row("TimeAndMaterial", client: "Acme", department: "Data", revenue: 100, cost: 60),
                Row("TimeAndMaterial", client: "Globex", department: "Data", revenue: 200, cost: 220),
                Row("ManagedService", client: "Acme", department: null, revenue: 500, cost: 100),
                Row("NonBillable", client: null, department: "Data", revenue: 0, cost: 50),
            ],
            Today).Currencies.Single();

        inr.Totals.Projects.ShouldBe(4);
        inr.Totals.Margin.ShouldBe(370);
        inr.ByModel[0].Label.ShouldBe("ManagedService");
        RollupRow tm = inr.ByModel.Single(row => row.Label == "TimeAndMaterial");
        tm.Revenue.ShouldBe(300);
        tm.Margin.ShouldBe(20);
        tm.MarginPercent.ShouldBe(6.7m);
        inr.ByModel.Single(row => row.Label == "NonBillable").MarginPercent.ShouldBeNull("no revenue, so no percentage");
        inr.ByClient.Single(row => row.Label == "Acme").Margin.ShouldBe(440);
        inr.ByClient.Select(row => row.Label).ShouldContain("No client");
        inr.ByDepartment.Select(row => row.Label).ShouldContain("No department");
        inr.ByDepartment.Sum(row => row.Revenue).ShouldBe(inr.Totals.Revenue);
    }

    [Fact]
    public void Health_CountsEveryProjectOnce()
    {
        HealthCount health = PortfolioRollup.Build([Row(health: "Green"), Row(health: "Amber"), Row(currency: "USD", health: "Red"), Row(health: "NotAssessed")], Today).Health;

        health.ShouldBe(new HealthCount(1, 1, 1, 1));
    }

    [Fact]
    public void TheWatchList_IsWhatIsRedOrLosingMoney_WorstFirst()
    {
        CurrencyPortfolio inr = PortfolioRollup.Build(
            [Row(revenue: 100, cost: 50), Row(health: "Red", revenue: 100, cost: 50), Row(revenue: 100, cost: 300), Row(revenue: 100, cost: 120)],
            Today).Currencies.Single();

        inr.Watch.Select(row => row.Margin).ShouldBe([-200m, -20m, 50m]);
    }

    // ----- Forecast -------------------------------------------------------------------------------

    [Fact]
    public void WhatIsLeftToEarn_SpreadsOverTheMonthsLeft()
    {
        (IReadOnlyList<ForecastMonth> months, decimal later) = PortfolioRollup.Forecast([Row(revenue: 100, forecast: 400, end: new DateOnly(2027, 1, 31))], Today);

        months.Count.ShouldBe(PortfolioRollup.ForecastMonths);
        months[0].Month.ShouldBe(new DateOnly(2026, 11, 1));
        months.Take(3).Select(month => month.Revenue).ShouldBe([100m, 100m, 100m]);
        months[3].Revenue.ShouldBe(0);
        later.ShouldBe(0);
    }

    [Fact]
    public void ALongProject_PutsTheRestInLater()
    {
        (IReadOnlyList<ForecastMonth> months, decimal later) = PortfolioRollup.Forecast([Row(revenue: 0, forecast: 1200, end: new DateOnly(2027, 10, 31))], Today);

        months.Sum(month => month.Revenue).ShouldBe(600);
        later.ShouldBe(600);
    }

    [Fact]
    public void AProjectPastItsEnd_ExpectsTheRestNow_AndOneAheadOfForecastExpectsNothing()
    {
        PortfolioRollup.Forecast([Row(revenue: 100, forecast: 300, end: new DateOnly(2026, 6, 30))], Today).Months[0].Revenue.ShouldBe(200);
        PortfolioRollup.Forecast([Row(revenue: 500, forecast: 300)], Today).Months.Sum(month => month.Revenue).ShouldBe(0);
    }

    // ----- Outcome pricing ------------------------------------------------------------------------

    private static ProjectFinancialFacts Project() => new(
        Guid.NewGuid(), "P1", "Project", "Active", "OutcomeBased", "INR", null, null, null, null, null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 0, [], []);

    private static ProjectOutcomeUnits Units(decimal quantity, decimal price, DateOnly? on = null, string reference = "Release 4")
        => ProjectOutcomeUnits.Accept(Guid.NewGuid(), on ?? new DateOnly(2026, 10, 5), quantity, price, reference, "Client lead", null);

    private static readonly CommercialTerms Terms = new(UnitName: "release", UnitPrice: 50000, TargetUnits: 10);

    private static FinanceInput Input(params ProjectOutcomeUnits[] units)
        => new(Project(), null, null, new Dictionary<Guid, decimal>(), [], [], [], [], [], Today) { Terms = Terms, OutcomeUnits = units };

    [Fact]
    public void Revenue_IsWhatTheClientAccepted_AtThePriceThenInForce()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input(Units(2, 50000), Units(1, 60000)));

        summary.Revenue.ShouldBe(160000m);
        summary.AcceptedUnits.ShouldBe(3);
        summary.ForecastRevenue.ShouldBe(500000m, "ten releases at today's price");
    }

    [Fact]
    public void TheForecast_NeverFallsBelowWhatIsAlreadyEarned()
        => FinanceCalculator.Calculate(Input(Units(20, 50000))).ForecastRevenue.ShouldBe(1000000m);

    [Fact]
    public void WithNothingAccepted_ThereIsNoRevenueOrCostPerUnit()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(Input());

        summary.Revenue.ShouldBe(0);
        summary.CostPerUnit.ShouldBeNull();
    }

    [Fact]
    public void AcceptedUnits_GoOnAnInvoiceOnce()
    {
        ProjectOutcomeUnits first = Units(2, 50000, reference: "Release 4");
        ProjectOutcomeUnits billed = Units(1, 50000, reference: "Release 3");
        var already = new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)> { (InvoiceLineKind.Outcome, null, billed.Id) }, 50000);

        PlannedLine line = BillingPlanner.Plan(Input(first, billed), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), already, _ => "Someone").Lines.Single();

        line.Kind.ShouldBe(InvoiceLineKind.Outcome);
        line.Amount.ShouldBe(100000);
        line.ReferenceId.ShouldBe(first.Id);
        line.Description.ShouldContain("Release 4");
        line.Description.ShouldContain("release");
    }

    [Fact]
    public void UnitsAcceptedAfterThePeriod_WaitForTheirInvoice()
        => BillingPlanner.Plan(Input(Units(1, 50000, new DateOnly(2026, 11, 5))), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), AlreadyBilled.Nothing, _ => "Someone").Lines.ShouldBeEmpty();

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, 0)]
    public void AcceptingUnits_NeedsAQuantityAndAPrice(int quantity, int price)
        => Should.Throw<Exception>(() => Units(quantity, price));

    [Fact]
    public void AcceptingUnits_NeedsToSayWhatAndWhoAccepted()
    {
        Should.Throw<Exception>(() => ProjectOutcomeUnits.Accept(Guid.NewGuid(), Today, 1, 100, " ", "Client lead", null));
        Should.Throw<Exception>(() => ProjectOutcomeUnits.Accept(Guid.NewGuid(), Today, 1, 100, "Release", " ", null));
    }
}
