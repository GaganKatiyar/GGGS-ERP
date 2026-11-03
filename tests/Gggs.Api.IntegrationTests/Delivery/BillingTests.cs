using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class BillingTests(PeopleOsApiFactory factory)
{
    private const decimal WeekOfHours = 80000m;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly LastWeek => Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)).AddDays(-7);

    private static DateOnly LastMonth => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

    private static object Body(string billing, Guid manager, DateOnly? start = null) => new
    {
        Code = "B" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
        Name = "Project " + CoreHrApi.Unique("N"),
        Type = "Implementation",
        ProjectManagerEmployeeId = manager,
        StartDate = Iso(start ?? Today.AddDays(-90)),
        EndDate = Iso(Today.AddDays(90)),
        Priority = "High",
        BillingType = billing,
        Currency = "INR",
        Confidentiality = "Internal",
        Methodology = "Agile",
    };

    private static async Task<Guid> ProjectOfAsync(HttpClient admin, string billing, Guid manager, DateOnly? start = null)
        => (await CreateProjectAsync(admin, Body(billing, manager, start))).GetProperty("id").GetGuid();

    private static async Task StaffAsync(HttpClient admin, Guid project, Guid person, decimal? rate = 2000m, decimal? monthly = null, DateOnly? start = null)
        => await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/team", new
        {
            EmployeeId = person,
            ProjectRoleId = await RoleAsync(admin, "DEV"),
            StartDate = Iso(start ?? Today.AddDays(-60)),
            AllocationPercent = 100,
            Billable = true,
            BillingRate = rate,
            CostRate = 1000m,
            MonthlyRate = monthly,
        }, Ct));

    /// <summary>A five-day week of billable hours, approved on submit because no workflow is active.</summary>
    private async Task<Guid> ApprovedWeekAsync(Guid project, Guid person, decimal hoursPerDay = 8m)
    {
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("bill-self"), person, (Permissions.Self.TimesheetSubmit, DataScope.Own));
        await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Iso(LastWeek)}", new
        {
            Entries = Enumerable.Range(0, 5).Select(day => new { Date = Iso(LastWeek.AddDays(day)), ProjectId = project, Category = "Billable", Hours = hoursPerDay }).ToArray(),
        }, Ct));
        JsonElement submitted = await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Iso(LastWeek)}/submit", null, Ct));
        submitted.GetProperty("status").GetString().ShouldBe("Approved");
        JsonElement week = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={Iso(LastWeek)}", Ct);
        return week.GetProperty("timesheet").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient client, Guid project, DateOnly from, DateOnly to)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}/billing/preview?from={Iso(from)}&to={Iso(to)}", Ct);

    private static async Task<JsonElement> PrepareAsync(HttpClient client, Guid project, DateOnly from, DateOnly to, decimal? tax = null)
        => await OkAsync(await client.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(from), To = Iso(to), DueDate = Iso(Today.AddDays(30)), TaxAmount = tax }, Ct));

    private static Task<JsonElement> PrepareWeekAsync(HttpClient client, Guid project, decimal? tax = null) => PrepareAsync(client, project, LastWeek, LastWeek.AddDays(6), tax);

    private static async Task<decimal> ReadyAsync(HttpClient client, Guid project) => (await PreviewAsync(client, project, LastWeek, LastWeek.AddDays(6))).GetProperty("amount").GetDecimal();

    private static async Task<JsonElement> ActAsync(HttpClient client, Guid project, JsonElement invoice, string action)
        => await OkAsync(await client.PostAsync($"/api/v1/project-financials/{project}/invoices/{invoice.GetProperty("id").GetGuid()}/{action}", null, Ct));

    private static async Task<JsonElement> FinanceAsync(HttpClient client, Guid project)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct);

    private static Task<HttpResponseMessage> ReopenAsync(HttpClient client, Guid timesheet)
        => client.PostAsJsonAsync($"/api/v1/timesheets/{timesheet}/reopen", new { Reason = "Wrong project" }, Ct);

    private static Task<HttpResponseMessage> CreditAsync(HttpClient client, Guid project, JsonElement invoice, object body)
        => client.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/{invoice.GetProperty("id").GetGuid()}/credit", body, Ct);

    // ----- Time and material ----------------------------------------------------------------------

    [Fact]
    public async Task ApprovedHours_GoOnAnInvoiceOnce_AndThenCannotBeReopened()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        Guid timesheet = await ApprovedWeekAsync(project, org.Requester);

        (await ReadyAsync(admin, project)).ShouldBe(WeekOfHours);

        JsonElement invoice = await PrepareWeekAsync(admin, project, tax: 14400m);
        invoice.GetProperty("status").GetString().ShouldBe("Draft");
        invoice.GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours);
        invoice.GetProperty("taxAmount").GetDecimal().ShouldBe(14400m);
        JsonElement line = invoice.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("kind").GetString().ShouldBe("Hours");
        line.GetProperty("quantity").GetDecimal().ShouldBe(40m);
        line.GetProperty("rate").GetDecimal().ShouldBe(2000m);

        (await ReadyAsync(admin, project)).ShouldBe(0m, "those hours are on an invoice now");
        HttpResponseMessage again = await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(LastWeek), To = Iso(LastWeek.AddDays(6)) }, Ct);
        (await CoreHrApi.ErrorCodeAsync(again)).ShouldBe("BILLING_NOTHING");

        (await CoreHrApi.ErrorCodeAsync(await ReopenAsync(admin, timesheet))).ShouldBe("TIMESHEET_HOURS_BILLED");

        (await FinanceAsync(admin, project)).GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(0m, "a draft has not gone out");
        await ActAsync(admin, project, invoice, "send");
        (await FinanceAsync(admin, project)).GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(WeekOfHours);
    }

    [Fact]
    public async Task CancellingAnInvoice_PutsItsHoursBackUpForBilling()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await ApprovedWeekAsync(project, org.Requester);

        JsonElement invoice = await PrepareWeekAsync(admin, project);
        (await ReadyAsync(admin, project)).ShouldBe(0m);

        await OkAsync(await admin.PostAsync($"/api/v1/project-financials/{project}/invoices/{invoice.GetProperty("id").GetGuid()}/cancel", null, Ct));

        (await ReadyAsync(admin, project)).ShouldBe(WeekOfHours);
        (await PrepareWeekAsync(admin, project)).GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours, "the database's guard on billed hours has let go of them too");
    }

    [Fact]
    public async Task ACreditNote_TakesBackTheHours_WhichCanThenBeReopened()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        Guid timesheet = await ApprovedWeekAsync(project, org.Requester);
        JsonElement invoice = await PrepareWeekAsync(admin, project, tax: 14400m);

        (await CoreHrApi.ErrorCodeAsync(await CreditAsync(admin, project, invoice, new { Reason = "Too soon" }))).ShouldBe("INVOICE_STATE");
        await ActAsync(admin, project, invoice, "send");

        JsonElement note = await OkAsync(await CreditAsync(admin, project, invoice, new { Reason = "Booked to the wrong project" }));
        note.GetProperty("number").GetString()!.ShouldStartWith("CN-");
        note.GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours);
        note.GetProperty("taxAmount").GetDecimal().ShouldBe(14400m);

        JsonElement finance = await FinanceAsync(admin, project);
        finance.GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(0m);
        JsonElement credited = finance.GetProperty("invoices").EnumerateArray().Single();
        credited.GetProperty("creditedAmount").GetDecimal().ShouldBe(WeekOfHours);
        credited.GetProperty("lines").EnumerateArray().Single().GetProperty("isCredited").GetBoolean().ShouldBeTrue();

        (await ReadyAsync(admin, project)).ShouldBe(WeekOfHours);
        (await ReopenAsync(admin, timesheet)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadyAsync(admin, project)).ShouldBe(0m, "reopened hours are no longer approved");

        JsonElement[] notes = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/project-financials/{project}/credit-notes", Ct))!;
        notes.Single().GetProperty("reason").GetString().ShouldBe("Booked to the wrong project");

        (await CoreHrApi.ErrorCodeAsync(await CreditAsync(admin, project, invoice, new { Reason = "Twice", Amount = 1m }))).ShouldBe("INVOICE_CREDIT_AMOUNT");
    }

    [Fact]
    public async Task ACreditNote_ForAnAmount_KeepsTheHoursBilled_AndAnUnknownLineIsRefused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await ApprovedWeekAsync(project, org.Requester);
        JsonElement invoice = await PrepareWeekAsync(admin, project);
        await ActAsync(admin, project, invoice, "send");

        HttpResponseMessage unknown = await CreditAsync(admin, project, invoice, new { Reason = "Oops", LineIds = new[] { Guid.NewGuid() } });
        (await CoreHrApi.ErrorCodeAsync(unknown)).ShouldBe("INVOICE_CREDIT_LINES");
        (await FinanceAsync(admin, project)).GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(WeekOfHours, "a refused credit note changes nothing");

        await OkAsync(await CreditAsync(admin, project, invoice, new { Reason = "Goodwill", Amount = 5000m }));

        (await FinanceAsync(admin, project)).GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(WeekOfHours - 5000m);
        (await ReadyAsync(admin, project)).ShouldBe(0m);
    }

    [Fact]
    public async Task ALineComingOffADraft_LowersItAndFreesItsHours()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await StaffAsync(admin, project, org.Deputy, rate: 1000m);
        await ApprovedWeekAsync(project, org.Requester);
        await ApprovedWeekAsync(project, org.Deputy);

        JsonElement invoice = await PrepareWeekAsync(admin, project);
        invoice.GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours + 40000m);
        Guid id = invoice.GetProperty("id").GetGuid();
        JsonElement[] lines = [.. invoice.GetProperty("lines").EnumerateArray()];
        Guid cheaper = lines.Single(line => line.GetProperty("rate").GetDecimal() == 1000m).GetProperty("id").GetGuid();
        Guid dearer = lines.Single(line => line.GetProperty("rate").GetDecimal() == 2000m).GetProperty("id").GetGuid();

        JsonElement lowered = await OkAsync(await admin.DeleteAsync($"/api/v1/project-financials/{project}/invoices/{id}/lines/{cheaper}", Ct));
        lowered.GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours);
        (await ReadyAsync(admin, project)).ShouldBe(40000m);

        (await CoreHrApi.ErrorCodeAsync(await admin.DeleteAsync($"/api/v1/project-financials/{project}/invoices/{id}/lines/{dearer}", Ct))).ShouldBe("INVOICE_LAST_LINE");

        JsonElement moved = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/invoices/{id}", new
        {
            Details = new { InvoiceDate = Iso(Today), DueDate = Iso(Today.AddDays(45)), Amount = 1m, TaxAmount = 100m },
        }, Ct));
        moved.GetProperty("amount").GetDecimal().ShouldBe(WeekOfHours, "an invoice built from the work adds up its lines");
        moved.GetProperty("taxAmount").GetDecimal().ShouldBe(100m);
    }

    [Fact]
    public async Task TheCap_HoldsBackWhatWouldGoOverIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { Terms = new { NotToExceed = 50000m } }, Ct));
        await ApprovedWeekAsync(project, org.Requester);

        JsonElement preview = await PreviewAsync(admin, project, LastWeek, LastWeek.AddDays(6));

        preview.GetProperty("amount").GetDecimal().ShouldBe(48000m, "three days fit under the cap");
        preview.GetProperty("heldByCap").GetDecimal().ShouldBe(32000m);
    }

    // ----- The other engagement models ------------------------------------------------------------

    [Fact]
    public async Task ARetainerMonth_IsBilledOnce_WithItsOverage()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "Retainer", org.Manager, LastMonth);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { RetainerMonthlyFee = 100000m, Terms = new { IncludedHoursPerMonth = 40m, OverageRate = 2000m } }, Ct));

        DateOnly end = LastMonth.AddMonths(1).AddDays(-1);
        JsonElement invoice = await PrepareAsync(admin, project, LastMonth, end);
        JsonElement line = invoice.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("kind").GetString().ShouldBe("MonthlyFee");
        invoice.GetProperty("amount").GetDecimal().ShouldBe(100000m);

        (await PreviewAsync(admin, project, LastMonth, end)).GetProperty("lines").GetArrayLength().ShouldBe(0);
        (await PreviewAsync(admin, project, LastMonth, Today)).GetProperty("lines").EnumerateArray().Single().GetProperty("kind").GetString().ShouldBe("MonthlyFee", "this month is still to come");
    }

    [Fact]
    public async Task ADedicatedTeam_BillsEachSeatForTheMonth()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "DedicatedTeam", org.Manager, LastMonth);
        await StaffAsync(admin, project, org.Requester, rate: null, monthly: 300000m, start: LastMonth);

        DateOnly end = LastMonth.AddMonths(1).AddDays(-1);
        JsonElement invoice = await PrepareAsync(admin, project, LastMonth, end);

        invoice.GetProperty("amount").GetDecimal().ShouldBe(300000m);
        invoice.GetProperty("lines").EnumerateArray().Single().GetProperty("kind").GetString().ShouldBe("Seat");
        (await PreviewAsync(admin, project, LastMonth, end)).GetProperty("amount").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task AMilestone_CanBeBilledOnlyOnceAccepted()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "Milestone", org.Manager);
        Guid milestone = (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones", new
        {
            Name = "Model in production",
            PlannedDate = Iso(Today),
            IsBillable = true,
            RequiresAcceptance = true,
        }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { MilestoneAmounts = new Dictionary<Guid, decimal> { [milestone] = 500000m } }, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones/{milestone}/achieve", new { On = Iso(Today) }, Ct));

        (await PreviewAsync(admin, project, Today, Today)).GetProperty("amount").GetDecimal().ShouldBe(0m);

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones/{milestone}/accept", new { AcceptedBy = "Client lead", Evidence = "Evaluation report" }, Ct));

        JsonElement invoice = await PrepareAsync(admin, project, Today, Today);
        invoice.GetProperty("amount").GetDecimal().ShouldBe(500000m);
        invoice.GetProperty("milestoneId").GetGuid().ShouldBe(milestone);
        (await PreviewAsync(admin, project, Today, Today)).GetProperty("amount").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task AMilestoneOnAnInvoiceTypedByHand_IsNotOfferedAgain_AndOtherHandInvoicesAreFlagged()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "Milestone", org.Manager);
        Guid milestone = (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones", new { Name = "Design", PlannedDate = Iso(Today), IsBillable = true }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { MilestoneAmounts = new Dictionary<Guid, decimal> { [milestone] = 200000m } }, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones/{milestone}/achieve", new { On = Iso(Today) }, Ct));

        JsonElement typed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices", new
        {
            Details = new { InvoiceDate = Iso(Today), DueDate = Iso(Today.AddDays(30)), Amount = 200000m, TaxAmount = 0m, MilestoneId = milestone },
        }, Ct));

        JsonElement preview = await PreviewAsync(admin, project, Today, Today);
        preview.GetProperty("amount").GetDecimal().ShouldBe(0m);
        preview.GetProperty("typedInvoices").EnumerateArray().Select(row => row.GetString()).ShouldContain(typed.GetProperty("number").GetString());
    }

    // ----- Approval -------------------------------------------------------------------------------

    [Theory]
    [InlineData("approve", "Sent", true)]
    [InlineData("reject", "Draft", false)]
    public async Task WithAWorkflow_AnInvoiceWaitsForItsApprover(string decision, string outcome, bool wentOut)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await ApprovedWeekAsync(project, org.Requester);
        JsonElement invoice = await PrepareWeekAsync(admin, project);

        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Finance", [Rule("Employee", org.Finance)])], subject: "project.invoice");
        await ActAsync(admin, project, invoice, "send");

        JsonElement waiting = (await FinanceAsync(admin, project)).GetProperty("invoices").EnumerateArray().Single();
        waiting.GetProperty("status").GetString().ShouldBe("PendingApproval");
        (await FinanceAsync(admin, project)).GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(0m, "it has not gone out");

        using HttpClient approver = await PersonAsync(factory, org.Finance);
        JsonElement task = (await PendingTasksAsync(approver)).Single(row => row.GetProperty("title").GetString()!.Contains(invoice.GetProperty("number").GetString()!, StringComparison.Ordinal));
        await DecideAsync(approver, await TaskForAsync(approver, task.GetProperty("instanceId").GetGuid()), decision, decision == "reject" ? "Wrong period" : null);
        await factory.DeliverEventsAsync();

        JsonElement finance = await FinanceAsync(admin, project);
        finance.GetProperty("invoices").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe(outcome);
        finance.GetProperty("summary").GetProperty("billed").GetDecimal().ShouldBe(wentOut ? WeekOfHours : 0m);
    }

    // ----- Across the portfolio -------------------------------------------------------------------

    [Fact]
    public async Task TheBillingRun_ListsWhatIsReady_AndWhatIsOverdue()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid ready = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, ready, org.Requester);
        await ApprovedWeekAsync(ready, org.Requester);

        Guid late = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        JsonElement overdue = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{late}/invoices", new
        {
            Details = new { InvoiceDate = Iso(Today.AddDays(-40)), DueDate = Iso(Today.AddDays(-10)), Amount = 1000m, TaxAmount = 0m },
        }, Ct));
        await ActAsync(admin, late, overdue, "send");

        JsonElement run = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/billing/run?month={Iso(LastWeek)}", Ct);

        JsonElement row = run.GetProperty("ready").EnumerateArray().Single(item => item.GetProperty("projectId").GetGuid() == ready);
        row.GetProperty("amount").GetDecimal().ShouldBeGreaterThan(0m);
        JsonElement owed = run.GetProperty("overdue").EnumerateArray().Single(item => item.GetProperty("invoiceId").GetGuid() == overdue.GetProperty("id").GetGuid());
        owed.GetProperty("daysOverdue").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task SomeoneWithoutAccess_CannotSeeBillOrExportAProject()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await ApprovedWeekAsync(project, org.Requester);
        JsonElement invoice = await PrepareWeekAsync(admin, project);
        await ActAsync(admin, project, invoice, "send");

        using HttpClient outsider = await factory.CreateScopedUserClientAsync(Email("bill-out"), org.SkipLevel, (Permissions.Project.FinancialsView, DataScope.Own));

        (await outsider.GetAsync($"/api/v1/project-financials/{project}/billing/preview?from={Iso(LastWeek)}&to={Iso(LastWeek.AddDays(6))}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(LastWeek), To = Iso(LastWeek) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        JsonElement run = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/billing/run?month={Iso(LastWeek)}", Ct);
        run.GetProperty("ready").EnumerateArray().ShouldNotContain(row => row.GetProperty("projectId").GetGuid() == project);

        string csv = await outsider.GetStringAsync($"/api/v1/project-financials/billing/export?from={Iso(Today.AddDays(-2))}&to={Iso(Today.AddDays(2))}", Ct);
        csv.ShouldNotContain(invoice.GetProperty("number").GetString()!);
    }

    [Fact]
    public async Task TheExport_CarriesWhatWentOutAndItsCreditNotes_NotDrafts()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await ProjectOfAsync(admin, "TimeAndMaterial", org.Manager);
        await StaffAsync(admin, project, org.Requester);
        await ApprovedWeekAsync(project, org.Requester);
        JsonElement invoice = await PrepareWeekAsync(admin, project, tax: 14400m);
        string number = invoice.GetProperty("number").GetString()!;
        string range = $"from={Iso(Today.AddDays(-2))}&to={Iso(Today.AddDays(2))}";

        (await admin.GetStringAsync($"/api/v1/project-financials/billing/export?{range}", Ct)).ShouldNotContain(number);

        await ActAsync(admin, project, invoice, "send");
        JsonElement note = await OkAsync(await CreditAsync(admin, project, invoice, new { Reason = "Wrong rate", Amount = 8000m, TaxAmount = 1440m }));

        (await admin.GetAsync($"/api/v1/project-financials/billing/export?{range}&format=tally", Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage tally = await admin.GetAsync($"/api/v1/project-financials/billing/export?{range}&format=tally&company=GGGS", Ct);
        tally.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        string xml = await tally.Content.ReadAsStringAsync(Ct);
        xml.ShouldContain($"<VOUCHERNUMBER>{number}</VOUCHERNUMBER>");
        xml.ShouldContain($"<VOUCHERNUMBER>{note.GetProperty("number").GetString()}</VOUCHERNUMBER>");
        xml.ShouldContain("<SVCURRENTCOMPANY>GGGS</SVCURRENTCOMPANY>");
        xml.ShouldContain("<AMOUNT>-94400.00</AMOUNT>", customMessage: "the client owes the invoice with its tax");

        string csv = await admin.GetStringAsync($"/api/v1/project-financials/billing/export?{range}", Ct);
        csv.ShouldContain($"Against {number}: Wrong rate");
        csv.ShouldContain("-8000.00,-1440.00,-9440.00");
    }
}
