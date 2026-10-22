using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Employee loans end to end: an application checked against the type's limits and approved, the
/// loan paid out by transfer, its first instalment deducted by a finalized payroll run, then a part
/// prepayment that shrinks the instalment and a full one that closes the loan. The loan's months
/// (2021) belong to no other test, so its payroll runs are its own.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class LoanTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static decimal Line(JsonElement item, string code)
        => item.GetProperty("lines").EnumerateArray().Where(line => line.GetProperty("code").GetString() == code).Sum(line => line.GetProperty("amount").GetDecimal());

    private static async Task<Guid> HireLongServingAsync(HttpClient admin)
    {
        object hire = new
        {
            FirstName = "Loan",
            LastName = $"Taker{Guid.NewGuid():N}"[..14],
            JoiningDate = "2020-01-15",
            Assignment = new { DepartmentId = await CoreHrApi.DepartmentAsync(admin), EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Engineer" },
        };
        JsonElement hired = await OkAsync(await admin.PostAsJsonAsync("/api/v1/employees", hire, Ct), HttpStatusCode.Created);
        Guid person = hired.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{person}/statutory", new { Pan = "ABCDE1234F", Uan = "100200300888", ProfessionalTaxStateCode = "IN-KA" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{person}/bank", new { BankName = "HDFC Bank", Ifsc = "HDFC0001234", AccountHolder = "Loan Taker", AccountNumber = "50100111222444" }, Ct)).EnsureSuccessStatusCode();
        Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!.First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{person}/salary", new { StructureId = structure, AnnualCtc = 840_000m, EffectiveFrom = "2020-01-15", Reason = "Hire" }, Ct));
        return person;
    }

    /// <summary>Calculates and finalizes a month's run with everyone but <paramref name="person"/> on hold, and returns their item.</summary>
    private static async Task<JsonElement> PayMonthAsync(PeopleOsApiFactory factory, HttpClient admin, string period, Guid person)
    {
        Guid runId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/payroll/runs", new { Period = period }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/calculate", null, Ct));
        JsonElement page = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items?pageSize=500", Ct))!;
        Guid itemId = Guid.Empty;
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("employeeId").GetGuid() == person)
            {
                itemId = item.GetProperty("id").GetGuid();
            }
            else if (!item.GetProperty("onHold").GetBoolean())
            {
                await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{runId}/items/{item.GetProperty("id").GetGuid()}/hold", new { Reason = "Not part of this test" }, Ct));
            }
        }

        JsonElement mine = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/payroll/runs/{runId}/items/{itemId}", Ct))!;
        (await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/finalize", null, Ct));
        await factory.DeliverEventsAsync();
        return mine;
    }

    [Fact]
    public async Task A_loan_is_approved_paid_out_deducted_by_payroll_and_prepaid()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await HireLongServingAsync(admin);
        using HttpClient self = await factory.CreateScopedUserClientAsync($"loan-{Guid.NewGuid():N}@example.test", person, (Permissions.Self.LoanApply, DataScope.Own));

        // The personal loan is open to someone with six years' service, up to three months' gross.
        JsonElement mine = (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/loans", Ct))!;
        JsonElement offer = mine.GetProperty("offers").EnumerateArray().Single(row => row.GetProperty("type").GetProperty("code").GetString() == "PERSONAL");
        offer.TryGetProperty("problem", out _).ShouldBeFalse("nothing stands in the way");
        offer.GetProperty("limit").GetDecimal().ShouldBeGreaterThan(150_000m);
        Guid personal = offer.GetProperty("type").GetProperty("id").GetGuid();

        JsonElement quote = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/loans/quote", new { TypeId = personal, Amount = 90_000m, TenureMonths = 6 }, Ct));
        quote.GetProperty("emi").GetDecimal().ShouldBe(15_352m);
        quote.GetProperty("schedule").GetArrayLength().ShouldBe(6);
        (await CoreHrApi.ErrorCodeAsync(await self.PostAsJsonAsync("/api/v1/me/loans", new { TypeId = personal, Amount = 5_000_000m, TenureMonths = 12, Purpose = "Too much" }, Ct))).ShouldBe("LOAN_INELIGIBLE");

        // No approval chain is defined, so the application is approved as it is made.
        JsonElement applied = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/loans", new { TypeId = personal, Amount = 90_000m, TenureMonths = 6, Purpose = "Home repairs" }, Ct));
        Guid loanId = applied.GetProperty("loan").GetProperty("id").GetGuid();
        applied.GetProperty("loan").GetProperty("status").GetString().ShouldBe("Approved");
        (await CoreHrApi.ErrorCodeAsync(await self.PostAsJsonAsync("/api/v1/me/loans", new { TypeId = personal, Amount = 10_000m, TenureMonths = 6, Purpose = "Another" }, Ct))).ShouldBe("LOAN_INELIGIBLE");
        (await self.PostAsJsonAsync($"/api/v1/loans/{loanId}/disburse", new { Mode = "Bank", Reference = "UTR-1" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Paid out by transfer in January 2021; six instalments from February, all sent to payroll.
        JsonElement disbursed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/loans/{loanId}/disburse", new { Mode = "Bank", Reference = "UTR-2021-01", DisbursedOn = "2021-01-10" }, Ct));
        disbursed.GetProperty("loan").GetProperty("status").GetString().ShouldBe("Active");
        disbursed.GetProperty("loan").GetProperty("firstEmiMonth").GetString().ShouldBe("2021-02-01");
        JsonElement[] schedule = [.. disbursed.GetProperty("schedule").EnumerateArray()];
        schedule.Length.ShouldBe(6);
        schedule.Sum(row => row.GetProperty("principal").GetDecimal()).ShouldBe(90_000m);
        schedule.ShouldAllBe(row => row.GetProperty("raised").GetBoolean());
        schedule[0].GetProperty("interest").GetDecimal().ShouldBe(600m);

        // February's payroll deducts the first instalment; the balance comes down by its principal.
        JsonElement item = await PayMonthAsync(factory, admin, "2021-02-01", person);
        Line(item, "LOAN_EMI").ShouldBe(15_352m);
        JsonElement afterPayroll = (await self.GetFromJsonAsync<JsonElement>($"/api/v1/loans/{loanId}", Ct))!;
        afterPayroll.GetProperty("schedule")[0].GetProperty("status").GetString().ShouldBe("Deducted");
        afterPayroll.GetProperty("loan").GetProperty("outstanding").GetDecimal().ShouldBe(75_248m);
        afterPayroll.GetProperty("loan").GetProperty("instalmentsLeft").GetInt32().ShouldBe(5);

        // A part prepayment spreads the rest over the same five months, for a smaller instalment.
        JsonElement part = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/loans/{loanId}/prepay", new { Amount = 30_000m, Date = "2021-02-20", Reference = "UTR-PRE-1" }, Ct));
        part.GetProperty("loan").GetProperty("outstanding").GetDecimal().ShouldBe(45_248m);
        part.GetProperty("loan").GetProperty("instalmentsLeft").GetInt32().ShouldBe(5);
        part.GetProperty("loan").GetProperty("emi").GetDecimal().ShouldBeLessThan(10_000m);
        part.GetProperty("schedule").EnumerateArray().Where(row => row.GetProperty("status").GetString() == "Scheduled").Sum(row => row.GetProperty("principal").GetDecimal()).ShouldBe(45_248m);
        JsonElement[] march = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/inputs?period=2021-03-01&employeeId={person}", Ct))!;
        march.Where(input => input.GetProperty("kind").GetString() == "LoanRecovery").ShouldHaveSingleItem().GetProperty("amount").GetDecimal()
            .ShouldBe(part.GetProperty("loan").GetProperty("nextInstalmentAmount").GetDecimal());

        // Paying the rest closes it, and nothing more is deducted.
        JsonElement closed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/loans/{loanId}/prepay", new { Amount = 45_248m, Date = "2021-02-25", Reference = "UTR-PRE-2" }, Ct));
        closed.GetProperty("loan").GetProperty("status").GetString().ShouldBe("Closed");
        closed.GetProperty("loan").GetProperty("closeNote").GetString().ShouldBe("Prepaid in full");
        closed.GetProperty("repayments").GetArrayLength().ShouldBe(2);
        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/inputs?period=2021-03-01&employeeId={person}", Ct))!
            .ShouldNotContain(input => input.GetProperty("kind").GetString() == "LoanRecovery");

        JsonElement[] listed = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/loans?employeeId={person}", Ct))!;
        listed.ShouldHaveSingleItem().GetProperty("number").GetString()!.ShouldStartWith("LN-");
    }

    [Fact]
    public async Task Loan_types_are_validated_and_an_advance_paid_with_salary_comes_back_as_a_deduction()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await HireLongServingAsync(admin);
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/loans/types", new
        {
            Details = new { Code = "BAD", Name = "No limit", InterestRate = 0m, MaxTenureMonths = 6, MinServiceMonths = 0, MaxActive = 1, MaxEmiPercentOfGross = 50m, PerquisiteExempt = false },
        }, Ct))).ShouldBe("LOAN_TYPE_LIMIT");
        string code = $"FEST{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        Guid festival = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/loans/types", new
        {
            Details = new { Code = code, Name = "Festival advance", InterestRate = 0m, MaxTenureMonths = 2, MaxAmount = 20_000m, MinServiceMonths = 0, MaxActive = 1, MaxEmiPercentOfGross = 50m, PerquisiteExempt = false },
        }, Ct))).GetProperty("id").GetGuid();

        // HR applies on the employee's behalf and pays the advance with the 2021-05 salary.
        Guid loanId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/loans", new { TypeId = festival, Amount = 12_000m, TenureMonths = 2, Purpose = "Diwali", EmployeeId = person }, Ct))).GetProperty("loan").GetProperty("id").GetGuid();
        JsonElement paid = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/loans/{loanId}/disburse", new { Mode = "Payroll", DisbursedOn = "2021-05-05" }, Ct));
        paid.GetProperty("loan").GetProperty("disbursementReference").GetString().ShouldBe("With May 2021 salary");
        paid.GetProperty("schedule").EnumerateArray().Select(row => row.GetProperty("amount").GetDecimal()).ShouldBe([6_000m, 6_000m]);

        JsonElement may = await PayMonthAsync(factory, admin, "2021-05-01", person);
        Line(may, "LOAN_DISBURSE").ShouldBe(12_000m);
        Line(may, "LOAN_EMI").ShouldBe(0m);
        JsonElement june = await PayMonthAsync(factory, admin, "2021-06-01", person);
        Line(june, "LOAN_EMI").ShouldBe(6_000m);

        // Writing off the rest stops the July deduction.
        JsonElement written = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/loans/{loanId}/write-off", new { Reason = "Hardship waiver approved by the CHRO" }, Ct));
        written.GetProperty("loan").GetProperty("status").GetString().ShouldBe("WrittenOff");
        written.GetProperty("repayments").EnumerateArray().ShouldHaveSingleItem().GetProperty("amount").GetDecimal().ShouldBe(6_000m);
        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/inputs?period=2021-07-01&employeeId={person}", Ct))!
            .ShouldNotContain(input => input.GetProperty("kind").GetString() == "LoanRecovery");
    }
}
