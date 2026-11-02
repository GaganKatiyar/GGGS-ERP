using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.TimeApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Leave policies, phase 2: leave granted per occurrence with a lifetime limit, supporting documents
/// on long requests, and the turn of the leave year - encashment, lapse and expiring carried days.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class LeavePlanYearEndTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LeaveGrantedPerOccurrence_NeedsNoBalance_AndStopsAtItsLifetimeLimit()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        (string engagement, Guid plan) = await PolicyForNewEngagementTypeAsync(admin, "Occurrence");
        await SetItemsAsync(admin, plan, Item(casual) with { AnnualEntitlement = 0, Accrual = "None", CarryForwardLimit = null, GrantPerOccurrence = 10, MaxOccurrencesInEmployment = 1 });

        Guid employee = await HireAsync(admin, engagement);
        using HttpClient self = await EmployeeAsync(factory, employee);
        JsonElement before = await BalanceAsync(self, "CL");
        before.GetProperty("available").GetDecimal().ShouldBe(10m, "one occurrence of 10 days is still available");

        DateOnly monday = Next(DayOfWeek.Monday, daysAhead: 21);
        HttpResponseMessage first = await self.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday.AddDays(4)), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("Approved");

        JsonElement[] ledger = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={employee}&leaveTypeId={casual}", Ct))!;
        ledger.Sum(entry => entry.GetProperty("days").GetDecimal()).ShouldBe(0m, "the grant and the leave taken cancel out");
        (await BalanceAsync(self, "CL")).GetProperty("available").GetDecimal().ShouldBe(0m, "the only occurrence is used");

        DateOnly later = monday.AddDays(28);
        HttpResponseMessage second = await self.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, later, later), Ct);
        (await second.Content.ReadAsStringAsync(Ct)).ShouldContain("1 time(s) in your employment");
    }

    [Fact]
    public async Task ALongRequest_WaitsAsADraft_UntilItsDocumentIsAttached()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        (string engagement, Guid plan) = await PolicyForNewEngagementTypeAsync(admin, "Documented");
        await SetItemsAsync(admin, plan, Item(casual) with { DocumentRequiredAboveDays = 1 });

        Guid employee = await HireAsync(admin, engagement);
        await GrantAsync(admin, employee, casual, 10);
        using HttpClient self = await EmployeeAsync(factory, employee);
        DateOnly monday = Next(DayOfWeek.Monday, daysAhead: 21);

        JsonElement preview = await (await self.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(casual, monday, monday.AddDays(1)), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        preview.GetProperty("documentRequired").GetBoolean().ShouldBeTrue();

        JsonElement draft = await (await self.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday.AddDays(1)), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        Guid id = draft.GetProperty("id").GetGuid();

        HttpResponseMessage early = await self.PostAsync($"/api/v1/me/leave/requests/{id}/submit", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(early)).ShouldBe("LEAVE_DOCUMENT_REQUIRED");

        HttpResponseMessage wrongType = await UploadAsync(self, id, Encoding.ASCII.GetBytes("MZ not a document"), "virus.exe");
        (await CoreHrApi.ErrorCodeAsync(wrongType)).ShouldBe("LEAVE_DOCUMENT_TYPE");
        (await UploadAsync(self, id, Encoding.ASCII.GetBytes("%PDF-1.4 medical certificate"), "certificate.pdf")).StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage submitted = await self.PostAsync($"/api/v1/me/leave/requests/{id}/submit", null, Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync(Ct));
        JsonElement sent = await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        sent.GetProperty("status").GetString().ShouldBe("Approved");
        sent.GetProperty("attachmentCount").GetInt32().ShouldBe(1);

        JsonElement[] attachments = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/requests/{id}/attachments", Ct))!;
        attachments.ShouldHaveSingleItem().GetProperty("contentType").GetString().ShouldBe("application/pdf");
        HttpResponseMessage file = await admin.GetAsync($"/api/v1/leave/requests/{id}/attachments/{attachments[0].GetProperty("id").GetGuid()}/content", Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await file.Content.ReadAsStringAsync(Ct)).ShouldStartWith("%PDF");
    }

    [Fact]
    public async Task AtTheTurnOfTheYear_TheExcessIsEncashedThenLapses_AndUnusedCarriedDaysExpire()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        (string engagement, Guid plan) = await PolicyForNewEngagementTypeAsync(admin, "Year end");
        await SetItemsAsync(admin, plan, Item(casual) with { CarryForwardLimit = 2, EncashAtYearEnd = true, MaxEncashDays = 3, CarryForwardExpiryMonths = 3 });

        Guid employee = await HireAsync(admin, engagement);
        await GrantAsync(admin, employee, casual, 10);
        int next = DateTime.UtcNow.Year + 1;

        (await admin.PostAsJsonAsync("/api/v1/leave/accrual/run", new { Year = next, Month = 1, EmployeeId = employee }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] ledger = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={employee}&leaveTypeId={casual}", Ct))!;
        ledger.Single(entry => entry.GetProperty("kind").GetString() == "Encashment").GetProperty("days").GetDecimal().ShouldBe(-3m);
        ledger.Single(entry => entry.GetProperty("kind").GetString() == "Lapse").GetProperty("days").GetDecimal().ShouldBe(-5m);

        // Running the month again changes nothing.
        await admin.PostAsJsonAsync("/api/v1/leave/accrual/run", new { Year = next, Month = 1, EmployeeId = employee }, Ct);
        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={employee}&leaveTypeId={casual}", Ct))!.Length.ShouldBe(ledger.Length);

        (await admin.PostAsJsonAsync("/api/v1/leave/accrual/run", new { Year = next, Month = 4, EmployeeId = employee }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] after = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={employee}&leaveTypeId={casual}", Ct))!;
        after.Single(entry => entry.GetProperty("note").GetString()?.Contains("not used within 3 month(s)") == true).GetProperty("days").GetDecimal().ShouldBe(-2m);
    }

    private sealed record ItemBody(
        Guid LeaveTypeId,
        decimal AnnualEntitlement,
        string Accrual,
        decimal? CarryForwardLimit,
        decimal MaxNegativeBalance,
        bool CountSandwichedDays,
        string[] EligibleGenderCodes,
        int MinServiceMonths,
        bool AllowDuringProbation,
        bool AllowDuringNotice,
        decimal? MinDaysPerRequest,
        int? MaxConsecutiveDays,
        int? MaxRequestsPerYear,
        int MinNoticeDays,
        int? MaxBackdatedDays,
        int? MaxAdvanceDays,
        decimal? DocumentRequiredAboveDays = null,
        string CreditTiming = "StartOfPeriod",
        int JoiningCutoffDay = 15,
        bool ProrateOnJoining = true,
        int? CarryForwardExpiryMonths = null,
        bool EncashAtYearEnd = false,
        decimal? MaxEncashDays = null,
        decimal? GrantPerOccurrence = null,
        int? MaxOccurrencesInEmployment = null);

    private static ItemBody Item(Guid leaveTypeId) => new(leaveTypeId, 12, "Monthly", 0, 0, false, [], 0, true, true, null, null, null, 0, null, null);

    private static async Task SetItemsAsync(HttpClient admin, Guid plan, ItemBody item)
    {
        HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { item }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>A new engagement type with its own new policy, so nothing here touches other tests.</summary>
    private static async Task<(string EngagementType, Guid Plan)> PolicyForNewEngagementTypeAsync(HttpClient admin, string label)
    {
        string code = CoreHrApi.Unique("ET").Replace('-', '_');
        (await admin.PostAsJsonAsync("/api/v1/master-data/employment-type", new { Code = code, Label = label }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid plan = await CoreHrApi.CreateAsync(admin, "/api/v1/leave/plans", new { Code = CoreHrApi.Unique("LP"), Name = label, LeaveYearStartMonth = 1 });
        HttpResponseMessage assigned = await admin.PutAsJsonAsync("/api/v1/leave/plan-assignments", new[] { new { EmploymentTypeCode = code, PlanId = plan } }, Ct);
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync(Ct));
        return (code, plan);
    }

    private static async Task<Guid> HireAsync(HttpClient admin, string engagementType)
    {
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Test",
            LastName = CoreHrApi.Unique("Person"),
            JoiningDate = "2024-01-15",
            Assignment = new { DepartmentId = department, EmploymentTypeCode = engagementType, WorkModeCode = "OFFICE", Designation = "Associate" },
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid requestId, byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await client.PostAsync($"/api/v1/me/leave/requests/{requestId}/attachments", form, Ct);
    }
}
