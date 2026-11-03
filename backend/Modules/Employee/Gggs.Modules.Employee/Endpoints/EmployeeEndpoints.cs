using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Employee.Application;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Gggs.Modules.Employee.Endpoints;

/// <summary>
/// Core HR under <c>/api/v1/employees</c>. Reads go through the caller's data scope; sensitive
/// values have their own routes under their own permissions, so an ordinary profile read never
/// carries a PAN or an account number.
/// </summary>
public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/employees").WithTags("Employees");

        group.MapGet("/", async ([AsParameters] EmployeeListQuery query, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.ListAsync(query, ct)))
            .WithName("ListEmployees")
            .RequirePermission(Permissions.Employee.View);

        group.MapGet("/org-chart", async (Guid? rootId, int? depth, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.OrgChartAsync(rootId, depth ?? 3, ct)))
            .WithName("GetOrgChart")
            .RequirePermission(Permissions.Employee.View);

        group.MapGet("/{id:guid}", async (Guid id, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.GetAsync(id, ct)))
            .WithName("GetEmployee")
            .RequirePermission(Permissions.Employee.View);

        group.MapGet("/{id:guid}/360", async (Guid id, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.Get360Async(id, ct)))
            .WithName("GetEmployee360")
            .RequirePermission(Permissions.Employee.View);

        group.MapPost("/", async (HireEmployeeRequest body, EmployeeService service, CancellationToken ct) =>
            {
                Domain.Employee employee = await service.HireAsync(body, ct);
                return TypedResults.Created($"/api/v1/employees/{employee.Id}", new { employee.Id, employee.EmployeeCode });
            })
            .WithName("HireEmployee")
            .RequirePermission(Permissions.Employee.Create);

        group.MapPut("/{id:guid}/personal", async (Guid id, UpdatePersonalRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.UpdatePersonalAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeePersonal")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapPut("/{id:guid}/contact", async (Guid id, UpdateContactRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.UpdateContactAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeContact")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapGet("/{id:guid}/statutory", async (Guid id, EmployeeQueries queries, ILoggerFactory loggers, ICurrentUser user, CancellationToken ct) =>
            {
                StatutoryView view = await queries.RevealStatutoryAsync(id, ct);

                // Every reveal is recorded: who looked at whose identifiers, and when.
                loggers.CreateLogger("Gggs.SensitiveAccess").LogInformation(
                    "User {UserId} revealed statutory identifiers of employee {EmployeeId}.", user.UserId, id);
                return TypedResults.Ok(view);
            })
            .WithName("RevealEmployeeStatutory")
            .RequirePermission(Permissions.Employee.IdentityDocumentView);

        group.MapPut("/{id:guid}/statutory", async (Guid id, UpdateStatutoryRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.UpdateStatutoryAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeStatutory")
            .RequirePermission(Permissions.Employee.IdentityDocumentManage);

        group.MapGet("/{id:guid}/bank", async (Guid id, EmployeeQueries queries, ILoggerFactory loggers, ICurrentUser user, CancellationToken ct) =>
            {
                BankView view = await queries.RevealBankAsync(id, ct);
                loggers.CreateLogger("Gggs.SensitiveAccess").LogInformation(
                    "User {UserId} revealed bank details of employee {EmployeeId}.", user.UserId, id);
                return TypedResults.Ok(view);
            })
            .WithName("RevealEmployeeBank")
            .RequirePermission(Permissions.Employee.BankView);

        group.MapPut("/{id:guid}/bank", async (Guid id, UpdateBankRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.UpdateBankAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateEmployeeBank")
            .RequirePermission(Permissions.Employee.BankManage);

        group.MapGet("/{id:guid}/history", async (Guid id, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.HistoryAsync(id, ct)))
            .WithName("GetEmployeeHistory")
            .RequirePermission(Permissions.Employee.HistoryView);

        group.MapPost("/{id:guid}/assignment-changes", async (Guid id, ChangeAssignmentRequest body, EmployeeService service, CancellationToken ct) =>
            {
                Domain.EmploymentRecord record = await service.ChangeAssignmentAsync(id, body, ct);
                return TypedResults.Created($"/api/v1/employees/{id}/history", new { record.Id, record.EffectiveFrom });
            })
            .WithName("ChangeEmployeeAssignment")
            .RequirePermission(Permissions.Employee.EmploymentManage);

        group.MapPost("/{id:guid}/confirm", async (Guid id, ConfirmRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.ConfirmAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConfirmEmployee")
            .RequirePermission(Permissions.Employee.EmploymentManage);

        group.MapPut("/{id:guid}/contract-end", async (Guid id, ContractEndRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.SetContractEndAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("SetContractEnd")
            .RequirePermission(Permissions.Employee.EmploymentManage);

        group.MapPost("/{id:guid}/notice", async (Guid id, StartNoticeRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.StartNoticeAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("StartEmployeeNotice")
            .RequirePermission(Permissions.Employee.ExitManage);

        group.MapPost("/{id:guid}/exit", async (Guid id, ExitRequest body, EmployeeService service, CancellationToken ct) =>
            {
                await service.ExitAsync(id, body, ct);
                return TypedResults.NoContent();
            })
            .WithName("ExitEmployee")
            .RequirePermission(Permissions.Employee.ExitManage);

        group.MapGet("/{id:guid}/dependents", async (Guid id, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.DependentsAsync(id, ct)))
            .WithName("ListEmployeeDependents")
            .RequirePermission(Permissions.Employee.View);

        group.MapPost("/{id:guid}/dependents", async (Guid id, AddDependentRequest body, EmployeeService service, CancellationToken ct) =>
            {
                Domain.EmployeeDependent dependent = await service.AddDependentAsync(id, body, ct);
                return TypedResults.Created($"/api/v1/employees/{id}/dependents/{dependent.Id}", new { dependent.Id });
            })
            .WithName("AddEmployeeDependent")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapDelete("/{id:guid}/dependents/{dependentId:guid}", async (Guid id, Guid dependentId, EmployeeService service, CancellationToken ct) =>
            {
                await service.RemoveDependentAsync(id, dependentId, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveEmployeeDependent")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapGet("/{id:guid}/emergency-contacts", async (Guid id, EmployeeQueries queries, CancellationToken ct)
                => TypedResults.Ok(await queries.EmergencyContactsAsync(id, ct)))
            .WithName("ListEmergencyContacts")
            .RequirePermission(Permissions.Employee.View);

        group.MapPost("/{id:guid}/emergency-contacts", async (Guid id, AddEmergencyContactRequest body, EmployeeService service, CancellationToken ct) =>
            {
                Domain.EmergencyContact contact = await service.AddEmergencyContactAsync(id, body, ct);
                return TypedResults.Created($"/api/v1/employees/{id}/emergency-contacts/{contact.Id}", new { contact.Id });
            })
            .WithName("AddEmergencyContact")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapDelete("/{id:guid}/emergency-contacts/{contactId:guid}", async (Guid id, Guid contactId, EmployeeService service, CancellationToken ct) =>
            {
                await service.RemoveEmergencyContactAsync(id, contactId, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveEmergencyContact")
            .RequirePermission(Permissions.Employee.Edit);

        group.MapPost("/import/preview", async (EmployeeImportRequest body, EmployeeImportService imports, CancellationToken ct)
                => TypedResults.Ok(await imports.RunAsync(body, commit: false, ct)))
            .WithName("PreviewEmployeeImport")
            .RequirePermission(Permissions.Employee.Create);

        group.MapPost("/import", async (EmployeeImportRequest body, EmployeeImportService imports, CancellationToken ct)
                => TypedResults.Ok(await imports.RunAsync(body, commit: true, ct)))
            .WithName("ImportEmployees")
            .RequirePermission(Permissions.Employee.Create);

        // Self-service: the signed-in employee's own record, whatever their HR permissions.
        endpoints.MapGet("/api/v1/me/employee", async Task<Results<Ok<EmployeeDetail>, NotFound>> (ICurrentUser user, EmployeeQueries queries, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                try
                {
                    return TypedResults.Ok(await queries.GetSelfAsync(employeeId, ct));
                }
                catch (NotFoundException)
                {
                    return TypedResults.NotFound();
                }
            })
            .WithTags("Me")
            .WithName("GetMyEmployeeRecord")
            .RequirePermission(Permissions.Self.ProfileView);

        MapPhotoEndpoints(endpoints, group);
        EmployeeBackgroundEndpoints.MapEmployeeBackgroundEndpoints(endpoints, group);

        return endpoints;
    }

    /// <summary>
    /// Profile photos. Image tags fetch them with the session cookie, so the routes take a <c>v</c>
    /// query (the photo id): a request naming the current photo may be cached for good, because a new
    /// photo gets a new id and so a new URL.
    /// </summary>
    private static void MapPhotoEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder group)
    {
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/employee").WithTags("Me");

        me.MapGet("/avatar", async Task<Results<Ok<PhotoInfo>, NotFound>> (ICurrentUser user, EmployeePhotoService photos, CancellationToken ct)
                => user.EmployeeId is { } employeeId ? TypedResults.Ok(await photos.GetInfoAsync(employeeId, ct)) : TypedResults.NotFound())
            .WithName("GetMyPhotoInfo")
            .RequirePermission(Permissions.Self.ProfileView);

        me.MapGet("/photo", async (Guid? v, HttpContext http, ICurrentUser user, EmployeePhotoService photos, CancellationToken ct)
                => user.EmployeeId is { } employeeId ? Photo(http, v, await photos.OpenOwnAsync(employeeId, ct)) : TypedResults.NotFound())
            .WithName("GetMyPhoto")
            .RequirePermission(Permissions.Self.ProfileView);

        me.MapPut("/photo", async Task<Results<Ok<PhotoInfo>, NotFound>> (IFormFile file, ICurrentUser user, EmployeePhotoService photos, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(file);
                return user.EmployeeId is { } employeeId
                    ? TypedResults.Ok(await photos.ReplaceAsync(employeeId, new IncomingPhoto(file.Length, file.OpenReadStream), ct))
                    : TypedResults.NotFound();
            })
            .WithName("ReplaceMyPhoto")
            .RequirePermission(Permissions.Self.ProfileEdit)
            .DisableAntiforgery(); // Guarded by the X-CSRF header check (BrowserRequestGuard), as every cookie write is.

        me.MapDelete("/photo", async Task<Results<NoContent, NotFound>> (ICurrentUser user, EmployeePhotoService photos, CancellationToken ct) =>
            {
                if (user.EmployeeId is not { } employeeId)
                {
                    return TypedResults.NotFound();
                }

                await photos.RemoveAsync(employeeId, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveMyPhoto")
            .RequirePermission(Permissions.Self.ProfileEdit);

        group.MapGet("/{id:guid}/photo", async (Guid id, Guid? v, HttpContext http, EmployeePhotoService photos, CancellationToken ct)
                => Photo(http, v, await photos.OpenAsync(id, ct)))
            .WithName("GetEmployeePhoto")
            .RequirePermission(Permissions.Employee.View);
    }

    private static IResult Photo(HttpContext http, Guid? version, PhotoContent? photo)
    {
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        // Private: a photo is personal data and must never sit in a shared cache.
        http.Response.Headers.CacheControl = version == photo.PhotoId ? "private, max-age=31536000, immutable" : "private, no-cache";
        return TypedResults.File(photo.Content, photo.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photo.PhotoId:N}\""));
    }
}
