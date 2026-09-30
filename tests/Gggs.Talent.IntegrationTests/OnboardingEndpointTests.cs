using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Talent.Api.Endpoints;
using Gggs.Talent.Domain.Onboarding;
using Shouldly;

namespace Gggs.Talent.IntegrationTests;

/// <summary><c>/api/v1/onboarding/templates*</c>, <c>/api/v1/onboarding/cases*</c> and
/// <c>/api/v1/onboarding/tasks/{id}/complete</c> over HTTP: defining a template's tasks, starting a
/// case (which copies every template task into a real task instance), and completing tasks moving
/// the case's completion percentage and eventually its status.</summary>
[Collection(nameof(TalentApiCollection))]
public sealed class OnboardingEndpointTests(TalentApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
    [
        Permissions.Onboarding.TemplateView,
        Permissions.Onboarding.TemplateManage,
        Permissions.Onboarding.CaseView,
        Permissions.Onboarding.CaseManage,
        Permissions.Onboarding.TaskComplete,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingATemplate_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/onboarding/templates",
            new CreateOnboardingTemplateBody("Standard Engineering Onboarding"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingATemplateAndAddingTasks_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/onboarding/templates/{templateId}/tasks",
            new AddOnboardingTemplateTaskBody("Issue laptop", OnboardingAssigneeRole.It, 0, true),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        OnboardingTemplateTaskResponse? body = await response.Content.ReadFromJsonAsync<OnboardingTemplateTaskResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Title.ShouldBe("Issue laptop");
        body.AssigneeRole.ShouldBe("It");
    }

    [Fact]
    public async Task StartingACase_WithATemplateThatHasNoTasks_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client); // no tasks added

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/onboarding/cases",
            new StartOnboardingCaseBody(Guid.NewGuid(), templateId, new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task StartingACase_CopiesEveryTemplateTaskIntoARealTaskWithADueDate()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client);
        await AddTemplateTaskAsync(client, templateId, "Send welcome email", OnboardingAssigneeRole.Hr, -3, true);
        await AddTemplateTaskAsync(client, templateId, "Issue laptop", OnboardingAssigneeRole.It, 0, true);

        var startDate = new DateOnly(2026, 4, 1);
        Guid caseId = await StartCaseAsync(client, Guid.NewGuid(), templateId, startDate);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/onboarding/cases/{caseId}/tasks", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<OnboardingTaskResponse>? tasks = await response.Content.ReadFromJsonAsync<List<OnboardingTaskResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        tasks.ShouldNotBeNull();
        tasks.Count.ShouldBe(2);
        tasks.ShouldContain(t => t.Title == "Send welcome email" && t.DueDate == startDate.AddDays(-3));
        tasks.ShouldContain(t => t.Title == "Issue laptop" && t.DueDate == startDate);
        tasks.ShouldAllBe(t => t.Status == "NotStarted");
    }

    [Fact]
    public async Task CompletingAllTasks_MarksTheCaseCompleted()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client);
        await AddTemplateTaskAsync(client, templateId, "Send welcome email", OnboardingAssigneeRole.Hr, -3, true);
        await AddTemplateTaskAsync(client, templateId, "Issue laptop", OnboardingAssigneeRole.It, 0, true);

        Guid caseId = await StartCaseAsync(client, Guid.NewGuid(), templateId, new DateOnly(2026, 4, 1));
        List<OnboardingTaskResponse> tasks = await GetTasksAsync(client, caseId);

        await CompleteTaskAsync(client, tasks[0].Id);
        OnboardingCaseResponse afterFirst = await GetCaseAsync(client, caseId);
        afterFirst.CompletionPercent.ShouldBe(50);
        afterFirst.Status.ShouldBe("InProgress");

        await CompleteTaskAsync(client, tasks[1].Id);
        OnboardingCaseResponse afterSecond = await GetCaseAsync(client, caseId);
        afterSecond.CompletionPercent.ShouldBe(100);
        afterSecond.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task CompletingTheSameTaskTwice_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client);
        await AddTemplateTaskAsync(client, templateId, "Issue laptop", OnboardingAssigneeRole.It, 0, true);

        Guid caseId = await StartCaseAsync(client, Guid.NewGuid(), templateId, new DateOnly(2026, 4, 1));
        List<OnboardingTaskResponse> tasks = await GetTasksAsync(client, caseId);

        await CompleteTaskAsync(client, tasks[0].Id);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/onboarding/tasks/{tasks[0].Id}/complete", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CancellingACase_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid templateId = await CreateTemplateAsync(client);
        await AddTemplateTaskAsync(client, templateId, "Issue laptop", OnboardingAssigneeRole.It, 0, true);
        Guid caseId = await StartCaseAsync(client, Guid.NewGuid(), templateId, new DateOnly(2026, 4, 1));

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/onboarding/cases/{caseId}/cancel", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        OnboardingCaseResponse? cancelled = await response.Content.ReadFromJsonAsync<OnboardingCaseResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        cancelled.ShouldNotBeNull();
        cancelled.Status.ShouldBe("Cancelled");
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/onboarding/templates",
            new CreateOnboardingTemplateBody($"Template {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);

        OnboardingTemplateResponse? body = await response.Content.ReadFromJsonAsync<OnboardingTemplateResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body!.Id;
    }

    private static async Task AddTemplateTaskAsync(
        HttpClient client, Guid templateId, string title, OnboardingAssigneeRole assigneeRole, int dueOffsetDays, bool isMandatory)
    {
        await client.PostAsJsonAsync(
            $"/api/v1/onboarding/templates/{templateId}/tasks",
            new AddOnboardingTemplateTaskBody(title, assigneeRole, dueOffsetDays, isMandatory),
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> StartCaseAsync(HttpClient client, Guid employeeId, Guid templateId, DateOnly startDate)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/onboarding/cases",
            new StartOnboardingCaseBody(employeeId, templateId, startDate),
            TestContext.Current.CancellationToken);

        OnboardingCaseResponse? body = await response.Content.ReadFromJsonAsync<OnboardingCaseResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body!.Id;
    }

    private static async Task<List<OnboardingTaskResponse>> GetTasksAsync(HttpClient client, Guid caseId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/onboarding/cases/{caseId}/tasks", TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<List<OnboardingTaskResponse>>(
            JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private static async Task<OnboardingTaskResponse> CompleteTaskAsync(HttpClient client, Guid taskId)
    {
        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/onboarding/tasks/{taskId}/complete", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OnboardingTaskResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private static async Task<OnboardingCaseResponse> GetCaseAsync(HttpClient client, Guid caseId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/onboarding/cases/{caseId}", TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<OnboardingCaseResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
