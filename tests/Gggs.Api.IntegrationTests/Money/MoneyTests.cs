using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.ProjectFinancials.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M7 end to end: versioned statutory rules, the payroll cycle from salary to bank file, tax
/// declarations, benefits, compensation feeding payroll, and project financials.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class MoneyTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Hires someone paid through payroll: statutory IDs, a Karnataka PT state and a bank account.</summary>
    private static async Task<Guid> PayeeAsync(HttpClient admin, Guid? gradeId = null)
    {
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        JsonElement hired = await OkAsync(await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Pay",
            LastName = CoreHrApi.Unique("Ee"),
            OfficialEmail = Email("payee"),
            JoiningDate = "2024-01-15",
            Assignment = new
            {
                DepartmentId = department,
                EmploymentTypeCode = "FULL_TIME",
                WorkModeCode = "HYBRID",
                Designation = "Engineer",
                GradeId = gradeId,
            },
        }, Ct), HttpStatusCode.Created);
        Guid id = hired.GetProperty("id").GetGuid();

        (await admin.PutAsJsonAsync($"/api/v1/employees/{id}/statutory", new { Pan = "ABCDE1234F", Uan = "100200300400", ProfessionalTaxStateCode = "IN-KA" }, Ct))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/v1/employees/{id}/bank", new { BankName = "HDFC Bank", Ifsc = "HDFC0001234", AccountHolder = "Pay Ee", AccountNumber = "50100123456789" }, Ct))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        return id;
    }

    private static async Task<Guid> StandardStructureAsync(HttpClient admin)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!
            .First(structure => structure.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();

    private static async Task AssignSalaryAsync(HttpClient admin, Guid employeeId, decimal ctc, string from = "2024-01-15")
        => await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{employeeId}/salary", new
        {
            StructureId = await StandardStructureAsync(admin),
            AnnualCtc = ctc,
            EffectiveFrom = from,
            Reason = "Hire",
        }, Ct));

    private static decimal Line(JsonElement item, string code)
        => item.GetProperty("lines").EnumerateArray().Where(line => line.GetProperty("code").GetString() == code).Sum(line => line.GetProperty("amount").GetDecimal());

    /// <summary>Opens (or reuses) the run for a month, calculates it and returns the run id.</summary>
    private static async Task<Guid> CalculatedRunAsync(HttpClient admin, string period)
    {
        JsonElement[] runs = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/runs", Ct))!;
        JsonElement existing = runs.FirstOrDefault(run => run.GetProperty("period").GetString() == period && run.GetProperty("status").GetString() != "Cancelled");
        Guid id = existing.ValueKind == JsonValueKind.Object
            ? existing.GetProperty("id").GetGuid()
            : (await OkAsync(await admin.PostAsJsonAsync("/api/v1/payroll/runs", new { Period = period }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{id}/calculate", null, Ct));
        return id;
    }

    /// <summary>Holds everyone in the run but <paramref name="keep"/>, so other tests' people cannot block it.</summary>
    private static async Task HoldOthersAsync(HttpClient admin, Guid runId, params Guid[] keep)
    {
        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items?pageSize=500", Ct))!;
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray().Where(item => !keep.Contains(item.GetProperty("employeeId").GetGuid()) && !item.GetProperty("onHold").GetBoolean()))
        {
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{runId}/items/{item.GetProperty("id").GetGuid()}/hold", new { Reason = "Not part of this test" }, Ct));
        }
    }

    private static async Task<JsonElement> ItemAsync(HttpClient admin, Guid runId, Guid employeeId)
    {
        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items?pageSize=500", Ct))!;
        Guid itemId = page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("employeeId").GetGuid() == employeeId).GetProperty("id").GetGuid();
        return (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items/{itemId}", Ct))!;
    }

    private static async Task FinalizeAsync(HttpClient admin, Guid runId)
    {
        JsonElement submitted = await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/submit", null, Ct));
        submitted.GetProperty("status").GetString().ShouldBe("Approved", "no approval workflow is defined for payroll runs");
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/finalize", null, Ct));
    }

    [Fact]
    public async Task StatutoryRules_AreSeeded_Versioned_AndResolvedByDate()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();

        JsonElement[] rules = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/compliance/rules", Ct))!;
        JsonElement esi = rules.Single(rule => rule.GetProperty("code").GetString() == "IN-ESI");
        JsonElement current = esi.GetProperty("current");
        current.GetProperty("configuration").GetProperty("wageThreshold").GetDecimal().ShouldBe(21000m);
        rules.ShouldContain(rule => rule.GetProperty("code").GetString() == "IN-KA-PT");

        // A published version cannot change; a new version from a later date supersedes it.
        Guid v1 = current.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/compliance/rules/{v1}", new
        {
            Name = "ESI",
            EffectiveFrom = "2019-07-01",
            Configuration = new { EmployeeRate = 1m, EmployerRate = 3m, WageThreshold = 21000m },
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        DateOnly nextYear = new(Today.Year + 1, 4, 1);
        JsonElement draft = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/compliance/rules/{v1}/versions", new
        {
            EffectiveFrom = Iso(nextYear),
            Configuration = new { EmployeeRate = 0.75m, EmployerRate = 3.25m, WageThreshold = 25000m },
            Notes = "Threshold raised",
        }, Ct));
        draft.GetProperty("version").GetInt32().ShouldBe(2);
        draft.GetProperty("status").GetString().ShouldBe("Draft");

        // A malformed configuration never reaches a published rule.
        (await admin.PutAsJsonAsync($"/api/v1/compliance/rules/{draft.GetProperty("id").GetGuid()}", new
        {
            Name = "ESI",
            EffectiveFrom = Iso(nextYear),
            Configuration = new { EmployeeRate = 140m, EmployerRate = 3.25m, WageThreshold = 25000m },
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await OkAsync(await admin.PostAsync($"/api/v1/compliance/rules/{draft.GetProperty("id").GetGuid()}/publish", null, Ct));
        JsonElement[] versions = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/compliance/rules/codes/IN-ESI", Ct))!;
        versions.Single(version => version.GetProperty("version").GetInt32() == 1).GetProperty("effectiveTo").GetString().ShouldBe(Iso(nextYear.AddDays(-1)));

        JsonElement today = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/compliance/rules/resolve?type=StateInsurance&on={Iso(Today)}", Ct))!;
        today.GetProperty("version").GetInt32().ShouldBe(1);
        JsonElement later = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/compliance/rules/resolve?type=StateInsurance&on={Iso(nextYear)}", Ct))!;
        later.GetProperty("version").GetInt32().ShouldBe(2);

        // Professional tax resolves by state.
        JsonElement pt = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/compliance/rules/resolve?type=ProfessionalTax&state=IN-KA&on={Iso(Today)}", Ct))!;
        pt.GetProperty("code").GetString().ShouldBe("IN-KA-PT");
    }

    [Fact]
    public async Task ComplianceCalendar_GeneratesFilings_AndTracksRegistrations()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        await OkAsync(await admin.PostAsync("/api/v1/compliance/filings/generate", null, Ct));

        JsonElement[] filings = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/compliance/filings?status=Pending", Ct))!;
        JsonElement ecr = filings.First(filing => filing.GetProperty("kind").GetString() == "ProvidentFundReturn");
        ecr.GetProperty("dueDate").GetString()!.ShouldEndWith("-15");

        JsonElement filed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/compliance/filings/{ecr.GetProperty("id").GetGuid()}/file", new
        {
            FiledOn = Iso(Today),
            Reference = "TRRN-12345",
            Amount = 45000m,
        }, Ct));
        filed.GetProperty("status").GetString().ShouldBe("Filed");
        (await admin.PostAsJsonAsync($"/api/v1/compliance/filings/{ecr.GetProperty("id").GetGuid()}/file", new { FiledOn = Iso(Today) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        JsonElement registration = await OkAsync(await admin.PostAsJsonAsync("/api/v1/compliance/registrations", new
        {
            Kind = "License",
            Name = "Shops and establishments - " + CoreHrApi.Unique("S"),
            Number = "SE/2024/123",
            IssuedOn = Iso(Today.AddYears(-1)),
            ExpiresOn = Iso(Today.AddDays(10)),
            RenewalLeadDays = 30,
        }, Ct));
        registration.GetProperty("validity").GetString().ShouldBe("ExpiringSoon");

        JsonElement dashboard = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/compliance/dashboard", Ct))!;
        dashboard.GetProperty("rulesInForce").GetInt32().ShouldBeGreaterThanOrEqualTo(8);
        dashboard.GetProperty("expiringRegistrations").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SalaryPreview_LaysOutTheCtc_WithPfGratuityAndProfessionalTax()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid structure = await StandardStructureAsync(admin);

        JsonElement preview = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/structures/{structure}/preview", new { AnnualCtc = 600_000m, StateCode = "IN-KA", Regime = "New" }, Ct));
        decimal Monthly(string code) => preview.GetProperty("lines").EnumerateArray().Where(line => line.GetProperty("code").GetString() == code).Sum(line => line.GetProperty("monthly").GetDecimal());

        Monthly("BASIC").ShouldBe(20000m);
        Monthly("HRA").ShouldBe(10000m);
        Monthly("PF_ER").ShouldBe(1800m, "12% of the 15,000 wage ceiling");
        Monthly("GRATUITY").ShouldBe(962m, "4.81% of basic");
        Monthly("SPECIAL").ShouldBe(50000m - 20000m - 10000m - 1800m - 962m);
        Monthly("ESI_ER").ShouldBe(0m, "gross is above the ESI threshold");
        Monthly("PF_EE").ShouldBe(1800m);
        Monthly("PT").ShouldBe(200m);
        Monthly("TDS").ShouldBe(0m, "income under 12 lakh pays no tax after the rebate in the new regime");
        preview.GetProperty("monthlyTakeHome").GetDecimal().ShouldBe(47238m - 1800m - 200m);
    }

    [Fact]
    public async Task PayrollRun_CalculatesApprovesFinalizes_PublishesPayslips_AndProducesFiles()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await PayeeAsync(admin);
        await factory.DeliverEventsAsync();
        await AssignSalaryAsync(admin, employee, 1_800_000m);

        const string period = "2025-05-01";
        JsonElement component = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/components", Ct))!.First(row => row.GetProperty("code").GetString() == "BONUS");
        await OkAsync(await admin.PostAsJsonAsync("/api/v1/payroll/inputs", new
        {
            EmployeeId = employee,
            Period = period,
            ComponentId = component.GetProperty("id").GetGuid(),
            Amount = 10000m,
            Description = "Spot bonus",
        }, Ct));

        Guid run = await CalculatedRunAsync(admin, period);
        JsonElement item = await ItemAsync(admin, run, employee);
        Line(item, "BASIC").ShouldBe(60000m, "40% of a 1.5 lakh monthly CTC");
        Line(item, "BONUS").ShouldBe(10000m);
        Line(item, "PF_EE").ShouldBe(1800m);
        Line(item, "PT").ShouldBe(200m);
        Line(item, "TDS").ShouldBeGreaterThan(0m);
        Line(item, "BENER_GHI").ShouldBe(800m, "group health insurance was enrolled automatically on joining");
        item.GetProperty("rules").EnumerateArray().Select(rule => rule.GetProperty("code").GetString()).ShouldContain("IN-EPF");
        item.GetProperty("tax").GetProperty("regime").GetString().ShouldBe("New");

        await HoldOthersAsync(admin, run, employee);
        await FinalizeAsync(admin, run);

        // Finalized means fixed.
        (await admin.PostAsync($"/api/v1/payroll/runs/{run}/calculate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{employee}/salary", new
        {
            StructureId = await StandardStructureAsync(admin),
            AnnualCtc = 2_000_000m,
            EffectiveFrom = "2025-05-10",
            Reason = "Correction",
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("payee-self"), employee, (Permissions.Self.PayslipView, DataScope.Own));
        JsonElement[] payslips = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/payslips", Ct))!;
        JsonElement payslip = payslips.Single(row => row.GetProperty("period").GetString() == period);
        payslip.GetProperty("net").GetDecimal().ShouldBe(item.GetProperty("net").GetDecimal());
        JsonElement document = (await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/payslips/{payslip.GetProperty("id").GetGuid()}", Ct))!.GetProperty("document");
        document.GetProperty("employee").GetProperty("bankAccount").GetString()!.ShouldNotContain("50100123456789", Case.Sensitive, "payslips show the account masked");
        document.GetProperty("netInWords").GetString()!.ShouldStartWith("Rupees ");

        string bankFile = await admin.GetStringAsync($"/api/v1/payroll/runs/{run}/reports/bank-file", Ct);
        bankFile.ShouldContain("50100123456789");
        string ecr = await admin.GetStringAsync($"/api/v1/payroll/runs/{run}/reports/ecr", Ct);
        ecr.ShouldContain("100200300400#~#");
        string journal = await admin.GetStringAsync($"/api/v1/payroll/runs/{run}/reports/journal", Ct);
        journal.ShouldContain("Salaries payable (net pay)");

        JsonElement paid = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{run}/paid", new { PaidOn = "2025-05-31", Reference = "NEFT-BATCH-1" }, Ct));
        paid.GetProperty("status").GetString().ShouldBe("Paid");

        JsonElement statement = (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/tax-statement?year=2025", Ct))!;
        statement.GetProperty("tdsDeducted").GetDecimal().ShouldBe(Line(item, "TDS"));
    }

    [Fact]
    public async Task TaxDeclaration_OldRegimeWithDeductions_IsComparedAndVerified()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await PayeeAsync(admin);
        await AssignSalaryAsync(admin, employee, 1_200_000m);
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("declarer"), employee, (Permissions.Self.TaxDeclarationManage, DataScope.Own));

        JsonElement mine = (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/tax-declaration", Ct))!;
        int year = mine.GetProperty("financialYear").GetInt32();
        mine.GetProperty("regime").GetString().ShouldBe("New");

        // The new regime allows none of these deductions.
        (await self.PutAsJsonAsync($"/api/v1/me/tax-declaration/{year}", new
        {
            Regime = "New",
            Items = new[] { new { Section = "80C", Description = "PPF", Declared = 150000m } },
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        JsonElement saved = await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/tax-declaration/{year}", new
        {
            Regime = "Old",
            Items = new[] { new { Section = "80C", Description = "PPF", Declared = 150000m }, new { Section = "80D", Description = "Health insurance", Declared = 40000m } },
            MonthlyRent = 25000m,
            MetroCity = true,
        }, Ct));
        JsonElement comparison = saved.GetProperty("comparison");
        comparison.GetProperty("oldRegimeTax").GetDecimal().ShouldBeGreaterThan(0m);
        comparison.GetProperty("newRegimeTax").GetDecimal().ShouldBe(0m, "12 lakh is within the new regime's rebate");

        JsonElement submitted = await OkAsync(await self.PostAsync($"/api/v1/me/tax-declaration/{year}/submit", null, Ct));
        submitted.GetProperty("status").GetString().ShouldBe("Submitted");

        JsonElement verified = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/tax-declarations/{submitted.GetProperty("id").GetGuid()}/verify", new
        {
            Accepted = new[] { 150000m, 25000m },
            Notes = "80D capped at the self limit",
        }, Ct));
        verified.GetProperty("status").GetString().ShouldBe("Verified");
        verified.GetProperty("items")[1].GetProperty("verified").GetDecimal().ShouldBe(25000m);

        (await self.PutAsJsonAsync($"/api/v1/me/tax-declaration/{year}", new { Regime = "Old" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Benefits_AutoEnrollJoiners_AndSelfEnrollmentCostsAppear()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid employee = await PayeeAsync(admin);
        await factory.DeliverEventsAsync();

        JsonElement[] enrolled = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/benefits/employees/{employee}", Ct))!;
        enrolled.Select(row => row.GetProperty("planCode").GetString()).ShouldBe(["GHI", "GTL", "GPA"], ignoreOrder: true);

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("benefits"), employee, (Permissions.Self.BenefitManage, DataScope.Own));
        JsonElement mine = (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/benefits", Ct))!;
        JsonElement wellness = mine.GetProperty("plans").EnumerateArray().Single(plan => plan.GetProperty("plan").GetProperty("code").GetString() == "WELLNESS");
        wellness.TryGetProperty("ineligibility", out JsonElement reason).ShouldBeFalse(reason.ToString());

        JsonElement joined = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/benefits", new { PlanId = wellness.GetProperty("plan").GetProperty("id").GetGuid() }, Ct));
        joined.GetProperty("status").GetString().ShouldBe("Active");
        joined.GetProperty("employeeMonthly").GetDecimal().ShouldBe(200m);

        // Mandatory cover cannot be left.
        Guid health = enrolled.Single(row => row.GetProperty("planCode").GetString() == "GHI").GetProperty("id").GetGuid();
        (await self.PostAsync($"/api/v1/me/benefits/{health}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        JsonElement[] plans = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/benefits/plans", Ct))!;
        plans.Single(plan => plan.GetProperty("code").GetString() == "GHI").GetProperty("enrolled").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Compensation_RevisionAppliesToPayroll_VariablePayIsPaidThroughPayroll_AndEquityVests()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid grade = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/organizations/grades", new { Code = CoreHrApi.Unique("G"), Name = "Senior engineer", Rank = 5 }, Ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Guid employee = await PayeeAsync(admin, grade);
        await AssignSalaryAsync(admin, employee, 1_000_000m);

        JsonElement band = await OkAsync(await admin.PostAsJsonAsync("/api/v1/compensation/bands", new
        {
            GradeId = grade,
            Minimum = 900_000m,
            Midpoint = 1_200_000m,
            Maximum = 1_500_000m,
            EffectiveFrom = "2024-01-01",
        }, Ct));
        band.GetProperty("employees").GetInt32().ShouldBe(1);
        band.GetProperty("averageCompaRatio").GetDecimal().ShouldBe(0.83m);

        // A merit revision with no approval workflow defined is approved and applied at once.
        JsonElement revision = await OkAsync(await admin.PostAsJsonAsync("/api/v1/compensation/revisions", new
        {
            EmployeeId = employee,
            Details = new { Type = "Merit", ProposedCtc = 1_150_000m, EffectiveDate = "2026-04-01", Justification = "Strong year" },
            Submit = true,
        }, Ct));
        revision.GetProperty("status").GetString().ShouldBe("Applied");
        revision.GetProperty("increasePercent").GetDecimal().ShouldBe(15m);
        revision.GetProperty("compaRatioAfter").GetDecimal().ShouldBe(0.96m);

        JsonElement salary = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/employees/{employee}/salary", Ct))!;
        salary.GetProperty("current").GetProperty("annualCtc").GetDecimal().ShouldBe(1_150_000m);
        salary.GetProperty("current").GetProperty("source").GetString().ShouldBe("Compensation");

        // Variable pay goes to payroll and is marked paid when the run is finalized.
        JsonElement award = await OkAsync(await admin.PostAsJsonAsync("/api/v1/compensation/variable-pay", new
        {
            EmployeeId = employee,
            Type = "PerformanceBonus",
            Amount = 50000m,
            PayoutMonth = "2025-06-01",
            Reason = "FY25 performance",
        }, Ct));
        award.GetProperty("status").GetString().ShouldBe("Approved");

        Guid run = await CalculatedRunAsync(admin, "2025-06-01");
        Line(await ItemAsync(admin, run, employee), "BONUS").ShouldBe(50000m);
        await HoldOthersAsync(admin, run, employee);
        await FinalizeAsync(admin, run);
        await factory.DeliverEventsAsync();

        JsonElement[] awards = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/compensation/variable-pay?employeeId={employee}", Ct))!;
        awards.Single().GetProperty("status").GetString().ShouldBe("Paid");

        // Equity: four years monthly after a one-year cliff.
        DateOnly start = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-24);
        JsonElement grant = await OkAsync(await admin.PostAsJsonAsync("/api/v1/compensation/equity", new
        {
            EmployeeId = employee,
            Details = new { PlanName = "ESOP 2024", GrantDate = Iso(start), Units = 4800m, StrikePrice = 10m, FairValuePerUnit = 60m, VestingMonths = 48, CliffMonths = 12, VestingFrequencyMonths = 1 },
        }, Ct));
        grant.GetProperty("vestedUnits").GetDecimal().ShouldBe(2400m);
        grant.GetProperty("vestedValue").GetDecimal().ShouldBe(2400m * 50m);
        (await admin.PostAsJsonAsync($"/api/v1/compensation/equity/{grant.GetProperty("id").GetGuid()}/exercise", new { Units = 3000m }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("rewards"), employee, (Permissions.Self.CompensationView, DataScope.Own));
        JsonElement rewards = (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/compensation", Ct))!;
        rewards.GetProperty("annualCtc").GetDecimal().ShouldBe(1_150_000m);
        rewards.GetProperty("band").GetProperty("position").GetString().ShouldBe("Within");
        rewards.GetProperty("grants").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ProjectFinancials_CostRevenueAndMargin_FromTimesheetsRatesCostsAndInvoices()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-60), end: Today.AddDays(30)))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/team", new
        {
            EmployeeId = org.Requester,
            ProjectRoleId = await RoleAsync(admin, "DEV"),
            StartDate = Iso(Today.AddDays(-45)),
            AllocationPercent = 100,
            Billable = true,
            BillingRate = 2500m,
            CostRate = 1200m,
        }, Ct));

        // A personal rate card line beats the team rate (2,500 / 1,200 per hour).
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/rates", new
        {
            EmployeeId = org.Requester,
            BillingRate = 2000m,
            CostRate = 1000m,
            EffectiveFrom = Iso(Today.AddDays(-60)),
        }, Ct));

        DateOnly monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)).AddDays(-7);
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("fin-self"), org.Requester, (Permissions.Self.TimesheetSubmit, DataScope.Own));
        await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Iso(monday)}", new
        {
            Entries = Enumerable.Range(0, 5).Select(offset => new { Date = Iso(monday.AddDays(offset)), ProjectId = project, Category = "Billable", Hours = 8m }).ToArray(),
        }, Ct));
        (await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Iso(monday)}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/costs", new
        {
            Category = "Vendor",
            Date = Iso(Today.AddDays(-3)),
            Amount = 5000m,
            Vendor = "Cloud Co",
            Description = "Test environment",
        }, Ct));
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/budget", new
        {
            Lines = new[] { new { Category = "Labour", Amount = 400_000m }, new { Category = "Vendor", Amount = 20_000m } },
        }, Ct));
        JsonElement invoice = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices", new
        {
            Details = new { InvoiceDate = Iso(Today), DueDate = Iso(Today.AddDays(30)), Amount = 50_000m, TaxAmount = 9_000m },
        }, Ct));
        invoice.GetProperty("number").GetString()!.ShouldStartWith("INV-");
        await OkAsync(await admin.PostAsync($"/api/v1/project-financials/{project}/invoices/{invoice.GetProperty("id").GetGuid()}/send", null, Ct));

        JsonElement finance = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct))!;
        JsonElement summary = finance.GetProperty("summary");
        summary.GetProperty("labourCost").GetDecimal().ShouldBe(40m * 1000m);
        summary.GetProperty("revenue").GetDecimal().ShouldBe(40m * 2000m);
        summary.GetProperty("otherCost").GetDecimal().ShouldBe(5000m);
        summary.GetProperty("grossMargin").GetDecimal().ShouldBe(80_000m - 45_000m);
        summary.GetProperty("billed").GetDecimal().ShouldBe(50_000m);
        summary.GetProperty("unbilled").GetDecimal().ShouldBe(30_000m);
        summary.GetProperty("budget").GetDecimal().ShouldBe(420_000m);
        summary.GetProperty("forecastCost").GetDecimal().ShouldBeGreaterThan(45_000m, "the team stays allocated until the project ends");

        JsonElement portfolio = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/project-financials", Ct))!;
        portfolio.GetProperty("projects").EnumerateArray().ShouldContain(row => row.GetProperty("projectId").GetGuid() == project);

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<FinanceService>().SnapshotAsync(Ct));
        JsonElement after = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct))!;
        after.GetProperty("trend").GetArrayLength().ShouldBe(1);

        // Someone outside the project sees none of it.
        using HttpClient member = await MemberAsync(factory, org.Finance);
        (await member.GetAsync($"/api/v1/project-financials/{project}", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
