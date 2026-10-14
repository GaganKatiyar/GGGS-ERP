using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PolicyTests(PeopleOsApiFactory factory)
{
    private const string PolicySubject = "policy.publish";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAnApprovalWorkflow_PublishingTakesEffectAtOnce_AndPeopleAcknowledgeIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        (Guid policy, Guid draft) = await CreateAsync(admin, requiresAcknowledgement: true);
        JsonElement published = await PublishAsync(admin, policy, draft);
        published.GetProperty("status").GetString().ShouldBe("Published");

        using HttpClient employee = await PersonAsync(factory, org.Requester);
        JsonElement mine = (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/policies", Ct))!
            .First(row => row.GetProperty("policyId").GetGuid() == policy);
        mine.GetProperty("requiresAcknowledgement").GetBoolean().ShouldBeTrue();
        mine.TryGetProperty("acknowledgedDate", out _).ShouldBeFalse();

        JsonElement detail = (await employee.GetFromJsonAsync<JsonElement>($"/api/v1/me/policies/{policy}", Ct))!;
        detail.GetProperty("content").GetString().ShouldContain("Be kind");

        (await employee.PostAsync($"/api/v1/me/policies/{policy}/acknowledge", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await employee.PostAsync($"/api/v1/me/policies/{policy}/acknowledge", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "acknowledging twice is harmless");

        JsonElement report = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/policies/{policy}/acknowledgements", Ct))!;
        report.GetProperty("acknowledgedCount").GetInt32().ShouldBe(1);
        report.GetProperty("audienceCount").GetInt32().ShouldBeGreaterThan(1);
        report.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("employeeId").GetGuid() == org.Requester)
            .GetProperty("acknowledgedDate").ValueKind.ShouldBe(JsonValueKind.String);

        // Everyone in the audience was asked, once the worker delivered the notifications.
        JsonElement[] inbox = [.. (await employee.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).GetProperty("items").EnumerateArray()];
        inbox.ShouldContain(item => item.GetProperty("eventCode").GetString() == "policy.published");
    }

    [Fact]
    public async Task ATargetedPolicy_ReachesOnlyItsAudience()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid otherDepartment = await CoreHrApi.DepartmentAsync(admin);
        Guid outsider = await CoreHrApi.HireAsync(admin, otherDepartment);

        (Guid policy, Guid draft) = await CreateAsync(admin, requiresAcknowledgement: true, departments: [org.Department]);
        await PublishAsync(admin, policy, draft);

        using HttpClient inside = await PersonAsync(factory, org.Requester);
        using HttpClient outside = await PersonAsync(factory, outsider);

        (await inside.GetAsync($"/api/v1/me/policies/{policy}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outside.GetAsync($"/api/v1/me/policies/{policy}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outside.GetFromJsonAsync<JsonElement[]>("/api/v1/me/policies", Ct))!
            .ShouldNotContain(row => row.GetProperty("policyId").GetGuid() == policy);

        JsonElement report = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/policies/{policy}/acknowledgements", Ct))!;
        report.GetProperty("audienceCount").GetInt32().ShouldBe(5, "the five people of the test department");
    }

    [Fact]
    public async Task WithAnApprovalWorkflow_PublishingWaitsForTheApprover()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Compliance", [Rule("Employee", org.Finance)])], subject: PolicySubject);

        (Guid policy, Guid draft) = await CreateAsync(admin, requiresAcknowledgement: false);
        JsonElement pending = await PublishAsync(admin, policy, draft);
        pending.GetProperty("status").GetString().ShouldBe("PendingApproval");
        Guid instance = pending.GetProperty("workflowInstanceId").GetGuid();

        using HttpClient approver = await PersonAsync(factory, org.Finance);
        await DecideAsync(approver, await TaskForAsync(approver, instance), "approve");
        await factory.DeliverEventsAsync();

        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/policies/{policy}", Ct))!;
        detail.GetProperty("versions")[0].GetProperty("status").GetString().ShouldBe("Published");
        detail.GetProperty("policy").GetProperty("publishedVersion").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ARejectedPublication_LeavesADraftToFix_AndTheNextVersionSupersedesTheOld()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        (Guid policy, Guid first) = await CreateAsync(admin, requiresAcknowledgement: false);
        await PublishAsync(admin, policy, first);

        JsonElement second = await (await admin.PostAsJsonAsync($"/api/v1/policies/{policy}/versions", new
        {
            Content = "# Code of conduct\n\nBe kind. Be on time.",
            ChangeSummary = "Punctuality",
            EffectiveFrom = "2026-11-01",
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Guid secondId = second.GetProperty("id").GetGuid();

        HttpResponseMessage another = await admin.PostAsJsonAsync($"/api/v1/policies/{policy}/versions", new { Content = "x", EffectiveFrom = "2026-11-01" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(another)).ShouldBe("POLICY_DRAFT_EXISTS");

        HttpResponseMessage frozen = await admin.PutAsJsonAsync($"/api/v1/policies/{policy}/versions/{first}", new { Content = "rewrite", EffectiveFrom = "2026-11-01" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(frozen)).ShouldBe("POLICY_VERSION_FROZEN");

        await using (await DefinitionAsync(admin, [Stage("Compliance", [Rule("Employee", org.Finance)])], subject: PolicySubject))
        {
            Guid instance = (await PublishAsync(admin, policy, secondId)).GetProperty("workflowInstanceId").GetGuid();
            using HttpClient approver = await PersonAsync(factory, org.Finance);
            await DecideAsync(approver, await TaskForAsync(approver, instance), "reject", "Clarify what on time means");
            await factory.DeliverEventsAsync();
        }

        JsonElement afterRejection = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/policies/{policy}", Ct))!;
        afterRejection.GetProperty("versions")[0].GetProperty("status").GetString().ShouldBe("Draft");

        // No workflow now: publishing the fixed draft supersedes version 1.
        await PublishAsync(admin, policy, secondId);
        JsonElement[] versions = [.. (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/policies/{policy}", Ct)).GetProperty("versions").EnumerateArray()];
        versions.Single(row => row.GetProperty("number").GetInt32() == 2).GetProperty("status").GetString().ShouldBe("Published");
        versions.Single(row => row.GetProperty("number").GetInt32() == 1).GetProperty("status").GetString().ShouldBe("Superseded");
    }

    private async Task<(Guid Policy, Guid Draft)> CreateAsync(HttpClient admin, bool requiresAcknowledgement, Guid[]? departments = null)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/policies", new
        {
            Code = CoreHrApi.Unique("POL"),
            Title = "Code of conduct " + CoreHrApi.Unique("T"),
            Summary = "How we treat each other.",
            Category = "Conduct",
            RequiresAcknowledgement = requiresAcknowledgement,
            AudienceDepartmentIds = departments ?? [],
            AudienceLocationIds = Array.Empty<Guid>(),
            Content = "# Code of conduct\n\nBe kind.",
            EffectiveFrom = "2026-10-01",
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        await factory.DeliverEventsAsync();
        return (created.GetProperty("policy").GetProperty("id").GetGuid(), created.GetProperty("versions")[0].GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> PublishAsync(HttpClient admin, Guid policy, Guid version)
    {
        HttpResponseMessage response = await admin.PostAsync($"/api/v1/policies/{policy}/versions/{version}/publish", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        await factory.DeliverEventsAsync();
        return result;
    }
}
