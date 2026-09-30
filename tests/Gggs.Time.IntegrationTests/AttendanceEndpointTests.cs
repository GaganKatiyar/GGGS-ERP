using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Domain.Snapshots;
using Gggs.Time.Infrastructure;
using Shouldly;

namespace Gggs.Time.IntegrationTests;

/// <summary><c>/api/v1/attendance/regularizations</c> and <c>/api/v1/attendance/overtime-requests</c>
/// over HTTP - the submit/cancel flow through to the <c>StartWorkflowIntegrationEvent</c> being
/// staged in the outbox. Neither ever reaches Approved/Rejected in these tests: that transition only
/// happens through <c>AttendanceWorkflowOutcomeConsumer</c>/<c>OvertimeWorkflowOutcomeConsumer</c>
/// reacting to a workflow outcome event, which is Workflow's own concern, not this API's - see
/// <c>LeaveRequestEndpointTests</c>'s own remarks on the same boundary.</summary>
[Collection(nameof(TimeApiCollection))]
public sealed class AttendanceEndpointTests(TimeApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task SubmittingARegularization_WithAnInvalidTimeRange_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.RegularizationCreate);
        await SeedEmployeeAsync();

        DateTime requestedIn = DateTime.UtcNow.Date.AddHours(9);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/regularizations",
            new SubmitRegularizationBody(null, DateOnly.FromDateTime(requestedIn), requestedIn, requestedIn.AddHours(-1), "MISSED_PUNCH", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingARegularization_ForSelf_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.RegularizationCreate);
        await SeedEmployeeAsync();

        DateTime requestedIn = DateTime.UtcNow.Date.AddHours(9);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/regularizations",
            new SubmitRegularizationBody(null, DateOnly.FromDateTime(requestedIn), requestedIn, requestedIn.AddHours(8), "MISSED_PUNCH", "Forgot to punch out"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        AttendanceRegularizationResponse? body = await response.Content.ReadFromJsonAsync<AttendanceRegularizationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Status.ShouldBe(Gggs.Time.Domain.Attendance.AttendanceRegularizationStatus.Pending);
    }

    [Fact]
    public async Task SubmittingARegularization_ForAnotherEmployee_WithoutManagePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.RegularizationCreate);
        await SeedEmployeeAsync();

        DateTime requestedIn = DateTime.UtcNow.Date.AddHours(9);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/regularizations",
            new SubmitRegularizationBody(Guid.NewGuid(), DateOnly.FromDateTime(requestedIn), requestedIn, requestedIn.AddHours(8), "MISSED_PUNCH", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CancellingARegularization_ThenCancellingItAgain_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.RegularizationCreate);
        await SeedEmployeeAsync();
        Guid regularizationId = await SubmitRegularizationAsync(client);

        (await client.PostAsync($"/api/v1/attendance/regularizations/{regularizationId}/cancel", content: null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage second = await client.PostAsync(
            $"/api/v1/attendance/regularizations/{regularizationId}/cancel", content: null, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingAnOvertimeRequest_WithZeroMinutes_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.OvertimeCreate);
        await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/overtime-requests",
            new SubmitOvertimeRequestBody(null, DateOnly.FromDateTime(DateTime.UtcNow), 0, "PROJECT_DEADLINE", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingAnOvertimeRequest_ForSelf_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.OvertimeCreate);
        await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/overtime-requests",
            new SubmitOvertimeRequestBody(null, DateOnly.FromDateTime(DateTime.UtcNow), 90, "PROJECT_DEADLINE", "Release crunch"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        OvertimeRequestResponse? body = await response.Content.ReadFromJsonAsync<OvertimeRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.RequestedMinutes.ShouldBe(90);
        body.Status.ShouldBe(Gggs.Time.Domain.Attendance.OvertimeRequestStatus.Pending);
        body.ApprovedMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task SubmittingAnOvertimeRequest_ForAnotherEmployee_WithoutManagePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.OvertimeCreate);
        await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/overtime-requests",
            new SubmitOvertimeRequestBody(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), 60, "PROJECT_DEADLINE", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CancellingAnOvertimeRequest_ThenCancellingItAgain_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.OvertimeCreate);
        await SeedEmployeeAsync();
        Guid requestId = await SubmitOvertimeRequestAsync(client);

        (await client.PostAsync($"/api/v1/attendance/overtime-requests/{requestId}/cancel", content: null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage second = await client.PostAsync(
            $"/api/v1/attendance/overtime-requests/{requestId}/cancel", content: null, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> SubmitRegularizationAsync(HttpClient client)
    {
        DateTime requestedIn = DateTime.UtcNow.Date.AddHours(9);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/regularizations",
            new SubmitRegularizationBody(null, DateOnly.FromDateTime(requestedIn), requestedIn, requestedIn.AddHours(8), "MISSED_PUNCH", null),
            TestContext.Current.CancellationToken);

        AttendanceRegularizationResponse? body = await response.Content.ReadFromJsonAsync<AttendanceRegularizationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> SubmitOvertimeRequestAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/overtime-requests",
            new SubmitOvertimeRequestBody(null, DateOnly.FromDateTime(DateTime.UtcNow), 60, "PROJECT_DEADLINE", null),
            TestContext.Current.CancellationToken);

        OvertimeRequestResponse? body = await response.Content.ReadFromJsonAsync<OvertimeRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private async Task SeedEmployeeAsync()
    {
        using TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            if (await db.EmployeeSnapshots.FindAsync([TimeApiFactory.CurrentEmployeeId]) is not null)
            {
                return;
            }

            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                TimeApiFactory.CurrentEmployeeId, "EMP-0001", "Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
