using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Onboarding;
using Gggs.Talent.Domain.Onboarding;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateOnboardingTemplateBody(string Name);

public sealed record AddOnboardingTemplateTaskBody(
    string Title, OnboardingAssigneeRole AssigneeRole, int DueOffsetDays, bool IsMandatory);

public sealed record OnboardingTemplateTaskResponse(
    Guid Id, string Title, string AssigneeRole, int DueOffsetDays, bool IsMandatory);

public sealed record OnboardingTemplateResponse(Guid Id, string Name);

public sealed record OnboardingTemplateListItem(Guid Id, string Name);

/// <summary>
/// Reusable onboarding checklists. No <c>AppliesToJson</c> targeting in this v1 slice - see
/// <see cref="OnboardingTemplate"/>'s own remarks - so a tenant picks a template by hand when
/// starting a case rather than the system selecting one automatically.
/// </summary>
public static class OnboardingTemplateEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/onboarding/templates").WithTags("Onboarding");

        group.MapGet("/", ListAsync).WithName("ListOnboardingTemplates").RequirePermission(Permissions.Onboarding.TemplateView);
        group.MapGet("/{id:guid}/tasks", ListTasksAsync).WithName("ListOnboardingTemplateTasks").RequirePermission(Permissions.Onboarding.TemplateView);

        group.MapPost("/", CreateAsync).WithName("CreateOnboardingTemplate").RequirePermission(Permissions.Onboarding.TemplateManage);
        group.MapPost("/{id:guid}/tasks", AddTaskAsync).WithName("AddOnboardingTemplateTask").RequirePermission(Permissions.Onboarding.TemplateManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<OnboardingTemplateListItem>>> ListAsync(TalentDbContext db, CancellationToken ct)
    {
        List<OnboardingTemplateListItem> items = await db.OnboardingTemplates.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new OnboardingTemplateListItem(x.Id, x.Name))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<OnboardingTemplateListItem>>(items);
    }

    private static async Task<Ok<IReadOnlyList<OnboardingTemplateTaskResponse>>> ListTasksAsync(
        Guid id, TalentDbContext db, CancellationToken ct)
    {
        List<OnboardingTemplateTaskResponse> items = await db.OnboardingTemplateTasks.AsNoTracking()
            .Where(x => x.TemplateId == id)
            .OrderBy(x => x.DueOffsetDays)
            .Select(x => new OnboardingTemplateTaskResponse(x.Id, x.Title, x.AssigneeRole.ToString(), x.DueOffsetDays, x.IsMandatory))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<OnboardingTemplateTaskResponse>>(items);
    }

    private static async Task<Created<OnboardingTemplateResponse>> CreateAsync(
        CreateOnboardingTemplateBody body, OnboardingTemplateService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        OnboardingTemplate entity = await service.CreateAsync(body.Name, ct);
        return TypedResults.Created($"/api/v1/onboarding/templates/{entity.Id}", new OnboardingTemplateResponse(entity.Id, entity.Name));
    }

    private static async Task<Created<OnboardingTemplateTaskResponse>> AddTaskAsync(
        Guid id, AddOnboardingTemplateTaskBody body, OnboardingTemplateService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        OnboardingTemplateTask task = await service.AddTaskAsync(
            id, body.Title, body.AssigneeRole, body.DueOffsetDays, body.IsMandatory, ct);

        return TypedResults.Created(
            $"/api/v1/onboarding/templates/{id}/tasks/{task.Id}",
            new OnboardingTemplateTaskResponse(task.Id, task.Title, task.AssigneeRole.ToString(), task.DueOffsetDays, task.IsMandatory));
    }
}
