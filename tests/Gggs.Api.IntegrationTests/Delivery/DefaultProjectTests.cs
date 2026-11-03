using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class DefaultProjectTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ThisMonday => Iso(Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)));

    private Task<HttpClient> SelfAsync(Guid employeeId)
        => factory.CreateScopedUserClientAsync(Email("self"), employeeId, (Permissions.Self.TimesheetSubmit, DataScope.Own));

    private static async Task<JsonElement[]> EnsureAsync(HttpClient admin)
        => (await OkAsync(await admin.PostAsync("/api/v1/projects/default-projects/ensure", null, Ct))).EnumerateArray().ToArray();

    private static JsonElement RowFor(JsonElement[] rows, Guid? departmentId)
        => rows.First(row => (row.TryGetProperty("departmentId", out JsonElement id) ? id.GetGuid() : (Guid?)null) == departmentId);

    private static object Booking(Guid projectId, Guid taskId, decimal hours, string category = "Internal")
        => new
        {
            Entries = new[] { new { Date = Iso(Today), ProjectId = projectId, TaskId = taskId, Category = category, Hours = hours } },
            Comment = (string?)null,
        };

    [Fact]
    public async Task AJoiner_BooksToTheirDepartmentsDefault_AndIsStillOnTheBench()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid joiner = await CoreHrApi.HireAsync(admin, department, email: Email("joiner"));
        Guid projectId = RowFor(await EnsureAsync(admin), department).GetProperty("projectId").GetGuid();

        using HttpClient self = await SelfAsync(joiner);
        JsonElement view = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={ThisMonday}", Ct);
        JsonElement target = view.GetProperty("targets").EnumerateArray().First(row => row.GetProperty("projectId").GetGuid() == projectId);
        target.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        target.GetProperty("billable").GetBoolean().ShouldBeFalse();
        JsonElement standUp = target.GetProperty("tasks").EnumerateArray().First(task => task.GetProperty("title").GetString() == "Stand-up meetings");
        standUp.GetProperty("activity").GetString().ShouldBe("Meetings");

        JsonElement saved = await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/timesheets/{ThisMonday}", Booking(projectId, standUp.GetProperty("taskId").GetGuid(), 2m), Ct));
        saved.GetProperty("timesheet").GetProperty("totalHours").GetDecimal().ShouldBe(2m);

        JsonElement[] bench = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/resources/bench", Ct))!;
        bench.ShouldContain(item => item.GetProperty("employeeId").GetGuid() == joiner, "a default project is not an allocation");
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/team", Ct)).GetArrayLength().ShouldBe(0);

        JsonElement[] bank = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/timesheets/bank", Ct))!;
        JsonElement line = bank.Single().GetProperty("projects").EnumerateArray().Single();
        line.GetProperty("projectId").GetGuid().ShouldBe(projectId);
        line.GetProperty("hours").GetDecimal().ShouldBe(2m);
    }

    [Fact]
    public async Task SomeoneCannotBookToAnotherDepartmentsDefault()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid mine = await CoreHrApi.DepartmentAsync(admin);
        Guid theirs = await CoreHrApi.DepartmentAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, mine, email: Email("person"));
        JsonElement[] rows = await EnsureAsync(admin);
        Guid theirProject = RowFor(rows, theirs).GetProperty("projectId").GetGuid();
        Guid theirTask = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{theirProject}/tasks?pageSize=50", Ct)).GetProperty("items").EnumerateArray()
            .First(task => task.GetProperty("type").GetString() == "Task").GetProperty("id").GetGuid();

        using HttpClient self = await SelfAsync(person);
        HttpResponseMessage response = await self.PutAsJsonAsync($"/api/v1/me/timesheets/{ThisMonday}", Booking(theirProject, theirTask, 1m), Ct);

        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("TIMESHEET_NOT_ON_PROJECT");
    }

    [Fact]
    public async Task ANewDepartment_InheritsItsParentsDefault_UntilItGetsItsOwn()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid parent = await CoreHrApi.DepartmentAsync(admin);
        Guid parentProject = RowFor(await EnsureAsync(admin), parent).GetProperty("projectId").GetGuid();
        Guid child = await CoreHrApi.DepartmentAsync(admin, parentId: parent);
        Guid joiner = await CoreHrApi.HireAsync(admin, child, email: Email("joiner"));

        JsonElement inherited = RowFor((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/projects/default-projects", Ct))!, child);
        inherited.GetProperty("projectId").GetGuid().ShouldBe(parentProject);
        inherited.GetProperty("isInherited").GetBoolean().ShouldBeTrue();

        using HttpClient self = await SelfAsync(joiner);
        (await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={ThisMonday}", Ct)).GetProperty("targets").EnumerateArray()
            .ShouldContain(target => target.GetProperty("projectId").GetGuid() == parentProject);

        JsonElement own = RowFor(await EnsureAsync(admin), child);
        own.GetProperty("projectId").GetGuid().ShouldNotBe(parentProject);
        own.GetProperty("isInherited").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task SomeoneWhoTransfers_BooksToTheDepartmentTheySatInThatWeek()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid before = await CoreHrApi.DepartmentAsync(admin);
        Guid after = await CoreHrApi.DepartmentAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, before, email: Email("person"));
        DateOnly thisMonday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        DateOnly weeksAgo = thisMonday.AddDays(-28);
        (await admin.PostAsJsonAsync($"/api/v1/employees/{person}/assignment-changes", new
        {
            ChangeType = "Transfer",
            EffectiveFrom = Iso(thisMonday.AddDays(-7)),
            Reason = "Team restructure",
            Assignment = new { DepartmentId = after, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "OFFICE", Designation = "Engineer" },
        }, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        JsonElement[] rows = await EnsureAsync(admin);
        Guid beforeProject = RowFor(rows, before).GetProperty("projectId").GetGuid();
        Guid afterProject = RowFor(rows, after).GetProperty("projectId").GetGuid();

        using HttpClient self = await SelfAsync(person);
        async Task<Guid[]> OfferedAsync(DateOnly week) => [.. (await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={Iso(week)}", Ct)).GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("projectId").GetGuid())];

        Guid[] then = await OfferedAsync(weeksAgo);
        then.ShouldContain(beforeProject);
        then.ShouldNotContain(afterProject);
        Guid[] now = await OfferedAsync(thisMonday);
        now.ShouldContain(afterProject);
        now.ShouldNotContain(beforeProject);
    }

    [Fact]
    public async Task EnsuringTwice_CreatesNothingMore()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid first = RowFor(await EnsureAsync(admin), department).GetProperty("projectId").GetGuid();

        RowFor(await EnsureAsync(admin), department).GetProperty("projectId").GetGuid().ShouldBe(first);
    }

    [Fact]
    public async Task DefaultProjects_StayOutOfThePortfolio_AndCannotBeMadeByHand()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        JsonElement row = RowFor(await EnsureAsync(admin), department);
        Guid projectId = row.GetProperty("projectId").GetGuid();
        string code = row.GetProperty("projectCode").GetString()!;

        // Searched by code: the tests make many departments, and this one may not be on the first page.
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects?search={code}&pageSize=200", Ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(project => project.GetProperty("id").GetGuid() == projectId);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects?type=DepartmentDefault&search={code}&pageSize=200", Ct)).GetProperty("items").EnumerateArray()
            .ShouldContain(project => project.GetProperty("id").GetGuid() == projectId);

        HttpResponseMessage byHand = await admin.PostAsJsonAsync("/api/v1/projects", new
        {
            Code = "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Name = "By hand",
            Type = "DepartmentDefault",
            StartDate = Iso(Today),
            EndDate = Iso(Today.AddDays(30)),
            Priority = "Medium",
            BillingType = "NonBillable",
            Currency = "INR",
            Confidentiality = "Internal",
            Methodology = "Agile",
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(byHand)).ShouldBe("PROJECT_DEFAULT_TYPE");
    }

    [Fact]
    public async Task ATaskOpenToTheTeam_CanBeBookedByAnyoneOnTheProject_NotJustItsAssignee()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-30)))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, projectId, org.Requester);
        Guid mine = (await CreateTaskAsync(admin, projectId, NewTask("Someone else's task", assigneeId: org.Manager))).GetProperty("id").GetGuid();
        Guid columnId = await ColumnAsync(admin, projectId, "Todo");
        JsonElement open = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
        {
            Type = "Task",
            ColumnId = columnId,
            Title = "Team meetings",
            Priority = "Medium",
            OpenToTeam = true,
        }, Ct));
        open.GetProperty("openToTeam").GetBoolean().ShouldBeTrue();

        using HttpClient self = await SelfAsync(org.Requester);
        JsonElement target = (await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={ThisMonday}", Ct)).GetProperty("targets").EnumerateArray()
            .First(row => row.GetProperty("projectId").GetGuid() == projectId);
        Guid[] offered = [.. target.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("taskId").GetGuid())];

        offered.ShouldContain(open.GetProperty("id").GetGuid());
        offered.ShouldNotContain(mine);
    }

    [Fact]
    public async Task RequiringEveryone_ChasesPeopleWithNoAllocation()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, department, email: Email("person"));
        JsonElement rules = await admin.GetFromJsonAsync<JsonElement>("/api/v1/timesheets/settings", Ct);

        async Task<bool> IsMissingAsync() => (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/timesheets/missing?week={ThisMonday}", Ct))!
            .Any(row => row.GetProperty("employeeId").GetGuid() == person);

        (await IsMissingAsync()).ShouldBeFalse("nobody asked for it, and they are on no project");
        try
        {
            await OkAsync(await admin.PutAsJsonAsync("/api/v1/timesheets/settings", Rules(rules, requireFromEveryone: true), Ct));
            (await IsMissingAsync()).ShouldBeTrue();
        }
        finally
        {
            await OkAsync(await admin.PutAsJsonAsync("/api/v1/timesheets/settings", Rules(rules, requireFromEveryone: false), Ct));
        }
    }

    private static object Rules(JsonElement rules, bool requireFromEveryone) => new
    {
        StandardHoursPerDay = rules.GetProperty("standardHoursPerDay").GetDecimal(),
        MinHoursPerDay = rules.GetProperty("minHoursPerDay").GetDecimal(),
        MaxHoursPerDay = rules.GetProperty("maxHoursPerDay").GetDecimal(),
        MinWeeklyHours = rules.GetProperty("minWeeklyHours").GetDecimal(),
        EnforceWeeklyMinimum = rules.GetProperty("enforceWeeklyMinimum").GetBoolean(),
        AllowWeekendEntries = rules.GetProperty("allowWeekendEntries").GetBoolean(),
        AllowHolidayEntries = rules.GetProperty("allowHolidayEntries").GetBoolean(),
        SubmissionDeadlineDays = rules.GetProperty("submissionDeadlineDays").GetInt32(),
        LockedThrough = (DateOnly?)null,
        RequireFromEveryone = requireFromEveryone,
    };
}
