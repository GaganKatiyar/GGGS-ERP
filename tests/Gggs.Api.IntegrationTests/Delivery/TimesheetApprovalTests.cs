using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class TimesheetApprovalTests(PeopleOsApiFactory factory)
{
    private readonly Dictionary<Guid, HttpClient> _approvers = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>This week's Monday: at most six days back, so after the staffing dates the test helpers use.</summary>
    private static DateOnly Monday => Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));

    private static string Week => Iso(Monday);

    private Task<HttpClient> SelfAsync(Guid employeeId)
        => factory.CreateScopedUserClientAsync(
            Email("self"), employeeId, (Permissions.Self.TimesheetSubmit, DataScope.Own), (Permissions.Workflow.TaskView, DataScope.Own), (Permissions.Workflow.TaskAct, DataScope.Own));

    private static object Line(Guid? projectId, Guid? taskId, decimal hours, int day = 0, string category = "Billable")
        => new { Date = Iso(Monday.AddDays(day)), ProjectId = projectId, TaskId = taskId, Category = category, Hours = hours };

    private static async Task<Guid> StaffedProjectAsync(HttpClient admin, Guid manager, Guid person)
    {
        Guid project = (await CreateProjectAsync(admin, Project(managerId: manager, start: Today.AddDays(-90)))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, project, person);
        return project;
    }

    private static async Task<Guid> TaskAsync(HttpClient admin, Guid project, Guid assignee)
        => (await CreateTaskAsync(admin, project, NewTask("Build it", assigneeId: assignee))).GetProperty("id").GetGuid();

    private static Task<ActiveDefinition> OwnerApprovesAsync(HttpClient admin)
        => DefinitionAsync(admin, [Stage("Approver", [Rule("SubjectOwner")])], subject: "timesheet.submission");

    private static JsonElement ShareFor(JsonElement view, Guid? projectId)
        => view.GetProperty("approvals").EnumerateArray().Single(share => (share.TryGetProperty("projectId", out JsonElement id) ? id.GetGuid() : (Guid?)null) == projectId);

    private async Task<JsonElement> WeekAsync(HttpClient self) => await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={Week}", Ct);

    private async Task DecideShareAsync(Guid approver, JsonElement share, string decision)
    {
        // An employee has one account, so someone deciding twice in a test keeps the client they had.
        if (!_approvers.TryGetValue(approver, out HttpClient? client))
        {
            client = await PersonAsync(factory, approver);
            _approvers[approver] = client;
        }

        await DecideAsync(client, await TaskForAsync(client, share.GetProperty("workflowInstanceId").GetGuid()), decision, decision == "reject" ? "Wrong project" : null);
        await factory.DeliverEventsAsync();
    }

    private async Task<decimal> ActualHoursAsync(HttpClient admin, Guid project, Guid task)
    {
        JsonElement found = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks/{task}", Ct);
        return found.TryGetProperty("actualHours", out JsonElement hours) ? hours.GetDecimal() : 0m;
    }

    [Fact]
    public async Task EachProjectManager_ApprovesOnlyTheirOwnHours_AndTheyCountAsSoonAsTheyDo()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid second = await StaffedProjectAsync(admin, org.Deputy, org.Requester);
        Guid firstTask = await TaskAsync(admin, first, org.Requester);
        Guid secondTask = await TaskAsync(admin, second, org.Requester);

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, firstTask, 5m), Line(second, secondTask, 3m) } }, Ct));
            (await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Submitted");

            JsonElement view = await WeekAsync(self);
            view.GetProperty("approvals").GetArrayLength().ShouldBe(2);
            JsonElement firstShare = ShareFor(view, first);
            firstShare.GetProperty("ownerEmployeeId").GetGuid().ShouldBe(org.Finance);
            firstShare.GetProperty("hours").GetDecimal().ShouldBe(5m);
            ShareFor(view, second).GetProperty("ownerEmployeeId").GetGuid().ShouldBe(org.Deputy);

            // The first manager answers: their hours count, the other project's do not, and the week is not yet approved.
            await DecideShareAsync(org.Finance, firstShare, "approve");
            view = await WeekAsync(self);
            view.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Submitted");
            (await ActualHoursAsync(admin, first, firstTask)).ShouldBe(5m);
            (await ActualHoursAsync(admin, second, secondTask)).ShouldBe(0m);

            string range = $"from={Week}&to={Iso(Monday.AddDays(6))}";
            JsonElement[] firstHours = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/timesheets/projects/{first}?{range}", Ct))!;
            firstHours.Sum(row => row.GetProperty("approvedHours").GetDecimal()).ShouldBe(5m);
            JsonElement[] secondHours = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/timesheets/projects/{second}?{range}", Ct))!;
            secondHours.Sum(row => row.GetProperty("hours").GetDecimal()).ShouldBe(3m, "still counted as submitted");
            secondHours.Sum(row => row.GetProperty("approvedHours").GetDecimal()).ShouldBe(0m);

            await DecideShareAsync(org.Deputy, ShareFor(view, second), "approve");
            (await WeekAsync(self)).GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Approved");
            (await ActualHoursAsync(admin, second, secondTask)).ShouldBe(3m);
        }
    }

    [Fact]
    public async Task AnApprover_SeesOnlyTheHoursThatCameToThem()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid second = await StaffedProjectAsync(admin, org.Deputy, org.Requester);

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, null, 5m), Line(second, null, 3m) } }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));
            Guid id = (await WeekAsync(self)).GetProperty("timesheet").GetProperty("id").GetGuid();

            using HttpClient approver = await factory.CreateScopedUserClientAsync(Email("pm"), org.Finance, (Permissions.Workflow.TaskAct, DataScope.Own));
            JsonElement seen = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/timesheets/{id}", Ct);
            JsonElement entry = seen.GetProperty("entries").EnumerateArray().ShouldHaveSingleItem();
            entry.GetProperty("projectId").GetGuid().ShouldBe(first);
            seen.GetProperty("approvals").EnumerateArray().ShouldHaveSingleItem();

            JsonElement listed = await approver.GetFromJsonAsync<JsonElement>("/api/v1/timesheets/approvals?status=Submitted", Ct);
            listed.GetProperty("items").EnumerateArray().ShouldContain(row => row.GetProperty("timesheetId").GetGuid() == id && row.GetProperty("projectId").GetGuid() == first);
            listed.GetProperty("items").EnumerateArray().ShouldNotContain(row => row.GetProperty("timesheetId").GetGuid() == id && row.GetProperty("projectId").GetGuid() == second);

            using HttpClient stranger = await factory.CreateScopedUserClientAsync(Email("other"), org.SkipLevel, (Permissions.Workflow.TaskAct, DataScope.Own));
            (await stranger.GetAsync($"/api/v1/timesheets/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task DefaultProjectAndNonProjectHours_GoToThePersonsManager_AsOneShare()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        JsonElement[] rows = (await OkAsync(await admin.PostAsync("/api/v1/projects/default-projects/ensure", null, Ct))).EnumerateArray().ToArray();
        Guid defaultProject = rows.First(row => row.TryGetProperty("departmentId", out JsonElement department) && department.GetGuid() == org.Department).GetProperty("projectId").GetGuid();

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new
            {
                Entries = new[] { Line(project, null, 4m), Line(defaultProject, null, 2m, category: "Internal"), Line(null, null, 1m, category: "Internal") },
            }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));

            JsonElement view = await WeekAsync(self);
            view.GetProperty("approvals").GetArrayLength().ShouldBe(2);
            JsonElement own = ShareFor(view, null);
            own.GetProperty("hours").GetDecimal().ShouldBe(3m);
            own.GetProperty("ownerEmployeeId").GetGuid().ShouldBe(org.Manager);
        }
    }

    [Fact]
    public async Task AManagersOwnHours_OnTheirOwnProject_GoToTheirManager()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-90)))).GetProperty("id").GetGuid();

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Manager);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(project, null, 8m) } }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));

            ShareFor(await WeekAsync(self), project).GetProperty("ownerEmployeeId").GetGuid().ShouldBe(org.SkipLevel);
        }
    }

    [Fact]
    public async Task ARejection_ReturnsOnlyThatProject_AndTheRestCannotChange()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid second = await StaffedProjectAsync(admin, org.Deputy, org.Requester);

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, null, 5m), Line(second, null, 3m) } }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));
            JsonElement view = await WeekAsync(self);

            await DecideShareAsync(org.Finance, ShareFor(view, first), "approve");
            await DecideShareAsync(org.Deputy, ShareFor(view, second), "reject");
            view = await WeekAsync(self);
            view.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Rejected");
            ShareFor(view, first).GetProperty("status").GetString().ShouldBe("Approved");
            ShareFor(view, second).GetProperty("status").GetString().ShouldBe("Rejected");

            // The approved project's hours are fixed; the rejected project's can be corrected.
            HttpResponseMessage changed = await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, null, 6m), Line(second, null, 2m) } }, Ct);
            (await CoreHrApi.ErrorCodeAsync(changed)).ShouldBe("TIMESHEET_SHARE_LOCKED");
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, null, 5m), Line(second, null, 2m) } }, Ct));

            // Submitting again goes only to the project that rejected, and its first decision is kept for the other.
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));
            view = await WeekAsync(self);
            view.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Submitted");
            view.GetProperty("timesheet").GetProperty("totalHours").GetDecimal().ShouldBe(7m);
            ShareFor(view, first).GetProperty("status").GetString().ShouldBe("Approved");
            ShareFor(view, second).GetProperty("hours").GetDecimal().ShouldBe(2m);

            await DecideShareAsync(org.Deputy, ShareFor(view, second), "approve");
            (await WeekAsync(self)).GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Approved");
        }
    }

    [Fact]
    public async Task ReopeningOneProject_TakesOnlyItsHoursBack()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid second = await StaffedProjectAsync(admin, org.Deputy, org.Requester);
        Guid firstTask = await TaskAsync(admin, first, org.Requester);
        Guid secondTask = await TaskAsync(admin, second, org.Requester);

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, firstTask, 5m), Line(second, secondTask, 3m) } }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));
            JsonElement view = await WeekAsync(self);
            await DecideShareAsync(org.Finance, ShareFor(view, first), "approve");
            await DecideShareAsync(org.Deputy, ShareFor(view, second), "approve");

            view = await WeekAsync(self);
            JsonElement header = view.GetProperty("timesheet");
            await OkAsync(
                await admin.PostAsJsonAsync($"/api/v1/timesheets/{header.GetProperty("id").GetGuid()}/reopen", new { Reason = "Wrong task", ApprovalId = ShareFor(view, first).GetProperty("id").GetGuid() }, Ct),
                HttpStatusCode.NoContent);
            await factory.DeliverEventsAsync();

            view = await WeekAsync(self);
            view.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Draft");
            view.GetProperty("timesheet").GetProperty("correctionCount").GetInt32().ShouldBe(1);
            ShareFor(view, first).GetProperty("status").GetString().ShouldBe("Returned");
            ShareFor(view, second).GetProperty("status").GetString().ShouldBe("Approved");
            (await ActualHoursAsync(admin, first, firstTask)).ShouldBe(0m);
            (await ActualHoursAsync(admin, second, secondTask)).ShouldBe(3m);
        }
    }

    [Fact]
    public async Task WithdrawingAWeek_LeavesWhatIsAlreadyApproved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid second = await StaffedProjectAsync(admin, org.Deputy, org.Requester);

        await using (await OwnerApprovesAsync(admin))
        {
            using HttpClient self = await SelfAsync(org.Requester);
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, null, 5m), Line(second, null, 3m) } }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct));
            await DecideShareAsync(org.Finance, ShareFor(await WeekAsync(self), first), "approve");

            await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/withdraw", null, Ct), HttpStatusCode.NoContent);

            JsonElement view = await WeekAsync(self);
            view.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Draft");
            ShareFor(view, first).GetProperty("status").GetString().ShouldBe("Approved");
            ShareFor(view, second).GetProperty("status").GetString().ShouldBe("Returned");
        }
    }

    [Fact]
    public async Task WithoutAWorkflow_EveryShareIsApprovedOnSubmit()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = await StaffedProjectAsync(admin, org.Finance, org.Requester);
        Guid task = await TaskAsync(admin, first, org.Requester);

        using HttpClient self = await SelfAsync(org.Requester);
        await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{Week}", new { Entries = new[] { Line(first, task, 5m), Line(null, null, 1m, category: "Internal") } }, Ct));
        (await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{Week}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");
        await factory.DeliverEventsAsync();

        (await ActualHoursAsync(admin, first, task)).ShouldBe(5m);
    }
}
