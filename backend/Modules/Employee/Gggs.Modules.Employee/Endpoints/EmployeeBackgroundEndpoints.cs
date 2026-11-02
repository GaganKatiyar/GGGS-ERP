using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Employee.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Modules.Employee.Endpoints;

/// <summary>
/// Education and prior work experience: the employee's own under <c>/api/v1/me/employee</c>, and
/// HR's view of anyone in their scope under <c>/api/v1/employees/{id}</c>. Also the employee's own
/// employment history, which HR already reads at <c>/api/v1/employees/{id}/history</c>.
/// </summary>
internal static class EmployeeBackgroundEndpoints
{
    public static void MapEmployeeBackgroundEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder group)
    {
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/employee").WithTags("Me");

        me.MapGet("/history", async Task<Results<Ok<IReadOnlyList<EmploymentHistoryItem>>, NotFound>> (ICurrentUser user, EmployeeQueries queries, CancellationToken ct)
                => user.EmployeeId is { } employeeId ? TypedResults.Ok(await queries.SelfHistoryAsync(employeeId, ct)) : TypedResults.NotFound())
            .WithName("GetMyEmploymentHistory")
            .RequirePermission(Permissions.Self.ProfileView);

        me.MapGet("/education", async Task<Results<Ok<IReadOnlyList<EducationItem>>, NotFound>> (ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct)
                => user.EmployeeId is { } employeeId ? TypedResults.Ok(await background.EducationAsync(employeeId, permission: null, ct)) : TypedResults.NotFound())
            .WithName("ListMyEducation")
            .RequirePermission(Permissions.Self.ProfileView);

        me.MapPost("/education", async Task<Results<Created<IdResponse>, NotFound>> (EducationInput body, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                Guid id = await background.AddEducationAsync(employeeId, permission: null, body, ct);
                return TypedResults.Created($"/api/v1/me/employee/education/{id}", new IdResponse(id));
            })
            .WithName("AddMyEducation")
            .RequirePermission(Permissions.Self.ProfileEdit);

        me.MapPut("/education/{educationId:guid}", async Task<Results<NoContent, NotFound>> (Guid educationId, EducationInput body, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                await background.UpdateEducationAsync(employeeId, educationId, permission: null, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateMyEducation")
            .RequirePermission(Permissions.Self.ProfileEdit);

        me.MapDelete("/education/{educationId:guid}", async Task<Results<NoContent, NotFound>> (Guid educationId, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                await background.RemoveEducationAsync(employeeId, educationId, permission: null, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveMyEducation")
            .RequirePermission(Permissions.Self.ProfileEdit);

        me.MapGet("/work-experience", async Task<Results<Ok<IReadOnlyList<WorkExperienceItem>>, NotFound>> (ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct)
                => user.EmployeeId is { } employeeId ? TypedResults.Ok(await background.WorkExperienceAsync(employeeId, permission: null, ct)) : TypedResults.NotFound())
            .WithName("ListMyWorkExperience")
            .RequirePermission(Permissions.Self.ProfileView);

        me.MapPost("/work-experience", async Task<Results<Created<IdResponse>, NotFound>> (WorkExperienceInput body, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                Guid id = await background.AddWorkExperienceAsync(employeeId, permission: null, body, ct);
                return TypedResults.Created($"/api/v1/me/employee/work-experience/{id}", new IdResponse(id));
            })
            .WithName("AddMyWorkExperience")
            .RequirePermission(Permissions.Self.ProfileEdit);

        me.MapPut("/work-experience/{experienceId:guid}", async Task<Results<NoContent, NotFound>> (Guid experienceId, WorkExperienceInput body, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                await background.UpdateWorkExperienceAsync(employeeId, experienceId, permission: null, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateMyWorkExperience")
            .RequirePermission(Permissions.Self.ProfileEdit);

        me.MapDelete("/work-experience/{experienceId:guid}", async Task<Results<NoContent, NotFound>> (Guid experienceId, ICurrentUser user, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                await background.RemoveWorkExperienceAsync(employeeId, experienceId, permission: null, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveMyWorkExperience")
            .RequirePermission(Permissions.Self.ProfileEdit);

        group.MapGet("/{id:guid}/education", async (Guid id, EmployeeBackgroundService background, CancellationToken ct)
                => TypedResults.Ok(await background.EducationAsync(id, Permissions.Employee.View, ct)))
            .WithName("ListEmployeeEducation")
            .RequirePermission(Permissions.Employee.View);

        group.MapPost("/{id:guid}/education", async (Guid id, EducationInput body, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                Guid educationId = await background.AddEducationAsync(id, Permissions.Employee.Edit, body, ct);
                return TypedResults.Created($"/api/v1/employees/{id}/education/{educationId}", new IdResponse(educationId));
            })
            .WithName("AddEmployeeEducation")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapPut("/{id:guid}/education/{educationId:guid}", async (Guid id, Guid educationId, EducationInput body, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                await background.UpdateEducationAsync(id, educationId, Permissions.Employee.Edit, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeEducation")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapDelete("/{id:guid}/education/{educationId:guid}", async (Guid id, Guid educationId, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                await background.RemoveEducationAsync(id, educationId, Permissions.Employee.Edit, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveEmployeeEducation")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapGet("/{id:guid}/work-experience", async (Guid id, EmployeeBackgroundService background, CancellationToken ct)
                => TypedResults.Ok(await background.WorkExperienceAsync(id, Permissions.Employee.View, ct)))
            .WithName("ListEmployeeWorkExperience")
            .RequirePermission(Permissions.Employee.View);

        group.MapPost("/{id:guid}/work-experience", async (Guid id, WorkExperienceInput body, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                Guid experienceId = await background.AddWorkExperienceAsync(id, Permissions.Employee.Edit, body, ct);
                return TypedResults.Created($"/api/v1/employees/{id}/work-experience/{experienceId}", new IdResponse(experienceId));
            })
            .WithName("AddEmployeeWorkExperience")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapPut("/{id:guid}/work-experience/{experienceId:guid}", async (Guid id, Guid experienceId, WorkExperienceInput body, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                await background.UpdateWorkExperienceAsync(id, experienceId, Permissions.Employee.Edit, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeWorkExperience")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapDelete("/{id:guid}/work-experience/{experienceId:guid}", async (Guid id, Guid experienceId, EmployeeBackgroundService background, CancellationToken ct) =>
            {
                await background.RemoveWorkExperienceAsync(id, experienceId, Permissions.Employee.Edit, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveEmployeeWorkExperience")
            .RequirePermission(Permissions.Employee.Edit);
    }
}

public sealed record IdResponse(Guid Id);
