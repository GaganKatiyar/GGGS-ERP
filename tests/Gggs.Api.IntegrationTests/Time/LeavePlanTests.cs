using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;
using static Gggs.Api.IntegrationTests.TimeApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Leave policies: HR builds named policies, assigns one to each engagement type, and gives single
/// employees exceptions. Each test makes its own engagement type, so assignments never touch the
/// types other tests hire into.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class LeavePlanTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryEngagementType_StartsOnTheStandardPolicy()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement[] assignments = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/leave/plan-assignments", Ct))!;
        assignments.Single(row => row.GetProperty("employmentTypeCode").GetString() == "FULL_TIME").GetProperty("planName").GetString().ShouldBe("Standard leave policy");

        JsonElement standard = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/leave/plans", Ct))!.Single(plan => plan.GetProperty("code").GetString() == "STANDARD");
        standard.GetProperty("leaveYear").GetString().ShouldBe("Jan–Dec");
        standard.GetProperty("leaveTypeCount").GetInt32().ShouldBeGreaterThanOrEqualTo(5);

        TestOrg org = await OrgAsync(admin);
        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        JsonElement mine = (await employee.GetFromJsonAsync<JsonElement>("/api/v1/me/leave/policy", Ct))!;
        mine.GetProperty("code").GetString().ShouldBe("STANDARD");
        mine.GetProperty("source").GetString().ShouldBe("EngagementType");
        mine.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("leaveTypeCode").GetString()).ShouldContain("CL");
    }

    [Fact]
    public async Task APolicyAssignedToAnEngagementType_DecidesBalancesAccrualAndWhatCanBeTaken()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        Guid sick = await LeaveTypeAsync(admin, "SL");
        string engagement = await EngagementTypeAsync(admin, "Seasonal");

        Guid plan = await PlanAsync(admin, "Seasonal staff", leaveYearStartMonth: 4);
        (await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 24) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssignAsync(admin, engagement, plan);

        Guid seasonal = await HireAsync(admin, engagement);
        JsonElement resolved = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/leave/plans/resolve?employeeId={seasonal}", Ct))!;
        resolved.GetProperty("planId").GetGuid().ShouldBe(plan);
        resolved.GetProperty("leaveYear").GetString().ShouldBe("Apr–Mar");

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await admin.PostAsJsonAsync("/api/v1/leave/accrual/run", new { today.Year, today.Month, EmployeeId = seasonal }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient employee = await EmployeeAsync(factory, seasonal);
        JsonElement[] balances = (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/leave/balances", Ct))!;
        balances.ShouldHaveSingleItem().GetProperty("code").GetString().ShouldBe("CL", "only the policy's leave types are shown");
        balances[0].GetProperty("balance").GetDecimal().ShouldBe(2m, "24 days a year accrue 2 a month");

        DateOnly monday = Next(DayOfWeek.Monday, daysAhead: 14);
        HttpResponseMessage notInPolicy = await employee.PostAsJsonAsync("/api/v1/me/leave/requests", Application(sick, monday, monday), Ct);
        (await CoreHrApi.ErrorCodeAsync(notInPolicy)).ShouldBe("LEAVE_NOT_IN_POLICY");

        string unassigned = await EngagementTypeAsync(admin, "Unassigned");
        Guid nobody = await HireAsync(admin, unassigned);
        using HttpClient stranded = await EmployeeAsync(factory, nobody);
        HttpResponseMessage noPolicy = await stranded.PostAsJsonAsync("/api/v1/me/leave/requests", Application(casual, monday, monday), Ct);
        (await CoreHrApi.ErrorCodeAsync(noPolicy)).ShouldBe("LEAVE_NO_POLICY");
        (await stranded.GetAsync("/api/v1/me/leave/policy", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnException_SwitchesOneEmployeesPolicy_FromItsDate()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        Guid plan = await PlanAsync(admin, "Senior hires", leaveYearStartMonth: 1);
        await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 30) }, Ct);

        TestOrg org = await OrgAsync(admin);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        HttpResponseMessage added = await admin.PostAsJsonAsync("/api/v1/leave/employee-plans", new { EmployeeId = org.Requester, PlanId = plan, EffectiveFrom = Iso(today.AddDays(-1)), Reason = "Joined at director level" }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Ct));
        Guid exception = (await added.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        JsonElement resolved = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/leave/plans/resolve?employeeId={org.Requester}", Ct))!;
        resolved.GetProperty("planId").GetGuid().ShouldBe(plan);
        resolved.GetProperty("source").GetString().ShouldBe("Exception");

        HttpResponseMessage overlap = await admin.PostAsJsonAsync("/api/v1/leave/employee-plans", new { EmployeeId = org.Requester, PlanId = plan, EffectiveFrom = Iso(today.AddDays(30)) }, Ct);
        (await CoreHrApi.ErrorCodeAsync(overlap)).ShouldBe("LEAVE_EXCEPTION_OVERLAP");

        HttpResponseMessage inUse = await admin.PostAsync($"/api/v1/leave/plans/{plan}/deactivate", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(inUse)).ShouldBe("LEAVE_PLAN_IN_USE");

        (await admin.DeleteAsync($"/api/v1/leave/employee-plans/{exception}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/leave/plans/resolve?employeeId={org.Requester}", Ct)).GetProperty("code").GetString().ShouldBe("STANDARD");
        (await admin.PostAsync($"/api/v1/leave/plans/{plan}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhoCanTakeIt_AndRequestLimits_AreChecked_WhenApplying()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        string engagement = await EngagementTypeAsync(admin, "Trainee");
        Guid plan = await PlanAsync(admin, "Trainees", leaveYearStartMonth: 1);
        HttpResponseMessage saved = await admin.PutAsJsonAsync(
            $"/api/v1/leave/plans/{plan}/items",
            new[] { Item(casual, entitlement: 12) with { EligibleGenderCodes = ["MALE"], MinServiceMonths = 600, MaxAdvanceDays = 30 } },
            Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        await AssignAsync(admin, engagement, plan);

        Guid trainee = await HireAsync(admin, engagement);
        await GrantAsync(admin, trainee, casual, 10);
        using HttpClient employee = await EmployeeAsync(factory, trainee);

        DateOnly farAhead = Next(DayOfWeek.Monday, daysAhead: 60);
        JsonElement preview = await (await employee.PostAsJsonAsync("/api/v1/me/leave/requests/preview", Application(casual, farAhead, farAhead), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        string[] problems = [.. preview.GetProperty("problems").EnumerateArray().Select(problem => problem.GetString()!)];
        problems.ShouldContain(problem => problem.Contains("not available"));
        problems.ShouldContain(problem => problem.Contains("600 month(s) of service"));
        problems.ShouldContain(problem => problem.Contains("30 day(s) ahead"));

        HttpResponseMessage badGender = await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 12) with { EligibleGenderCodes = ["MARTIAN"] } }, Ct);
        (await CoreHrApi.ErrorCodeAsync(badGender)).ShouldBe("LEAVE_POLICY_GENDER");
    }

    [Fact]
    public async Task ALeaveType_CanBeAddedToAPolicyThatAlreadyHasSome()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        Guid sick = await LeaveTypeAsync(admin, "SL");
        Guid plan = await PlanAsync(admin, "Growing", leaveYearStartMonth: 1);
        (await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 12) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage grown = await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 10), Item(sick, entitlement: 8) }, Ct);
        grown.StatusCode.ShouldBe(HttpStatusCode.OK, await grown.Content.ReadAsStringAsync(Ct));
        JsonElement[] items = [.. (await grown.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()];
        items.Select(item => (item.GetProperty("leaveTypeCode").GetString(), item.GetProperty("annualEntitlement").GetDecimal())).ShouldBe([("CL", 10m), ("SL", 8m)], ignoreOrder: true);
    }

    [Fact]
    public async Task APolicyCanBeCopied_WithItsRules()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid casual = await LeaveTypeAsync(admin, "CL");
        Guid plan = await PlanAsync(admin, "Interns", leaveYearStartMonth: 7);
        await admin.PutAsJsonAsync($"/api/v1/leave/plans/{plan}/items", new[] { Item(casual, entitlement: 6) }, Ct);

        HttpResponseMessage copied = await admin.PostAsync($"/api/v1/leave/plans/{plan}/copy", null, Ct);
        copied.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement copy = await copied.Content.ReadFromJsonAsync<JsonElement>(Ct);
        copy.GetProperty("name").GetString().ShouldStartWith("Copy of Interns");
        copy.GetProperty("leaveYearStartMonth").GetInt32().ShouldBe(7);
        copy.GetProperty("items").EnumerateArray().Single().GetProperty("annualEntitlement").GetDecimal().ShouldBe(6m);
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
        int? MaxAdvanceDays);

    private static ItemBody Item(Guid leaveTypeId, decimal entitlement)
        => new(leaveTypeId, entitlement, "Monthly", 0, 0, false, [], 0, true, true, null, null, null, 0, null, null);

    private static async Task<Guid> PlanAsync(HttpClient admin, string name, int leaveYearStartMonth)
        => await CoreHrApi.CreateAsync(admin, "/api/v1/leave/plans", new { Code = CoreHrApi.Unique("LP"), Name = name, LeaveYearStartMonth = leaveYearStartMonth });

    private static async Task AssignAsync(HttpClient admin, string engagementType, Guid planId)
    {
        HttpResponseMessage response = await admin.PutAsJsonAsync("/api/v1/leave/plan-assignments", new[] { new { EmploymentTypeCode = engagementType, PlanId = planId } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>A new engagement type in master data, so assigning it touches no other test.</summary>
    private static async Task<string> EngagementTypeAsync(HttpClient admin, string label)
    {
        string code = CoreHrApi.Unique("ET").ToUpperInvariant().Replace('-', '_');
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/master-data/employment-type", new { Code = code, Label = label }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return code;
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
}
