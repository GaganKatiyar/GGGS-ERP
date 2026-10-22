using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.Modules.Separation.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Separation v2 phase D end to end: a settlement worked out with the policy's leave encashment and
/// statutory deadlines, approved and paid through an off-cycle payroll run - salary to the last day
/// with PF and TDS, terminal benefits as inputs - that the month's regular run leaves out, whose
/// payslip and payment come back to the case, and whose month lists the exit for the EPF portal.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class FinalSettlementPayrollTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Month = "2025-02-01";

    private static readonly DateOnly LastDay = new(2025, 2, 20);

    private static JsonElement Line(JsonElement lines, string code) => lines.EnumerateArray().First(line => line.GetProperty("code").GetString() == code);

    private static bool Has(JsonElement lines, string code) => lines.EnumerateArray().Any(line => line.GetProperty("code").GetString() == code);

    private static async Task<JsonElement> RunItemAsync(HttpClient admin, Guid runId)
    {
        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items?pageSize=500", Ct))!;
        Guid itemId = page.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        return (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items/{itemId}", Ct))!;
    }

    [Fact]
    public async Task An_approved_settlement_is_paid_through_an_off_cycle_payroll_run()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        (await admin.PutAsJsonAsync($"/api/v1/employees/{person}/statutory", new { Pan = "ABCDE1234F", Uan = "100200300777", ProfessionalTaxStateCode = "IN-KA" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{person}/bank", new { BankName = "HDFC Bank", Ifsc = "HDFC0001234", AccountHolder = "Leaver", AccountNumber = "50100111222333" }, Ct)).EnsureSuccessStatusCode();
        Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!.First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{person}/salary", new { StructureId = structure, AnnualCtc = 840_000m, EffectiveFrom = "2024-01-15", Reason = "Hire" }, Ct));
        Guid earned = await TimeApi.LeaveTypeAsync(admin, "EL");
        await TimeApi.GrantAsync(admin, person, earned, 5);

        // A mutual separation that ended on 20 February 2025, clearance done.
        JsonElement opened = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
        {
            EmployeeId = person,
            Type = "Mutual",
            Reason = "Agreed exit",
            NoticeDate = "2025-01-21",
            LastWorkingDay = Iso(LastDay),
        }, Ct));
        Guid caseId = opened.GetProperty("case").GetProperty("id").GetGuid();
        opened.GetProperty("case").GetProperty("settlementDueOn").GetString().ShouldBe("2025-02-22", "two working days: Friday 21 and Saturday 22");
        opened.GetProperty("case").GetProperty("gratuityDueOn").GetString().ShouldBe("2025-03-22");
        foreach (JsonElement item in opened.GetProperty("clearance").EnumerateArray())
        {
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/clearance/{item.GetProperty("id").GetGuid()}", new { Status = "Cleared" }, Ct));
        }

        JsonElement calculated = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
        JsonElement lines = calculated.GetProperty("settlement").GetProperty("lines");
        Line(lines, "LEAVE_ENCASH").GetProperty("basis").GetString()!.ShouldStartWith("5 days (EL) x basic/26");
        calculated.GetProperty("case").GetProperty("settlementOverdue").GetBoolean().ShouldBeTrue("the deadline passed long ago");

        // Approval sends it to payroll; a reopen takes the run back, and approving again makes a new one.
        JsonElement approved = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/approve", null, Ct));
        Guid firstRun = approved.GetProperty("settlement").GetProperty("payrollRunId").GetGuid();
        approved.GetProperty("settlement").GetProperty("payrollStatus").GetString().ShouldBe("Calculated");
        approved.GetProperty("settlement").GetProperty("payrollSalaryIncluded").GetBoolean().ShouldBeTrue();
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync($"/api/v1/separation/cases/{caseId}/settlement/pay", new { Reference = "NEFT" }, Ct))).ShouldBe("SETTLEMENT_IN_PAYROLL");
        await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/reopen", null, Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{firstRun}", Ct)).GetProperty("run").GetProperty("status").GetString().ShouldBe("Cancelled");
        Guid runId = (await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/approve", null, Ct))).GetProperty("settlement").GetProperty("payrollRunId").GetGuid();
        runId.ShouldNotBe(firstRun);

        // The run: salary to the 20th with PF and TDS, leave encashment tax-free.
        JsonElement run = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}", Ct)).GetProperty("run");
        run.GetProperty("kind").GetString().ShouldBe("FullAndFinal");
        run.GetProperty("period").GetString().ShouldBe(Month);
        JsonElement result = await RunItemAsync(admin, runId);
        result.GetProperty("paidDays").GetDecimal().ShouldBe(20m);
        JsonElement paid = result.GetProperty("lines");
        Has(paid, "BASIC").ShouldBeTrue();
        Line(paid, "LEAVE_ENCASH").GetProperty("taxable").GetBoolean().ShouldBeFalse();

        // The month's regular payroll leaves the leaver to the settlement run.
        JsonElement regular = await OkAsync(await admin.PostAsJsonAsync("/api/v1/payroll/runs", new { Period = Month }, Ct));
        Guid regularId = regular.GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{regularId}/calculate", null, Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{regularId}/items?pageSize=500", Ct))!.GetProperty("items").EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("employeeId").GetGuid() == person);
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{regularId}/cancel", null, Ct));

        // Approved, finalized (payslip) and paid in payroll: the settlement follows.
        (await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/finalize", null, Ct));
        await factory.DeliverEventsAsync();
        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/employees/{person}/payslips", Ct))!.ShouldNotBeEmpty();
        (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("events").EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString()).ShouldContain("settlement-payslip");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{runId}/paid", new { PaidOn = "2025-02-24", Reference = "NEFT-FNF-77" }, Ct));
        await factory.DeliverEventsAsync();
        JsonElement settled = (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("settlement");
        settled.GetProperty("status").GetString().ShouldBe("Paid");
        settled.GetProperty("paymentReference").GetString().ShouldBe("NEFT-FNF-77");
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/reopen", null, Ct))).ShouldBe("SETTLEMENT_PAYROLL_FINAL", "a finalized payroll run cannot be taken back");

        // Once the exit takes effect, the month's EPF exit list names the member (C: cessation).
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<SeparationService>().SweepAsync(Ct));
        HttpResponseMessage exits = await admin.GetAsync($"/api/v1/payroll/runs/{runId}/reports/epf-exits", Ct);
        exits.EnsureSuccessStatusCode();
        string csv = Encoding.UTF8.GetString(await exits.Content.ReadAsByteArrayAsync(Ct));
        csv.ShouldContain("100200300777");
        csv.ShouldContain("20/02/2025");
        csv.TrimEnd().Split('\n').Last().TrimEnd('\r').ShouldEndWith("C");
    }

    [Fact]
    public async Task The_statutory_bonus_and_exemption_rules_are_seeded_as_versioned_rules()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement[] rules = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/compliance/rules", Ct))!;
        JsonElement bonus = rules.Single(rule => rule.GetProperty("code").GetString() == "IN-BONUS").GetProperty("current").GetProperty("configuration");
        bonus.GetProperty("eligibilityWageCeiling").GetDecimal().ShouldBe(21000m);
        bonus.GetProperty("payablePercent").GetDecimal().ShouldBe(8.33m);
        rules.Single(rule => rule.GetProperty("code").GetString() == "IN-TERMINAL-EXEMPT").GetProperty("current").GetProperty("configuration")
            .GetProperty("leaveEncashment").GetDecimal().ShouldBe(2_500_000m);
    }
}
