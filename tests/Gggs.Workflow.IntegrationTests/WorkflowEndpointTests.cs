using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Workflow.Api.Contracts;
using Gggs.Workflow.Domain.Workflows;
using Gggs.Workflow.Infrastructure;
using Shouldly;

namespace Gggs.Workflow.IntegrationTests;

/// <summary><c>/api/v1/workflows/definitions*</c> over HTTP.</summary>
[Collection(nameof(WorkflowApiCollection))]
public sealed class WorkflowDefinitionEndpointTests(WorkflowApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingADefinition_WithAUniqueCode_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage);
        string code = $"LEAVE-{Guid.CreateVersion7():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workflows/definitions",
            new CreateWorkflowDefinitionBody(code, "Leave request", "LEAVE_REQUEST"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        WorkflowDefinitionResponse? body = await response.Content.ReadFromJsonAsync<WorkflowDefinitionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Code.ShouldBe(code.ToUpperInvariant());
    }

    [Fact]
    public async Task CreatingADefinition_WithADuplicateCode_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage);
        string code = $"LEAVE-{Guid.CreateVersion7():N}";
        var body = new CreateWorkflowDefinitionBody(code, "Leave request", "LEAVE_REQUEST");

        (await client.PostAsJsonAsync("/api/v1/workflows/definitions", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/workflows/definitions", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingADefinition_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workflows/definitions",
            new CreateWorkflowDefinitionBody("X", "X", "X"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingADraftVersion_WithNoSteps_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage);
        Guid definitionId = await CreateDefinitionAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/definitions/{definitionId}/versions",
            new CreateWorkflowDefinitionVersionBody("""{ "steps": [] }"""),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingADraftVersion_WithDuplicateStepKeys_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage);
        Guid definitionId = await CreateDefinitionAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/definitions/{definitionId}/versions",
            new CreateWorkflowDefinitionVersionBody(
                """
                { "steps": [
                    { "key": "manager", "name": "A", "approvers": { "type": "SpecificUser", "userId": "11111111-1111-1111-1111-111111111111" } },
                    { "key": "manager", "name": "B", "approvers": { "type": "SpecificUser", "userId": "11111111-1111-1111-1111-111111111111" } }
                ] }
                """),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PublishingADraftVersion_MakesItTheDefinitionsPublishedVersion()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage);
        Guid definitionId = await CreateDefinitionAsync(client);
        Guid versionId = await CreateValidDraftVersionAsync(client, definitionId);

        HttpResponseMessage publishResponse = await client.PostAsync(
            $"/api/v1/workflows/definitions/versions/{versionId}/publish", content: null, TestContext.Current.CancellationToken);

        publishResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkflowDefinitionVersionResponse? published = await publishResponse.Content.ReadFromJsonAsync<WorkflowDefinitionVersionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        published.ShouldNotBeNull();
        published.Status.ShouldBe(WorkflowVersionStatus.Published);
    }

    [Fact]
    public async Task PublishingASecondVersion_RetiresTheFirstOne()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.DefinitionManage, Permissions.Workflow.DefinitionView);
        Guid definitionId = await CreateDefinitionAsync(client);
        Guid firstVersionId = await CreateValidDraftVersionAsync(client, definitionId);
        await PublishAsync(client, firstVersionId);

        Guid secondVersionId = await CreateValidDraftVersionAsync(client, definitionId);
        await PublishAsync(client, secondVersionId);

        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/v1/workflows/definitions/{definitionId}/versions", TestContext.Current.CancellationToken);
        List<WorkflowDefinitionVersionResponse>? versions = await listResponse.Content.ReadFromJsonAsync<List<WorkflowDefinitionVersionResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        versions.ShouldNotBeNull();
        versions.Single(v => v.Id == firstVersionId).Status.ShouldBe(WorkflowVersionStatus.Retired);
        versions.Single(v => v.Id == secondVersionId).Status.ShouldBe(WorkflowVersionStatus.Published);
    }

    private static async Task<Guid> CreateDefinitionAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workflows/definitions",
            new CreateWorkflowDefinitionBody($"CODE-{Guid.CreateVersion7():N}", "A definition", "LEAVE_REQUEST"),
            TestContext.Current.CancellationToken);

        WorkflowDefinitionResponse? body = await response.Content.ReadFromJsonAsync<WorkflowDefinitionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateValidDraftVersionAsync(HttpClient client, Guid definitionId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/definitions/{definitionId}/versions",
            new CreateWorkflowDefinitionVersionBody(
                """
                { "steps": [
                    { "key": "manager", "name": "Manager", "approvers": { "type": "SpecificUser", "userId": "11111111-1111-1111-1111-111111111111" } }
                ] }
                """),
            TestContext.Current.CancellationToken);

        WorkflowDefinitionVersionResponse? body = await response.Content.ReadFromJsonAsync<WorkflowDefinitionVersionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task PublishAsync(HttpClient client, Guid versionId)
        => (await client.PostAsync(
            $"/api/v1/workflows/definitions/versions/{versionId}/publish", content: null, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
}

/// <summary><c>/api/v1/workflows/tasks*</c> over HTTP - the approvals inbox.</summary>
[Collection(nameof(WorkflowApiCollection))]
public sealed class WorkflowTaskEndpointTests(WorkflowApiFactory factory)
{
    [Fact]
    public async Task ApprovingATask_NotAssignedToTheCaller_IsRejected()
    {
        // A task assigned to a different user - the caller has the permission but is not this
        // task's assignee, which WorkflowEngine.ActAsync checks independently of RBAC.
        Guid taskId;

        using (WorkflowDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle))
        {
            using (scopeHandle)
            {
                WorkflowDefinition definition = WorkflowDefinition.Create($"CODE-{Guid.CreateVersion7():N}", "A definition", "LEAVE_REQUEST");
                WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(
                    definition.Id, 1, """{ "steps": [{ "key": "manager", "name": "Manager", "approvers": { "type": "SpecificUser" } }] }""");
                version.Publish(DateTime.UtcNow);

                WorkflowInstance instance = WorkflowInstance.Start(
                    version.Id, "LEAVE_REQUEST", Guid.NewGuid(), "time",
                    initiatorUserId: Guid.NewGuid(), subjectEmployeeId: Guid.NewGuid(), "{}", null, "manager", DateTime.UtcNow);

                WorkflowTask task = WorkflowTask.Create(instance.Id, "manager", Guid.NewGuid(), null, null);

                db.Definitions.Add(definition);
                db.DefinitionVersions.Add(version);
                db.Instances.Add(instance);
                db.Tasks.Add(task);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                taskId = task.Id;
            }
        }

        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.TaskAct);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/tasks/{taskId}/approve", new ActOnWorkflowTaskBody(null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ApprovingATask_AssignedToTheCaller_CompletesTheSingleStepInstance()
    {
        Guid taskId;

        using (WorkflowDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle))
        {
            using (scopeHandle)
            {
                WorkflowDefinition definition = WorkflowDefinition.Create($"CODE-{Guid.CreateVersion7():N}", "A definition", "LEAVE_REQUEST");
                WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(
                    definition.Id, 1, """{ "steps": [{ "key": "manager", "name": "Manager", "approvers": { "type": "SpecificUser" } }] }""");
                version.Publish(DateTime.UtcNow);

                WorkflowInstance instance = WorkflowInstance.Start(
                    version.Id, "LEAVE_REQUEST", Guid.NewGuid(), "time",
                    initiatorUserId: Guid.NewGuid(), subjectEmployeeId: Guid.NewGuid(), "{}", null, "manager", DateTime.UtcNow);

                WorkflowTask task = WorkflowTask.Create(instance.Id, "manager", WorkflowApiFactory.CurrentUserId, null, null);

                db.Definitions.Add(definition);
                db.DefinitionVersions.Add(version);
                db.Instances.Add(instance);
                db.Tasks.Add(task);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                taskId = task.Id;
            }
        }

        using HttpClient client = factory.CreateClientFor(Permissions.Workflow.TaskAct);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/tasks/{taskId}/approve", new ActOnWorkflowTaskBody("Approved"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ListingMyTasks_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, WorkflowApiFactory.SeededTenantId.ToString());

        HttpResponseMessage response = await client.GetAsync("/api/v1/workflows/tasks", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
