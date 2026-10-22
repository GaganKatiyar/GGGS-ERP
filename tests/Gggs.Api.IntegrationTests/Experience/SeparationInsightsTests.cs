using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Application;
using Gggs.Modules.Separation.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Separation v2 phase E end to end: the exit survey answered from self-service and hidden from
/// the manager on request, attrition analytics, a former employee recognised when they apply again,
/// and the assistant tools for one's own separation and for drafting an exit summary.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SeparationInsightsTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee =
    [
        (Permissions.Self.ResignationSubmit, DataScope.Own),
        (Permissions.Ai.AssistantUse, DataScope.Own),
    ];

    private async Task<Guid> PastExitAsync(HttpClient admin, Guid person, string type = "Termination", string reasonCode = "PERFORMANCE")
    {
        Guid caseId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
        {
            EmployeeId = person,
            Type = type,
            Reason = "Left the company",
            NoticeDate = Iso(Today.AddDays(-20)),
            LastWorkingDay = Iso(Today.AddDays(-2)),
            ReasonCode = reasonCode,
        }, Ct))).GetProperty("case").GetProperty("id").GetGuid();
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => await services.GetRequiredService<SeparationService>().SweepAsync(Ct));
        return caseId;
    }

    [Fact]
    public async Task The_exit_survey_is_answered_by_the_leaver_and_can_be_hidden_from_the_manager()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid person = await CoreHrApi.HireAsync(admin, department, managerId: manager);
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("survey"), person, Employee);
        using HttpClient boss = await factory.CreateScopedUserClientAsync(Email("survey-boss"), manager, (Permissions.Separation.CaseView, DataScope.Tenant));

        Guid caseId = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "New opportunity" }, Ct))).GetProperty("case").GetProperty("id").GetGuid();
        JsonElement mine = await OkAsync(await self.GetAsync("/api/v1/me/separation/survey", Ct));
        mine.GetProperty("canAnswer").GetBoolean().ShouldBeTrue();
        mine.GetProperty("survey").GetProperty("questions").GetArrayLength().ShouldBeGreaterThan(3);

        (await CoreHrApi.ErrorCodeAsync(await self.PutAsJsonAsync("/api/v1/me/separation/survey", new { Answers = new Dictionary<string, string> { ["overall"] = "4" }, HiddenFromManager = true }, Ct)))
            .ShouldBe("EXIT_SURVEY_REQUIRED");
        var answers = new Dictionary<string, string> { ["overall"] = "4", ["manager"] = "2", ["growth"] = "3", ["main-reason"] = "Manager", ["recommend"] = "no", ["improve"] = "Clearer goals" };
        JsonElement answered = await OkAsync(await self.PutAsJsonAsync("/api/v1/me/separation/survey", new { Answers = answers, HiddenFromManager = true }, Ct));
        answered.GetProperty("response").GetProperty("hiddenFromManager").GetBoolean().ShouldBeTrue();

        JsonElement hr = (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("survey");
        hr.GetProperty("withheld").GetBoolean().ShouldBeFalse();
        hr.GetProperty("answers").EnumerateArray().Single(answer => answer.GetProperty("key").GetString() == "main-reason").GetProperty("answer").GetString().ShouldBe("Manager");

        JsonElement theirs = (await OkAsync(await boss.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("survey");
        theirs.GetProperty("withheld").GetBoolean().ShouldBeTrue();
        theirs.GetProperty("answers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Attrition_analytics_count_exits_by_kind_reason_and_month()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        await PastExitAsync(admin, person);

        JsonElement analytics = await OkAsync(await admin.GetAsync("/api/v1/separation/analytics?months=3", Ct));
        analytics.GetProperty("months").GetInt32().ShouldBe(3);
        analytics.GetProperty("exits").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        analytics.GetProperty("involuntary").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        analytics.GetProperty("trend").GetArrayLength().ShouldBe(3);
        analytics.GetProperty("reasons").EnumerateArray().ShouldContain(row => row.GetProperty("label").GetString() == "Performance");
        analytics.GetProperty("tenure").EnumerateArray().ShouldContain(row => row.GetProperty("label").GetString() == "2-5 years" && row.GetProperty("count").GetInt32() >= 1);
        analytics.GetProperty("attritionRate").GetDouble().ShouldBeGreaterThan(0);

        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("analytics"), await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin)), Employee);
        (await self.GetAsync("/api/v1/separation/analytics", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_former_employee_who_applies_again_is_flagged_with_their_rehire_eligibility()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        string personal = Email("boomerang");
        (await admin.PutAsJsonAsync($"/api/v1/employees/{person}/contact", new { PersonalEmail = personal, MobilePhone = "+91 98450 77665" }, Ct)).EnsureSuccessStatusCode();
        await PastExitAsync(admin, person, "Resignation", "RELOCATION");

        JsonElement alumnus = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/separation/alumni", Ct))!.Single(row => row.GetProperty("employeeId").GetGuid() == person);
        alumnus.GetProperty("email").GetString().ShouldBe(personal.ToLowerInvariant());
        alumnus.GetProperty("mobile").GetString().ShouldBe("919845077665");

        JsonElement candidate = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new
        {
            Details = new { FirstName = "Returning", LastName = CoreHrApi.Unique("Cand"), Email = Email("boomerang-new"), Mobile = "919845077665", Skills = new[] { "C#" } },
            Source = "Direct",
        }, Ct));
        JsonElement former = candidate.GetProperty("candidate").GetProperty("formerEmployee");
        former.GetProperty("employeeId").GetGuid().ShouldBe(person);
        former.GetProperty("eligibleForRehire").GetBoolean().ShouldBeTrue();
        former.GetProperty("separationType").GetString().ShouldBe("Resignation");
    }

    [Fact]
    public async Task The_assistant_reports_ones_own_separation_and_drafts_an_exit_summary_for_approval()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("ai-exit"), person, Employee);
        Guid caseId = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Higher studies", ReasonCode = "HIGHER_STUDIES" }, Ct))).GetProperty("case").GetProperty("id").GetGuid();

        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("my_separation_status", new { });
        factory.Llm.ReplyText("Your resignation is approved.");
        await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "When is my last day?" }, Ct));
        LlmToolResult status = factory.Llm.Requests.Last().Messages[^1].Blocks.OfType<LlmToolResult>().Single();
        status.IsError.ShouldBeFalse();
        status.Content.ShouldContain("\"status\":\"Approved\"");

        string name = (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("case").GetProperty("employeeName").GetString()!;
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("draft_exit_summary", new { employeeName = name, summary = "Leaving for a master's degree abroad; regretted; open to rehire after graduation." });
        factory.Llm.ReplyText("I've drafted the summary for you to approve.");
        JsonElement view = await OkAsync(await admin.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "hr", Message = $"Draft an exit summary for {name}" }, Ct));
        Guid actionId = view.GetProperty("actions").EnumerateArray().Single().GetProperty("id").GetGuid();
        (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("events").EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("kind").GetString() == "exit-summary", "nothing is added before approval");

        await OkAsync(await admin.PostAsync($"/api/v1/ai/actions/{actionId}/approve", null, Ct));
        (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("events").EnumerateArray()
            .ShouldContain(entry => entry.GetProperty("kind").GetString() == "exit-summary" && entry.GetProperty("note").GetString()!.StartsWith("Leaving for a master's degree", StringComparison.Ordinal));
    }
}
