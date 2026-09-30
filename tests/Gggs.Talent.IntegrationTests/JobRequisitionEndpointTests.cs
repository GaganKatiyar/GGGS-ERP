using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/recruitment/requisitions*</c> over HTTP: creation, listing, and the status
/// lifecycle (Draft -&gt; Open &lt;-&gt; OnHold, Open/OnHold -&gt; Closed or Cancelled).</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class JobRequisitionEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingARequisition_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/recruitment/requisitions",
            new CreateJobRequisitionBody("Software Engineer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingARequisition_WithValidFields_StartsAsDraft()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/recruitment/requisitions",
            new CreateJobRequisitionBody("Software Engineer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, null, "Backlog growth"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JobRequisitionResponse? body = await response.Content.ReadFromJsonAsync<JobRequisitionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Status.ShouldBe("Draft");
        body.RequisitionNo.ShouldStartWith("REQ-");
        body.Openings.ShouldBe(2);
    }

    [Fact]
    public async Task CreatingARequisition_WithZeroOpenings_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/recruitment/requisitions",
            new CreateJobRequisitionBody("Software Engineer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OpeningARequisition_ThenPuttingItOnHoldThenClosing_MovesThroughTheFullLifecycle()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);
        Guid id = await CreateRequisitionAsync(client);

        JobRequisitionResponse opened = await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/open");
        opened.Status.ShouldBe("Open");

        JobRequisitionResponse onHold = await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/hold");
        onHold.Status.ShouldBe("OnHold");

        JobRequisitionResponse resumed = await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/resume");
        resumed.Status.ShouldBe("Open");

        JobRequisitionResponse closed = await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/close");
        closed.Status.ShouldBe("Closed");
    }

    [Fact]
    public async Task OpeningARequisitionTwice_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);
        Guid id = await CreateRequisitionAsync(client);

        await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/open");

        HttpResponseMessage second = await client.PostAsync(
            $"/api/v1/recruitment/requisitions/{id}/open", content: null, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CancellingADraftRequisition_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);
        Guid id = await CreateRequisitionAsync(client);

        JobRequisitionResponse cancelled = await PostAsync(client, $"/api/v1/recruitment/requisitions/{id}/cancel");
        cancelled.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task GettingARequisitionWithAHiringManager_ResolvesTheirDisplayNameFromTheSnapshot()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Recruitment.RequisitionManage);

        Guid managerId = Guid.NewGuid();
        var db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        db.EmployeeSnapshots.Add(Gggs.Talent.Domain.Snapshots.EmployeeSnapshot.Create(managerId, "MGR001", "Priya Sharma", version: 1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        scopeHandle.Dispose();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/recruitment/requisitions",
            new CreateJobRequisitionBody("Software Engineer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, managerId, null),
            TestContext.Current.CancellationToken);

        JobRequisitionResponse? created = await createResponse.Content.ReadFromJsonAsync<JobRequisitionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        created.ShouldNotBeNull();
        created.HiringManagerDisplayName.ShouldBe("Priya Sharma");
    }

    private static async Task<Guid> CreateRequisitionAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/recruitment/requisitions",
            new CreateJobRequisitionBody("Software Engineer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, null, null),
            TestContext.Current.CancellationToken);

        JobRequisitionResponse? body = await response.Content.ReadFromJsonAsync<JobRequisitionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body!.Id;
    }

    private static async Task<JobRequisitionResponse> PostAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobRequisitionResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
