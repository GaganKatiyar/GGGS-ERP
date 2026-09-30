using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Onboarding;
using Gggs.Talent.Domain.Onboarding;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record StartOnboardingCaseBody(Guid EmployeeId, Guid TemplateId, DateOnly StartDate);

public sealed record OnboardingCaseResponse(
    Guid Id, Guid EmployeeId, Guid TemplateId, DateOnly StartDate, string Status, int CompletionPercent);

public sealed record OnboardingTaskResponse(
    Guid Id, Guid CaseId, string Title, string AssigneeRole, DateOnly DueDate, bool IsMandatory, string Status, DateTime? CompletedDate);

/// <summary>
/// One employee's run through a template's checklist - started manually against an employee id
/// (no automatic trigger from an accepted <c>Recruitment.Offer</c> yet, deferred rather than
/// bundled into this slice - see <see cref="Gggs.Talent.Domain.Onboarding.OnboardingCase"/>'s own
/// remarks). Tasks are nested under their case for listing; completing one acts on the task by its
/// own id.
/// </summary>
public static class OnboardingCaseEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingCaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder caseGroup = endpoints.MapGroup("/api/v1/onboarding/cases").WithTags("Onboarding");

        caseGroup.MapGet("/{id:guid}", GetAsync).WithName("GetOnboardingCase").RequirePermission(Permissions.Onboarding.CaseView);
        caseGroup.MapGet("/{id:guid}/tasks", ListTasksAsync).WithName("ListOnboardingCaseTasks").RequirePermission(Permissions.Onboarding.CaseView);
        caseGroup.MapPost("/", StartAsync).WithName("StartOnboardingCase").RequirePermission(Permissions.Onboarding.CaseManage);
        caseGroup.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelOnboardingCase").RequirePermission(Permissions.Onboarding.CaseManage);

        RouteGroupBuilder taskGroup = endpoints.MapGroup("/api/v1/onboarding/tasks").WithTags("Onboarding");

        taskGroup.MapPost("/{id:guid}/complete", CompleteTaskAsync).WithName("CompleteOnboardingTask").RequirePermission(Permissions.Onboarding.TaskComplete);

        return endpoints;
    }

    private static async Task<Results<Ok<OnboardingCaseResponse>, NotFound>> GetAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        OnboardingCase? entity = await db.OnboardingCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Ok<IReadOnlyList<OnboardingTaskResponse>>> ListTasksAsync(Guid id, TalentDbContext db, CancellationToken ct)
    {
        List<OnboardingTaskResponse> items = await db.OnboardingTasks.AsNoTracking()
            .Where(x => x.CaseId == id)
            .OrderBy(x => x.DueDate)
            .Select(x => new OnboardingTaskResponse(
                x.Id, x.CaseId, x.Title, x.AssigneeRole.ToString(), x.DueDate, x.IsMandatory, x.Status.ToString(), x.CompletedDate))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<OnboardingTaskResponse>>(items);
    }

    private static async Task<Created<OnboardingCaseResponse>> StartAsync(
        StartOnboardingCaseBody body, OnboardingCaseService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        OnboardingCase entity = await service.StartAsync(body.EmployeeId, body.TemplateId, body.StartDate, ct);
        return TypedResults.Created($"/api/v1/onboarding/cases/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<OnboardingCaseResponse>> CancelAsync(Guid id, OnboardingCaseService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.CancelAsync(id, ct)));

    private static async Task<Ok<OnboardingTaskResponse>> CompleteTaskAsync(Guid id, OnboardingCaseService service, CancellationToken ct)
    {
        OnboardingTask task = await service.CompleteTaskAsync(id, ct);
        return TypedResults.Ok(new OnboardingTaskResponse(
            task.Id, task.CaseId, task.Title, task.AssigneeRole.ToString(), task.DueDate, task.IsMandatory, task.Status.ToString(), task.CompletedDate));
    }

    private static OnboardingCaseResponse ToResponse(OnboardingCase entity) => new(
        entity.Id, entity.EmployeeId, entity.TemplateId, entity.StartDate, entity.Status.ToString(), entity.CompletionPercent);
}
