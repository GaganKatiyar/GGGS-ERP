using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class EngagementModelApiTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object ProjectBody(string billing, Guid? manager = null, Guid? templateId = null, DateOnly? start = null) => new
    {
        Code = "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
        Name = "Project " + CoreHrApi.Unique("N"),
        Type = "Implementation",
        ProjectManagerEmployeeId = manager,
        StartDate = Iso(start ?? Today.AddDays(-30)),
        EndDate = Iso(Today.AddDays(60)),
        Priority = "High",
        BillingType = billing,
        Currency = "INR",
        Confidentiality = "Internal",
        Methodology = "Agile",
        TemplateId = templateId,
    };

    private static async Task<JsonElement[]> TemplatesAsync(HttpClient admin)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/projects/templates", Ct))!;

    [Fact]
    public async Task ATemplate_SetsUpTheBoardPhasesAndSharedWork()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid template = (await TemplatesAsync(admin)).First(row => row.GetProperty("billingType").GetString() == "ManagedService").GetProperty("id").GetGuid();

        JsonElement created = await CreateProjectAsync(admin, ProjectBody("ManagedService", templateId: template));
        Guid project = created.GetProperty("id").GetGuid();

        JsonElement[] phases = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{project}/phases", Ct))!;
        phases.Select(phase => phase.GetProperty("name").GetString()).ShouldBe(["Transition", "Steady state"]);

        JsonElement board = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/board", Ct);
        board.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("name").GetString()).ShouldContain("Waiting on client");

        JsonElement tasks = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/tasks?pageSize=50", Ct);
        JsonElement monitoring = tasks.GetProperty("items").EnumerateArray().First(task => task.GetProperty("title").GetString() == "Monitoring");
        monitoring.GetProperty("openToTeam").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task WithoutATemplate_AProjectGetsTheStandardBoard()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid project = (await CreateProjectAsync(admin, ProjectBody("TimeAndMaterial"))).GetProperty("id").GetGuid();

        (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{project}/phases", Ct))!.ShouldBeEmpty();
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/board", Ct)).GetProperty("columns").EnumerateArray()
            .Select(column => column.GetProperty("name").GetString()).ShouldContain("Code Review");
    }

    [Fact]
    public async Task ATemplateForAnotherModel_IsRefused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid template = (await TemplatesAsync(admin)).First(row => row.GetProperty("billingType").GetString() == "Retainer").GetProperty("id").GetGuid();

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/projects", ProjectBody("FixedPrice", templateId: template), Ct);

        (await CoreHrApi.ErrorCodeAsync(response)).ShouldBe("PROJECT_TEMPLATE_MODEL");
    }

    [Fact]
    public async Task ATemplate_CanBeChanged_AndBadBoardsAreRefused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement template = (await TemplatesAsync(admin)).First(row => row.GetProperty("billingType").GetString() == "DedicatedTeam");
        Guid id = template.GetProperty("id").GetGuid();

        JsonElement saved = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/projects/templates/{id}", new
        {
            Name = "Our dedicated teams",
            Phases = new[] { "Ramp-up", "Run" },
            Columns = new[] { new { Name = "To do", Category = "Todo" }, new { Name = "Done", Category = "Done" } },
            Activities = new[] { new { Name = "Meetings", Tasks = new[] { "Stand-up" } } },
        }, Ct));
        saved.GetProperty("name").GetString().ShouldBe("Our dedicated teams");
        saved.GetProperty("definition").GetProperty("phases").EnumerateArray().Select(phase => phase.GetString()).ShouldBe(["Ramp-up", "Run"]);

        HttpResponseMessage bad = await admin.PutAsJsonAsync($"/api/v1/projects/templates/{id}", new
        {
            Name = "No finish",
            Phases = Array.Empty<string>(),
            Columns = new[] { new { Name = "To do", Category = "Todo" } },
            Activities = Array.Empty<object>(),
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(bad)).ShouldBe("PROJECT_TEMPLATE_COLUMNS");
    }

    [Fact]
    public async Task ABaseline_IsCapturedAtKickoff_AndMovedByApprovedChanges()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, ProjectBody("FixedPrice", org.Manager))).GetProperty("id").GetGuid();
        await CreateTaskAsync(admin, project, new { Type = "Task", Title = "Build it", Priority = "Medium", EstimatedHours = 100m, ColumnId = await ColumnAsync(admin, project, "Todo") });

        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/dashboard", Ct)).TryGetProperty("baseline", out _).ShouldBeFalse("nothing has been agreed yet");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/stage", new { Stage = "Kickoff" }, Ct));
        JsonElement baseline = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/dashboard", Ct)).GetProperty("baseline");
        baseline.GetProperty("baselineHours").GetDecimal().ShouldBe(100m);
        baseline.GetProperty("scheduleVarianceDays").GetInt32().ShouldBe(0);
        string agreedEnd = baseline.GetProperty("agreedEndDate").GetString()!;

        // With no approval workflow for change requests, submitting approves it.
        JsonElement change = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/change-requests", new
        {
            Title = "More scope",
            Description = "Add reporting",
            EstimatedHours = 20m,
            EstimatedCost = 50000m,
            ScheduleImpactDays = 10,
            Priority = "Medium",
        }, Ct));
        await OkAsync(await admin.PostAsync($"/api/v1/projects/{project}/change-requests/{change.GetProperty("id").GetGuid()}/submit", null, Ct));

        baseline = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{project}/dashboard", Ct)).GetProperty("baseline");
        baseline.GetProperty("approvedChanges").GetInt32().ShouldBe(1);
        baseline.GetProperty("agreedHours").GetDecimal().ShouldBe(120m);
        baseline.GetProperty("approvedChangeCost").GetDecimal().ShouldBe(50000m);
        DateOnly.Parse(baseline.GetProperty("agreedEndDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(DateOnly.Parse(agreedEnd, System.Globalization.CultureInfo.InvariantCulture).AddDays(10));
    }

    [Fact]
    public async Task AMilestoneNeedingAcceptance_EarnsOnlyWhenAccepted()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, ProjectBody("Milestone", org.Manager))).GetProperty("id").GetGuid();
        JsonElement milestone = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones", new
        {
            Name = "Model in production",
            PlannedDate = Iso(Today),
            IsBillable = true,
            RequiresAcceptance = true,
        }, Ct));
        Guid id = milestone.GetProperty("id").GetGuid();
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { MilestoneAmounts = new Dictionary<Guid, decimal> { [id] = 500000m } }, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones/{id}/achieve", new { On = Iso(Today) }, Ct));

        decimal Revenue(JsonElement finance) => finance.GetProperty("summary").GetProperty("revenue").GetDecimal();
        Revenue(await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct)).ShouldBe(0m);

        JsonElement accepted = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/milestones/{id}/accept", new { AcceptedBy = "Asha Rao", Evidence = "Evaluation: precision 0.94" }, Ct));
        accepted.GetProperty("acceptedBy").GetString().ShouldBe("Asha Rao");
        Revenue(await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct)).ShouldBe(500000m);
    }

    [Fact]
    public async Task TheTermsAreSaved_AndADedicatedTeamEarnsItsSeats()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, ProjectBody("DedicatedTeam", org.Manager, start: new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1)))).GetProperty("id").GetGuid();
        Guid role = await RoleAsync(admin, "DEV");
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{project}/team", new
        {
            EmployeeId = org.Requester,
            ProjectRoleId = role,
            StartDate = Iso(new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1)),
            AllocationPercent = 100,
            Billable = true,
            MonthlyRate = 300000m,
        }, Ct));

        JsonElement finance = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new
        {
            Terms = new { PoNumber = "PO-77", PoValue = 2000000m, IncludedHoursPerMonth = 160m, RolloverPercent = 50, OverageRate = 2500m },
        }, Ct));
        JsonElement terms = finance.GetProperty("terms");
        terms.GetProperty("poNumber").GetString().ShouldBe("PO-77");
        terms.GetProperty("rolloverPercent").GetInt32().ShouldBe(50);

        JsonElement summary = finance.GetProperty("summary");
        summary.GetProperty("revenue").GetDecimal().ShouldBeGreaterThanOrEqualTo(300000m, "at least last month's whole fee");
        summary.GetProperty("poBalance").GetDecimal().ShouldBe(2000000m);

        HttpResponseMessage bad = await admin.PutAsJsonAsync($"/api/v1/project-financials/{project}/profile", new { Terms = new { RolloverPercent = 150 } }, Ct);
        (await CoreHrApi.ErrorCodeAsync(bad)).ShouldBe("PROFILE_ROLLOVER");
    }

    [Fact]
    public async Task AiUsage_CanBeRecordedAsAProjectCost()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid project = (await CreateProjectAsync(admin, ProjectBody("FixedPrice", org.Manager))).GetProperty("id").GetGuid();

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/project-financials/{project}/costs", new
        {
            Category = "AiUsage",
            Date = Iso(Today),
            Amount = 18000m,
            Vendor = "Anthropic",
            Description = "LLM API, October",
            Planned = false,
        }, Ct));

        JsonElement summary = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/project-financials/{project}", Ct)).GetProperty("summary");
        summary.GetProperty("otherCost").GetDecimal().ShouldBe(18000m);
        summary.GetProperty("categories").EnumerateArray().Single(row => row.GetProperty("category").GetString() == "AiUsage").GetProperty("actual").GetDecimal().ShouldBe(18000m);
    }
}
