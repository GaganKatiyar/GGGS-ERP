using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;
using static Gggs.Api.IntegrationTests.TimeApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ShiftAssignmentTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheShiftList_CountsWhoWorksEach_AndEndingAnAssignment_SendsThePersonBackToTheDefault()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid late = await CreateShiftAsync(admin, "Late shift", "12:00", "21:00");
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        (await PeopleOnAsync(admin, late)).ShouldBe(0);

        await AssignAsync(admin, org.Requester, late, today);
        (await PeopleOnAsync(admin, late)).ShouldBe(1);

        JsonElement assignment = (await CurrentAssignmentsAsync(admin)).Single(row => row.GetProperty("employeeId").GetGuid() == org.Requester);
        assignment.GetProperty("shiftName").GetString().ShouldBe("Late shift");
        assignment.GetProperty("employeeName").GetString().ShouldNotBeNullOrWhiteSpace();

        // Back to the default from today: an assignment that started today has nothing left, so it goes.
        (await EndAsync(admin, assignment.GetProperty("id").GetGuid(), today)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PeopleOnAsync(admin, late)).ShouldBe(0);
        (await CurrentAssignmentsAsync(admin)).ShouldNotContain(row => row.GetProperty("employeeId").GetGuid() == org.Requester);
    }

    [Fact]
    public async Task EndingAnAssignmentPartWay_KeepsTheDaysBefore_AndAnEndedOneCannotEndAgainLater()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid early = await CreateShiftAsync(admin, "Early shift", "07:00", "16:00");
        DateOnly start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        await AssignAsync(admin, org.Finance, early, start);
        Guid id = (await CurrentAssignmentsAsync(admin)).Single(row => row.GetProperty("employeeId").GetGuid() == org.Finance).GetProperty("id").GetGuid();

        (await EndAsync(admin, id, start.AddDays(3))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement kept = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/shifts/assignments?employeeId={org.Finance}", Ct))!.Single();
        kept.GetProperty("effectiveFrom").GetString().ShouldBe(Iso(start));
        kept.GetProperty("effectiveTo").GetString().ShouldBe(Iso(start.AddDays(2)));

        HttpResponseMessage again = await EndAsync(admin, id, start.AddDays(5));
        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(again)).ShouldBe("SHIFT_ASSIGNMENT_ENDED");
    }

    private static Task<Guid> CreateShiftAsync(HttpClient admin, string name, string start, string end)
        => CoreHrApi.CreateAsync(admin, "/api/v1/shifts", new
        {
            Code = CoreHrApi.Unique("S"),
            Name = name,
            StartTime = start,
            EndTime = end,
            BreakMinutes = 60,
            GraceMinutes = 15,
            HalfDayMinutes = 240,
            FullDayMinutes = 480,
            WeeklyOff = new[] { "Saturday", "Sunday" },
        });

    private static async Task AssignAsync(HttpClient admin, Guid employeeId, Guid shiftId, DateOnly from)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/shifts/assignments", new { EmployeeIds = new[] { employeeId }, ShiftId = shiftId, EffectiveFrom = Iso(from) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<int> PeopleOnAsync(HttpClient admin, Guid shiftId)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/shifts", Ct))!.Single(shift => shift.GetProperty("id").GetGuid() == shiftId).GetProperty("people").GetInt32();

    private static async Task<JsonElement[]> CurrentAssignmentsAsync(HttpClient admin)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/shifts/assignments", Ct))!;

    private static Task<HttpResponseMessage> EndAsync(HttpClient admin, Guid id, DateOnly returnOn)
        => admin.PostAsJsonAsync($"/api/v1/shifts/assignments/{id}/end", new { ReturnOn = Iso(returnOn) }, Ct);
}
