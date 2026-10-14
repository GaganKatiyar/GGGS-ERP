using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// A small organization for one test: a requester, their manager, the manager's manager, and two
/// more people to approve or stand in. Every one is a real employee; clients sign in as them.
/// </summary>
internal sealed record TestOrg(Guid Department, Guid SkipLevel, Guid Manager, Guid Requester, Guid Finance, Guid Deputy);

internal static class PlatformServicesApi
{
    /// <summary>Registered by the test host (see <see cref="PeopleOsApiFactory"/>) so tests own a subject nothing else starts.</summary>
    public const string TestSubject = "test.approval";

    public static WorkflowSubject TestSubjectDefinition { get; } = new(
        TestSubject,
        "Test approval",
        "tests",
        [new WorkflowSubjectField("amount", "Amount", WorkflowFieldType.Number)]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<TestOrg> OrgAsync(HttpClient admin)
    {
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid skipLevel = await CoreHrApi.HireAsync(admin, department, email: Email("skip"));
        Guid manager = await CoreHrApi.HireAsync(admin, department, managerId: skipLevel, email: Email("manager"));
        Guid requester = await CoreHrApi.HireAsync(admin, department, managerId: manager, email: Email("requester"));
        Guid finance = await CoreHrApi.HireAsync(admin, department, email: Email("finance"));
        Guid deputy = await CoreHrApi.HireAsync(admin, department, managerId: skipLevel, email: Email("deputy"));
        return new TestOrg(department, skipLevel, manager, requester, finance, deputy);
    }

    public static string Email(string who) => $"{who}-{Guid.NewGuid():N}@testco.example";

    /// <summary>Someone who approves: their own tasks, their own delegations, their own inbox and self-service.</summary>
    public static Task<HttpClient> PersonAsync(PeopleOsApiFactory factory, Guid employeeId)
        => factory.CreateScopedUserClientAsync(
            Email("person"),
            employeeId,
            (Permissions.Workflow.TaskView, DataScope.Own),
            (Permissions.Workflow.TaskAct, DataScope.Own),
            (Permissions.Workflow.DelegationManage, DataScope.Own),
            (Permissions.Notification.InboxView, DataScope.Own),
            (Permissions.Notification.InboxManage, DataScope.Own),
            (Permissions.Self.DocumentView, DataScope.Own),
            (Permissions.Self.PolicyView, DataScope.Own));

    /// <summary>Creates and activates a definition for the test subject; dispose to deactivate it again.</summary>
    public static async Task<ActiveDefinition> DefinitionAsync(HttpClient admin, object[] stages, Guid? fallback = null, string subject = TestSubject)
    {
        Guid id = await CoreHrApi.CreateAsync(admin, "/api/v1/workflows/definitions", new
        {
            Code = CoreHrApi.Unique("WF"),
            Name = "Test workflow",
            Description = (string?)null,
            SubjectType = subject,
            Stages = stages,
            FallbackApproverEmployeeId = fallback,
        });

        HttpResponseMessage activated = await admin.PostAsync($"/api/v1/workflows/definitions/{id}/activate", null, Ct);
        activated.StatusCode.ShouldBe(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync(Ct));
        return new ActiveDefinition(admin, id);
    }

    public static object Stage(string name, object[] approvers, string mode = "All", object? condition = null, int? slaHours = null, object? escalateTo = null)
        => new { Name = name, Mode = mode, Approvers = approvers, Condition = condition, SlaHours = slaHours, EscalateTo = escalateTo };

    public static object Rule(string kind, Guid? employeeId = null) => new { Kind = kind, EmployeeId = employeeId };

    /// <summary>Starts a request the way an owning module does: through the contract, in its own unit of work.</summary>
    public static async Task<WorkflowStartResult> StartAsync(PeopleOsApiFactory factory, Guid requester, decimal amount = 5000m)
    {
        WorkflowStartResult result = null!;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            result = await services.GetRequiredService<IWorkflowStarter>().StartAsync(
                new WorkflowStartRequest
                {
                    SubjectType = TestSubject,
                    SubjectId = Guid.NewGuid(),
                    Title = "Test request " + CoreHrApi.Unique("R"),
                    RequesterEmployeeId = requester,
                    Fields = new Dictionary<string, string> { ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    Link = "/tests",
                },
                Ct);

            await services.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
        });

        return result;
    }

    public static async Task<JsonElement[]> PendingTasksAsync(HttpClient client)
    {
        JsonElement page = (await client.GetFromJsonAsync<JsonElement>("/api/v1/workflows/tasks", Ct))!;
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    public static async Task<Guid> TaskForAsync(HttpClient client, Guid instanceId)
    {
        JsonElement[] tasks = await PendingTasksAsync(client);
        JsonElement task = tasks.FirstOrDefault(row => row.GetProperty("instanceId").GetGuid() == instanceId);
        task.ValueKind.ShouldBe(JsonValueKind.Object, $"expected a pending task for request {instanceId}");
        return task.GetProperty("id").GetGuid();
    }

    public static async Task<bool> HasTaskForAsync(HttpClient client, Guid instanceId)
        => (await PendingTasksAsync(client)).Any(row => row.GetProperty("instanceId").GetGuid() == instanceId);

    public static async Task DecideAsync(HttpClient client, Guid taskId, string decision, string? comment = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/workflows/tasks/{taskId}/{decision}", new { Comment = comment }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    public static async Task<JsonElement> InstanceAsync(HttpClient client, Guid instanceId)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflows/instances/{instanceId}", Ct))!;

    public static async Task<string> StatusAsync(HttpClient client, Guid instanceId)
        => (await InstanceAsync(client, instanceId)).GetProperty("request").GetProperty("status").GetString()!;
}

/// <summary>An active definition that switches itself off when the test is done, so the next test starts clean.</summary>
internal sealed class ActiveDefinition(HttpClient admin, Guid id) : IAsyncDisposable
{
    public Guid Id { get; } = id;

    public async ValueTask DisposeAsync()
        => await admin.PostAsync($"/api/v1/workflows/definitions/{Id}/deactivate", null, TestContext.Current.CancellationToken);
}
