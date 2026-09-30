using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/performance/cycles*</c>, <c>/api/v1/performance/goals*</c> and
/// <c>/api/v1/performance/reviews*</c> over HTTP: a cycle's Draft -&gt; Open -&gt; Closed lifecycle,
/// goals (gated on the cycle being Open) with check-ins updating both the goal's current value and
/// its status once the target is reached, and a review's fixed self-then-manager structure.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class PerformanceEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
    [
        Permissions.Performance.CycleManage,
        Permissions.Performance.CycleView,
        Permissions.Performance.GoalManageOwn,
        Permissions.Performance.GoalManage,
        Permissions.Performance.ReviewView,
        Permissions.Performance.ReviewManageOwn,
        Permissions.Performance.ReviewManageAsManager,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingACycle_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/cycles",
            new CreatePerformanceCycleBody("H1 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OpeningThenClosingACycle_MovesThroughTheFullLifecycle()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateCycleAsync(client);

        PerformanceCycleResponse opened = await PostCycleAsync(client, $"/api/v1/performance/cycles/{cycleId}/open");
        opened.Status.ShouldBe("Open");

        PerformanceCycleResponse closed = await PostCycleAsync(client, $"/api/v1/performance/cycles/{cycleId}/close");
        closed.Status.ShouldBe("Closed");
    }

    [Fact]
    public async Task CreatingAGoal_AgainstADraftCycle_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateCycleAsync(client); // never opened

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/goals",
            new CreateGoalBody(cycleId, Guid.NewGuid(), "Ship v2", null, 100m, "%", new DateOnly(2027, 6, 30)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CheckingInAGoal_UpdatesCurrentValue_AndCompletesItOnceTargetIsReached()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid employeeId = Guid.NewGuid();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/performance/goals",
            new CreateGoalBody(cycleId, employeeId, "Ship v2", "Ship the v2 API", 100m, "%", new DateOnly(2027, 6, 30)),
            TestContext.Current.CancellationToken);

        GoalResponse? created = await createResponse.Content.ReadFromJsonAsync<GoalResponse>(JsonOptions, TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();
        created.Status.ShouldBe("Active");

        HttpResponseMessage partialResponse = await client.PostAsJsonAsync(
            $"/api/v1/performance/goals/{created.Id}/check-ins",
            new CheckInGoalBody(new DateOnly(2027, 3, 1), 40m, "Halfway there"),
            TestContext.Current.CancellationToken);

        GoalResponse? partial = await partialResponse.Content.ReadFromJsonAsync<GoalResponse>(JsonOptions, TestContext.Current.CancellationToken);
        partial.ShouldNotBeNull();
        partial.CurrentValue.ShouldBe(40m);
        partial.Status.ShouldBe("Active");

        HttpResponseMessage completeResponse = await client.PostAsJsonAsync(
            $"/api/v1/performance/goals/{created.Id}/check-ins",
            new CheckInGoalBody(new DateOnly(2027, 6, 1), 100m, "Done"),
            TestContext.Current.CancellationToken);

        GoalResponse? complete = await completeResponse.Content.ReadFromJsonAsync<GoalResponse>(JsonOptions, TestContext.Current.CancellationToken);
        complete.ShouldNotBeNull();
        complete.CurrentValue.ShouldBe(100m);
        complete.Status.ShouldBe("Completed");

        HttpResponseMessage checkInsResponse = await client.GetAsync(
            $"/api/v1/performance/goals/{created.Id}/check-ins", TestContext.Current.CancellationToken);
        List<GoalCheckInResponse>? checkIns = await checkInsResponse.Content.ReadFromJsonAsync<List<GoalCheckInResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        checkIns.ShouldNotBeNull();
        checkIns.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CheckingInACompletedGoal_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid goalId = await CreateGoalAsync(client, cycleId, Guid.NewGuid(), targetValue: 10m);

        await client.PostAsJsonAsync(
            $"/api/v1/performance/goals/{goalId}/check-ins",
            new CheckInGoalBody(new DateOnly(2027, 2, 1), 10m, null),
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/performance/goals/{goalId}/check-ins",
            new CheckInGoalBody(new DateOnly(2027, 3, 1), 5m, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingAReview_WhereTheEmployeeIsTheirOwnManager_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid employeeId = Guid.NewGuid();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/reviews",
            new CreateReviewBody(cycleId, employeeId, employeeId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingASecondReview_ForTheSameEmployeeAndCycle_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid employeeId = Guid.NewGuid();
        var body = new CreateReviewBody(cycleId, employeeId, Guid.NewGuid());

        (await client.PostAsJsonAsync("/api/v1/performance/reviews", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/performance/reviews", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingSelfThenManagerReview_ThenReleasing_MovesThroughTheFullLifecycle()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid reviewId = await CreateReviewAsync(client, cycleId, Guid.NewGuid(), Guid.NewGuid());

        HttpResponseMessage selfResponse = await client.PostAsJsonAsync(
            $"/api/v1/performance/reviews/{reviewId}/self", new SubmitReviewRatingBody(4, "Strong quarter"), TestContext.Current.CancellationToken);
        ReviewResponse? self = await selfResponse.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, TestContext.Current.CancellationToken);
        self.ShouldNotBeNull();
        self.Status.ShouldBe("SelfSubmitted");
        self.SelfRating.ShouldBe(4);

        HttpResponseMessage managerResponse = await client.PostAsJsonAsync(
            $"/api/v1/performance/reviews/{reviewId}/manager", new SubmitReviewRatingBody(3, "Agreed, with room to grow"), TestContext.Current.CancellationToken);
        ReviewResponse? manager = await managerResponse.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, TestContext.Current.CancellationToken);
        manager.ShouldNotBeNull();
        manager.Status.ShouldBe("ManagerSubmitted");
        manager.ManagerRating.ShouldBe(3);

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/v1/performance/reviews/{reviewId}/release", content: null, TestContext.Current.CancellationToken);
        ReviewResponse? released = await releaseResponse.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, TestContext.Current.CancellationToken);
        released.ShouldNotBeNull();
        released.Status.ShouldBe("Released");
        released.ReleasedDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task SubmittingManagerReview_BeforeSelfReview_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid reviewId = await CreateReviewAsync(client, cycleId, Guid.NewGuid(), Guid.NewGuid());

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/performance/reviews/{reviewId}/manager", new SubmitReviewRatingBody(3, null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingARatingOutOfRange_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid reviewId = await CreateReviewAsync(client, cycleId, Guid.NewGuid(), Guid.NewGuid());

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/performance/reviews/{reviewId}/self", new SubmitReviewRatingBody(6, null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GettingAReviewByEmployeeAndCycle_WhenNoneExists_ReturnsNotFound()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/performance/employees/{Guid.NewGuid()}/cycles/{cycleId}/review", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GettingAReviewByEmployeeAndCycle_WhenOneExists_ReturnsIt()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid cycleId = await CreateOpenCycleAsync(client);
        Guid employeeId = Guid.NewGuid();
        Guid managerEmployeeId = Guid.NewGuid();
        Guid reviewId = await CreateReviewAsync(client, cycleId, employeeId, managerEmployeeId);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/performance/employees/{employeeId}/cycles/{cycleId}/review", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReviewResponse? review = await response.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, TestContext.Current.CancellationToken);
        review.ShouldNotBeNull();
        review.Id.ShouldBe(reviewId);
        review.ManagerEmployeeId.ShouldBe(managerEmployeeId);
    }

    private static async Task<Guid> CreateCycleAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/cycles",
            new CreatePerformanceCycleBody($"Cycle {Guid.NewGuid():N}", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)),
            TestContext.Current.CancellationToken);

        PerformanceCycleResponse? body = await response.Content.ReadFromJsonAsync<PerformanceCycleResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body!.Id;
    }

    private static async Task<Guid> CreateOpenCycleAsync(HttpClient client)
    {
        Guid id = await CreateCycleAsync(client);
        await client.PostAsync($"/api/v1/performance/cycles/{id}/open", content: null, TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<Guid> CreateGoalAsync(HttpClient client, Guid cycleId, Guid employeeId, decimal targetValue)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/goals",
            new CreateGoalBody(cycleId, employeeId, "Goal", null, targetValue, "%", new DateOnly(2027, 6, 30)),
            TestContext.Current.CancellationToken);

        GoalResponse? body = await response.Content.ReadFromJsonAsync<GoalResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateReviewAsync(HttpClient client, Guid cycleId, Guid employeeId, Guid managerEmployeeId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/performance/reviews",
            new CreateReviewBody(cycleId, employeeId, managerEmployeeId),
            TestContext.Current.CancellationToken);

        ReviewResponse? body = await response.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<PerformanceCycleResponse> PostCycleAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PerformanceCycleResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
