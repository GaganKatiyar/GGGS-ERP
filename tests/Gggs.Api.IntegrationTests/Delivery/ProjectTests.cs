using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ProjectTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProject_StartsWithABoard_AndRecordsItsLifecycle()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid clientId = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();
        Guid contractId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId, "Sow"), Ct), HttpStatusCode.Created)).GetProperty("contract").GetProperty("id").GetGuid();

        JsonElement project = await CreateProjectAsync(admin, Project(clientId, contractId, org.Manager));
        Guid projectId = project.GetProperty("id").GetGuid();
        project.GetProperty("clientName").GetString().ShouldNotBeNullOrEmpty();
        project.GetProperty("stage").GetString().ShouldBe("Planning");
        project.GetProperty("projectManagerName").GetString().ShouldNotBeNullOrEmpty();

        JsonElement board = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/board", Ct);
        board.GetProperty("columns").GetArrayLength().ShouldBe(7);

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/stage", new { Stage = "Execution", Comment = "Kick-off done" }, Ct));
        JsonElement closed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/status", new { Status = "Completed" }, Ct));
        closed.GetProperty("project").GetProperty("actualEndDate").GetString().ShouldNotBeNull();

        JsonElement[] history = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/history", Ct))!;
        history.Select(row => row.GetProperty("toValue").GetString()).ShouldContain("Execution");
        history.Select(row => row.GetProperty("toValue").GetString()).ShouldContain("Completed");

        HttpResponseMessage stageWhenClosed = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/stage", new { Stage = "Support" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(stageWhenClosed)).ShouldBe("PROJECT_CLOSED");

        Guid otherClient = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();
        HttpResponseMessage mismatched = await admin.PostAsJsonAsync("/api/v1/projects", Project(otherClient, contractId), Ct);
        (await CoreHrApi.ErrorCodeAsync(mismatched)).ShouldBe("PROJECT_CONTRACT_CLIENT");

        HttpResponseMessage reused = await admin.PostAsJsonAsync("/api/v1/projects", Project(code: project.GetProperty("code").GetString()!), Ct);
        (await CoreHrApi.ErrorCodeAsync(reused)).ShouldBe("PROJECT_CODE_IN_USE");
    }

    [Fact]
    public async Task Tasks_FollowTheHierarchy_TheBoard_AndRefuseDependencyLoops()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        JsonElement member = await AddMemberAsync(admin, projectId, org.Requester);
        member.GetProperty("roleName").GetString().ShouldBe("Developer");

        JsonElement epic = await CreateTaskAsync(admin, projectId, NewTask("Checkout", "Epic"));
        epic.GetProperty("code").GetString()!.ShouldEndWith("-1");
        JsonElement story = await CreateTaskAsync(admin, projectId, NewTask("Pay by card", "Story", epic.GetProperty("id").GetGuid(), org.Requester, points: 5));
        story.GetProperty("parentCode").GetString().ShouldBe(epic.GetProperty("code").GetString());
        story.GetProperty("category").GetString().ShouldBe("Backlog");

        HttpResponseMessage subtaskUnderEpic = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", NewTask("Wrong", "Subtask", epic.GetProperty("id").GetGuid()), Ct);
        (await CoreHrApi.ErrorCodeAsync(subtaskUnderEpic)).ShouldBe("PROJECT_TASK_HIERARCHY");

        HttpResponseMessage outsider = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", NewTask("Nobody's", assigneeId: org.Finance), Ct);
        (await CoreHrApi.ErrorCodeAsync(outsider)).ShouldBe("PROJECT_TASK_ASSIGNEE");

        JsonElement build = await CreateTaskAsync(admin, projectId, NewTask("Build API", parentId: story.GetProperty("id").GetGuid()));
        JsonElement test = await CreateTaskAsync(admin, projectId, NewTask("Test API", parentId: story.GetProperty("id").GetGuid()));
        Guid buildId = build.GetProperty("id").GetGuid();
        Guid testId = test.GetProperty("id").GetGuid();

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks/{testId}/dependencies", new { PredecessorTaskId = buildId, Type = "FinishToStart" }, Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/tasks/{testId}", Ct)).GetProperty("openPredecessors").GetInt32().ShouldBe(1);

        HttpResponseMessage loop = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks/{buildId}/dependencies", new { PredecessorTaskId = testId, Type = "FinishToStart" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(loop)).ShouldBe("PROJECT_DEPENDENCY_CYCLE");

        Guid done = await ColumnAsync(admin, projectId, "Done");
        JsonElement moved = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks/{buildId}/move", new { ColumnId = done, Index = 0 }, Ct));
        moved.GetProperty("completedDate").GetString().ShouldNotBeNull();
        moved.GetProperty("progress").GetInt32().ShouldBe(100);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/tasks/{testId}", Ct)).GetProperty("openPredecessors").GetInt32().ShouldBe(0);

        Guid backlog = await ColumnAsync(admin, projectId, "Backlog");
        JsonElement reopened = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks/{buildId}/move", new { ColumnId = backlog }, Ct));
        IsAbsent(reopened, "completedDate").ShouldBeTrue();

        HttpResponseMessage parentDelete = await admin.DeleteAsync($"/api/v1/projects/{projectId}/tasks/{story.GetProperty("id").GetGuid()}", Ct);
        (await CoreHrApi.ErrorCodeAsync(parentDelete)).ShouldBe("PROJECT_TASK_HAS_CHILDREN");

        using HttpClient requester = await MemberAsync(factory, org.Requester);
        JsonElement mine = await requester.GetFromJsonAsync<JsonElement>("/api/v1/project-tasks", Ct);
        mine.GetProperty("items").EnumerateArray().ShouldContain(row => row.GetProperty("id").GetGuid() == story.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ASprint_RecordsItsVelocity_AndSendsUnfinishedWorkBack()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();

        JsonElement sprint = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/sprints", new
        {
            Name = "Sprint 1",
            StartDate = Iso(Today.AddDays(-5)),
            EndDate = Iso(Today.AddDays(9)),
            Goal = "Card payments",
            CapacityPoints = 20,
        }, Ct));
        Guid sprintId = sprint.GetProperty("id").GetGuid();

        Guid first = (await CreateTaskAsync(admin, projectId, NewTask("Card form", "Story", points: 5))).GetProperty("id").GetGuid();
        Guid second = (await CreateTaskAsync(admin, projectId, NewTask("3-D Secure", "Story", points: 8))).GetProperty("id").GetGuid();
        JsonElement planned = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/sprints/{sprintId}/tasks", new { TaskIds = new[] { first, second } }, Ct));
        planned.GetProperty("plannedPoints").GetInt32().ShouldBe(13);

        JsonElement started = await OkAsync(await admin.PostAsync($"/api/v1/projects/{projectId}/sprints/{sprintId}/start", null, Ct));
        started.GetProperty("committedPoints").GetInt32().ShouldBe(13);

        Guid done = await ColumnAsync(admin, projectId, "Done");
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks/{first}/move", new { ColumnId = done }, Ct));

        JsonElement[] burndown = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/sprints/{sprintId}/burndown", Ct))!;
        burndown.Length.ShouldBe(15);
        burndown.Last(point => !IsAbsent(point, "remaining")).GetProperty("remaining").GetInt32().ShouldBe(8);

        HttpResponseMessage secondSprint = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/sprints", new { Name = "Sprint 2", StartDate = Iso(Today.AddDays(10)), EndDate = Iso(Today.AddDays(23)) }, Ct);
        Guid nextId = (await OkAsync(secondSprint)).GetProperty("id").GetGuid();
        HttpResponseMessage twoRunning = await admin.PostAsync($"/api/v1/projects/{projectId}/sprints/{nextId}/start", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(twoRunning)).ShouldBe("PROJECT_SPRINT_RUNNING");

        JsonElement completed = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/sprints/{sprintId}/complete", new { MoveOpenTasksToSprintId = nextId }, Ct));
        completed.GetProperty("completedPoints").GetInt32().ShouldBe(5);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/tasks/{second}", Ct)).GetProperty("sprintId").GetGuid().ShouldBe(nextId);

        JsonElement[] velocity = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/velocity", Ct))!;
        velocity.Single().GetProperty("completed").GetInt32().ShouldBe(5);
        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/sprints/{sprintId}/burndown", Ct))!.Length.ShouldBe(15, "the burndown is kept once the sprint closes");
    }

    [Fact]
    public async Task AChangeRequest_GoesThroughApproval_ThenImplementationToClosure()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();

        JsonElement draft = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/change-requests", new
        {
            Title = "Add UPI payments",
            Description = "The client wants UPI alongside cards.",
            Reason = "Market demand",
            Impact = "Payments service and checkout UI",
            EstimatedHours = 120m,
            EstimatedCost = 300_000m,
            ScheduleImpactDays = 10,
            Priority = "High",
            RequestedByName = "Asha Rao (client)",
        }, Ct));
        draft.GetProperty("code").GetString().ShouldBe("CR-1");
        Guid crId = draft.GetProperty("id").GetGuid();

        await using (await DefinitionAsync(admin, [Stage("Delivery head", [Rule("Employee", org.Finance)])], subject: "project.change-request"))
        {
            JsonElement submitted = await OkAsync(await admin.PostAsync($"/api/v1/projects/{projectId}/change-requests/{crId}/submit", null, Ct));
            submitted.GetProperty("status").GetString().ShouldBe("PendingApproval");

            HttpResponseMessage frozen = await admin.PutAsJsonAsync($"/api/v1/projects/{projectId}/change-requests/{crId}", new { Title = "x", Description = "y", Priority = "Low" }, Ct);
            (await CoreHrApi.ErrorCodeAsync(frozen)).ShouldBe("PROJECT_CR_FROZEN");

            using HttpClient approver = await PersonAsync(factory, org.Finance);
            await DecideAsync(approver, await TaskForAsync(approver, submitted.GetProperty("workflowInstanceId").GetGuid()), "approve");
            await factory.DeliverEventsAsync();
        }

        JsonElement[] list = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/change-requests", Ct))!;
        list.Single().GetProperty("status").GetString().ShouldBe("Approved");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/change-requests/{crId}/client-approval", new { On = Iso(Today), Reference = "PO amendment 7" }, Ct));
        HttpResponseMessage skipped = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/change-requests/{crId}/advance", new { Status = "Testing" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(skipped)).ShouldBe("PROJECT_CR_STATE");

        foreach (string step in new[] { "Implementing", "Testing", "Closed" })
        {
            JsonElement advanced = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/change-requests/{crId}/advance", new { Status = step }, Ct));
            advanced.GetProperty("status").GetString().ShouldBe(step);
        }

        JsonElement health = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/health", Ct);
        health.GetProperty("current").GetProperty("scope").GetString().ShouldBe("Amber", "an approved change added ten days");
    }

    [Fact]
    public async Task TheRaidLog_ScoresRisks_AndFeedsProjectHealth()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, projectId, org.Requester);

        JsonElement risk = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/raid", new
        {
            Kind = "Risk",
            Title = "Payment gateway certification may slip",
            Probability = 4,
            Impact = 5,
            Priority = "High",
            OwnerEmployeeId = org.Manager,
            Mitigation = "Start certification in sprint 1",
        }, Ct));
        risk.GetProperty("code").GetString().ShouldBe("RSK-1");
        risk.GetProperty("score").GetInt32().ShouldBe(20);

        HttpResponseMessage noSeverity = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/raid", new { Kind = "Issue", Title = "Build broken", Priority = "High" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(noSeverity)).ShouldBe("PROJECT_ISSUE_SEVERITY");

        JsonElement issue = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/raid", new { Kind = "Issue", Title = "Staging is down", Severity = "High", Priority = "High", DueDate = Iso(Today.AddDays(-2)) }, Ct));
        issue.GetProperty("code").GetString().ShouldBe("ISS-2");
        issue.GetProperty("isOverdue").GetBoolean().ShouldBeTrue();

        JsonElement health = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/health", Ct)).GetProperty("current");
        health.GetProperty("risk").GetString().ShouldBe("Red");
        health.GetProperty("issues").GetString().ShouldBe("Amber");
        health.GetProperty("budget").GetString().ShouldBe("NotAssessed");
        health.GetProperty("resources").GetString().ShouldBe("Green");

        JsonElement assessed = await OkAsync(await admin.PostAsync($"/api/v1/projects/{projectId}/health/assess", null, Ct));
        assessed.GetProperty("trend").GetArrayLength().ShouldBe(1);
        JsonElement detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}", Ct);
        detail.GetProperty("project").GetProperty("healthStatus").GetString().ShouldBe(assessed.GetProperty("current").GetProperty("overall").GetString());

        JsonElement[] risks = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/project-risks", Ct))!;
        risks.ShouldContain(row => row.GetProperty("id").GetGuid() == risk.GetProperty("id").GetGuid());

        JsonElement dashboard = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/dashboard", Ct);
        dashboard.GetProperty("raid").GetProperty("highRisks").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ATeamMember_WorksOnTheirProject_AndNobodyElsesIsVisible()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid mineId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid otherId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, mineId, org.Requester);

        using HttpClient member = await MemberAsync(factory, org.Requester);
        JsonElement detail = await member.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{mineId}", Ct);
        detail.GetProperty("can").GetProperty("manageTasks").GetBoolean().ShouldBeTrue();
        detail.GetProperty("can").GetProperty("manage").GetBoolean().ShouldBeFalse();

        JsonElement[] team = (await member.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{mineId}/team", Ct))!;
        IsAbsent(team.Single(), "billingRate").ShouldBeTrue("rates are for those who run the project");

        await CreateTaskAsync(member, mineId, NewTask("Write tests", assigneeId: org.Requester));
        HttpResponseMessage edit = await member.PutAsJsonAsync($"/api/v1/projects/{mineId}", Project(), Ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await member.GetAsync($"/api/v1/projects/{otherId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/projects/{otherId}/tasks", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        JsonElement listed = await member.GetFromJsonAsync<JsonElement>("/api/v1/projects", Ct);
        listed.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ShouldBe([mineId]);

        JsonElement[] memberships = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/by-employee/{org.Requester}", Ct))!;
        memberships.Single().GetProperty("projectId").GetGuid().ShouldBe(mineId);
    }

    [Fact]
    public async Task ProjectDocuments_FollowTheProjectTeam()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid projectId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid otherId = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, projectId, org.Requester);
        Guid sow = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/documents/types", Ct))!.First(type => type.GetProperty("code").GetString() == "SOW").GetProperty("id").GetGuid();

        using HttpClient member = await MemberAsync(factory, org.Requester);
        HttpResponseMessage uploaded = await UploadAsync(member, projectId, sow);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(Ct));

        JsonElement page = await member.GetFromJsonAsync<JsonElement>($"/api/v1/documents?ownerType=Project&ownerId={projectId}", Ct);
        JsonElement document = page.GetProperty("items").EnumerateArray().Single();
        document.GetProperty("ownerName").GetString()!.ShouldContain("Project");

        (await UploadAsync(member, otherId, sow)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/documents?ownerType=Project&ownerId={otherId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>The API leaves nulls out of its JSON.</summary>
    private static bool IsAbsent(JsonElement element, string property)
        => !element.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null;

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid projectId, Guid typeId)
    {
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Project"), "ownerType" },
            { new StringContent(projectId.ToString()), "ownerId" },
            { new StringContent(typeId.ToString()), "documentTypeId" },
            { new StringContent("Signed SOW"), "title" },
            { file, "file", "sow.pdf" },
        };

        return await client.PostAsync("/api/v1/documents", form, Ct);
    }
}
