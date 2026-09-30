using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Payroll.Api.Contracts;
using Gggs.Payroll.Application.Payroll;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Payroll.Api.Endpoints;

/// <summary>
/// Salary components, structures and their lines, and employee salary assignment. No payroll runs,
/// payslips or statutory compliance in this v1 slice - see <c>EmployeeSalary</c>'s own remarks.
/// </summary>
public static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapComponentEndpoints(endpoints);
        MapStructureEndpoints(endpoints);
        MapEmployeeSalaryEndpoints(endpoints);

        return endpoints;
    }

    private static void MapComponentEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/components").WithTags("Payroll");

        group.MapGet("/", ListComponentsAsync).WithName("ListSalaryComponents").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/", CreateComponentAsync).WithName("CreateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateComponentAsync)
            .WithName("DeactivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/{id:guid}/reactivate", ReactivateComponentAsync)
            .WithName("ReactivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
    }

    private static async Task<Ok<IReadOnlyList<SalaryComponentResponse>>> ListComponentsAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryComponentResponse> items = await db.SalaryComponents.AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryComponentResponse>>(items);
    }

    private static async Task<Created<SalaryComponentResponse>> CreateComponentAsync(
        CreateSalaryComponentBody body, SalaryComponentService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryComponent entity = await service.CreateAsync(
            body.Code, body.Name, body.Kind, body.CalcType, body.IsTaxable, body.DisplayOrder, ct);

        return TypedResults.Created($"/api/v1/payroll/components/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<SalaryComponentResponse>> DeactivateComponentAsync(Guid id, SalaryComponentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)));

    private static async Task<Ok<SalaryComponentResponse>> ReactivateComponentAsync(Guid id, SalaryComponentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)));

    private static void MapStructureEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/structures").WithTags("Payroll");

        group.MapGet("/", ListStructuresAsync).WithName("ListSalaryStructures").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapGet("/{id:guid}", GetStructureAsync).WithName("GetSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/", CreateStructureAsync).WithName("CreateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);

        group.MapGet("/{id:guid}/components", ListStructureComponentsAsync)
            .WithName("ListSalaryStructureComponents").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/{id:guid}/components", AddStructureComponentAsync)
            .WithName("AddSalaryStructureComponent").RequirePermission(Permissions.Payroll.StructureManage);
    }

    private static async Task<Ok<IReadOnlyList<SalaryStructureResponse>>> ListStructuresAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryStructureResponse> items = await db.SalaryStructures.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryStructureResponse>>(items);
    }

    private static async Task<Results<Ok<SalaryStructureResponse>, NotFound>> GetStructureAsync(Guid id, PayrollDbContext db, CancellationToken ct)
    {
        SalaryStructure? entity = await db.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<SalaryStructureResponse>> CreateStructureAsync(
        CreateSalaryStructureBody body, SalaryStructureService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryStructure entity = await service.CreateAsync(body.Code, body.Name, ct);
        return TypedResults.Created($"/api/v1/payroll/structures/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<IReadOnlyList<SalaryStructureComponentResponse>>> ListStructureComponentsAsync(
        Guid id, PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryStructureComponentResponse> items = await db.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.StructureId == id)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryStructureComponentResponse>>(items);
    }

    private static async Task<Created<SalaryStructureComponentResponse>> AddStructureComponentAsync(
        Guid id, AddSalaryStructureComponentBody body, SalaryStructureService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryStructureComponent entity = await service.AddComponentAsync(id, body.ComponentId, body.Value, body.DisplayOrder, ct);
        return TypedResults.Created($"/api/v1/payroll/structures/{id}/components/{entity.Id}", ToResponse(entity));
    }

    private static void MapEmployeeSalaryEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/employees/{employeeId:guid}/salary").WithTags("Payroll");

        group.MapGet("/", GetEmployeeSalaryAsync).WithName("GetEmployeeSalary").RequirePermission(Permissions.Payroll.EmployeeSalaryView);

        endpoints.MapPost("/api/v1/payroll/employee-salaries", AssignEmployeeSalaryAsync)
            .WithName("AssignEmployeeSalary").WithTags("Payroll").RequirePermission(Permissions.Payroll.EmployeeSalaryManage);
    }

    private static async Task<Results<Ok<EmployeeSalaryResponse>, NotFound>> GetEmployeeSalaryAsync(
        Guid employeeId, PayrollDbContext db, CancellationToken ct)
    {
        EmployeeSalary? entity = await db.EmployeeSalaries.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.CreatedDate)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Created<EmployeeSalaryResponse>> AssignEmployeeSalaryAsync(
        AssignEmployeeSalaryBody body, EmployeeSalaryService service, PayrollDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        EmployeeSalary entity = await service.AssignAsync(
            body.EmployeeId, body.StructureId, body.AnnualCtc, body.Currency, body.EffectiveFrom, ct);

        return TypedResults.Created(
            $"/api/v1/payroll/employees/{body.EmployeeId}/salary", await ToResponseAsync(entity, db, ct));
    }

    private static async Task<EmployeeSalaryResponse> ToResponseAsync(EmployeeSalary entity, PayrollDbContext db, CancellationToken ct)
    {
        List<EmployeeSalaryComponentResponse> components = await db.EmployeeSalaryComponents.AsNoTracking()
            .Where(x => x.EmployeeSalaryId == entity.Id)
            .Select(x => new EmployeeSalaryComponentResponse(x.Id, x.ComponentId, x.MonthlyAmount, x.AnnualAmount))
            .ToListAsync(ct);

        return new EmployeeSalaryResponse(
            entity.Id, entity.EmployeeId, entity.StructureId, entity.AnnualCtc, entity.Currency, entity.EffectiveFrom, components);
    }

    private static SalaryComponentResponse ToResponse(SalaryComponent entity) => new(
        entity.Id, entity.Code, entity.Name, entity.Kind, entity.CalcType, entity.IsTaxable, entity.DisplayOrder, entity.IsActive);

    private static SalaryStructureResponse ToResponse(SalaryStructure entity) => new(entity.Id, entity.Code, entity.Name, entity.IsActive);

    private static SalaryStructureComponentResponse ToResponse(SalaryStructureComponent entity) => new(
        entity.Id, entity.StructureId, entity.ComponentId, entity.Value, entity.DisplayOrder);
}
