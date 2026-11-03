using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PortfolioAndCodeLinkTests(PeopleOsApiFactory factory)
{
    private const string Secret = "a-long-enough-shared-secret";
    private const string TenantCode = PeopleOsApiFactory.SeededTenantCode;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Body(string billing, Guid manager, Guid? clientId = null, string currency = "INR") => new
    {
        Code = "Q" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
        Name = "Project " + CoreHrApi.Unique("N"),
        Type = "Implementation",
        ClientId = clientId,
        ProjectManagerEmployeeId = manager,
        StartDate = Iso(Today.AddDays(-60)),
        EndDate = Iso(Today.AddDays(120)),
        Priority = "High",
        BillingType = billing,
        Currency = currency,
        Confidentiality = "Internal",
        Methodology = "Agile",
    };

    // ----- Code links: who may send ---------------------------------------------------------------

    private static async Task<Guid> ConnectionAsync(HttpClient admin, string adapter = "github", bool active = true)
        => (await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new { AdapterKey = adapter, Name = adapter + " " + CoreHrApi.Unique("C"), Settings = new Dictionary<string, string> { ["secret"] = Secret }, IsActive = active }, Ct))).GetProperty("id").GetGuid();

    private static HttpRequestMessage GitHubRequest(Guid connection, string eventName, string json, string? secret = Secret, string tenant = TenantCode)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/git/{tenant}/{connection}") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-GitHub-Event", eventName);
        if (secret is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        }

        return request;
    }

    private static string Push(string code, string sha = "abc123") => JsonSerializer.Serialize(new
    {
        @ref = "refs/heads/main",
        commits = new[] { new { id = sha, message = $"{code} fix the total\n\nDetail.", url = $"https://github.com/acme/app/commit/{sha}", timestamp = "2026-11-02T09:00:00Z", author = new { name = "Asha" } } },
    });

    private async Task<(Guid Project, Guid Task, string Code)> TaskAsync(HttpClient admin)
    {
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Body("TimeAndMaterial", org.Manager))).GetProperty("id").GetGuid();
        JsonElement task = await CreateTaskAsync(admin, project, NewTask("Fix the total"));
        return (project, task.GetProperty("id").GetGuid(), task.GetProperty("code").GetString()!);
    }

    private static async Task<JsonElement[]> LinksAsync(HttpClient client, Guid project, Guid task)
        => [.. (await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks/{task}/conversation", Ct)).GetProperty("codeLinks").EnumerateArray()];

    [Fact]
    public async Task ASignedPush_LinksTheCommitToTheTaskItNames_WithNoSignIn()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, Guid task, string code) = await TaskAsync(admin);
        Guid connection = await ConnectionAsync(admin);
        using HttpClient outside = factory.CreateClient();

        JsonElement answer = await OkAsync(await outside.SendAsync(GitHubRequest(connection, "push", Push(code)), Ct));

        answer.GetProperty("linked").GetInt32().ShouldBe(1);
        JsonElement link = (await LinksAsync(admin, project, task)).Single();
        link.GetProperty("kind").GetString().ShouldBe("Commit");
        link.GetProperty("provider").GetString().ShouldBe("github");
        link.GetProperty("title").GetString().ShouldBe($"{code} fix the total");
        link.GetProperty("url").GetString().ShouldBe("https://github.com/acme/app/commit/abc123");
        link.GetProperty("author").GetString().ShouldBe("Asha");
    }

    [Fact]
    public async Task AnythingUnsigned_OrSignedWrongly_IsRefused_AndChangesNothing()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, Guid task, string code) = await TaskAsync(admin);
        Guid connection = await ConnectionAsync(admin);
        using HttpClient outside = factory.CreateClient();

        (await outside.SendAsync(GitHubRequest(connection, "push", Push(code), secret: null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outside.SendAsync(GitHubRequest(connection, "push", Push(code), secret: "the-wrong-secret-entirely"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await LinksAsync(admin, project, task)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryWayOfGettingItWrong_LooksTheSame_SoNothingCanBeProbed()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (_, _, string code) = await TaskAsync(admin);
        Guid inactive = await ConnectionAsync(admin, active: false);
        Guid notCode = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new { AdapterKey = "webhook", Name = "Other " + CoreHrApi.Unique("C"), Settings = new Dictionary<string, string> { ["url"] = "https://example.com/hook", ["secret"] = Secret } }, Ct))).GetProperty("id").GetGuid();
        Guid good = await ConnectionAsync(admin);
        using HttpClient outside = factory.CreateClient();

        (await outside.SendAsync(GitHubRequest(Guid.NewGuid(), "push", Push(code)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outside.SendAsync(GitHubRequest(good, "push", Push(code), tenant: "no-such-tenant"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outside.SendAsync(GitHubRequest(inactive, "push", Push(code)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outside.SendAsync(GitHubRequest(notCode, "push", Push(code)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheSameDelivery_Twice_LinksOnce_AndAPullRequestKeepsUp()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, Guid task, string code) = await TaskAsync(admin);
        Guid connection = await ConnectionAsync(admin);
        using HttpClient outside = factory.CreateClient();

        await OkAsync(await outside.SendAsync(GitHubRequest(connection, "push", Push(code)), Ct));
        (await OkAsync(await outside.SendAsync(GitHubRequest(connection, "push", Push(code)), Ct))).GetProperty("linked").GetInt32().ShouldBe(0);
        (await LinksAsync(admin, project, task)).Length.ShouldBe(1);

        string Pull(string state, bool merged) => JsonSerializer.Serialize(new { repository = new { full_name = "acme/app" }, pull_request = new { number = 7, title = $"{code} checkout", state, merged, html_url = "https://github.com/acme/app/pull/7", user = new { login = "asha" }, updated_at = "2026-11-02T09:00:00Z" } });
        await OkAsync(await outside.SendAsync(GitHubRequest(connection, "pull_request", Pull("open", false)), Ct));
        await OkAsync(await outside.SendAsync(GitHubRequest(connection, "pull_request", Pull("closed", true)), Ct));

        JsonElement[] links = await LinksAsync(admin, project, task);
        links.Length.ShouldBe(2);
        links.Single(link => link.GetProperty("kind").GetString() == "PullRequest").GetProperty("state").GetString().ShouldBe("merged");
    }

    [Fact]
    public async Task ACodeNoTaskHas_OrABadLink_DoesNoHarm()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, Guid task, string code) = await TaskAsync(admin);
        Guid connection = await ConnectionAsync(admin);
        using HttpClient outside = factory.CreateClient();

        (await OkAsync(await outside.SendAsync(GitHubRequest(connection, "push", Push("NOSUCHTHING-99")), Ct))).GetProperty("linked").GetInt32().ShouldBe(0);
        (await OkAsync(await outside.SendAsync(GitHubRequest(connection, "ping", """{"zen":"hello"}"""), Ct))).GetProperty("linked").GetInt32().ShouldBe(0);
        (await outside.SendAsync(GitHubRequest(connection, "push", "not json"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        string sneaky = JsonSerializer.Serialize(new { commits = new[] { new { id = "evil", message = $"{code} <script>alert(1)</script>", url = "javascript:alert(1)" } } });
        await OkAsync(await outside.SendAsync(GitHubRequest(connection, "push", sneaky), Ct));
        (await LinksAsync(admin, project, task)).Single().TryGetProperty("url", out _).ShouldBeFalse("only a web address may become a link");
    }

    [Fact]
    public async Task AzureDevOps_UsesTheSecretAsItsPassword()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, Guid task, string code) = await TaskAsync(admin);
        Guid connection = await ConnectionAsync(admin, "azure-devops");
        using HttpClient outside = factory.CreateClient();
        string json = JsonSerializer.Serialize(new
        {
            eventType = "git.push",
            resource = new { repository = new { remoteUrl = "https://dev.azure.com/acme/_git/app" }, commits = new[] { new { commitId = "abc", comment = $"{code} fix", author = new { name = "Asha", date = "2026-11-02T09:00:00Z" } } } },
        });

        HttpRequestMessage Send(string password)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/git/{TenantCode}/{connection}") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("azure:" + password)));
            return request;
        }

        (await outside.SendAsync(Send("wrong"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await OkAsync(await outside.SendAsync(Send(Secret), Ct));

        (await LinksAsync(admin, project, task)).Single().GetProperty("url").GetString().ShouldBe("https://dev.azure.com/acme/_git/app/commit/abc");
    }

    [Fact]
    public async Task TheSetupPage_GivesTheAddress_AndNeverTheSecret()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/projects/code-links/setup", Ct);
        string text = await response.Content.ReadAsStringAsync(Ct);

        text.ShouldContain($"/api/v1/webhooks/git/{TenantCode}/{connection}");
        text.ShouldNotContain(Secret);
    }

    // ----- Outcome pricing ------------------------------------------------------------------------

    private static async Task<(TestOrg Org, Guid Project)> OutcomeProjectAsync(HttpClient admin)
    {
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Body("OutcomeBased", org.Manager))).GetProperty("id").GetGuid();
        return (org, project);
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, Guid project, decimal quantity = 2, string reference = "Release 4")
        => client.PostAsJsonAsync($"/api/v1/project-financials/{project}/outcome-units", new { AcceptedOn = Iso(Today.AddDays(-3)), Quantity = quantity, Reference = reference, AcceptedBy = "Client lead", Evidence = "Sign-off email" }, Ct);

    private static string Period => $"from={Iso(Today.AddDays(-10))}&to={Iso(Today)}";

    [Fact]
    public async Task AcceptedUnits_EarnAtTheirPrice_ReachTheInvoiceOnce_AndCannotBeRemovedOnceBilled()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg _, Guid project) = await OutcomeProjectAsync(admin);

        (await CoreHrApi.ErrorCodeAsync(await AcceptAsync(admin, project))).ShouldBe("OUTCOME_PRICE");

        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { Terms = new { UnitName = "release", UnitPrice = 50000m, TargetUnits = 10m } }, Ct));
        JsonElement[] units = [.. (await OkAsync(await AcceptAsync(admin, project))).EnumerateArray()];
        units.Single().GetProperty("amount").GetDecimal().ShouldBe(100000m);
        units.Single().GetProperty("isBilled").GetBoolean().ShouldBeFalse();
        Guid id = units.Single().GetProperty("id").GetGuid();

        // A later price leaves what was accepted alone.
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { Terms = new { UnitName = "release", UnitPrice = 60000m, TargetUnits = 10m } }, Ct));
        JsonElement finance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct);
        finance.GetProperty("summary").GetProperty("revenue").GetDecimal().ShouldBe(100000m);
        finance.GetProperty("summary").GetProperty("acceptedUnits").GetDecimal().ShouldBe(2m);
        finance.GetProperty("summary").GetProperty("forecastRevenue").GetDecimal().ShouldBe(600000m);

        JsonElement invoice = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/invoices/prepare", new { From = Iso(Today.AddDays(-10)), To = Iso(Today) }, Ct));
        invoice.GetProperty("amount").GetDecimal().ShouldBe(100000m);
        invoice.GetProperty("lines").EnumerateArray().Single().GetProperty("kind").GetString().ShouldBe("Outcome");
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}/billing/preview?{Period}", Ct)).GetProperty("amount").GetDecimal().ShouldBe(0m);

        (await CoreHrApi.ErrorCodeAsync(await admin.DeleteAsync($"/api/v1/project-financials/{project}/outcome-units/{id}", Ct))).ShouldBe("OUTCOME_BILLED");

        await OkAsync(await admin.PostAsync($"/api/v1/project-financials/{project}/invoices/{invoice.GetProperty("id").GetGuid()}/cancel", null, Ct));
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}/billing/preview?{Period}", Ct)).GetProperty("amount").GetDecimal().ShouldBe(100000m);
        (await OkAsync(await admin.DeleteAsync($"/api/v1/project-financials/{project}/outcome-units/{id}", Ct))).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task OnlyThoseWhoManageFinancials_CanRecordUnits()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (TestOrg org, Guid project) = await OutcomeProjectAsync(admin);
        using HttpClient viewer = await factory.CreateScopedUserClientAsync(Email("unit-view"), org.Manager, (Permissions.Project.View, DataScope.Own), (Permissions.Project.FinancialsView, DataScope.Own));

        (await viewer.GetAsync($"/api/v1/project-financials/{project}/outcome-units", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AcceptAsync(viewer, project)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnOutcomeBasedTemplate_IsOffered()
        => (await (await factory.CreateAdminClientAsync()).GetFromJsonAsync<JsonElement[]>("/api/v1/projects/templates", Ct))!
            .ShouldContain(template => template.GetProperty("billingType").GetString() == "OutcomeBased" || template.GetProperty("code").GetString() == "OUTCOME");

    // ----- Portfolio ------------------------------------------------------------------------------

    [Fact]
    public async Task TheDashboard_RollsUpByModelClientAndDepartment_WithHealth_AndCurrenciesApart()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        JsonElement created = await ClientAsync(admin);
        string clientName = created.GetProperty("client").GetProperty("name").GetString()!;
        Guid usd = (await CreateProjectAsync(admin, Body("OutcomeBased", org.Manager, created.GetProperty("client").GetProperty("id").GetGuid(), "USD"))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{usd}/profile", new { Terms = new { UnitName = "release", UnitPrice = 1000m, TargetUnits = 4m } }, Ct));
        await OkAsync(await AcceptAsync(admin, usd, 1));

        JsonElement dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/v1/project-financials/portfolio/dashboard", Ct);

        JsonElement dollars = dashboard.GetProperty("currencies").EnumerateArray().Single(row => row.GetProperty("currency").GetString() == "USD");
        dollars.GetProperty("byModel").EnumerateArray().Single(row => row.GetProperty("label").GetString() == "OutcomeBased").GetProperty("revenue").GetDecimal().ShouldBeGreaterThanOrEqualTo(1000m);
        dollars.GetProperty("byClient").EnumerateArray().ShouldContain(row => row.GetProperty("label").GetString() == clientName);
        dollars.GetProperty("forecast").GetArrayLength().ShouldBe(6);
        dashboard.GetProperty("health").GetProperty("notAssessed").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task TheDashboard_ShowsOnlyProjectsTheCallerCanSee()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid hidden = (await CreateProjectAsync(admin, Body("OutcomeBased", org.Manager, currency: "EUR"))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{hidden}/profile", new { Terms = new { UnitPrice = 1000m } }, Ct));
        await OkAsync(await AcceptAsync(admin, hidden, 1));
        using HttpClient outsider = await factory.CreateScopedUserClientAsync(Email("dash-out"), org.SkipLevel, (Permissions.Project.View, DataScope.Own), (Permissions.Project.FinancialsView, DataScope.Own));

        JsonElement dashboard = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/project-financials/portfolio/dashboard", Ct);

        dashboard.GetProperty("currencies").EnumerateArray().ShouldNotContain(row => row.GetProperty("currency").GetString() == "EUR");
    }

    // ----- Assistants that only draft -------------------------------------------------------------

    private static async Task<JsonElement> AskAsync(HttpClient client, string assistant, string message)
        => await OkAsync(await client.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = assistant, Message = message }, Ct));

    [Fact]
    public async Task ATaskBreakdown_IsOnlyAProposal_UntilAPersonApprovesIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, _, string code) = await TaskAsync(admin);
        string projectCode = code[..code.LastIndexOf('-')];
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("draft_task_breakdown", new
        {
            project = projectCode,
            tasks = new object[]
            {
                new { title = "Design the schema", type = "Task", estimatedHours = 8 },
                new { title = "Does re-ranking help?", type = "Experiment", estimatedHours = 16 },
            },
        });
        factory.Llm.ReplyText("Here is a draft for you to approve.");

        JsonElement view = await AskAsync(admin, "projects", "Break down the checkout requirement.");

        JsonElement action = view.GetProperty("actions").EnumerateArray().Single();
        action.GetProperty("status").GetString().ShouldBe("Proposed");
        action.GetProperty("summary").GetString()!.ShouldContain("Design the schema");
        string[] Titles(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("title").GetString()!)];
        Titles(await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks", Ct)).ShouldNotContain("Design the schema");

        JsonElement approved = await OkAsync(await admin.PostAsync($"/api/v1/ai/actions/{action.GetProperty("id").GetGuid()}/approve", null, Ct));

        approved.GetProperty("status").GetString().ShouldBe("Executed");
        string[] titles = Titles(await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks", Ct));
        titles.ShouldContain("Design the schema");
        titles.ShouldContain("Does re-ranking help?");
    }

    [Fact]
    public async Task SomeoneOffTheProject_CannotGetABreakdownOrItsFacts()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid _, _, string code) = await TaskAsync(admin);
        TestOrg org = await OrgAsync(admin);
        string projectCode = code[..code.LastIndexOf('-')];
        using HttpClient outsider = await factory.CreateScopedUserClientAsync(
            Email("ai-out"), org.Finance, (Permissions.Ai.AssistantUse, DataScope.Own), (Permissions.Project.View, DataScope.Own), (Permissions.Project.TaskView, DataScope.Own), (Permissions.Project.TaskManage, DataScope.Own));
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("project_week", new { project = projectCode });
        factory.Llm.ReplyText("I can't see that.");

        await AskAsync(outsider, "projects", "How was the week?");

        string shown = factory.Llm.Requests.Last().Messages[^1].Blocks.OfType<Gggs.Modules.Ai.Application.LlmToolResult>().Single().Content;
        shown.ShouldContain("No project you can see");
    }

    [Fact]
    public async Task TheWeekAndItsRisks_AreReadAsTheUser()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, _, string code) = await TaskAsync(admin);
        string projectCode = code[..code.LastIndexOf('-')];
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/tasks", new { Type = "Task", Title = "Overdue work", Priority = "High", DueDate = Iso(Today.AddDays(-3)) }, Ct));
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("project_risk_signals", new { project = projectCode });
        factory.Llm.ReplyToolUse("project_week", new { project = projectCode });
        factory.Llm.ReplyText("One task is late.");

        await AskAsync(admin, "projects", "What should I worry about?");

        List<string> shown = [.. factory.Llm.Requests.SelectMany(request => request.Messages).SelectMany(message => message.Blocks).OfType<Gggs.Modules.Ai.Application.LlmToolResult>().Select(result => result.Content)];
        shown.ShouldContain(result => result.Contains("Late tasks", StringComparison.Ordinal));
        shown.ShouldContain(result => result.Contains("Overdue work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARisk_IsLoggedOnlyOnceApproved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid project, _, string code) = await TaskAsync(admin);
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("raise_project_risk", new { project = code[..code.LastIndexOf('-')], title = "Key person leaving", probability = 4, impact = 9, mitigation = "Pair up" });
        factory.Llm.ReplyText("Drafted.");

        JsonElement action = (await AskAsync(admin, "projects", "Log this risk.")).GetProperty("actions").EnumerateArray().Single();
        action.GetProperty("summary").GetString()!.ShouldContain("impact 5");

        await OkAsync(await admin.PostAsync($"/api/v1/ai/actions/{action.GetProperty("id").GetGuid()}/approve", null, Ct));

        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/raid?kind=Risk", Ct)).ToString().ShouldContain("Key person leaving");
    }

    [Fact]
    public async Task ATimesheetDraft_IsSavedOnlyWhenApproved_NeverSubmitted_AndNeverOverAnotherWeek()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, Body("TimeAndMaterial", org.Manager))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/team", new { EmployeeId = org.Requester, ProjectRoleId = await RoleAsync(admin, "DEV"), StartDate = Iso(Today.AddDays(-30)), AllocationPercent = 100, Billable = true, BillingRate = 1000m, CostRate = 500m }, Ct));
        await CreateTaskAsync(admin, project, NewTask("Assigned work", assigneeId: org.Requester));
        using HttpClient self = await factory.CreateScopedUserClientAsync(
            Email("draft-self"), org.Requester, (Permissions.Ai.AssistantUse, DataScope.Own), (Permissions.Self.TimesheetSubmit, DataScope.Own));
        DateOnly monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        string week = Iso(monday);
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("draft_my_timesheet", new { week });
        factory.Llm.ReplyText("Drafted your week.");

        JsonElement action = (await AskAsync(self, "me", "Fill in my timesheet.")).GetProperty("actions").EnumerateArray().Single();
        action.GetProperty("summary").GetString()!.ShouldContain("stays a draft");
        (await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={week}", Ct)).GetProperty("entries").GetArrayLength().ShouldBe(0);

        await OkAsync(await self.PostAsync($"/api/v1/ai/actions/{action.GetProperty("id").GetGuid()}/approve", null, Ct));

        JsonElement saved = await self.GetFromJsonAsync<JsonElement>($"/api/v1/me/timesheets?week={week}", Ct);
        saved.GetProperty("entries").GetArrayLength().ShouldBeGreaterThan(0);
        saved.GetProperty("timesheet").GetProperty("status").GetString().ShouldBe("Draft");

        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("draft_my_timesheet", new { week });
        factory.Llm.ReplyText("That week is already filled.");
        JsonElement again = await AskAsync(self, "me", "Do it again.");
        again.GetProperty("actions").GetArrayLength().ShouldBe(0, "a draft must not overwrite hours already there");
    }
}
