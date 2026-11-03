using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Shouldly;

namespace Gggs.Money.UnitTests;

public sealed class BillingTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly JanEnd = new(2026, 1, 31);
    private static readonly DateOnly Feb = new(2026, 2, 1);
    private static readonly DateOnly FebEnd = new(2026, 2, 28);
    private static readonly DateOnly Mar = new(2026, 3, 1);

    private static string NameOf(Guid id) => id == Person ? "Asha" : "Ravi";

    private static ProjectAllocation Seat(Guid person, decimal? billingRate = 2000m, decimal? monthlyRate = null, DateOnly? start = null, DateOnly? end = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), "P1", "Project", "Active", person, Guid.NewGuid(), "Developer", 100, start ?? Jan, end, true, billingRate, 1000m, true, monthlyRate);

    private static ProjectFinancialFacts Project(string billing, IReadOnlyList<ProjectAllocation>? team = null, decimal progress = 0, IReadOnlyList<MilestoneFact>? milestones = null) => new(
        Guid.NewGuid(), "P1", "Project", "Active", billing, "INR", null, null, null, null, null, null, null, Jan, new DateOnly(2026, 6, 30), progress, milestones ?? [], team ?? []);

    private static LoggedHours Entry(DateOnly day, decimal hours, Guid? person = null, HourCategory category = HourCategory.Billable, bool approved = true) =>
        new(person ?? Person, null, null, day, category, hours, approved, Guid.NewGuid());

    private static FinanceInput Input(
        ProjectFinancialFacts project,
        IReadOnlyList<LoggedHours>? hours = null,
        CommercialTerms? terms = null,
        decimal? contract = null,
        decimal? fee = null,
        IReadOnlyDictionary<Guid, decimal>? milestoneAmounts = null,
        DateOnly? today = null) => new(project, contract, fee, milestoneAmounts ?? new Dictionary<Guid, decimal>(), hours ?? [], [], [], [], [], today ?? Mar)
        {
            Terms = terms ?? new CommercialTerms(),
        };

    private static BillingPlan Plan(FinanceInput input, DateOnly from, DateOnly to, AlreadyBilled? billed = null)
        => BillingPlanner.Plan(input, from, to, billed ?? AlreadyBilled.Nothing, NameOf);

    private static AlreadyBilled BilledLines(params (InvoiceLineKind Kind, DateOnly? Month, Guid? Reference)[] lines)
        => new(new HashSet<Guid>(), lines.ToHashSet(), 0);

    // ----- Time and material ----------------------------------------------------------------------

    [Fact]
    public void Hours_AreBilledPerPersonAndRate_AndRecordTheirEntries()
    {
        LoggedHours monday = Entry(new DateOnly(2026, 1, 5), 8);
        LoggedHours tuesday = Entry(new DateOnly(2026, 1, 6), 4);
        LoggedHours other = Entry(new DateOnly(2026, 1, 6), 2, Other);

        BillingPlan plan = Plan(Input(Project("TimeAndMaterial", [Seat(Person), Seat(Other, 3000m)]), [monday, tuesday, other]), Jan, JanEnd);

        plan.Lines.Count.ShouldBe(2);
        PlannedLine first = plan.Lines[0];
        first.Kind.ShouldBe(InvoiceLineKind.Hours);
        first.Quantity.ShouldBe(12);
        first.Amount.ShouldBe(24000m);
        first.EntryIds!.ShouldBe([monday.EntryId!.Value, tuesday.EntryId!.Value]);
        plan.Lines[1].Amount.ShouldBe(6000m);
        plan.Amount.ShouldBe(30000m);
    }

    [Fact]
    public void Hours_ThatAreUnapprovedNonBillableOrOutsideThePeriod_AreLeftOut()
    {
        BillingPlan plan = Plan(
            Input(Project("TimeAndMaterial", [Seat(Person)]), [
                Entry(new DateOnly(2026, 1, 5), 8, approved: false),
                Entry(new DateOnly(2026, 1, 6), 8, category: HourCategory.NonBillable),
                Entry(new DateOnly(2026, 2, 2), 8),
            ]),
            Jan,
            JanEnd);

        plan.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Hours_AlreadyOnAnInvoice_AreNotBilledAgain()
    {
        LoggedHours billed = Entry(new DateOnly(2026, 1, 5), 8);
        LoggedHours fresh = Entry(new DateOnly(2026, 1, 6), 4);

        BillingPlan plan = Plan(
            Input(Project("TimeAndMaterial", [Seat(Person)]), [billed, fresh]),
            Jan,
            JanEnd,
            new AlreadyBilled(new HashSet<Guid> { billed.EntryId!.Value }, new HashSet<(InvoiceLineKind, DateOnly?, Guid?)>(), 16000m));

        plan.Amount.ShouldBe(8000m);
        plan.Lines[0].EntryIds!.ShouldBe([fresh.EntryId!.Value]);
    }

    [Fact]
    public void TheCap_TakesWholeEntries_AndHoldsBackTheRest()
    {
        BillingPlan plan = Plan(
            Input(Project("TimeAndMaterial", [Seat(Person)]), [Entry(new DateOnly(2026, 1, 5), 8), Entry(new DateOnly(2026, 1, 6), 8)], new CommercialTerms(NotToExceed: 40000)),
            Jan,
            JanEnd,
            new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)>(), 20000m));

        plan.Amount.ShouldBe(16000m, "20,000 of the 40,000 is left: one day fits, the second would go over");
        plan.HeldByCap.ShouldBe(16000m);
    }

    [Fact]
    public void Hours_WithNoRate_AreReportedRatherThanBilledAtNothing()
    {
        BillingPlan plan = Plan(Input(Project("TimeAndMaterial", [Seat(Person, billingRate: null)]), [Entry(new DateOnly(2026, 1, 5), 8)]), Jan, JanEnd);

        plan.Lines.ShouldBeEmpty();
        plan.UnpricedHours.ShouldBe(8);
    }

    // ----- Fixed price and milestones -------------------------------------------------------------

    [Fact]
    public void FixedPrice_BillsProgressLessWhatHasBeenInvoiced()
    {
        BillingPlan plan = Plan(
            Input(Project("FixedPrice", progress: 40), contract: 1000000m),
            Jan,
            JanEnd,
            new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)>(), 250000m));

        plan.Lines.Single().Amount.ShouldBe(150000m);
        plan.Lines.Single().Kind.ShouldBe(InvoiceLineKind.Progress);
    }

    [Fact]
    public void FixedPrice_InvoicedAheadOfProgress_HasNothingToBill()
    {
        BillingPlan plan = Plan(
            Input(Project("FixedPrice", progress: 20), contract: 1000000m),
            Jan,
            JanEnd,
            new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)>(), 250000m));

        plan.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Milestones_AreBilledOnceEarned_AndOnlyOnce()
    {
        Guid done = Guid.NewGuid();
        Guid waitingForClient = Guid.NewGuid();
        Guid accepted = Guid.NewGuid();
        Guid planned = Guid.NewGuid();
        Guid billed = Guid.NewGuid();
        MilestoneFact[] milestones =
        [
            new(done, "Design", Jan, Jan, "Achieved", true),
            new(waitingForClient, "Pilot", Jan, Jan, "Achieved", true, RequiresAcceptance: true),
            new(accepted, "Model", Jan, Jan, "Achieved", true, RequiresAcceptance: true, AcceptedOn: Jan),
            new(planned, "Rollout", Mar, null, "Planned", true),
            new(billed, "Kickoff", Jan, Jan, "Achieved", true),
        ];
        var amounts = new Dictionary<Guid, decimal> { [done] = 100m, [waitingForClient] = 200m, [accepted] = 300m, [planned] = 400m, [billed] = 500m };

        BillingPlan plan = Plan(Input(Project("Milestone", milestones: milestones), milestoneAmounts: amounts), Jan, JanEnd, BilledLines((InvoiceLineKind.Milestone, null, billed)));

        plan.Lines.Select(line => line.ReferenceId).ShouldBe([done, accepted]);
        plan.Amount.ShouldBe(400m);
    }

    // ----- Monthly fees ---------------------------------------------------------------------------

    private static readonly CommercialTerms Bucket = new(IncludedHoursPerMonth: 40, OverageRate: 2000);

    [Theory]
    [InlineData("Retainer")]
    [InlineData("ManagedService")]
    public void AMonth_BillsItsFee_AndOverageOnceItIsOver(string model)
    {
        BillingPlan plan = Plan(Input(Project(model), [Entry(new DateOnly(2026, 1, 12), 45)], Bucket, fee: 100000m), Jan, JanEnd);

        plan.Lines.Select(line => line.Kind).ShouldBe([InvoiceLineKind.MonthlyFee, InvoiceLineKind.Overage]);
        plan.Lines[0].Amount.ShouldBe(100000m);
        plan.Lines[1].Amount.ShouldBe(10000m);
        plan.Lines[1].Quantity.ShouldBe(5);
    }

    [Fact]
    public void TheCurrentMonth_BillsItsFee_ButNotOverageYet()
    {
        BillingPlan plan = Plan(Input(Project("Retainer"), [Entry(new DateOnly(2026, 1, 12), 45)], Bucket, fee: 100000m, today: new DateOnly(2026, 1, 20)), Jan, JanEnd);

        plan.Lines.Single().Kind.ShouldBe(InvoiceLineKind.MonthlyFee);
    }

    [Fact]
    public void AMonthAlreadyBilled_IsNotBilledAgain_ButTheNextOneIs()
    {
        BillingPlan plan = Plan(Input(Project("Retainer"), terms: Bucket, fee: 100000m), Jan, FebEnd, BilledLines((InvoiceLineKind.MonthlyFee, Jan, null)));

        plan.Lines.Single().Month.ShouldBe(Feb);
    }

    [Fact]
    public void HoursApprovedAfterAMonthWasBilled_AreBilledAsTheDifference()
    {
        var billed = new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)> { (InvoiceLineKind.MonthlyFee, Jan, null), (InvoiceLineKind.Overage, Jan, null) }, 110000m)
        {
            Overage = new Dictionary<DateOnly, decimal> { [Jan] = 10000m },
        };

        BillingPlan plan = Plan(Input(Project("Retainer"), [Entry(new DateOnly(2026, 1, 12), 47)], Bucket, fee: 100000m), Jan, JanEnd, billed);

        PlannedLine line = plan.Lines.Single();
        line.Kind.ShouldBe(InvoiceLineKind.Overage);
        line.Amount.ShouldBe(4000m);
        line.Quantity.ShouldBe(2);
    }

    [Fact]
    public void Rollover_FromEarlierMonths_CountsEvenWhenOnlyALaterMonthIsBilled()
    {
        var terms = new CommercialTerms(IncludedHoursPerMonth: 40, RolloverPercent: 100, OverageRate: 2000);

        BillingPlan plan = Plan(Input(Project("Retainer"), [Entry(new DateOnly(2026, 1, 12), 30), Entry(new DateOnly(2026, 2, 12), 45)], terms, fee: 100000m), Feb, FebEnd);

        plan.Lines.Single().Kind.ShouldBe(InvoiceLineKind.MonthlyFee, "ten hours rolled in from January cover February's five over");
    }

    // ----- Service credits -------------------------------------------------------------------------

    private static FinanceInput WithCredits(IReadOnlyDictionary<DateOnly, decimal> credits, DateOnly? today = null)
        => Input(Project("ManagedService"), terms: Bucket, fee: 100000m, today: today) with { ServiceCredits = credits };

    [Fact]
    public void AServiceCredit_ComesOffTheFee_ForAMonthThatHasEnded()
    {
        BillingPlan plan = Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }), Jan, JanEnd);

        plan.Lines.Select(line => line.Kind).ShouldBe([InvoiceLineKind.MonthlyFee, InvoiceLineKind.ServiceCredit]);
        plan.Lines[1].Amount.ShouldBe(-5000m);
        plan.Amount.ShouldBe(95000m);
    }

    [Fact]
    public void AServiceCredit_WaitsUntilItsMonthHasEnded()
    {
        BillingPlan plan = Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }, new DateOnly(2026, 1, 20)), Jan, JanEnd);

        plan.Lines.Single().Kind.ShouldBe(InvoiceLineKind.MonthlyFee, "more breaches can still land in January");
    }

    [Fact]
    public void ACreditForAnEarlierMonth_ComesOffTheNextInvoice()
    {
        BillingPlan plan = Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }), Feb, FebEnd, BilledLines((InvoiceLineKind.MonthlyFee, Jan, null)));

        plan.Lines.Select(line => line.Month).ShouldBe([Feb, Jan]);
        plan.Amount.ShouldBe(95000m);
    }

    [Fact]
    public void ACreditAlreadyGiven_IsNotGivenAgain_ButALaterBreachAddsTheDifference()
    {
        var billed = new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)> { (InvoiceLineKind.MonthlyFee, Jan, null) }, 95000m)
        {
            ServiceCredits = new Dictionary<DateOnly, decimal> { [Jan] = 5000m },
        };

        Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }), Feb, FebEnd, billed).Lines.Single().Kind.ShouldBe(InvoiceLineKind.MonthlyFee);

        BillingPlan more = Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 8 }), Feb, FebEnd, billed);
        more.Lines[1].Amount.ShouldBe(-3000m);
    }

    [Fact]
    public void WithNothingToCharge_ACreditWaits_RatherThanMakingANegativeInvoice()
    {
        var billed = new AlreadyBilled(new HashSet<Guid>(), new HashSet<(InvoiceLineKind, DateOnly?, Guid?)> { (InvoiceLineKind.MonthlyFee, Jan, null) }, 100000m);

        BillingPlan plan = Plan(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }), Jan, JanEnd, billed);

        plan.Lines.ShouldBeEmpty();
        plan.CreditsWaiting.ShouldBe(5000m);
    }

    [Fact]
    public void CreditsGoOnOldestFirst_AndWhatDoesNotFitWaits()
    {
        var input = Input(Project("ManagedService"), terms: Bucket, fee: 10000m) with { ServiceCredits = new Dictionary<DateOnly, decimal> { [Jan] = 60, [Feb] = 60 } };

        BillingPlan plan = Plan(input, Mar, new DateOnly(2026, 3, 31), BilledLines((InvoiceLineKind.MonthlyFee, Jan, null), (InvoiceLineKind.MonthlyFee, Feb, null)));

        plan.Amount.ShouldBe(4000m, "March's fee less January's credit");
        plan.CreditsWaiting.ShouldBe(6000m);
    }

    [Fact]
    public void Revenue_NetsOffServiceCredits_MonthByMonth()
    {
        FinanceSummary summary = FinanceCalculator.Calculate(WithCredits(new Dictionary<DateOnly, decimal> { [Jan] = 5 }, new DateOnly(2026, 1, 20)));

        summary.Revenue.ShouldBe(95000m);
        summary.RetainerMonths!.Single().Credit.ShouldBe(5000m);
    }

    // ----- Dedicated team -------------------------------------------------------------------------

    [Fact]
    public void Seats_BillEachMonthInThePeriod_ProratedAtTheEnds()
    {
        ProjectAllocation asha = Seat(Person, null, 310000m);
        ProjectAllocation ravi = Seat(Other, null, 280000m, start: new DateOnly(2026, 2, 15));

        BillingPlan plan = Plan(Input(Project("DedicatedTeam", [asha, ravi])), Feb, FebEnd);

        plan.Lines.Count.ShouldBe(2);
        plan.Lines[0].ReferenceId.ShouldBe(asha.MemberId);
        plan.Lines[0].Amount.ShouldBe(310000m);
        plan.Lines[1].ReferenceId.ShouldBe(ravi.MemberId);
        plan.Lines[1].Amount.ShouldBeLessThan(280000m);
        plan.Lines[1].Amount.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void ASeatAlreadyBilledForTheMonth_IsNotBilledAgain()
    {
        ProjectAllocation asha = Seat(Person, null, 310000m);
        ProjectAllocation ravi = Seat(Other, null, 280000m);

        BillingPlan plan = Plan(Input(Project("DedicatedTeam", [asha, ravi])), Feb, FebEnd, BilledLines((InvoiceLineKind.Seat, Feb, asha.MemberId)));

        plan.Lines.Single().ReferenceId.ShouldBe(ravi.MemberId);
    }

    [Fact]
    public void NonBillableWork_HasNothingToInvoice()
        => Plan(Input(Project("NonBillable", [Seat(Person)]), [Entry(new DateOnly(2026, 1, 5), 8)]), Jan, JanEnd).Lines.ShouldBeEmpty();

    // ----- The invoice ----------------------------------------------------------------------------

    private static ProjectInvoice Invoice(decimal amount = 1000m, decimal tax = 180m)
        => ProjectInvoice.Raise(Guid.NewGuid(), "INV-1", new InvoiceDetails(Jan, Feb, null, null, amount, tax, null, null));

    [Fact]
    public void ADraft_TakesNoCreditNote()
        => Should.Throw<DomainRuleException>(() => Invoice().Credit(100m)).ErrorCode.ShouldBe("INVOICE_STATE");

    [Fact]
    public void ACreditNote_ReducesWhatStands_AndCannotExceedIt()
    {
        ProjectInvoice invoice = Invoice();
        invoice.Send();

        invoice.Credit(400m);
        invoice.OpenAmount.ShouldBe(600m);

        Should.Throw<DomainRuleException>(() => invoice.Credit(601m)).ErrorCode.ShouldBe("INVOICE_CREDIT_AMOUNT");
        invoice.Credit(600m);
        invoice.OpenAmount.ShouldBe(0m);
        invoice.IsOverdue(Mar).ShouldBeFalse("nothing is owed on it any more");
    }

    [Fact]
    public void ACreditedInvoice_CannotBeCancelledOrChanged()
    {
        ProjectInvoice invoice = Invoice();
        invoice.Send();
        invoice.Credit(100m);

        Should.Throw<DomainRuleException>(invoice.Cancel).ErrorCode.ShouldBe("INVOICE_STATE");
        Should.Throw<DomainRuleException>(() => invoice.Update(new InvoiceDetails(Jan, Feb, null, null, 50m, 0, null, null))).ErrorCode.ShouldBe("INVOICE_STATE");
    }

    [Fact]
    public void AnInvoiceAwaitingApproval_CannotChange_AndReturnsToDraftIfTurnedDown()
    {
        ProjectInvoice invoice = Invoice();
        invoice.AwaitApproval(Guid.NewGuid());
        invoice.Status.ShouldBe(InvoiceStatus.PendingApproval);
        Should.Throw<DomainRuleException>(() => invoice.Update(new InvoiceDetails(Jan, Feb, null, null, 50m, 0, null, null))).ErrorCode.ShouldBe("INVOICE_STATE");

        invoice.ReturnToDraft();
        invoice.Status.ShouldBe(InvoiceStatus.Draft);
        invoice.WorkflowInstanceId.ShouldBeNull();
    }

    [Fact]
    public void CreditNotes_ComeOffBilledAndCollected()
    {
        ProjectInvoice invoice = Invoice();
        invoice.Send();
        invoice.MarkPaid(Feb);
        invoice.Credit(250m);

        var input = new FinanceInput(Project("TimeAndMaterial"), null, null, new Dictionary<Guid, decimal>(), [], [], [], [invoice], [], Mar);
        FinanceSummary summary = FinanceCalculator.Calculate(input);

        summary.Billed.ShouldBe(750m);
        summary.Collected.ShouldBe(750m);
    }

    // ----- For the accounting system --------------------------------------------------------------

    private static readonly ExportLedgers Ledgers = new("GGGS & Co", "Sales", "Output GST");

    [Fact]
    public void Tally_AnInvoiceDebitsTheClient_AndCreditsSalesAndTax()
    {
        string xml = InvoiceExport.TallyXml(Ledgers, [new ExportVoucher(false, "INV-2026-0001", new DateOnly(2026, 1, 31), "Acme <India>", 1000m, 180m, "P1")]);

        xml.ShouldContain("<SVCURRENTCOMPANY>GGGS &amp; Co</SVCURRENTCOMPANY>");
        xml.ShouldContain("VCHTYPE=\"Sales\"");
        xml.ShouldContain("<DATE>20260131</DATE>");
        xml.ShouldContain("<VOUCHERNUMBER>INV-2026-0001</VOUCHERNUMBER>");
        xml.ShouldContain("<LEDGERNAME>Acme &lt;India&gt;</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-1180.00</AMOUNT>");
        xml.ShouldContain("<LEDGERNAME>Sales</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>1000.00</AMOUNT>");
        xml.ShouldContain("<LEDGERNAME>Output GST</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>180.00</AMOUNT>");
    }

    [Fact]
    public void Tally_ACreditNoteReverses_AndNoTaxMeansNoTaxLine()
    {
        string xml = InvoiceExport.TallyXml(Ledgers, [new ExportVoucher(true, "CN-2026-0001", Jan, "Acme", 500m, 0m, "Against INV-1: wrong rate")]);

        xml.ShouldContain("VCHTYPE=\"Credit Note\"");
        xml.ShouldContain("<LEDGERNAME>Acme</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>500.00</AMOUNT>");
        xml.ShouldContain("<LEDGERNAME>Sales</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-500.00</AMOUNT>");
        xml.ShouldNotContain("Output GST");
    }

    [Fact]
    public void Csv_ShowsCreditsAsNegative_QuotesCommas_AndDefusesFormulas()
    {
        string csv = InvoiceExport.Csv([
            new ExportVoucher(false, "INV-1", Jan, "Acme, Inc", 1000m, 180m, "=HYPERLINK(1)"),
            new ExportVoucher(true, "CN-1", Jan, "Acme", 100m, 18m, "note"),
        ]);

        string[] rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        rows[1].ShouldBe("Invoice,INV-1,2026-01-01,\"Acme, Inc\",1000.00,180.00,1180.00,'=HYPERLINK(1)");
        rows[2].ShouldBe("Credit note,CN-1,2026-01-01,Acme,-100.00,-18.00,-118.00,note");
    }
}
