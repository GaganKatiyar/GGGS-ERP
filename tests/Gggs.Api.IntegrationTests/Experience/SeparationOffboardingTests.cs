using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Separation.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Separation v2 phase B end to end: the offboarding checklist laid out on approval, handover of
/// reports, project work and approvals to a successor, tasks closed by their owners, and the
/// leaver's sign-in switched off - by HR at once, or by the exit sweep.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SeparationOffboardingTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee =
    [
        (Permissions.Self.ResignationSubmit, DataScope.Own),
    ];

    private static JsonElement Find(JsonElement list, string property, string value)
        => list.EnumerateArray().First(row => row.GetProperty(property).GetString() == value);

    [Fact]
    public async Task Approval_lays_out_the_checklist_and_the_handover_moves_work_to_the_successor()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid leaver = await CoreHrApi.HireAsync(admin, department, managerId: manager);
        Guid report = await CoreHrApi.HireAsync(admin, department, managerId: leaver);
        Guid successor = await CoreHrApi.HireAsync(admin, department);
        Guid project = (await CreateProjectAsync(admin, Project())).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, project, leaver);
        Guid task = (await CreateTaskAsync(admin, project, NewTask("Write the spec", assigneeId: leaver))).GetProperty("id").GetGuid();
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("offboard"), leaver, Employee);
        using (admin)
        using (self)
        {
            Guid caseId = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Moving cities", ReasonCode = "RELOCATION" }, Ct))).GetProperty("case").GetProperty("id").GetGuid();

            JsonElement view = await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
            JsonElement offboarding = view.GetProperty("offboarding");
            JsonElement tasks = offboarding.GetProperty("tasks");
            tasks.GetArrayLength().ShouldBeGreaterThan(0);
            Find(tasks, "owner", "Manager").GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(manager);
            Find(tasks, "owner", "Employee").GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(leaver);
            Find(tasks, "owner", "Successor").TryGetProperty("assigneeEmployeeId", out _).ShouldBeFalse("nobody is named as successor yet");
            view.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("kind").GetString()).ShouldContain("offboarding-started");

            JsonElement handover = offboarding.GetProperty("handover");
            Find(handover, "kind", "DirectReport").GetProperty("toEmployeeId").GetGuid().ShouldBe(manager, "a report goes to the leaver's manager until a successor is named");
            Find(handover, "kind", "ProjectAllocation").GetProperty("needsRecipient").GetBoolean().ShouldBeFalse();
            Find(handover, "kind", "ProjectTasks").GetProperty("title").GetString().ShouldBe("Hand over 1 open project task(s)");

            // Naming the successor gives them their task and the items nobody was down to receive.
            JsonElement named = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/handover", new { SuccessorEmployeeId = successor, Note = "Takes over the spec" }, Ct));
            offboarding = named.GetProperty("offboarding");
            offboarding.GetProperty("successorEmployeeId").GetGuid().ShouldBe(successor);
            Find(offboarding.GetProperty("tasks"), "owner", "Successor").GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(successor);
            Find(offboarding.GetProperty("handover"), "kind", "ProjectTasks").GetProperty("toEmployeeId").GetGuid().ShouldBe(successor);
            (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/handover", new { SuccessorEmployeeId = leaver }, Ct))).ShouldBe("SEPARATION_SUCCESSOR");

            Guid reportItem = Find(offboarding.GetProperty("handover"), "kind", "DirectReport").GetProperty("id").GetGuid();
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync($"/api/v1/separation/handover/{reportItem}", new { ToEmployeeId = leaver }, Ct))).ShouldBe("HANDOVER_RECIPIENT");
            JsonElement moved = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/handover/{reportItem}", new { ToEmployeeId = successor }, Ct));
            Find(moved.GetProperty("offboarding").GetProperty("handover"), "kind", "DirectReport").GetProperty("status").GetString().ShouldBe("Done");
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{report}", Ct)).GetProperty("assignment").GetProperty("manager").GetProperty("id").GetGuid().ShouldBe(successor);

            Guid tasksItem = Find(moved.GetProperty("offboarding").GetProperty("handover"), "kind", "ProjectTasks").GetProperty("id").GetGuid();
            JsonElement handed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/handover/{tasksItem}", new { }, Ct));
            Find(handed.GetProperty("offboarding").GetProperty("handover"), "kind", "ProjectTasks").GetProperty("outcome").GetString()!.ShouldStartWith("1 task(s) moved");
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks/{task}", Ct)).GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(successor);

            Guid seatItem = Find(handed.GetProperty("offboarding").GetProperty("handover"), "kind", "ProjectAllocation").GetProperty("id").GetGuid();
            JsonElement released = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/handover/{seatItem}", new { }, Ct));
            Find(released.GetProperty("offboarding").GetProperty("handover"), "kind", "ProjectAllocation").GetProperty("status").GetString().ShouldBe("Done");

            // The leaver closes their own steps from self-service; required ones cannot be skipped.
            JsonElement mine = await OkAsync(await self.GetAsync("/api/v1/separation/offboarding/tasks", Ct));
            JsonElement own = Find(mine, "owner", "Employee");
            own.GetProperty("canAct").GetBoolean().ShouldBeTrue();
            Guid ownId = own.GetProperty("id").GetGuid();
            (await CoreHrApi.ErrorCodeAsync(await self.PostAsJsonAsync($"/api/v1/separation/offboarding/tasks/{ownId}", new { Skipped = true }, Ct))).ShouldBe("OFFBOARDING_TASK_REQUIRED");
            await OkAsync(await self.PostAsJsonAsync($"/api/v1/separation/offboarding/tasks/{ownId}", new { Skipped = false, Notes = "Walked through with the successor" }, Ct));
            JsonElement mySeparation = await OkAsync(await self.GetAsync("/api/v1/me/separation", Ct));
            mySeparation.GetProperty("offboarding").GetProperty("handover").GetArrayLength().ShouldBe(0, "handover is HR's view");
            mySeparation.GetProperty("offboarding").GetProperty("tasks").EnumerateArray().First(row => row.GetProperty("id").GetGuid() == ownId).GetProperty("status").GetString().ShouldBe("Done");

            Guid managerTask = Find(offboarding.GetProperty("tasks"), "owner", "Manager").GetProperty("id").GetGuid();
            (await self.PostAsJsonAsync($"/api/v1/separation/offboarding/tasks/{managerTask}", new { Skipped = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_template_for_the_employment_type_beats_the_default()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        using (admin)
        {
            (await OkAsync(await admin.GetAsync("/api/v1/separation/offboarding/templates", Ct))).EnumerateArray().ShouldContain(row => row.GetProperty("isDefault").GetBoolean());
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/separation/offboarding/templates", new
            {
                Name = "Broken",
                IsDefault = false,
                IsActive = true,
                Steps = new[] { new { Title = "Too late", Owner = "Hr", DueOffsetDays = 90, Required = true } },
            }, Ct))).ShouldBe("OFFBOARDING_STEPS");

            JsonElement fullTime = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/offboarding/templates", new
            {
                Name = "Full-time offboarding",
                EmploymentTypeCode = "full_time",
                IsDefault = false,
                IsActive = true,
                Steps = new[] { new { Title = "Collect the handover report", Owner = "Manager", DueOffsetDays = -2, Required = true } },
            }, Ct));
            Guid template = fullTime.GetProperty("id").GetGuid();
            try
            {
                Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
                JsonElement started = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
                {
                    EmployeeId = person,
                    Type = "ContractEnd",
                    Reason = "Fixed term over",
                    NoticeDate = Iso(Today),
                    LastWorkingDay = Iso(Today.AddDays(10)),
                }, Ct));
                JsonElement tasks = started.GetProperty("offboarding").GetProperty("tasks");
                tasks.GetArrayLength().ShouldBe(1);
                tasks[0].GetProperty("title").GetString().ShouldBe("Collect the handover report");
                tasks[0].GetProperty("dueDate").GetString().ShouldBe(Iso(Today.AddDays(8)));

                // Moving the last day moves the open tasks with it.
                Guid caseId = started.GetProperty("case").GetProperty("id").GetGuid();
                JsonElement moved = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/terms", new { LastWorkingDay = Iso(Today.AddDays(20)), ShortfallWaived = true, EligibleForRehire = true }, Ct));
                moved.GetProperty("offboarding").GetProperty("tasks")[0].GetProperty("dueDate").GetString().ShouldBe(Iso(Today.AddDays(18)));
            }
            finally
            {
                await admin.DeleteAsync($"/api/v1/separation/offboarding/templates/{template}", Ct);
            }
        }
    }

    [Fact]
    public async Task Hr_can_switch_access_off_at_once_and_the_exit_does_it_anyway()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid dismissed = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("dismissed"), dismissed, Employee);
        using (admin)
        using (self)
        {
            Guid caseId = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Leaving" }, Ct))).GetProperty("case").GetProperty("id").GetGuid();
            JsonElement off = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/access/disable", null, Ct));
            off.GetProperty("offboarding").TryGetProperty("accessDisabledAt", out _).ShouldBeTrue();
            off.GetProperty("offboarding").GetProperty("accountStatus").GetString().ShouldBe("Disabled");
            (await self.GetAsync("/api/v1/me/separation", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        HttpClient hr = await factory.CreateAdminClientAsync();
        Guid leaving = await CoreHrApi.HireAsync(hr, await CoreHrApi.DepartmentAsync(hr));
        HttpClient leaver = await factory.CreateScopedUserClientAsync(Email("leaving"), leaving, Employee);
        using (hr)
        using (leaver)
        {
            Guid caseId = (await OkAsync(await hr.PostAsJsonAsync("/api/v1/separation/cases", new
            {
                EmployeeId = leaving,
                Type = "ContractEnd",
                Reason = "Contract ended",
                NoticeDate = Iso(Today.AddDays(-30)),
                LastWorkingDay = Iso(Today.AddDays(-1)),
            }, Ct))).GetProperty("case").GetProperty("id").GetGuid();
            (await leaver.GetAsync("/api/v1/me/separation", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<SeparationService>().SweepAsync(Ct));

            JsonElement done = await OkAsync(await hr.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
            done.GetProperty("case").GetProperty("status").GetString().ShouldBe("Completed");
            done.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("kind").GetString()).ShouldContain("access-disabled");
            (await leaver.GetAsync("/api/v1/me/separation", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }
    }
}
