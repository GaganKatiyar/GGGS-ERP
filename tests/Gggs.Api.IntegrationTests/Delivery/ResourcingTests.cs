using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ResourcingTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The Monday two weeks back: a whole past week, safely after the test allocations start.</summary>
    private static DateOnly PastWeek()
    {
        DateOnly today = Today;
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(-14);
    }

    private static Task<HttpClient> SelfServiceAsync(PeopleOsApiFactory factory, Guid employeeId)
        => factory.CreateScopedUserClientAsync(
            Email("self"),
            employeeId,
            (Permissions.Self.TimesheetSubmit, DataScope.Own),
            (Permissions.Self.SkillManage, DataScope.Own),
            (Permissions.Workflow.TaskView, DataScope.Own),
            (Permissions.Workflow.TaskAct, DataScope.Own));

    private static async Task<Guid> SkillAsync(HttpClient admin, string code)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/skills", Ct))!.First(skill => skill.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    private static async Task<JsonElement> AllocateAsync(HttpClient admin, Guid projectId, Guid employeeId, int percent, DateOnly? start = null, DateOnly? end = null)
        => await OkAsync(await admin.PostAsJsonAsync("/api/v1/resource-allocations", new
        {
            ProjectId = projectId,
            EmployeeId = employeeId,
            StartDate = Iso(start ?? Today.AddDays(-60)),
            EndDate = end is null ? null : Iso(end.Value),
            AllocationPercent = percent,
            Billable = true,
        }, Ct));

    private static object Week(DateOnly monday, Guid projectId, Guid? taskId, decimal hoursPerDay = 8, int days = 5)
        => new
        {
            Entries = Enumerable.Range(0, days).Select(offset => new
            {
                Date = Iso(monday.AddDays(offset)),
                ProjectId = projectId,
                TaskId = taskId,
                Category = "Billable",
                Hours = hoursPerDay,
                Activity = "Development",
            }).ToArray(),
            Comment = (string?)null,
        };

    [Fact]
    public async Task Skills_AreAssessedByManagersAndSelf_AndAddUpInTheMatrix()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid dotnet = await SkillAsync(admin, "DOTNET");
        Guid angular = await SkillAsync(admin, "ANGULAR");

        JsonElement profile = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/skills/employees/{org.Requester}", new
        {
            SkillId = dotnet,
            CurrentLevel = 3,
            TargetLevel = 5,
            YearsExperience = 4m,
            IsPrimary = true,
        }, Ct));
        JsonElement skill = profile.GetProperty("skills").EnumerateArray().Single();
        skill.GetProperty("gap").GetInt32().ShouldBe(2);
        skill.GetProperty("source").GetString().ShouldBe("Manager");

        using HttpClient self = await SelfServiceAsync(factory, org.Requester);
        JsonElement mine = await OkAsync(await self.PutAsJsonAsync("/api/v1/me/skills", new { SkillId = angular, CurrentLevel = 2, IsPrimary = false }, Ct));
        mine.GetProperty("skills").GetArrayLength().ShouldBe(2);
        mine.GetProperty("skills").EnumerateArray().Single(row => row.GetProperty("skillId").GetGuid() == angular).GetProperty("source").GetString().ShouldBe("Self");
        await OkAsync(await self.PostAsJsonAsync("/api/v1/me/skills/certifications", new { Name = "Azure Developer Associate", Issuer = "Microsoft", ExpiresOn = Iso(Today.AddYears(1)) }, Ct));

        HttpResponseMessage badLevel = await admin.PutAsJsonAsync($"/api/v1/skills/employees/{org.Requester}", new { SkillId = dotnet, CurrentLevel = 7 }, Ct);
        (await CoreHrApi.ErrorCodeAsync(badLevel)).ShouldBe("SKILL_LEVEL");

        JsonElement[] matrix = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/skills/matrix", Ct))!;
        JsonElement row = matrix.Single(entry => entry.GetProperty("skillId").GetGuid() == dotnet);
        row.GetProperty("people").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        row.GetProperty("withGap").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        JsonElement[] people = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/skills/{dotnet}/people?minLevel=3", Ct))!;
        people.ShouldContain(person => person.GetProperty("employeeId").GetGuid() == org.Requester);
    }

    [Fact]
    public async Task AllocationsDetectOverallocation_AndTheCalendarShowsIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid first = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid second = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();

        (await AllocateAsync(admin, first, org.Requester, 60)).GetProperty("overallocated").GetBoolean().ShouldBeFalse();
        JsonElement over = await AllocateAsync(admin, second, org.Requester, 50);
        over.GetProperty("overallocated").GetBoolean().ShouldBeTrue();
        over.GetProperty("peakAllocationPercent").GetInt32().ShouldBe(110);

        JsonElement profile = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/resources/{org.Requester}", Ct);
        profile.GetProperty("current").GetArrayLength().ShouldBe(2);
        profile.GetProperty("capacity")[0].GetProperty("state").GetString().ShouldBe("Overallocated");

        JsonElement[] calendar = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/resources/calendar?projectId={first}&months=2", Ct))!;
        JsonElement mine = calendar.Single(row => row.GetProperty("employeeId").GetGuid() == org.Requester);
        mine.GetProperty("cells").GetArrayLength().ShouldBe(2);

        // Releasing the second brings them back to 60%.
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/resource-allocations/{over.GetProperty("memberId").GetGuid()}/release", new { On = Iso(Today.AddDays(-1)) }, Ct), HttpStatusCode.NoContent);
        JsonElement after = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/resources/{org.Requester}", Ct);
        after.GetProperty("current").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ARequest_RanksTheBestFit_AndAllocatingACandidateFillsTheSeat()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid dotnet = await SkillAsync(admin, "DOTNET");
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/skills/employees/{org.Deputy}", new { SkillId = dotnet, CurrentLevel = 5, YearsExperience = 8m, IsPrimary = true }, Ct));
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/skills/employees/{org.Finance}", new { SkillId = dotnet, CurrentLevel = 2, YearsExperience = 1m, IsPrimary = true }, Ct));

        JsonElement request = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/resource-requests", new
        {
            ProjectId = projectId,
            Title = "Senior .NET developer",
            Skills = new[] { new { SkillId = dotnet, MinLevel = 4 } },
            MinYearsExperience = 5m,
            Quantity = 1,
            AllocationPercent = 100,
            StartDate = Iso(Today.AddDays(7)),
            EndDate = Iso(Today.AddDays(90)),
            Billable = true,
            Priority = "High",
        }, Ct))).GetProperty("request");
        Guid requestId = request.GetProperty("id").GetGuid();

        JsonElement[] matches = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/resource-requests/{requestId}/matches?take=50", Ct))!;
        int deputyAt = Array.FindIndex(matches, match => match.GetProperty("employeeId").GetGuid() == org.Deputy);
        int financeAt = Array.FindIndex(matches, match => match.GetProperty("employeeId").GetGuid() == org.Finance);
        deputyAt.ShouldBeGreaterThanOrEqualTo(0);
        deputyAt.ShouldBeLessThan(financeAt < 0 ? int.MaxValue : financeAt, "the expert outranks the junior");
        matches[deputyAt].GetProperty("score").GetProperty("skills").GetDecimal().ShouldBe(100m);

        JsonElement shortlisted = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/resource-requests/{requestId}/candidates", new { EmployeeId = org.Deputy, Note = "Strong fit" }, Ct));
        Guid candidateId = shortlisted.GetProperty("candidates").EnumerateArray().Single().GetProperty("id").GetGuid();

        JsonElement filled = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/resource-requests/{requestId}/candidates/{candidateId}/allocate", new { }, Ct));
        filled.GetProperty("request").GetProperty("status").GetString().ShouldBe("Fulfilled");
        filled.GetProperty("candidates")[0].GetProperty("status").GetString().ShouldBe("Allocated");

        JsonElement[] team = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/team", Ct))!;
        team.ShouldContain(member => member.GetProperty("employeeId").GetGuid() == org.Deputy && member.GetProperty("allocationPercent").GetInt32() == 100);
    }

    [Fact]
    public async Task ATransfer_ReleasesFromOneProject_AndAllocatesOnTheOther()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid from = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid to = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid memberId = (await AllocateAsync(admin, from, org.Requester, 100)).GetProperty("memberId").GetGuid();

        JsonElement transfer = await OkAsync(await admin.PostAsJsonAsync("/api/v1/resources/transfers", new
        {
            FromMemberId = memberId,
            ToProjectId = to,
            AllocationPercent = 100,
            EffectiveDate = Iso(Today.AddDays(14)),
            Billable = true,
            Reason = "Client B needs a lead",
        }, Ct));
        transfer.GetProperty("status").GetString().ShouldBe("Completed", "no approval workflow is defined for transfers");

        JsonElement profile = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/resources/{org.Requester}", Ct);
        JsonElement current = profile.GetProperty("current").EnumerateArray().Single();
        current.GetProperty("projectId").GetGuid().ShouldBe(from);
        current.GetProperty("endDate").GetString().ShouldBe(Iso(Today.AddDays(13)));
        profile.GetProperty("upcoming").EnumerateArray().Single().GetProperty("projectId").GetGuid().ShouldBe(to);
    }

    [Fact]
    public async Task ATimesheet_IsBookedSubmittedAndApproved_AndItsHoursReachTasksAndUtilization()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-90)))).GetProperty("id").GetGuid();
        await AllocateAsync(admin, projectId, org.Requester, 100);
        Guid taskId = (await CreateTaskAsync(admin, projectId, NewTask("Build checkout", assigneeId: org.Requester))).GetProperty("id").GetGuid();
        Guid otherProject = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();

        DateOnly monday = PastWeek();
        string week = Iso(monday);
        using HttpClient self = await SelfServiceAsync(factory, org.Requester);

        JsonElement view = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={week}", Ct);
        view.GetProperty("targets").EnumerateArray().ShouldContain(target => target.GetProperty("projectId").GetGuid() == projectId);
        view.GetProperty("days").GetArrayLength().ShouldBe(7);

        HttpResponseMessage notOnProject = await self.PutAsJsonAsync($"/api/v1/me/timesheets/{week}", Week(monday, otherProject, null), Ct);
        (await CoreHrApi.ErrorCodeAsync(notOnProject)).ShouldBe("TIMESHEET_NOT_ON_PROJECT");

        await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{week}", Week(monday, projectId, taskId, hoursPerDay: 13, days: 1), Ct));
        HttpResponseMessage tooLong = await self.PostAsync($"/api/v1/me/timesheets/{week}/submit", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(tooLong)).ShouldBe("TIMESHEET_DAY_MAX");

        JsonElement saved = await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{week}", Week(monday, projectId, taskId), Ct));
        saved.GetProperty("timesheet").GetProperty("totalHours").GetDecimal().ShouldBe(40m);
        saved.GetProperty("issues").EnumerateArray().ShouldNotContain(issue => issue.GetProperty("level").GetString() == "Error");

        Guid timesheetId;
        await using (await DefinitionAsync(admin, [Stage("Manager", [Rule("Employee", org.Manager)])], subject: "timesheet.submission"))
        {
            JsonElement submitted = await OkAsync(await self.PostAsync($"/api/v1/me/timesheets/{week}/submit", null, Ct));
            submitted.GetProperty("status").GetString().ShouldBe("Submitted");

            HttpResponseMessage frozen = await self.PutAsJsonAsync($"/api/v1/me/timesheets/{week}", Week(monday, projectId, taskId), Ct);
            (await CoreHrApi.ErrorCodeAsync(frozen)).ShouldBe("TIMESHEET_FROZEN");

            using HttpClient manager = await PersonAsync(factory, org.Manager);
            await DecideAsync(manager, await TaskForAsync(manager, submitted.GetProperty("workflowInstanceId").GetGuid()), "approve");
            await factory.DeliverEventsAsync();

            JsonElement approved = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={week}", Ct);
            approved.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Approved");
            timesheetId = approved.GetProperty("timesheet").GetProperty("id").GetGuid();
        }

        await factory.DeliverEventsAsync();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/tasks/{taskId}", Ct)).GetProperty("actualHours").GetDecimal().ShouldBe(40m);

        JsonElement utilization = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/resources/utilization?from={week}&to={Iso(monday.AddDays(4))}&groupBy=Employee", Ct);
        JsonElement row = utilization.GetProperty("rows").EnumerateArray().Single(entry => entry.GetProperty("key").GetGuid() == org.Requester);
        row.GetProperty("billableHours").GetDecimal().ShouldBe(40m);
        row.GetProperty("availableHours").GetDecimal().ShouldBe(40m);
        row.GetProperty("utilizationPercent").GetDecimal().ShouldBe(100m);

        JsonElement[] projectHours = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/timesheets/projects/{projectId}?from={week}&to={Iso(monday.AddDays(6))}", Ct))!;
        projectHours.Sum(entry => entry.GetProperty("approvedHours").GetDecimal()).ShouldBe(40m);

        // A correction: reopened, the hours come off the task until approved again.
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/timesheets/{timesheetId}/reopen", new { Reason = "Wrong task" }, Ct), HttpStatusCode.NoContent);
        await factory.DeliverEventsAsync();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/tasks/{taskId}", Ct)).GetProperty("actualHours").GetDecimal().ShouldBe(0m);
        JsonElement reopened = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={week}", Ct);
        reopened.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Draft");
        reopened.GetProperty("timesheet").GetProperty("correctionCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ALockedPeriod_TakesNoChanges_AndMissingTimesheetsAreListed()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-90)))).GetProperty("id").GetGuid();
        await AllocateAsync(admin, projectId, org.Finance, 100);

        await OkAsync(await admin.PostAsJsonAsync("/api/v1/timesheets/settings/lock", new { Through = "2020-12-31" }, Ct));
        using HttpClient self = await SelfServiceAsync(factory, org.Finance);
        HttpResponseMessage locked = await self.PutAsJsonAsync("/api/v1/me/timesheets/2020-06-01", new
        {
            Entries = new[] { new { Date = "2020-06-01", Category = "Internal", Hours = 8m } },
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(locked)).ShouldBe("TIMESHEET_LOCKED");

        HttpResponseMessage backwards = await admin.PostAsJsonAsync("/api/v1/timesheets/settings/lock", new { Through = "2019-01-01" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(backwards)).ShouldBe("TIMESHEET_LOCK_BACKWARDS");

        JsonElement[] missing = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/timesheets/missing?week={Iso(PastWeek())}", Ct))!;
        missing.ShouldContain(row => row.GetProperty("employeeId").GetGuid() == org.Finance);
    }

    [Fact]
    public async Task TheBenchForecastAndDashboard_ReflectAllocationsAndOpenDemand()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid python = await SkillAsync(admin, "PYTHON");
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/skills/employees/{org.SkipLevel}", new { SkillId = python, CurrentLevel = 4, IsPrimary = true }, Ct));

        await OkAsync(await admin.PostAsJsonAsync("/api/v1/resource-requests", new
        {
            ProjectId = projectId,
            Title = "Python engineers",
            Skills = new[] { new { SkillId = python, MinLevel = 3 } },
            Quantity = 3,
            AllocationPercent = 100,
            StartDate = Iso(Today),
            EndDate = Iso(Today.AddDays(60)),
            Billable = true,
            Priority = "Medium",
        }, Ct));

        JsonElement[] bench = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/resources/bench", Ct))!;
        bench.ShouldContain(item => item.GetProperty("employeeId").GetGuid() == org.SkipLevel, "nobody has allocated them yet");

        JsonElement forecast = await admin.GetFromJsonAsync<JsonElement>("/api/v1/resources/forecast?months=2", Ct);
        JsonElement row = forecast.GetProperty("skills").EnumerateArray().Single(entry => entry.GetProperty("skillId").GetGuid() == python);
        JsonElement thisMonth = row.GetProperty("months")[0];
        thisMonth.GetProperty("demand").GetInt32().ShouldBeGreaterThanOrEqualTo(3);
        thisMonth.GetProperty("supply").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        forecast.GetProperty("capacity").GetArrayLength().ShouldBe(2);

        JsonElement dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/v1/resources/dashboard", Ct);
        dashboard.GetProperty("openDemand").GetInt32().ShouldBeGreaterThanOrEqualTo(3);
        dashboard.GetProperty("onBench").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }
}
