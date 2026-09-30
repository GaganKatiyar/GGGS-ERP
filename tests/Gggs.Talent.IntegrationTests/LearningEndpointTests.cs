using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/learning/courses*</c> and <c>/api/v1/learning/enrollments*</c> over HTTP: the
/// course catalog, and an enrollment's Assigned -&gt; InProgress -&gt; Completed lifecycle (or
/// Cancelled from either open state). No session scheduling or certification tracking in this v1
/// slice - see <see cref="Gggs.Talent.Domain.Learning.Course"/>'s own remarks.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class LearningEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
    [
        Permissions.Learning.CourseView,
        Permissions.Learning.CourseManage,
        Permissions.Learning.EnrollmentView,
        Permissions.Learning.EnrollmentManage,
        Permissions.Learning.EnrollmentManageOwn,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingACourse_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/learning/courses",
            new CreateCourseBody(ShortCode(), "Workplace Safety", null, 2m),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingACourse_WithADuplicateCode_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        string code = ShortCode();
        var body = new CreateCourseBody(code, "Advanced Excel", null, 4m);

        (await client.PostAsJsonAsync("/api/v1/learning/courses", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/learning/courses", body with { Title = "Advanced Excel (Retake)" }, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeactivatingThenReactivatingACourse_MovesThroughBothStates()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid courseId = await CreateCourseAsync(client);

        HttpResponseMessage deactivateResponse = await client.PostAsync(
            $"/api/v1/learning/courses/{courseId}/deactivate", content: null, TestContext.Current.CancellationToken);
        CourseResponse? deactivated = await deactivateResponse.Content.ReadFromJsonAsync<CourseResponse>(JsonOptions, TestContext.Current.CancellationToken);
        deactivated.ShouldNotBeNull();
        deactivated.IsActive.ShouldBeFalse();

        HttpResponseMessage reactivateResponse = await client.PostAsync(
            $"/api/v1/learning/courses/{courseId}/reactivate", content: null, TestContext.Current.CancellationToken);
        CourseResponse? reactivated = await reactivateResponse.Content.ReadFromJsonAsync<CourseResponse>(JsonOptions, TestContext.Current.CancellationToken);
        reactivated.ShouldNotBeNull();
        reactivated.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task EnrollingAgainstAnInactiveCourse_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid courseId = await CreateCourseAsync(client);
        await client.PostAsync($"/api/v1/learning/courses/{courseId}/deactivate", content: null, TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/learning/enrollments",
            new CreateEnrollmentBody(courseId, Guid.NewGuid(), null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task StartingThenCompletingAnEnrollment_MovesThroughTheFullLifecycle()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid courseId = await CreateCourseAsync(client);
        Guid employeeId = Guid.NewGuid();
        Guid enrollmentId = await CreateEnrollmentAsync(client, courseId, employeeId);

        HttpResponseMessage startResponse = await client.PostAsync(
            $"/api/v1/learning/enrollments/{enrollmentId}/start", content: null, TestContext.Current.CancellationToken);
        EnrollmentResponse? started = await startResponse.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        started.ShouldNotBeNull();
        started.Status.ShouldBe("InProgress");

        HttpResponseMessage completeResponse = await client.PostAsJsonAsync(
            $"/api/v1/learning/enrollments/{enrollmentId}/complete", new CompleteEnrollmentBody(88m), TestContext.Current.CancellationToken);
        EnrollmentResponse? completed = await completeResponse.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        completed.ShouldNotBeNull();
        completed.Status.ShouldBe("Completed");
        completed.Score.ShouldBe(88m);
        completed.CompletedDate.ShouldNotBeNull();

        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/v1/learning/employees/{employeeId}/enrollments", TestContext.Current.CancellationToken);
        List<EnrollmentListItem>? items = await listResponse.Content.ReadFromJsonAsync<List<EnrollmentListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        items.ShouldNotBeNull();
        items.ShouldHaveSingleItem();
        items[0].Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task CompletingAnEnrollment_WithAScoreOutOfRange_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid courseId = await CreateCourseAsync(client);
        Guid enrollmentId = await CreateEnrollmentAsync(client, courseId, Guid.NewGuid());

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/learning/enrollments/{enrollmentId}/complete", new CompleteEnrollmentBody(150m), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CancellingAnEnrollment_ThenCompletingIt_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid courseId = await CreateCourseAsync(client);
        Guid enrollmentId = await CreateEnrollmentAsync(client, courseId, Guid.NewGuid());

        HttpResponseMessage cancelResponse = await client.PostAsync(
            $"/api/v1/learning/enrollments/{enrollmentId}/cancel", content: null, TestContext.Current.CancellationToken);
        EnrollmentResponse? cancelled = await cancelResponse.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        cancelled.ShouldNotBeNull();
        cancelled.Status.ShouldBe("Cancelled");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/learning/enrollments/{enrollmentId}/complete", new CompleteEnrollmentBody(null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/learning/courses",
            new CreateCourseBody(ShortCode(), "Workplace Safety", "Annual refresher", 2m),
            TestContext.Current.CancellationToken);

        CourseResponse? body = await response.Content.ReadFromJsonAsync<CourseResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>A code short enough for <c>Course.Code</c>'s 30-character limit while still unique
    /// per test run.</summary>
    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..20];

    private static async Task<Guid> CreateEnrollmentAsync(HttpClient client, Guid courseId, Guid employeeId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/learning/enrollments",
            new CreateEnrollmentBody(courseId, employeeId, null),
            TestContext.Current.CancellationToken);

        EnrollmentResponse? body = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }
}
