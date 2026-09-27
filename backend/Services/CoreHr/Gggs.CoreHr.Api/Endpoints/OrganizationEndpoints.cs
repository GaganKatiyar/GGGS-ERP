using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Organization;
using Gggs.CoreHr.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.CoreHr.Api.Endpoints;

// Request and response shapes. Reads are thin list items plus a fuller detail shape, the same
// split every other module in this codebase uses; writes are task-based where a change is really a
// transition (moving a department, fencing a branch) rather than a generic PATCH.

public sealed record AddressBody(string Line1, string? Line2, string City, string? StateCode, string? PostalCode, string CountryCode)
{
    public Address ToAddress() => new(Line1, Line2, City, StateCode, PostalCode, CountryCode);

    public static AddressBody From(Address address)
        => new(address.Line1, address.Line2, address.City, address.StateCode, address.PostalCode, address.CountryCode);
}

public sealed record CreateLegalEntityBody(string Code, string Name, string CountryCode, string Currency, string? RegistrationNumber);

public sealed record UpdateLegalEntityBody(string Name, AddressBody Address, string? CompliancePackCode);

public sealed record LegalEntityResponse(
    Guid Id,
    string Code,
    string Name,
    string CountryCode,
    string Currency,
    string? RegistrationNumber,
    string? CompliancePackCode,
    AddressBody Address);

public sealed record LegalEntityListItem(Guid Id, string Code, string Name, string CountryCode, string Currency);

public sealed record CreateBusinessUnitBody(string Code, string Name);

public sealed record UpdateBusinessUnitBody(string Name, Guid? HeadEmployeeId);

public sealed record BusinessUnitResponse(Guid Id, string Code, string Name, Guid? HeadEmployeeId);

public sealed record CreateBranchBody(string Code, string Name, Guid LegalEntityId, string CountryCode, string TimeZone);

public sealed record UpdateBranchBody(string Name, string TimeZone, AddressBody Address, string? StateCode);

public sealed record SetBranchGeoFenceBody(double? Latitude, double? Longitude, int? RadiusMeters);

public sealed record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    Guid LegalEntityId,
    string CountryCode,
    string? StateCode,
    string TimeZone,
    double? GeoLatitude,
    double? GeoLongitude,
    int? GeoFenceMeters,
    AddressBody Address);

public sealed record BranchListItem(Guid Id, string Code, string Name, Guid LegalEntityId, string TimeZone);

public sealed record CreateLocationBody(string Code, string Name, Guid BranchId, LocationType LocationType);

public sealed record LocationResponse(Guid Id, string Code, string Name, Guid BranchId, LocationType LocationType);

public sealed record CreateDepartmentBody(string Code, string Name, Guid? ParentDepartmentId);

public sealed record RenameDepartmentBody(string Name);

public sealed record SetDepartmentAssignmentsBody(Guid? HeadEmployeeId, Guid? CostCenterId, Guid? BusinessUnitId);

public sealed record MoveDepartmentBody(Guid? NewParentId);

public sealed record DepartmentResponse(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentDepartmentId,
    Guid? HeadEmployeeId,
    Guid? CostCenterId,
    Guid? BusinessUnitId);

public sealed record DepartmentListItem(Guid Id, string Code, string Name, Guid? ParentDepartmentId);

public sealed record CreateDesignationBody(string Code, string Title, string? JobFamily, int? JobLevel);

public sealed record UpdateDesignationBody(string Title, string? JobFamily, int? JobLevel);

public sealed record DesignationResponse(Guid Id, string Code, string Title, string? JobFamily, int? JobLevel);

public sealed record CreateGradeBody(string Code, string Name, int Rank);

public sealed record UpdateGradeBody(string Name, decimal? SalaryBandMinimum, decimal? SalaryBandMaximum, string? Currency);

public sealed record GradeResponse(
    Guid Id,
    string Code,
    string Name,
    int Rank,
    decimal? SalaryBandMinimum,
    decimal? SalaryBandMaximum,
    string? Currency);

public sealed record CreateCostCenterBody(string Code, string Name, string? GlAccountCode);

public sealed record RenameCostCenterBody(string Name);

public sealed record SetCostCenterOwnerBody(Guid? OwnerEmployeeId);

public sealed record MoveCostCenterBody(Guid? NewParentId);

public sealed record CostCenterResponse(
    Guid Id,
    string Code,
    string Name,
    string? GlAccountCode,
    Guid? OwnerEmployeeId,
    Guid? ParentCostCenterId);

/// <summary>
/// Organization structure: legal entities, business units, branches, locations, departments,
/// designations, grades and cost centers.
/// <para>
/// One file for eight resources rather than eight, matching how the rest of this codebase groups an
/// endpoint module by bounded concept, not by table. Every write here is small and mechanical -
/// there is no cross-cutting rule that spans all eight - so the size is the DTOs, not the logic.
/// </para>
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapLegalEntityEndpoints(endpoints);
        MapBusinessUnitEndpoints(endpoints);
        MapBranchEndpoints(endpoints);
        MapLocationEndpoints(endpoints);
        MapDepartmentEndpoints(endpoints);
        MapDesignationEndpoints(endpoints);
        MapGradeEndpoints(endpoints);
        MapCostCenterEndpoints(endpoints);

        return endpoints;
    }

    private static void MapLegalEntityEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/legal-entities").WithTags("Organization");

        group.MapGet("/", async ([AsParameters] PageQuery page, CoreHrDbContext db, CancellationToken ct) =>
        {
            PageRequest request = page.ToPageRequest();
            IQueryable<LegalEntity> query = db.LegalEntities.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                string term = request.Search.Trim();
                query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
            }

            long? total = request.IncludeTotal ? await query.LongCountAsync(ct) : null;

            List<LegalEntityListItem> items = await query
                .OrderBy(x => x.Name)
                .Skip(request.Skip)
                .Take(request.PageSize)
                .Select(x => new LegalEntityListItem(x.Id, x.Code, x.Name, x.CountryCode, x.Currency))
                .ToListAsync(ct);

            return TypedResults.Ok(PagedResult.From(items, request, total));
        }).WithName("ListLegalEntities").RequirePermission(Permissions.Organization.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            LegalEntity? entity = await db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetLegalEntity").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateLegalEntityBody body, OrganizationService service, CancellationToken ct) =>
        {
            LegalEntity entity = await service.CreateLegalEntityAsync(
                body.Code, body.Name, body.CountryCode, body.Currency, body.RegistrationNumber, ct);

            return TypedResults.Created($"/api/v1/legal-entities/{entity.Id}", ToResponse(entity));
        }).WithName("CreateLegalEntity").RequirePermission(Permissions.Organization.LegalEntityManage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateLegalEntityBody body, OrganizationService service, CancellationToken ct) =>
        {
            LegalEntity entity = await service.UpdateLegalEntityAsync(
                id, body.Name, body.Address.ToAddress(), body.CompliancePackCode, ct);

            return TypedResults.Ok(ToResponse(entity));
        }).WithName("UpdateLegalEntity").RequirePermission(Permissions.Organization.LegalEntityManage);
    }

    private static LegalEntityResponse ToResponse(LegalEntity entity) => new(
        entity.Id,
        entity.Code,
        entity.Name,
        entity.CountryCode,
        entity.Currency,
        entity.RegistrationNumber,
        entity.CompliancePackCode,
        AddressBody.From(entity.Address));

    private static void MapBusinessUnitEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/business-units").WithTags("Organization");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<BusinessUnitResponse> items = await db.BusinessUnits.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new BusinessUnitResponse(x.Id, x.Code, x.Name, x.HeadEmployeeId))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListBusinessUnits").RequirePermission(Permissions.Organization.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            BusinessUnit? entity = await db.BusinessUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null
                ? Results.NotFound()
                : Results.Ok(new BusinessUnitResponse(entity.Id, entity.Code, entity.Name, entity.HeadEmployeeId));
        }).WithName("GetBusinessUnit").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateBusinessUnitBody body, OrganizationService service, CancellationToken ct) =>
        {
            BusinessUnit entity = await service.CreateBusinessUnitAsync(body.Code, body.Name, ct);
            return TypedResults.Created(
                $"/api/v1/business-units/{entity.Id}",
                new BusinessUnitResponse(entity.Id, entity.Code, entity.Name, entity.HeadEmployeeId));
        }).WithName("CreateBusinessUnit").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateBusinessUnitBody body, OrganizationService service, CancellationToken ct) =>
        {
            BusinessUnit entity = await service.UpdateBusinessUnitAsync(id, body.Name, body.HeadEmployeeId, ct);
            return TypedResults.Ok(new BusinessUnitResponse(entity.Id, entity.Code, entity.Name, entity.HeadEmployeeId));
        }).WithName("UpdateBusinessUnit").RequirePermission(Permissions.Organization.Manage);
    }

    private static void MapBranchEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/branches").WithTags("Organization");

        group.MapGet("/", async ([FromQuery] Guid? legalEntityId, CoreHrDbContext db, CancellationToken ct) =>
        {
            IQueryable<Branch> query = db.Branches.AsNoTracking();

            if (legalEntityId is Guid id)
            {
                query = query.Where(x => x.LegalEntityId == id);
            }

            List<BranchListItem> items = await query
                .OrderBy(x => x.Name)
                .Select(x => new BranchListItem(x.Id, x.Code, x.Name, x.LegalEntityId, x.TimeZone))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListBranches").RequirePermission(Permissions.Organization.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            Branch? entity = await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetBranch").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateBranchBody body, OrganizationService service, CancellationToken ct) =>
        {
            Branch entity = await service.CreateBranchAsync(
                body.Code, body.Name, body.LegalEntityId, body.CountryCode, body.TimeZone, ct);

            return TypedResults.Created($"/api/v1/branches/{entity.Id}", ToResponse(entity));
        }).WithName("CreateBranch").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateBranchBody body, OrganizationService service, CancellationToken ct) =>
        {
            Branch entity = await service.UpdateBranchAsync(
                id, body.Name, body.TimeZone, body.Address.ToAddress(), body.StateCode, ct);

            return TypedResults.Ok(ToResponse(entity));
        }).WithName("UpdateBranch").RequirePermission(Permissions.Organization.Manage);

        group.MapPost("/{id:guid}/geo-fence", async (Guid id, SetBranchGeoFenceBody body, OrganizationService service, CancellationToken ct) =>
        {
            Branch entity = await service.SetBranchGeoFenceAsync(id, body.Latitude, body.Longitude, body.RadiusMeters, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("SetBranchGeoFence").RequirePermission(Permissions.Organization.Manage);
    }

    private static BranchResponse ToResponse(Branch entity) => new(
        entity.Id,
        entity.Code,
        entity.Name,
        entity.LegalEntityId,
        entity.CountryCode,
        entity.StateCode,
        entity.TimeZone,
        entity.GeoLatitude,
        entity.GeoLongitude,
        entity.GeoFenceMeters,
        AddressBody.From(entity.Address));

    private static void MapLocationEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/locations").WithTags("Organization");

        group.MapGet("/", async ([FromQuery] Guid? branchId, CoreHrDbContext db, CancellationToken ct) =>
        {
            IQueryable<Location> query = db.Locations.AsNoTracking();

            if (branchId is Guid id)
            {
                query = query.Where(x => x.BranchId == id);
            }

            List<LocationResponse> items = await query
                .OrderBy(x => x.Name)
                .Select(x => new LocationResponse(x.Id, x.Code, x.Name, x.BranchId, x.LocationType))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListLocations").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateLocationBody body, OrganizationService service, CancellationToken ct) =>
        {
            Location entity = await service.CreateLocationAsync(body.Code, body.Name, body.BranchId, body.LocationType, ct);

            return TypedResults.Created(
                $"/api/v1/locations/{entity.Id}",
                new LocationResponse(entity.Id, entity.Code, entity.Name, entity.BranchId, entity.LocationType));
        }).WithName("CreateLocation").RequirePermission(Permissions.Organization.Manage);
    }

    private static void MapDepartmentEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/departments").WithTags("Organization");

        group.MapGet("/", async ([FromQuery] Guid? parentDepartmentId, CoreHrDbContext db, CancellationToken ct) =>
        {
            IQueryable<Department> query = db.Departments.AsNoTracking();

            if (parentDepartmentId is Guid id)
            {
                query = query.Where(x => x.ParentDepartmentId == id);
            }

            List<DepartmentListItem> items = await query
                .OrderBy(x => x.Name)
                .Select(x => new DepartmentListItem(x.Id, x.Code, x.Name, x.ParentDepartmentId))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListDepartments").RequirePermission(Permissions.Organization.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            Department? entity = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetDepartment").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateDepartmentBody body, DepartmentService service, CancellationToken ct) =>
        {
            Department entity = await service.CreateAsync(body.Code, body.Name, body.ParentDepartmentId, ct);
            return TypedResults.Created($"/api/v1/departments/{entity.Id}", ToResponse(entity));
        }).WithName("CreateDepartment").RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPut("/{id:guid}", async (Guid id, RenameDepartmentBody body, DepartmentService service, CancellationToken ct) =>
        {
            Department entity = await service.RenameAsync(id, body.Name, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("RenameDepartment").RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPut("/{id:guid}/assignments", async (Guid id, SetDepartmentAssignmentsBody body, DepartmentService service, CancellationToken ct) =>
        {
            Department entity = await service.SetAssignmentsAsync(
                id, body.HeadEmployeeId, body.CostCenterId, body.BusinessUnitId, ct);

            return TypedResults.Ok(ToResponse(entity));
        }).WithName("SetDepartmentAssignments").RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPost("/{id:guid}/move", async (Guid id, MoveDepartmentBody body, DepartmentService service, CancellationToken ct) =>
        {
            Department entity = await service.MoveAsync(id, body.NewParentId, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("MoveDepartment").RequirePermission(Permissions.Organization.DepartmentManage);
    }

    private static DepartmentResponse ToResponse(Department entity) => new(
        entity.Id,
        entity.Code,
        entity.Name,
        entity.ParentDepartmentId,
        entity.HeadEmployeeId,
        entity.CostCenterId,
        entity.BusinessUnitId);

    private static void MapDesignationEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/designations").WithTags("Organization");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<DesignationResponse> items = await db.Designations.AsNoTracking()
                .OrderBy(x => x.Title)
                .Select(x => new DesignationResponse(x.Id, x.Code, x.Title, x.JobFamily, x.JobLevel))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListDesignations").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateDesignationBody body, OrganizationService service, CancellationToken ct) =>
        {
            Designation entity = await service.CreateDesignationAsync(body.Code, body.Title, body.JobFamily, body.JobLevel, ct);

            return TypedResults.Created(
                $"/api/v1/designations/{entity.Id}",
                new DesignationResponse(entity.Id, entity.Code, entity.Title, entity.JobFamily, entity.JobLevel));
        }).WithName("CreateDesignation").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateDesignationBody body, OrganizationService service, CancellationToken ct) =>
        {
            Designation entity = await service.UpdateDesignationAsync(id, body.Title, body.JobFamily, body.JobLevel, ct);

            return TypedResults.Ok(new DesignationResponse(entity.Id, entity.Code, entity.Title, entity.JobFamily, entity.JobLevel));
        }).WithName("UpdateDesignation").RequirePermission(Permissions.Organization.Manage);
    }

    private static void MapGradeEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/grades").WithTags("Organization");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<GradeResponse> items = await db.Grades.AsNoTracking()
                .OrderBy(x => x.Rank)
                .Select(x => new GradeResponse(x.Id, x.Code, x.Name, x.Rank, x.SalaryBandMinimum, x.SalaryBandMaximum, x.Currency))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListGrades").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateGradeBody body, OrganizationService service, CancellationToken ct) =>
        {
            Grade entity = await service.CreateGradeAsync(body.Code, body.Name, body.Rank, ct);
            return TypedResults.Created($"/api/v1/grades/{entity.Id}", ToResponse(entity));
        }).WithName("CreateGrade").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateGradeBody body, OrganizationService service, CancellationToken ct) =>
        {
            Grade entity = await service.UpdateGradeAsync(
                id, body.Name, body.SalaryBandMinimum, body.SalaryBandMaximum, body.Currency, ct);

            return TypedResults.Ok(ToResponse(entity));
        }).WithName("UpdateGrade").RequirePermission(Permissions.Organization.Manage);
    }

    private static GradeResponse ToResponse(Grade entity) => new(
        entity.Id, entity.Code, entity.Name, entity.Rank, entity.SalaryBandMinimum, entity.SalaryBandMaximum, entity.Currency);

    private static void MapCostCenterEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/cost-centers").WithTags("Organization");

        group.MapGet("/", async (CoreHrDbContext db, CancellationToken ct) =>
        {
            List<CostCenterResponse> items = await db.CostCenters.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new CostCenterResponse(x.Id, x.Code, x.Name, x.GlAccountCode, x.OwnerEmployeeId, x.ParentCostCenterId))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("ListCostCenters").RequirePermission(Permissions.Organization.View);

        group.MapGet("/{id:guid}", async (Guid id, CoreHrDbContext db, CancellationToken ct) =>
        {
            CostCenter? entity = await db.CostCenters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        }).WithName("GetCostCenter").RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (CreateCostCenterBody body, CostCenterService service, CancellationToken ct) =>
        {
            CostCenter entity = await service.CreateAsync(body.Code, body.Name, body.GlAccountCode, ct);
            return TypedResults.Created($"/api/v1/cost-centers/{entity.Id}", ToResponse(entity));
        }).WithName("CreateCostCenter").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, RenameCostCenterBody body, CostCenterService service, CancellationToken ct) =>
        {
            CostCenter entity = await service.RenameAsync(id, body.Name, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("RenameCostCenter").RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}/owner", async (Guid id, SetCostCenterOwnerBody body, CostCenterService service, CancellationToken ct) =>
        {
            CostCenter entity = await service.SetOwnerAsync(id, body.OwnerEmployeeId, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("SetCostCenterOwner").RequirePermission(Permissions.Organization.Manage);

        group.MapPost("/{id:guid}/move", async (Guid id, MoveCostCenterBody body, CostCenterService service, CancellationToken ct) =>
        {
            CostCenter entity = await service.MoveAsync(id, body.NewParentId, ct);
            return TypedResults.Ok(ToResponse(entity));
        }).WithName("MoveCostCenter").RequirePermission(Permissions.Organization.Manage);
    }

    private static CostCenterResponse ToResponse(CostCenter entity) => new(
        entity.Id, entity.Code, entity.Name, entity.GlAccountCode, entity.OwnerEmployeeId, entity.ParentCostCenterId);
}
