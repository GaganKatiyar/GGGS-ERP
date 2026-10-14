using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Workflow.Application;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class WorkflowTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequest_MovesThroughItsStagesInOrder_AndEndsApproved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin,
        [
            Stage("Manager", [Rule("ReportingManager")], slaHours: 24),
            Stage("Finance", [Rule("Employee", org.Finance)], condition: new { Field = "amount", Operator = "GreaterThan", Value = "1000" }),
        ]);

        WorkflowStartResult started = await StartAsync(factory, org.Requester, amount: 5000);
        started.Outcome.ShouldBe(WorkflowStartOutcome.Pending);
        Guid instance = started.InstanceId!.Value;

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient finance = await PersonAsync(factory, org.Finance);
        using HttpClient requester = await PersonAsync(factory, org.Requester);

        (await HasTaskForAsync(finance, instance)).ShouldBeFalse("finance's stage has not started yet");
        JsonElement task = (await PendingTasksAsync(manager)).Single(row => row.GetProperty("instanceId").GetGuid() == instance);
        task.GetProperty("stageName").GetString().ShouldBe("Manager");
        task.TryGetProperty("dueDate", out JsonElement due).ShouldBeTrue("the stage has an SLA");
        due.ValueKind.ShouldBe(JsonValueKind.String);

        await DecideAsync(manager, task.GetProperty("id").GetGuid(), "approve");
        await DecideAsync(finance, await TaskForAsync(finance, instance), "approve", "Within budget");

        (await StatusAsync(requester, instance)).ShouldBe("Approved");
        JsonElement detail = await InstanceAsync(requester, instance);
        string[] history = [.. detail.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()!)];
        history.ShouldBe(["Submitted", "Approved", "Approved", "Completed"]);
        detail.GetProperty("canWithdraw").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task AStageWhoseConditionIsNotMet_IsSkipped()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin,
        [
            Stage("Manager", [Rule("ReportingManager")]),
            Stage("Finance", [Rule("Employee", org.Finance)], condition: new { Field = "amount", Operator = "GreaterThan", Value = "1000" }),
        ]);

        Guid instance = (await StartAsync(factory, org.Requester, amount: 100)).InstanceId!.Value;
        using HttpClient manager = await PersonAsync(factory, org.Manager);
        await DecideAsync(manager, await TaskForAsync(manager, instance), "approve");

        JsonElement detail = await InstanceAsync(manager, instance);
        detail.GetProperty("request").GetProperty("status").GetString().ShouldBe("Approved");
        detail.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()).ShouldContain("StageSkipped");
    }

    [Fact]
    public async Task AnAllStageWaitsForEveryone_AndAnAnyStageForTheFirstAnswer()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin,
        [
            Stage("Both", [Rule("Employee", org.Manager), Rule("Employee", org.Finance)], mode: "All"),
            Stage("Either", [Rule("Employee", org.SkipLevel), Rule("Employee", org.Deputy)], mode: "Any"),
        ]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient finance = await PersonAsync(factory, org.Finance);
        using HttpClient skip = await PersonAsync(factory, org.SkipLevel);
        using HttpClient deputy = await PersonAsync(factory, org.Deputy);

        await DecideAsync(manager, await TaskForAsync(manager, instance), "approve");
        (await HasTaskForAsync(skip, instance)).ShouldBeFalse("the first stage still waits for finance");

        await DecideAsync(finance, await TaskForAsync(finance, instance), "approve");
        (await HasTaskForAsync(skip, instance)).ShouldBeTrue();
        (await HasTaskForAsync(deputy, instance)).ShouldBeTrue();

        await DecideAsync(skip, await TaskForAsync(skip, instance), "approve");
        (await HasTaskForAsync(deputy, instance)).ShouldBeFalse("the first answer decided an Any stage");
        (await StatusAsync(skip, instance)).ShouldBe("Approved");
    }

    [Fact]
    public async Task RejectingNeedsAReason_AndClosesTheRequest()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        using HttpClient manager = await PersonAsync(factory, org.Manager);
        Guid task = await TaskForAsync(manager, instance);

        HttpResponseMessage silent = await manager.PostAsJsonAsync($"/api/v1/workflows/tasks/{task}/reject", new { Comment = (string?)null }, Ct);
        silent.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(silent)).ShouldBe("WORKFLOW_COMMENT_REQUIRED");

        await DecideAsync(manager, task, "reject", "Not this quarter");
        (await StatusAsync(manager, instance)).ShouldBe("Rejected");

        HttpResponseMessage again = await manager.PostAsJsonAsync($"/api/v1/workflows/tasks/{task}/approve", new { }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "a decided request takes no more decisions");
    }

    [Fact]
    public async Task ARequestSentBack_CanBeResubmittedByTheRequester_AsANewRound()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient requester = await PersonAsync(factory, org.Requester);
        using HttpClient stranger = await PersonAsync(factory, org.Finance);

        await DecideAsync(manager, await TaskForAsync(manager, instance), "send-back", "Attach the quote");
        JsonElement sentBack = await InstanceAsync(requester, instance);
        sentBack.GetProperty("request").GetProperty("status").GetString().ShouldBe("SentBack");
        sentBack.GetProperty("canResubmit").GetBoolean().ShouldBeTrue();

        (await stranger.PostAsJsonAsync($"/api/v1/workflows/instances/{instance}/resubmit", new { }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "only the requester resubmits");

        HttpResponseMessage resubmitted = await requester.PostAsJsonAsync($"/api/v1/workflows/instances/{instance}/resubmit", new { Comment = "Quote attached" }, Ct);
        resubmitted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await resubmitted.Content.ReadAsStringAsync(Ct));

        JsonElement detail = await InstanceAsync(requester, instance);
        detail.GetProperty("request").GetProperty("status").GetString().ShouldBe("Pending");
        detail.GetProperty("request").GetProperty("round").GetInt32().ShouldBe(2);
        (await HasTaskForAsync(manager, instance)).ShouldBeTrue("the first stage starts again");
    }

    [Fact]
    public async Task NobodyApprovesTheirOwnRequest_TheFallbackDoes()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        await using (await DefinitionAsync(admin, [Stage("Self", [Rule("Employee", org.Requester)])]))
        {
            DomainRuleException refused = await Should.ThrowAsync<DomainRuleException>(() => StartAsync(factory, org.Requester));
            refused.ErrorCode.ShouldBe("WORKFLOW_NO_APPROVER");
        }

        await using ActiveDefinition withFallback = await DefinitionAsync(admin, [Stage("Self", [Rule("Employee", org.Requester)])], fallback: org.Finance);
        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;

        using HttpClient requester = await PersonAsync(factory, org.Requester);
        using HttpClient finance = await PersonAsync(factory, org.Finance);
        (await HasTaskForAsync(requester, instance)).ShouldBeFalse();
        (await HasTaskForAsync(finance, instance)).ShouldBeTrue();
    }

    [Fact]
    public async Task ADelegation_SendsNewTasksToTheDelegate_AndSaysOnWhoseBehalf()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient deputy = await PersonAsync(factory, org.Deputy);
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        HttpResponseMessage delegated = await manager.PostAsJsonAsync("/api/v1/workflows/delegations", new
        {
            DelegateEmployeeId = org.Deputy,
            StartsOn = today,
            EndsOn = today,
            Reason = "On leave",
        }, Ct);
        delegated.StatusCode.ShouldBe(HttpStatusCode.Created, await delegated.Content.ReadAsStringAsync(Ct));

        HttpResponseMessage forSomeoneElse = await manager.PostAsJsonAsync("/api/v1/workflows/delegations", new
        {
            DelegatorEmployeeId = org.Finance,
            DelegateEmployeeId = org.Deputy,
            StartsOn = today,
            EndsOn = today,
        }, Ct);
        forSomeoneElse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "delegating another person's approvals is an administrator's call");

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        (await HasTaskForAsync(manager, instance)).ShouldBeFalse();

        JsonElement task = (await PendingTasksAsync(deputy)).Single(row => row.GetProperty("instanceId").GetGuid() == instance);
        task.GetProperty("delegatedFromName").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SomeoneElsesTask_IsNotFound()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient finance = await PersonAsync(factory, org.Finance);
        Guid task = await TaskForAsync(manager, instance);

        (await finance.PostAsJsonAsync($"/api/v1/workflows/tasks/{task}/approve", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await finance.GetAsync($"/api/v1/workflows/instances/{instance}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "a request is visible to its requester, its approvers and tenant-wide reviewers only");
    }

    [Fact]
    public async Task AnOverdueTask_EscalatesToTheApproversManager()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")], slaHours: 1)]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;

        int escalated = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            AppDbContext database = services.GetRequiredService<AppDbContext>();
            await database.Set<WorkflowTask>()
                .Where(task => task.InstanceId == instance)
                .ExecuteUpdateAsync(task => task.SetProperty(row => row.DueDate, DateTime.UtcNow.AddHours(-2)), Ct);

            escalated = await services.GetRequiredService<WorkflowEngine>().EscalateOverdueAsync(Ct);
        });

        escalated.ShouldBeGreaterThanOrEqualTo(1);

        using HttpClient manager = await PersonAsync(factory, org.Manager);
        using HttpClient skip = await PersonAsync(factory, org.SkipLevel);
        (await HasTaskForAsync(manager, instance)).ShouldBeFalse();
        (await HasTaskForAsync(skip, instance)).ShouldBeTrue("the manager's manager is next in line");
        (await InstanceAsync(skip, instance)).GetProperty("history").EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString()).ShouldContain("Escalated");
    }

    [Fact]
    public async Task TheRequesterCanWithdraw_AndNothingIsLeftToApprove()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        await using ActiveDefinition definition = await DefinitionAsync(admin, [Stage("Manager", [Rule("ReportingManager")])]);

        Guid instance = (await StartAsync(factory, org.Requester)).InstanceId!.Value;
        using HttpClient requester = await PersonAsync(factory, org.Requester);
        using HttpClient manager = await PersonAsync(factory, org.Manager);

        (await requester.PostAsJsonAsync($"/api/v1/workflows/instances/{instance}/withdraw", new { Comment = "Raised in error" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(requester, instance)).ShouldBe("Withdrawn");
        (await HasTaskForAsync(manager, instance)).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutAnActiveDefinition_ARequestIsApprovedAtOnce()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        WorkflowStartResult result = await StartAsync(factory, org.Requester);
        result.Outcome.ShouldBe(WorkflowStartOutcome.AutoApproved);
        result.InstanceId.ShouldBeNull();
    }

    [Fact]
    public async Task Definitions_AreChecked_AndOnlyOneIsActivePerSubject()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        HttpResponseMessage unknownSubject = await admin.PostAsJsonAsync("/api/v1/workflows/definitions", new
        {
            Code = CoreHrApi.Unique("WF"),
            Name = "x",
            SubjectType = "nothing.here",
            Stages = new[] { Stage("A", [Rule("ReportingManager")]) },
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(unknownSubject)).ShouldBe("WORKFLOW_SUBJECT_UNKNOWN");

        HttpResponseMessage unknownField = await admin.PostAsJsonAsync("/api/v1/workflows/definitions", new
        {
            Code = CoreHrApi.Unique("WF"),
            Name = "x",
            SubjectType = TestSubject,
            Stages = new[] { Stage("A", [Rule("ReportingManager")], condition: new { Field = "colour", Operator = "Equals", Value = "red" }) },
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(unknownField)).ShouldBe("WORKFLOW_CONDITION_FIELD");

        HttpResponseMessage noApprovers = await admin.PostAsJsonAsync("/api/v1/workflows/definitions", new
        {
            Code = CoreHrApi.Unique("WF"),
            Name = "x",
            SubjectType = TestSubject,
            Stages = new[] { Stage("A", []) },
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(noApprovers)).ShouldBe("WORKFLOW_STAGE_APPROVERS");

        await using ActiveDefinition first = await DefinitionAsync(admin, [Stage("A", [Rule("Employee", org.Finance)])]);
        Guid second = await CoreHrApi.CreateAsync(admin, "/api/v1/workflows/definitions", new
        {
            Code = CoreHrApi.Unique("WF"),
            Name = "Second",
            SubjectType = TestSubject,
            Stages = new[] { Stage("A", [Rule("ReportingManager")]) },
        });

        HttpResponseMessage clash = await admin.PostAsync($"/api/v1/workflows/definitions/{second}/activate", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(clash)).ShouldBe("WORKFLOW_SUBJECT_TAKEN");
    }
}
