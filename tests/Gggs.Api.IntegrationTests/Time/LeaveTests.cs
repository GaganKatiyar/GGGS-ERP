using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;
using static Gggs.Api.IntegrationTests.TimeApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class LeaveTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAnApprovalWorkflow_LeaveIsApprovedAtOnce_AndLeavesTheBalance()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await LeaveTypeAsync(admin, "CL");
        await GrantAsync(admin, org.Requester, casual, 5);

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        DateOnly monday = Next(DayOfWeek.Monday);

        HttpResponseMessage applied = await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday.AddDays(1)), Ct);
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync(Ct));
        JsonElement request = await applied.Content.ReadFromJsonAsync<JsonElement>(Ct);
        request.GetProperty("status").GetString().ShouldBe("Approved");
        request.GetProperty("days").GetDecimal().ShouldBe(2m);

        JsonElement balance = await BalanceAsync(employee, "CL");
        balance.GetProperty("balance").GetDecimal().ShouldBe(3m);
        balance.GetProperty("available").GetDecimal().ShouldBe(3m);
    }

    [Fact]
    public async Task TheSandwichRule_CountsTheWeekendBetweenLeaveDays_OnlyWhereThePolicySaysSo()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid earned = await LeaveTypeAsync(admin, "EL");
        Guid casual = await LeaveTypeAsync(admin, "CL");
        await GrantAsync(admin, org.Requester, earned, 10);
        await GrantAsync(admin, org.Requester, casual, 10);

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        DateOnly friday = Next(DayOfWeek.Friday, daysAhead: 10);
        DateOnly monday = friday.AddDays(3);

        JsonElement sandwiched = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(earned, friday, monday), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        sandwiched.GetProperty("days").GetDecimal().ShouldBe(4m, "earned leave counts the weekend it surrounds");

        JsonElement plain = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(casual, friday, monday), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        plain.GetProperty("days").GetDecimal().ShouldBe(2m, "casual leave counts working days only");

        JsonElement halfDay = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(casual, friday, friday, startsSecondHalf: true), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        halfDay.GetProperty("days").GetDecimal().ShouldBe(0.5m);
    }

    [Fact]
    public async Task ThePolicyRules_AreChecked_WhenApplying()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid earned = await LeaveTypeAsync(admin, "EL");
        Guid casual = await LeaveTypeAsync(admin, "CL");
        using HttpClient employee = await EmployeeAsync(factory, org.Requester);

        DateOnly tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        JsonElement noNotice = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(earned, tomorrow, tomorrow), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        noNotice.GetProperty("problems").EnumerateArray().Select(problem => problem.GetString()).ShouldContain(problem => problem!.Contains("notice"));

        DateOnly monday = Next(DayOfWeek.Monday, daysAhead: 21);
        HttpResponseMessage noBalance = await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday), Ct);
        noBalance.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(noBalance)).ShouldBe("LEAVE_NOT_ALLOWED");

        await GrantAsync(admin, org.Requester, casual, 10);
        HttpResponseMessage tooLong = await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday.AddDays(3)), Ct);
        (await tooLong.Content.ReadAsStringAsync(Ct)).ShouldContain("at most 3 day(s)");

        (await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        HttpResponseMessage overlap = await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday), Ct);
        (await overlap.Content.ReadAsStringAsync(Ct)).ShouldContain("overlap");
    }

    [Fact]
    public async Task AHoliday_IsNotTakenFromTheBalance()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await LeaveTypeAsync(admin, "CL");

        JsonElement calendar = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/leave/holiday-calendars", Ct))!.First(row => row.GetProperty("isDefault").GetBoolean());
        DateOnly wednesday = Next(DayOfWeek.Wednesday, daysAhead: 400 + Random.Shared.Next(0, 300));
        HttpResponseMessage added = await admin.PostAsJsonAsync(
            $"/api/v1/leave/holiday-calendars/{calendar.GetProperty("id").GetGuid()}/holidays",
            new { Date = Iso(wednesday), Name = "Test holiday", IsOptional = false },
            Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Ct));

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        JsonElement preview = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(casual, wednesday.AddDays(-1), wednesday.AddDays(1)), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        preview.GetProperty("days").GetDecimal().ShouldBe(2m);
    }

    [Fact]
    public async Task WithAnApprovalWorkflow_LeaveWaits_HoldsItsDays_AndCancellingRestoresThem()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await LeaveTypeAsync(admin, "CL");
        await GrantAsync(admin, org.Requester, casual, 4);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])], subject: "leave.request");

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        using HttpClient manager = await EmployeeAsync(factory, org.Manager);
        DateOnly tuesday = Next(DayOfWeek.Tuesday, daysAhead: 14);

        JsonElement request = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, tuesday, tuesday.AddDays(1)), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        request.GetProperty("status").GetString().ShouldBe("Pending");
        Guid requestId = request.GetProperty("id").GetGuid();

        JsonElement held = await BalanceAsync(employee, "CL");
        held.GetProperty("balance").GetDecimal().ShouldBe(4m);
        held.GetProperty("pending").GetDecimal().ShouldBe(2m);
        held.GetProperty("available").GetDecimal().ShouldBe(2m);

        await DecideAsync(manager, await TaskForAsync(manager, request.GetProperty("workflowInstanceId").GetGuid()), "approve");
        await factory.DeliverEventsAsync();

        JsonElement taken = await BalanceAsync(employee, "CL");
        taken.GetProperty("balance").GetDecimal().ShouldBe(2m);
        taken.GetProperty("pending").GetDecimal().ShouldBe(0m);

        (await employee.PostAsJsonAsync($"/api/v1/me/leave/requests/{requestId}/cancel", new { Reason = "Plans changed" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BalanceAsync(employee, "CL")).GetProperty("balance").GetDecimal().ShouldBe(4m, "cancelling approved leave gives the days back");

        JsonElement[] ledger = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={org.Requester}&leaveTypeId={casual}", Ct))!;
        ledger.Select(entry => entry.GetProperty("kind").GetString()).ShouldBe(["Restored", "Taken", "Grant"], ignoreOrder: true);
    }

    [Fact]
    public async Task Accrual_CreditsAMonthOnce_HoweverOftenItRuns()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await LeaveTypeAsync(admin, "CL");
        int year = 2031 + Random.Shared.Next(0, 50);

        for (int run = 0; run < 2; run++)
        {
            HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/leave/accrual/run", new { Year = year, Month = 6, EmployeeId = org.Requester }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        JsonElement[] ledger = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/leave/ledger?employeeId={org.Requester}&leaveTypeId={casual}", Ct))!;
        ledger.Where(entry => entry.GetProperty("kind").GetString() == "Accrual").ShouldHaveSingleItem()
            .GetProperty("days").GetDecimal().ShouldBe(1m, "twelve days a year is one a month");
    }

    [Fact]
    public async Task ApprovedLeave_ShowsOnTheAttendanceDay()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid casual = await LeaveTypeAsync(admin, "CL");
        await GrantAsync(admin, org.Requester, casual, 2);
        DateOnly pastWednesday = Previous(DayOfWeek.Wednesday, daysBack: 10);

        HttpResponseMessage onBehalf = await admin.PostAsJsonAsync("/api/v1/leave/requests", Application(casual, pastWednesday, pastWednesday, employeeId: org.Requester), Ct);
        onBehalf.StatusCode.ShouldBe(HttpStatusCode.OK, await onBehalf.Content.ReadAsStringAsync(Ct));

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        JsonElement day = await MyDayAsync(employee, pastWednesday);
        day.GetProperty("status").GetString().ShouldBe("OnLeave");
        day.GetProperty("note").GetString().ShouldBe("Casual leave");
    }
}
