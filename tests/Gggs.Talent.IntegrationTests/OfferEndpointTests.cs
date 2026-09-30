using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Gggs.Talent.Contracts;
using Gggs.Talent.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/recruitment/applications/{applicationId}/offer</c> and
/// <c>/api/v1/recruitment/offers/{id}*</c> over HTTP: creation once an application reaches the
/// Offer stage, and the Draft -&gt; Sent -&gt; Accepted/Declined/Revoked lifecycle, including that
/// accepting an offer also marks its application Hired.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class OfferEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
    [
        Permissions.Recruitment.RequisitionManage,
        Permissions.Recruitment.PostingManage,
        Permissions.Recruitment.ApplicationManage,
        Permissions.Recruitment.ApplicationView,
        Permissions.Recruitment.OfferManage,
        Permissions.Recruitment.OfferView,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingAnOffer_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{Guid.NewGuid()}/offer",
            new CreateOfferBody(Guid.NewGuid(), null, 1200000m, "INR", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingAnOffer_BeforeTheApplicationReachesOfferStage_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid postingId = await CreatePublishedPostingAsync(client);
        Guid applicationId = (await ApplyAsync(client, postingId, $"priya-{Guid.NewGuid():N}@example.com")).Id;
        // Still at Applied - never advanced.

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{applicationId}/offer",
            new CreateOfferBody(Guid.NewGuid(), null, 1200000m, "INR", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingAnOffer_OnceTheApplicationReachesOfferStage_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{applicationId}/offer",
            new CreateOfferBody(Guid.NewGuid(), null, 1500000m, "INR", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        OfferResponse? body = await response.Content.ReadFromJsonAsync<OfferResponse>(JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Status.ShouldBe("Draft");
        body.OfferNo.ShouldStartWith("OFR-");
        body.AnnualCtc.ShouldBe(1500000m);
    }

    [Fact]
    public async Task CreatingASecondOffer_ForTheSameApplication_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        var body = new CreateOfferBody(Guid.NewGuid(), null, 1500000m, "INR", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15));

        (await client.PostAsJsonAsync($"/api/v1/recruitment/applications/{applicationId}/offer", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{applicationId}/offer", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AcceptingAnOffer_AlsoMarksItsApplicationHired()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        Guid offerId = await CreateOfferAsync(client, applicationId);

        await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/send");
        OfferResponse accepted = await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/accept");

        accepted.Status.ShouldBe("Accepted");
        accepted.AcceptedDate.ShouldNotBeNull();

        HttpResponseMessage applicationResponse = await client.GetAsync(
            $"/api/v1/recruitment/applications/{applicationId}", TestContext.Current.CancellationToken);
        JobApplicationResponse? application = await applicationResponse.Content.ReadFromJsonAsync<JobApplicationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        application.ShouldNotBeNull();
        application.Stage.ShouldBe("Hired");
    }

    [Fact]
    public async Task AcceptingAnOffer_StagesAnOfferAcceptedOutboxMessageInTheSameTransaction()
    {
        // Proves the actual cross-process handoff this session's OfferAcceptedConsumer/Worker work
        // depends on: not just that AcceptAsync returns the right HTTP response, but that a real
        // outbox row lands in Talent's own database, addressed with the published wire name, and
        // deserializes back into the exact event Core HR's consumer expects. No broker is running
        // in this test environment - see docs/runbooks/local-dev-login.md's own remarks on why - so
        // this is the outbox half of the handoff, which is also the half a crash between "accepted"
        // and "published" would otherwise lose.
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        Guid offerId = await CreateOfferAsync(client, applicationId);

        await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/send");
        await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/accept");

        TalentDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);

        OutboxMessage message = await db.Set<OutboxMessage>()
            .Where(x => x.MessageType == "gggs.talent.offer.accepted")
            .OrderByDescending(x => x.OccurredDate)
            .FirstAsync(TestContext.Current.CancellationToken);

        message.Status.ShouldBe(OutboxStatus.Pending);
        message.TenantId.ShouldBe(TalentApiFactory.SeededTenantId);

        var payload = JsonSerializer.Deserialize<OfferAcceptedIntegrationEvent>(message.Payload, JsonOptions);
        payload.ShouldNotBeNull();
        payload.OfferId.ShouldBe(offerId);
        payload.ApplicationId.ShouldBe(applicationId);
        payload.CandidateFirstName.ShouldBe("Priya");
        payload.CandidateLastName.ShouldBe("Sharma");

        scopeHandle.Dispose();
    }

    [Fact]
    public async Task DecliningASentOffer_Succeeds_AndLeavesTheApplicationAtOfferStage()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        Guid offerId = await CreateOfferAsync(client, applicationId);

        await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/send");
        OfferResponse declined = await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/decline");

        declined.Status.ShouldBe("Declined");

        HttpResponseMessage applicationResponse = await client.GetAsync(
            $"/api/v1/recruitment/applications/{applicationId}", TestContext.Current.CancellationToken);
        JobApplicationResponse? application = await applicationResponse.Content.ReadFromJsonAsync<JobApplicationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        application.ShouldNotBeNull();
        application.Stage.ShouldBe("Offer");
    }

    [Fact]
    public async Task RevokingADraftOffer_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        Guid offerId = await CreateOfferAsync(client, applicationId);

        OfferResponse revoked = await PostAsync(client, $"/api/v1/recruitment/offers/{offerId}/revoke");
        revoked.Status.ShouldBe("Revoked");
    }

    [Fact]
    public async Task AcceptingADraftOffer_WithoutSendingItFirst_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid applicationId = await CreateApplicationAtOfferStageAsync(client);
        Guid offerId = await CreateOfferAsync(client, applicationId);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/recruitment/offers/{offerId}/accept", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
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

    private static async Task<Guid> CreateApplicationAtOfferStageAsync(HttpClient client)
    {
        Guid postingId = await CreatePublishedPostingAsync(client);
        Guid applicationId = (await ApplyAsync(client, postingId, $"priya-{Guid.NewGuid():N}@example.com")).Id;

        // Applied -> Screening -> Interview -> Offer.
        for (int i = 0; i < 3; i++)
        {
            await client.PostAsync(
                $"/api/v1/recruitment/applications/{applicationId}/advance", content: null, TestContext.Current.CancellationToken);
        }

        return applicationId;
    }

    private static async Task<Guid> CreateOfferAsync(HttpClient client, Guid applicationId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/recruitment/applications/{applicationId}/offer",
            new CreateOfferBody(Guid.NewGuid(), null, 1500000m, "INR", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15)),
            TestContext.Current.CancellationToken);

        OfferResponse? body = await response.Content.ReadFromJsonAsync<OfferResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<OfferResponse> PostAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
