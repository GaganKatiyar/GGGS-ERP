using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/recruitment/postings/{postingId}/applications*</c> and
/// <c>/api/v1/recruitment/applications/{id}*</c> over HTTP: applying to a Published posting
/// (find-or-create the candidate by email), and the Applied -&gt; Screening -&gt; Interview -&gt;
/// Offer -&gt; Hired happy path, with Rejected as the terminal side exit.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class JobApplicationEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task ApplyingToAPosting_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/postings/{Guid.NewGuid()}/applications",
            new ApplyToPostingBody("Priya", "Sharma", $"priya-{Guid.NewGuid():N}@example.com", null, null, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApplyingToADraftPosting_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        Guid postingId = await CreatePostingAsync(client, requisitionId); // never published

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/postings/{postingId}/applications",
            new ApplyToPostingBody("Priya", "Sharma", $"priya-{Guid.NewGuid():N}@example.com", null, null, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ApplyingToAPublishedPosting_CreatesTheApplicationAndTheCandidate()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingId = await CreatePublishedPostingAsync(client);
        string email = $"priya-{Guid.NewGuid():N}@example.com";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/postings/{postingId}/applications",
            new ApplyToPostingBody("Priya", "Sharma", email, "+91-9000000000", "Acme Corp", 36, "LinkedIn"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JobApplicationResponse? body = await response.Content.ReadFromJsonAsync<JobApplicationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Stage.ShouldBe("Applied");
        body.PostingId.ShouldBe(postingId);
        body.CandidateDisplayName.ShouldBe("Priya Sharma");
        body.CandidateEmail.ShouldBe(email.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplyingTwiceWithTheSameEmailToTheSamePosting_IsRejectedTheSecondTime()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingId = await CreatePublishedPostingAsync(client);
        string email = $"priya-{Guid.NewGuid():N}@example.com";
        var body = new ApplyToPostingBody("Priya", "Sharma", email, null, null, null, null);

        (await client.PostAsJsonAsync($"/api/v1/recruitment/postings/{postingId}/applications", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/postings/{postingId}/applications", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ApplyingWithTheSameEmailTwice_ReusesTheExistingCandidate()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingA = await CreatePublishedPostingAsync(client);
        Guid postingB = await CreatePublishedPostingAsync(client);
        string email = $"priya-{Guid.NewGuid():N}@example.com";

        JobApplicationResponse first = await ApplyAsync(client, postingA, email);
        JobApplicationResponse second = await ApplyAsync(client, postingB, email);

        second.CandidateId.ShouldBe(first.CandidateId);
    }

    [Fact]
    public async Task AdvancingAnApplication_ThroughTheFullHappyPath_ReachesHired()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingId = await CreatePublishedPostingAsync(client);
        JobApplicationResponse application = await ApplyAsync(client, postingId, $"priya-{Guid.NewGuid():N}@example.com");

        JobApplicationResponse screening = await PostAsync(client, $"/api/v1/recruitment/applications/{application.Id}/advance");
        screening.Stage.ShouldBe("Screening");

        JobApplicationResponse interview = await PostAsync(client, $"/api/v1/recruitment/applications/{application.Id}/advance");
        interview.Stage.ShouldBe("Interview");

        JobApplicationResponse offer = await PostAsync(client, $"/api/v1/recruitment/applications/{application.Id}/advance");
        offer.Stage.ShouldBe("Offer");

        JobApplicationResponse hired = await PostAsync(client, $"/api/v1/recruitment/applications/{application.Id}/advance");
        hired.Stage.ShouldBe("Hired");
    }

    [Fact]
    public async Task AdvancingAHiredApplication_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingId = await CreatePublishedPostingAsync(client);
        JobApplicationResponse application = await ApplyAsync(client, postingId, $"priya-{Guid.NewGuid():N}@example.com");

        for (int i = 0; i < 4; i++)
        {
            await PostAsync(client, $"/api/v1/recruitment/applications/{application.Id}/advance");
        }

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/recruitment/applications/{application.Id}/advance", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RejectingAnApplication_IsTerminal()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingId = await CreatePublishedPostingAsync(client);
        JobApplicationResponse application = await ApplyAsync(client, postingId, $"priya-{Guid.NewGuid():N}@example.com");

        HttpResponseMessage rejectResponse = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{application.Id}/reject",
            new RejectApplicationBody("Not enough relevant experience"),
            TestContext.Current.CancellationToken);

        rejectResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        JobApplicationResponse? rejected = await rejectResponse.Content.ReadFromJsonAsync<JobApplicationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        rejected.ShouldNotBeNull();
        rejected.Stage.ShouldBe("Rejected");
        rejected.RejectionReason.ShouldBe("Not enough relevant experience");

        HttpResponseMessage advanceAfterReject = await client.PostAsync(
            $"/api/v1/recruitment/applications/{application.Id}/advance", content: null, TestContext.Current.CancellationToken);

        advanceAfterReject.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ListingApplicationsForAPosting_OnlyReturnsThoseForThatPosting()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Recruitment.RequisitionManage, Permissions.Recruitment.PostingManage,
            Permissions.Recruitment.ApplicationManage, Permissions.Recruitment.ApplicationView);

        Guid postingA = await CreatePublishedPostingAsync(client);
        Guid postingB = await CreatePublishedPostingAsync(client);
        await ApplyAsync(client, postingA, $"priya-{Guid.NewGuid():N}@example.com");
        await ApplyAsync(client, postingA, $"raj-{Guid.NewGuid():N}@example.com");
        await ApplyAsync(client, postingB, $"other-{Guid.NewGuid():N}@example.com");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/recruitment/postings/{postingA}/applications", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<JobApplicationListItem>? items = await response.Content.ReadFromJsonAsync<List<JobApplicationListItem>>(
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

    private static async Task<Guid> CreatePublishedPostingAsync(HttpClient client)
    {
        Guid requisitionId = await CreateOpenRequisitionAsync(client);
        Guid postingId = await CreatePostingAsync(client, requisitionId);
        await client.PostAsync(
            $"/api/v1/recruitment/requisitions/{requisitionId}/postings/{postingId}/publish", content: null, TestContext.Current.CancellationToken);
        return postingId;
    }

    private static async Task<JobApplicationResponse> ApplyAsync(HttpClient client, Guid postingId, string email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/postings/{postingId}/applications",
            new ApplyToPostingBody("Priya", "Sharma", email, null, null, null, null),
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<JobApplicationResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private static async Task<JobApplicationResponse> PostAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobApplicationResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
