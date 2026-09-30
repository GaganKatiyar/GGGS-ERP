using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/recruitment/requisitions/{requisitionId}/postings*</c> over HTTP: creation
/// (gated on the parent requisition being Open, and on a unique slug), and the Draft -&gt;
/// Published -&gt; Closed lifecycle.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class JobPostingEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingAPosting_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{Guid.NewGuid()}/postings",
            new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", null, true, true, $"job-{Guid.NewGuid():N}", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingAPosting_AgainstADraftRequisition_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateRequisitionAsync(client); // stays Draft - never opened

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
            new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", null, true, true, $"job-{Guid.NewGuid():N}", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingAPosting_AgainstAnOpenRequisition_StartsAsDraft()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        string slug = $"job-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
            new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", "Bangalore", true, true, slug, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JobPostingResponse? body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Status.ShouldBe("Draft");
        body.RequisitionId.ShouldBe(requisitionId);
        body.Slug.ShouldBe(slug);
    }

    [Fact]
    public async Task CreatingAPosting_WithNeitherInternalNorPublic_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
            new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", null, false, false, $"job-{Guid.NewGuid():N}", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingAPosting_WithASlugAlreadyInUse_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        string slug = $"job-{Guid.NewGuid():N}";

        (await client.PostAsJsonAsync(
                $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
                new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", null, true, false, slug, null),
                TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
            new CreateJobPostingBody("Another Role", "<p>Join us</p>", null, true, false, slug, null),
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PublishingThenClosingAPosting_MovesThroughTheFullLifecycle()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        Guid postingId = await CreatePostingAsync(client, requisitionId);

        JobPostingResponse published = await PostAsync(client, $"/api/v1/recruitment/requisitions/{requisitionId}/postings/{postingId}/publish");
        published.Status.ShouldBe("Published");
        published.PublishedDate.ShouldNotBeNull();

        JobPostingResponse closed = await PostAsync(client, $"/api/v1/recruitment/requisitions/{requisitionId}/postings/{postingId}/close");
        closed.Status.ShouldBe("Closed");
    }

    [Fact]
    public async Task PublishingAPosting_ThroughTheWrongRequisitionsUrl_IsNotFound()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        Guid postingId = await CreatePostingAsync(client, requisitionId);
        Guid otherRequisitionId = await CreateOpenRequisitionAsync(client);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/recruitment/requisitions/{otherRequisitionId}/postings/{postingId}/publish",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListingPostings_OnlyReturnsThoseForTheGivenRequisition()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage, Permissions.Recruitment.PostingView);

        Guid requisitionA = await CreateOpenRequisitionAsync(client);
        Guid requisitionB = await CreateOpenRequisitionAsync(client);
        await CreatePostingAsync(client, requisitionA);
        await CreatePostingAsync(client, requisitionA);
        await CreatePostingAsync(client, requisitionB);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/recruitment/requisitions/{requisitionA}/postings", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<JobPostingListItem>? items = await response.Content.ReadFromJsonAsync<List<JobPostingListItem>>(
            JsonOptions, TestContext.Current.CancellationToken);

        items.ShouldNotBeNull();
        items.Count.ShouldBe(2);
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

    private static async Task<Guid> CreateOpenRequisitionAsync(HttpClient client)
    {
        Guid id = await CreateRequisitionAsync(client);
        await client.PostAsync($"/api/v1/recruitment/requisitions/{id}/open", content: null, TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<Guid> CreatePostingAsync(HttpClient client, Guid requisitionId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings",
            new CreateJobPostingBody("Software Engineer", "<p>Join us</p>", null, true, true, $"job-{Guid.NewGuid():N}", null),
            TestContext.Current.CancellationToken);

        JobPostingResponse? body = await response.Content.ReadFromJsonAsync<JobPostingResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body!.Id;
    }

    private static async Task<JobPostingResponse> PostAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobPostingResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
