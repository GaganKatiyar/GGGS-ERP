using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class TaskCollaborationTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TestOrg Org, Guid Project)> StaffedAsync(HttpClient admin)
    {
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Project(managerId: org.Manager, start: Today.AddDays(-30)))).GetProperty("id").GetGuid();
        await AddMemberAsync(admin, project, org.Requester);
        await AddMemberAsync(admin, project, org.Deputy);
        return (org, project);
    }

    private static async Task<Guid> TaskIdAsync(HttpClient client, Guid project, string title = "Build it", string type = "Task", Guid? assignee = null)
        => (await CreateTaskAsync(client, project, NewTask(title, type, assigneeId: assignee))).GetProperty("id").GetGuid();

    private static Task<JsonElement> ConversationAsync(HttpClient client, Guid project, Guid task)
        => client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks/{task}/conversation", Ct);

    private static async Task<JsonElement> CommentAsync(HttpClient client, Guid project, Guid task, string body, params Guid[] mentioned)
        => await OkAsync(await client.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/comments", new { Body = body, MentionedEmployeeIds = mentioned }, Ct));

    private async Task<string> InboxAsync(Guid employee)
    {
        await factory.DeliverEventsAsync();
        using HttpClient client = await PersonAsync(factory, employee);
        return (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).ToString();
    }

    // ----- Comments -------------------------------------------------------------------------------

    [Fact]
    public async Task ATeamMember_CanComment_AndWatchesFromThenOn()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient member = await MemberAsync(factory, org.Requester);

        JsonElement conversation = await CommentAsync(member, project, task, "  Looks good to me  ");

        JsonElement comment = conversation.GetProperty("comments").EnumerateArray().Single();
        comment.GetProperty("body").GetString().ShouldBe("Looks good to me");
        comment.GetProperty("authorName").GetString().ShouldNotBeNullOrEmpty();
        comment.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        conversation.GetProperty("isWatching").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task AMention_TellsThePersonNamed_AndAComment_TellsWhoeverIsInvolved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project, "Tune the model", assignee: org.Deputy);
        using HttpClient member = await MemberAsync(factory, org.Requester);

        await CommentAsync(member, project, task, "@Manager please check", org.Manager);

        (await InboxAsync(org.Manager)).ShouldContain("mentioned you");
        string assignee = await InboxAsync(org.Deputy);
        assignee.ShouldContain("commented on");
        assignee.ShouldNotContain("please check", customMessage: "what is said stays behind the sign-in");
    }

    [Fact]
    public async Task NamingSomeoneOffTheProject_DoesNotReachThem()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient member = await MemberAsync(factory, org.Requester);

        await CommentAsync(member, project, task, "Anyone?", org.Finance);

        (await InboxAsync(org.Finance)).ShouldNotContain("mentioned you");
    }

    [Fact]
    public async Task SomeoneOffTheProject_CannotSeeOrComment()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient outsider = await MemberAsync(factory, org.Finance);

        (await outsider.GetAsync($"/api/v1/projects/{project}/tasks/{task}/conversation", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/comments", new { Body = "Hello" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATaskOnAnotherProject_CannotBeReachedThroughThisOne()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid other = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid task = await TaskIdAsync(admin, other);
        using HttpClient member = await MemberAsync(factory, org.Requester);

        (await member.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/comments", new { Body = "Hello" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyTheAuthor_CanEdit_AndAManagerCanAlsoDelete()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient author = await MemberAsync(factory, org.Requester);
        using HttpClient colleague = await MemberAsync(factory, org.Deputy);
        Guid comment = (await CommentAsync(author, project, task, "First")).GetProperty("comments")[0].GetProperty("id").GetGuid();
        string url = $"/api/v1/projects/{project}/tasks/{task}/comments/{comment}";

        (await CoreHrApi.ErrorCodeAsync(await colleague.PutAsJsonAsync(url, new { Body = "Changed" }, Ct))).ShouldBe("TASK_COMMENT_AUTHOR");
        (await CoreHrApi.ErrorCodeAsync(await colleague.DeleteAsync(url, Ct))).ShouldBe("TASK_COMMENT_AUTHOR");

        JsonElement edited = await OkAsync(await author.PutAsJsonAsync(url, new { Body = "Second" }, Ct));
        edited.GetProperty("comments")[0].GetProperty("body").GetString().ShouldBe("Second");
        edited.GetProperty("comments")[0].TryGetProperty("editedAt", out _).ShouldBeTrue();

        JsonElement gone = await OkAsync(await admin.DeleteAsync(url, Ct));
        gone.GetProperty("comments").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Watching_TurnsOnAndOff_AndAWatcherHearsOfComments()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient watcher = await factory.CreateScopedUserClientAsync(
            Email("watcher"), org.Deputy, (Permissions.Project.View, DataScope.Own), (Permissions.Project.TaskView, DataScope.Own), (Permissions.Notification.InboxView, DataScope.Own));
        using HttpClient member = await MemberAsync(factory, org.Requester);

        JsonElement on = await OkAsync(await watcher.PostAsync($"/api/v1/projects/{project}/tasks/{task}/watch", null, Ct));
        on.GetProperty("isWatching").GetBoolean().ShouldBeTrue();
        on.GetProperty("watchers").GetArrayLength().ShouldBe(1);

        await CommentAsync(member, project, task, "News");
        await factory.DeliverEventsAsync();
        (await watcher.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).ToString().ShouldContain("commented on");

        JsonElement off = await OkAsync(await watcher.DeleteAsync($"/api/v1/projects/{project}/tasks/{task}/watch", Ct));
        off.GetProperty("isWatching").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task AComment_IsTheFirstResponseToATicket_UnlessFromWhoeverRaisedIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", new
        {
            Calendar = new { AlwaysOn = true, DayStart = "00:00", DayEnd = "00:00", WorkingDays = Array.Empty<string>() },
            Targets = new[] { new { Priority = "Medium", ResponseMinutes = 60, ResolutionMinutes = 240, CreditPercent = 1m } },
            PausedColumnIds = Array.Empty<Guid>(),
            MonthlyCreditCapPercent = 10m,
        }, Ct));
        using HttpClient reporter = await MemberAsync(factory, org.Requester);
        using HttpClient agent = await MemberAsync(factory, org.Deputy);
        Guid task = (await CreateTaskAsync(reporter, project, NewTask("Payments are down", "Incident"))).GetProperty("id").GetGuid();

        async Task<bool> AnsweredAsync()
        {
            JsonElement view = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/service-levels?month={Iso(Today)}", Ct);
            return view.GetProperty("tickets").EnumerateArray().Single(row => row.GetProperty("taskId").GetGuid() == task).TryGetProperty("respondedAt", out _);
        }

        await CommentAsync(reporter, project, task, "Still down");
        (await AnsweredAsync()).ShouldBeFalse();

        await CommentAsync(agent, project, task, "Looking now");
        (await AnsweredAsync()).ShouldBeTrue();
    }

    // ----- Attachments ----------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> AttachAsync(HttpClient client, Guid typeId, Guid task)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("ProjectTask"), "ownerType" },
            { new StringContent(task.ToString()), "ownerId" },
            { new StringContent(typeId.ToString()), "documentTypeId" },
            { new StringContent("Evaluation results"), "title" },
        };
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "results.pdf");
        return await client.PostAsync("/api/v1/documents", form, Ct);
    }

    [Fact]
    public async Task AFile_CanBeAttachedToATask_AndOnlyThoseOnTheProjectSeeIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient member = await MemberAsync(factory, org.Requester);
        Guid type = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/documents/types", Ct))!.First(row => row.GetProperty("code").GetString() == "SOW").GetProperty("id").GetGuid();

        HttpResponseMessage attached = await AttachAsync(member, type, task);
        attached.IsSuccessStatusCode.ShouldBeTrue(await attached.Content.ReadAsStringAsync(Ct));

        JsonElement page = await member.GetFromJsonAsync<JsonElement>($"/api/v1/documents?ownerType=ProjectTask&ownerId={task}", Ct);
        JsonElement document = page.GetProperty("items").EnumerateArray().Single();
        document.GetProperty("title").GetString().ShouldBe("Evaluation results");
        document.GetProperty("ownerName").GetString()!.ShouldEndWith("-1");

        using HttpClient outsider = await MemberAsync(factory, org.Finance);
        (await outsider.GetAsync($"/api/v1/documents?ownerType=ProjectTask&ownerId={task}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AttachAsync(outsider, type, task)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- History --------------------------------------------------------------------------------

    private static string[] FieldsIn(JsonElement conversation)
        => [.. conversation.GetProperty("activity").EnumerateArray().Select(row => row.GetProperty("field").GetString()!)];

    [Fact]
    public async Task EveryChange_IsInTheHistory_WithNamesAsTheyRead()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        using HttpClient member = await MemberAsync(factory, org.Requester);
        Guid task = await TaskIdAsync(member, project);
        (await ConversationAsync(member, project, task)).GetProperty("activity").EnumerateArray().Single().GetProperty("field").GetString().ShouldBe("created");

        await OkAsync(await member.PutAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}", new { Type = "Task", Title = "Build it", Priority = "High", AssigneeEmployeeId = org.Requester, DueDate = Iso(Today.AddDays(5)) }, Ct));
        await OkAsync(await member.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/move", new { ColumnId = await ColumnAsync(admin, project, "InProgress") }, Ct));

        JsonElement conversation = await ConversationAsync(member, project, task);
        FieldsIn(conversation).ShouldBe(["created", "priority", "assignee", "dueDate", "column"], ignoreOrder: true);
        JsonElement priority = conversation.GetProperty("activity").EnumerateArray().Single(row => row.GetProperty("field").GetString() == "priority");
        priority.GetProperty("oldValue").GetString().ShouldBe("Medium");
        priority.GetProperty("newValue").GetString().ShouldBe("High");
        priority.GetProperty("actorName").GetString().ShouldNotBeNullOrEmpty();
        conversation.GetProperty("activity").EnumerateArray().Single(row => row.GetProperty("field").GetString() == "column").GetProperty("newValue").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ASaveThatChangesNothing_AddsNoHistory()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);

        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}", new { Type = "Task", Title = "Build it", Priority = "Medium" }, Ct));

        FieldsIn(await ConversationAsync(admin, project, task)).ShouldBe(["created"]);
    }

    // ----- Filters and saved views ----------------------------------------------------------------

    private static async Task<string[]> TitlesAsync(HttpClient client, Guid project, string query)
        => [.. (await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks?{query}", Ct)).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("title").GetString()!)];

    [Fact]
    public async Task TheList_FiltersByPriorityOverdueAndUnassigned()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Late", Priority = "Critical", DueDate = Iso(Today.AddDays(-2)), AssigneeEmployeeId = org.Requester }, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Fine", Priority = "Low", DueDate = Iso(Today.AddDays(5)) }, Ct));

        (await TitlesAsync(admin, project, "priority=Critical")).ShouldBe(["Late"]);
        (await TitlesAsync(admin, project, "overdue=true")).ShouldBe(["Late"]);
        (await TitlesAsync(admin, project, "unassigned=true")).ShouldBe(["Fine"]);
    }

    [Fact]
    public async Task AView_IsPrivateUnlessShared_AndOnlyThoseWhoManageTasksCanShare()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        using HttpClient mine = await MemberAsync(factory, org.Requester);
        using HttpClient theirs = await MemberAsync(factory, org.Deputy);
        using HttpClient viewer = await factory.CreateScopedUserClientAsync(Email("view-only"), org.Manager, (Permissions.Project.View, DataScope.Own), (Permissions.Project.TaskView, DataScope.Own));
        string url = $"/api/v1/projects/{project}/task-views";

        JsonElement[] saved = (await mine.PostAsJsonAsync(url, new { Name = "My bugs", Filters = new { Type = "Bug", Overdue = true } }, Ct)).Content.ReadFromJsonAsync<JsonElement[]>(Ct).Result!;
        saved.Single().GetProperty("filters").GetProperty("type").GetString().ShouldBe("Bug");
        saved.Single().GetProperty("isShared").GetBoolean().ShouldBeFalse();
        (await theirs.GetFromJsonAsync<JsonElement[]>(url, Ct))!.ShouldBeEmpty();

        (await viewer.PostAsJsonAsync(url, new { Name = "Team", Filters = new { }, Shared = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await OkAsync(await mine.PostAsJsonAsync(url, new { Name = "Team", Filters = new { Priority = "High" }, Shared = true }, Ct));
        JsonElement[] seen = (await theirs.GetFromJsonAsync<JsonElement[]>(url, Ct))!;
        seen.Single().GetProperty("name").GetString().ShouldBe("Team");

        Guid privateView = saved.Single().GetProperty("id").GetGuid();
        (await theirs.DeleteAsync($"{url}/{privateView}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await OkAsync(await mine.DeleteAsync($"{url}/{privateView}", Ct));

        Guid shared = seen.Single().GetProperty("id").GetGuid();
        (await viewer.DeleteAsync($"{url}/{shared}", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    // ----- Bulk edit ------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> BulkAsync(HttpClient client, Guid project, object body)
        => client.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/bulk", body, Ct);

    private static async Task<JsonElement> TaskAsync(HttpClient client, Guid project, Guid task)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks/{task}", Ct);

    [Fact]
    public async Task ManyTasks_CanChangeTogether_AndItIsInEachHistory()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid one = await TaskIdAsync(admin, project, "One", assignee: org.Deputy);
        Guid two = await TaskIdAsync(admin, project, "Two");
        Guid doing = await ColumnAsync(admin, project, "InProgress");

        JsonElement result = await OkAsync(await BulkAsync(admin, project, new { TaskIds = new[] { one, two }, Priority = "Critical", ColumnId = doing, AssigneeEmployeeId = org.Requester, DueDate = Iso(Today.AddDays(9)) }));

        result.GetProperty("updated").GetInt32().ShouldBe(2);
        foreach (Guid id in new[] { one, two })
        {
            JsonElement task = await TaskAsync(admin, project, id);
            task.GetProperty("priority").GetString().ShouldBe("Critical");
            task.GetProperty("columnId").GetGuid().ShouldBe(doing);
            task.GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(org.Requester);
            task.GetProperty("dueDate").GetString().ShouldBe(Iso(Today.AddDays(9)));
        }

        FieldsIn(await ConversationAsync(admin, project, one)).ShouldContain("assignee");
        (await InboxAsync(org.Requester)).ShouldContain("One");
    }

    [Fact]
    public async Task OneThatIsNotAllowed_StopsThemAll()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        Guid epic = await TaskIdAsync(admin, project, "Big", "Epic");
        Guid sprint = (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/sprints", new { Name = "S1", StartDate = Iso(Today), EndDate = Iso(Today.AddDays(13)) }, Ct))).GetProperty("id").GetGuid();

        (await CoreHrApi.ErrorCodeAsync(await BulkAsync(admin, project, new { TaskIds = new[] { task, epic }, SprintId = sprint, Priority = "Critical" }))).ShouldBe("PROJECT_SPRINT_EPIC");
        (await TaskAsync(admin, project, task)).GetProperty("priority").GetString().ShouldBe("Medium");

        (await CoreHrApi.ErrorCodeAsync(await BulkAsync(admin, project, new { TaskIds = new[] { task }, AssigneeEmployeeId = org.Finance, Priority = "Critical" }))).ShouldBe("PROJECT_TASK_ASSIGNEE");
        (await TaskAsync(admin, project, task)).GetProperty("priority").GetString().ShouldBe("Medium");
    }

    [Fact]
    public async Task BulkEdit_Clears_OnlyWhenAsked_AndRefusesOtherProjectsAndHugeLists()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project, assignee: org.Requester);

        await OkAsync(await BulkAsync(admin, project, new { TaskIds = new[] { task }, Priority = "Low" }));
        (await TaskAsync(admin, project, task)).GetProperty("assigneeEmployeeId").GetGuid().ShouldBe(org.Requester);

        await OkAsync(await BulkAsync(admin, project, new { TaskIds = new[] { task }, ClearAssignee = true }));
        (await TaskAsync(admin, project, task)).TryGetProperty("assigneeEmployeeId", out _).ShouldBeFalse();

        Guid other = (await CreateProjectAsync(admin, Project(managerId: org.Manager))).GetProperty("id").GetGuid();
        Guid foreign = await TaskIdAsync(admin, other);
        (await CoreHrApi.ErrorCodeAsync(await BulkAsync(admin, project, new { TaskIds = new[] { task, foreign }, Priority = "Low" }))).ShouldBe("PROJECT_TASK_BULK");
        (await CoreHrApi.ErrorCodeAsync(await BulkAsync(admin, project, new { TaskIds = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray() }))).ShouldBe("PROJECT_TASK_BULK");
    }

    // ----- Import ---------------------------------------------------------------------------------

    private const string Jira = "Summary,Issue key,Issue id,Issue Type,Status,Priority,Parent id\nCheckout,AP-1,1,Epic,To Do,Highest,\nPay by card,AP-2,2,Story,Done,Major,1\n,AP-3,3,Task,To Do,,\n";

    [Fact]
    public async Task AFile_IsPreviewedWithNothingSaved_ThenImported_AndNotTwice()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await StaffedAsync(admin);
        string url = $"/api/v1/projects/{project}/tasks/import";

        JsonElement preview = await OkAsync(await admin.PostAsJsonAsync($"{url}/preview", new { Csv = Jira }, Ct));
        preview.GetProperty("willCreate").GetInt32().ShouldBe(2);
        preview.GetProperty("withErrors").GetInt32().ShouldBe(1);
        (await TitlesAsync(admin, project, "includeDone=true")).ShouldBeEmpty();

        JsonElement result = await OkAsync(await admin.PostAsJsonAsync(url, new { Csv = Jira }, Ct));
        result.GetProperty("created").GetInt32().ShouldBe(2);

        JsonElement[] tasks = [.. (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks?includeDone=true", Ct)).GetProperty("items").EnumerateArray()];
        JsonElement epic = tasks.Single(row => row.GetProperty("title").GetString() == "Checkout");
        JsonElement story = tasks.Single(row => row.GetProperty("title").GetString() == "Pay by card");
        epic.GetProperty("type").GetString().ShouldBe("Epic");
        story.GetProperty("parentTaskId").GetGuid().ShouldBe(epic.GetProperty("id").GetGuid());
        story.GetProperty("category").GetString().ShouldBe("Done");
        FieldsIn(await ConversationAsync(admin, project, story.GetProperty("id").GetGuid())).ShouldBe(["imported"]);

        JsonElement again = await OkAsync(await admin.PostAsJsonAsync(url, new { Csv = Jira }, Ct));
        again.GetProperty("created").GetInt32().ShouldBe(0);
        again.GetProperty("alreadyImported").GetInt32().ShouldBe(2);
        (await TitlesAsync(admin, project, "includeDone=true")).Length.ShouldBe(2);
    }

    [Fact]
    public async Task ALaterFile_CanHangOffWhatCameBefore()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await StaffedAsync(admin);
        string url = $"/api/v1/projects/{project}/tasks/import";
        await OkAsync(await admin.PostAsJsonAsync(url, new { Csv = Jira }, Ct));

        await OkAsync(await admin.PostAsJsonAsync(url, new { Csv = "Title,Key,Type,Parent\nStep two,AP-9,Story,AP-1" }, Ct));

        JsonElement[] tasks = [.. (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks?includeDone=true", Ct)).GetProperty("items").EnumerateArray()];
        tasks.Single(row => row.GetProperty("title").GetString() == "Step two").GetProperty("parentTaskId").GetGuid()
            .ShouldBe(tasks.Single(row => row.GetProperty("title").GetString() == "Checkout").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ImportedAssignees_MustBeOnTheTeam_AndTicketsStartTheirClock()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/{project}/service-levels/policy", new
        {
            Calendar = new { AlwaysOn = true, DayStart = "00:00", DayEnd = "00:00", WorkingDays = Array.Empty<string>() },
            Targets = new[] { new { Priority = "High", ResponseMinutes = 60, ResolutionMinutes = 240, CreditPercent = 1m } },
            PausedColumnIds = Array.Empty<Guid>(),
            MonthlyCreditCapPercent = 10m,
        }, Ct));
        string csv = "Title,Type,Priority,Assignee\nOutage,Incident,High,someone.else@elsewhere.example";

        JsonElement preview = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/import/preview", new { Csv = csv }, Ct));
        preview.GetProperty("rows")[0].GetProperty("warnings").GetArrayLength().ShouldBe(1);

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/import", new { Csv = csv }, Ct));

        JsonElement view = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/service-levels?month={Iso(Today)}", Ct);
        JsonElement ticket = view.GetProperty("tickets").EnumerateArray().Single();
        ticket.GetProperty("title").GetString().ShouldBe("Outage");
        ticket.TryGetProperty("assigneeEmployeeId", out _).ShouldBeFalse();
        org.Requester.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task OnlyThoseWhoManageTasks_CanImport()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        using HttpClient viewer = await factory.CreateScopedUserClientAsync(Email("import-view"), org.Requester, (Permissions.Project.TaskView, DataScope.Own));
        using HttpClient outsider = await MemberAsync(factory, org.Finance);

        (await viewer.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/import", new { Csv = Jira }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/import", new { Csv = Jira }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/import/preview", new { Csv = Jira }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- Gantt ----------------------------------------------------------------------------------

    [Fact]
    public async Task TheGantt_ShowsDatesDependenciesAndTheBaseline_AndABarCanMove()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await StaffedAsync(admin);
        JsonElement first = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "First", Priority = "Medium", StartDate = Iso(Today), DueDate = Iso(Today.AddDays(4)) }, Ct));
        JsonElement second = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Second", Priority = "Medium", StartDate = Iso(Today.AddDays(5)), DueDate = Iso(Today.AddDays(9)) }, Ct));
        Guid firstId = first.GetProperty("id").GetGuid();
        Guid secondId = second.GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{secondId}/dependencies", new { PredecessorTaskId = firstId, Type = "FinishToStart" }, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/projects/{project}/baseline", null, Ct));

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{firstId}/schedule", new { StartDate = Iso(Today.AddDays(2)), DueDate = Iso(Today.AddDays(6)) }, Ct));

        JsonElement gantt = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/gantt", Ct);
        JsonElement moved = gantt.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == firstId);
        moved.GetProperty("dueDate").GetString().ShouldBe(Iso(Today.AddDays(6)));
        moved.GetProperty("baselineDueDate").GetString().ShouldBe(Iso(Today.AddDays(4)));
        gantt.GetProperty("dependencies").EnumerateArray().Single().GetProperty("successorTaskId").GetGuid().ShouldBe(secondId);
        gantt.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        FieldsIn(await ConversationAsync(admin, project, firstId)).ShouldContain("dueDate");

        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{firstId}/schedule", new { StartDate = Iso(Today.AddDays(9)), DueDate = Iso(Today) }, Ct))).ShouldBe("PROJECT_TASK_DATES");
    }

    [Fact]
    public async Task OnlyThoseWhoManageTasks_CanMoveABar()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        Guid task = await TaskIdAsync(admin, project);
        using HttpClient viewer = await factory.CreateScopedUserClientAsync(Email("gantt-view"), org.Requester, (Permissions.Project.View, DataScope.Own), (Permissions.Project.TaskView, DataScope.Own));

        JsonElement gantt = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/gantt", Ct);
        gantt.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        (await viewer.PostAsJsonAsync($"/api/v1/projects/{project}/tasks/{task}/schedule", new { DueDate = Iso(Today) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ----- Workload -------------------------------------------------------------------------------

    [Fact]
    public async Task TheWorkload_ShowsEachPersonsWeeksAgainstTheirCapacity()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await StaffedAsync(admin);
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Big", Priority = "Medium", AssigneeEmployeeId = org.Requester, EstimatedHours = 30m, DueDate = Iso(Today.AddDays(60)) }, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Vague", Priority = "Medium", AssigneeEmployeeId = org.Requester }, Ct));
        await TaskIdAsync(admin, project, "Nobody's");

        JsonElement workload = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/workload?weeks=4", Ct);

        workload.GetProperty("weeks").GetArrayLength().ShouldBe(4);
        workload.GetProperty("unassignedTasks").GetInt32().ShouldBe(1);
        JsonElement person = workload.GetProperty("people").EnumerateArray().Single(row => row.GetProperty("employeeId").GetGuid() == org.Requester);
        person.GetProperty("openTasks").GetInt32().ShouldBe(2);
        person.GetProperty("unestimated").GetInt32().ShouldBe(1);
        person.GetProperty("remainingHours").GetDecimal().ShouldBe(30m);
        person.GetProperty("weeks").EnumerateArray().Sum(week => week.GetProperty("capacityHours").GetDecimal()).ShouldBeGreaterThan(0m);
        workload.GetProperty("people").EnumerateArray().ShouldContain(row => row.GetProperty("employeeId").GetGuid() == org.Deputy, "on the team with nothing to do");
    }

    // ----- AI work --------------------------------------------------------------------------------

    [Fact]
    public async Task AnExperiment_IsATask_AndTheAiRolesAreOffered()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await StaffedAsync(admin);

        (await CreateTaskAsync(admin, project, NewTask("Does re-ranking help?", "Experiment"))).GetProperty("type").GetString().ShouldBe("Experiment");

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Gggs.Modules.Project.Application.ProjectCatalogSeeder>(services).SeedCurrentTenantAsync(Ct);
        });
        string[] roles = [.. (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/projects/roles", Ct))!.Select(role => role.GetProperty("code").GetString()!)];
        roles.ShouldContain("MLE");
        roles.ShouldContain("AIQA");
    }
}
