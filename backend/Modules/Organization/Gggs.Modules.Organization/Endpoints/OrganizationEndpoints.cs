using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Organization.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Endpoints;

public sealed record AddressDto(string? Line1, string? Line2, string? City, string? StateCode, string? CountryCode, string? PostalCode);

public sealed record LegalEntityItem(
    Guid Id, string Code, string Name, string? LegalName, string CountryCode, string CurrencyCode,
    AddressDto? Address, string? Gstin, string? Pan, string? Cin, string? Tan, bool IsActive);

public sealed record SaveLegalEntityRequest(
    string Code, string Name, string? LegalName, string CountryCode, string CurrencyCode,
    AddressDto? Address, string? Gstin, string? Pan, string? Cin, string? Tan);

public sealed record BusinessUnitItem(Guid Id, string Code, string Name, Guid? LegalEntityId, Guid? HeadEmployeeId, bool IsActive);

public sealed record SaveBusinessUnitRequest(string Code, string Name, Guid? LegalEntityId, Guid? HeadEmployeeId);

public sealed record DivisionItem(Guid Id, string Code, string Name, Guid? BusinessUnitId, Guid? HeadEmployeeId, bool IsActive);

public sealed record SaveDivisionRequest(string Code, string Name, Guid? BusinessUnitId, Guid? HeadEmployeeId);

public sealed record CenterItem(Guid Id, string Code, string Name, Guid? OwnerEmployeeId, bool IsActive);

public sealed record SaveCenterRequest(string Code, string Name, Guid? OwnerEmployeeId);

public sealed record LocationItem(Guid Id, string Code, string Name, Guid? LegalEntityId, AddressDto? Address, string? TimeZone, bool IsActive);

public sealed record SaveLocationRequest(string Code, string Name, Guid? LegalEntityId, AddressDto? Address, string? TimeZone);

public sealed record RankedItem(Guid Id, string Code, string Name, int Rank, bool IsActive);

public sealed record SaveRankedRequest(string Code, string Name, int Rank);

public sealed record DepartmentItem(
    Guid Id, string Code, string Name, Guid? ParentDepartmentId, Guid? DivisionId, Guid? HeadEmployeeId,
    Guid? CostCenterId, Guid? ProfitCenterId, Guid? LocationId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record SaveDepartmentRequest(
    string Code, string Name, Guid? ParentDepartmentId, Guid? DivisionId, Guid? HeadEmployeeId,
    Guid? CostCenterId, Guid? ProfitCenterId, Guid? LocationId, DateOnly? EffectiveFrom);

public sealed record CloseRequest(DateOnly EffectiveTo);

public sealed record DepartmentNode(Guid Id, string Code, string Name, Guid? HeadEmployeeId, int Headcount, bool IsActive, IReadOnlyList<DepartmentNode> Children);

public sealed record JobItem(
    Guid Id, string Code, string Name, string? JobFamilyCode, Guid? JobLevelId, Guid? DepartmentId,
    decimal? MinimumExperienceYears, decimal? MaximumExperienceYears, string? Description, IReadOnlyList<string> RequiredSkills, bool IsActive);

public sealed record SaveJobRequest(
    string Code, string Name, string? JobFamilyCode, Guid? JobLevelId, Guid? DepartmentId,
    decimal? MinimumExperienceYears, decimal? MaximumExperienceYears, string? Description, IReadOnlyList<string>? RequiredSkills);

public sealed record PositionItem(
    Guid Id, string Code, string Name, Guid JobId, Guid DepartmentId, Guid? LocationId, Guid? GradeId,
    Guid? ReportsToPositionId, bool IsBudgeted, int Headcount, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record SavePositionRequest(
    string Code, string Name, Guid JobId, Guid DepartmentId, Guid? LocationId, Guid? GradeId,
    Guid? ReportsToPositionId, bool IsBudgeted, int? Headcount, DateOnly? EffectiveFrom);

public sealed record PositionOccupancy(Guid PositionId, int Headcount, int Occupied, int Vacant);

/// <summary>
/// The organization structure (spec §10) under <c>/api/v1/organizations</c>. Every kind gets the
/// standard list/get/activate/deactivate routes from <see cref="OrgEndpointKit"/>; what follows is
/// each kind's own create and update rules.
/// </summary>
public static class OrganizationEndpoints
{
    private const string Base = "/api/v1/organizations";

    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapLegalEntities(endpoints);
        MapBusinessUnits(endpoints);
        MapDivisions(endpoints);
        MapCenters(endpoints);
        MapLocations(endpoints);
        MapRanked(endpoints);
        MapDepartments(endpoints);
        MapJobs(endpoints);
        MapPositions(endpoints);

        return endpoints;
    }

    private static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string path, string tag)
        => endpoints.MapGroup($"{Base}/{path}").WithTags(tag);

    private static PostalAddress? ToAddress(AddressDto? dto)
        => dto is null || (dto.Line1, dto.Line2, dto.City, dto.StateCode, dto.CountryCode, dto.PostalCode) is (null, null, null, null, null, null)
            ? null
            : new PostalAddress(Trim(dto.Line1), Trim(dto.Line2), Trim(dto.City), Upper(dto.StateCode), Upper(dto.CountryCode), Trim(dto.PostalCode));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static async Task EnsureAddressAsync(IMasterDataLookup lookups, AddressDto? address, CancellationToken cancellationToken)
    {
        await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.Country, address?.CountryCode, "country", cancellationToken);
        await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.State, address?.StateCode, "state", cancellationToken);
    }

    // ----- Companies --------------------------------------------------------------------------

    private static void MapLegalEntities(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "legal-entities", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "LegalEntities", db => db.LegalEntities,
            unit => new LegalEntityItem(
                unit.Id, unit.Code, unit.Name, unit.LegalName, unit.CountryCode, unit.CurrencyCode,
                unit.Address == null ? null : new AddressDto(unit.Address.Line1, unit.Address.Line2, unit.Address.City, unit.Address.StateCode, unit.Address.CountryCode, unit.Address.PostalCode),
                unit.Gstin, unit.Pan, unit.Cin, unit.Tan, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.LegalEntityManage);

        group.MapPost("/", async (SaveLegalEntityRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.LegalEntities, body.Code, null, ct);
                await ValidateAsync(body, lookups, ct);

                LegalEntity entity = LegalEntity.Create(body.Code, body.Name, body.CountryCode, body.CurrencyCode);
                entity.UpdateDetails(body.Name, body.LegalName, body.CountryCode, body.CurrencyCode, ToAddress(body.Address));
                entity.UpdateRegistration(body.Gstin, body.Pan, body.Cin, body.Tan);
                db.LegalEntities.Add(entity);
                await unitOfWork.SaveChangesAsync(ct);

                return TypedResults.Created($"{Base}/legal-entities/{entity.Id}", new { entity.Id });
            })
            .WithName("CreateLegalEntity")
            .RequirePermission(Permissions.Organization.LegalEntityManage);

        group.MapPut("/{id:guid}", async (Guid id, SaveLegalEntityRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                LegalEntity entity = await OrgEndpointKit.FindAsync(db.LegalEntities, id, ct);
                await ValidateAsync(body, lookups, ct);

                entity.UpdateDetails(body.Name, body.LegalName, body.CountryCode, body.CurrencyCode, ToAddress(body.Address));
                entity.UpdateRegistration(body.Gstin, body.Pan, body.Cin, body.Tan);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateLegalEntity")
            .RequirePermission(Permissions.Organization.LegalEntityManage);

        static async Task ValidateAsync(SaveLegalEntityRequest body, IMasterDataLookup lookups, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.Country, body.CountryCode, "country", ct);
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.Currency, body.CurrencyCode, "currency", ct);
            await EnsureAddressAsync(lookups, body.Address, ct);
        }
    }

    // ----- Business units, divisions ----------------------------------------------------------

    private static void MapBusinessUnits(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "business-units", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "BusinessUnits", db => db.BusinessUnits,
            unit => new BusinessUnitItem(unit.Id, unit.Code, unit.Name, unit.LegalEntityId, unit.HeadEmployeeId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveBusinessUnitRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.BusinessUnits, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.LegalEntities, body.LegalEntityId, "company", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                BusinessUnit unit = BusinessUnit.Create(body.Code, body.Name, body.LegalEntityId, body.HeadEmployeeId);
                db.BusinessUnits.Add(unit);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/business-units/{unit.Id}", new { unit.Id });
            })
            .WithName("CreateBusinessUnit")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveBusinessUnitRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                BusinessUnit unit = await OrgEndpointKit.FindAsync(db.BusinessUnits, id, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.LegalEntities, body.LegalEntityId, "company", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                unit.Rename(body.Name);
                unit.Assign(body.LegalEntityId, body.HeadEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateBusinessUnit")
            .RequirePermission(Permissions.Organization.Manage);
    }

    private static void MapDivisions(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "divisions", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Divisions", db => db.Divisions,
            unit => new DivisionItem(unit.Id, unit.Code, unit.Name, unit.BusinessUnitId, unit.HeadEmployeeId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveDivisionRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Divisions, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.BusinessUnits, body.BusinessUnitId, "business unit", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                Division division = Division.Create(body.Code, body.Name, body.BusinessUnitId, body.HeadEmployeeId);
                db.Divisions.Add(division);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/divisions/{division.Id}", new { division.Id });
            })
            .WithName("CreateDivision")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveDivisionRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Division division = await OrgEndpointKit.FindAsync(db.Divisions, id, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.BusinessUnits, body.BusinessUnitId, "business unit", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                division.Rename(body.Name);
                division.Assign(body.BusinessUnitId, body.HeadEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateDivision")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Cost and profit centers --------------------------------------------------------------

    private static void MapCenters(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder costs = Group(endpoints, "cost-centers", "Organization");

        OrgEndpointKit.MapStandardRoutes(costs, "CostCenters", db => db.CostCenters,
            unit => new CenterItem(unit.Id, unit.Code, unit.Name, unit.OwnerEmployeeId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        costs.MapPost("/", async (SaveCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.CostCenters, body.Code, null, ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.OwnerEmployeeId, "owner", ct);

                CostCenter center = CostCenter.Create(body.Code, body.Name, body.OwnerEmployeeId);
                db.CostCenters.Add(center);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/cost-centers/{center.Id}", new { center.Id });
            })
            .WithName("CreateCostCenter")
            .RequirePermission(Permissions.Organization.Manage);

        costs.MapPut("/{id:guid}", async (Guid id, SaveCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                CostCenter center = await OrgEndpointKit.FindAsync(db.CostCenters, id, ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.OwnerEmployeeId, "owner", ct);
                center.Rename(body.Name);
                center.SetOwner(body.OwnerEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateCostCenter")
            .RequirePermission(Permissions.Organization.Manage);

        RouteGroupBuilder profits = Group(endpoints, "profit-centers", "Organization");

        OrgEndpointKit.MapStandardRoutes(profits, "ProfitCenters", db => db.ProfitCenters,
            unit => new CenterItem(unit.Id, unit.Code, unit.Name, unit.OwnerEmployeeId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        profits.MapPost("/", async (SaveCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.ProfitCenters, body.Code, null, ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.OwnerEmployeeId, "owner", ct);

                ProfitCenter center = ProfitCenter.Create(body.Code, body.Name, body.OwnerEmployeeId);
                db.ProfitCenters.Add(center);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/profit-centers/{center.Id}", new { center.Id });
            })
            .WithName("CreateProfitCenter")
            .RequirePermission(Permissions.Organization.Manage);

        profits.MapPut("/{id:guid}", async (Guid id, SaveCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                ProfitCenter center = await OrgEndpointKit.FindAsync(db.ProfitCenters, id, ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.OwnerEmployeeId, "owner", ct);
                center.Rename(body.Name);
                center.SetOwner(body.OwnerEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateProfitCenter")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Locations --------------------------------------------------------------------------

    private static void MapLocations(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "locations", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Locations", db => db.Locations,
            unit => new LocationItem(
                unit.Id, unit.Code, unit.Name, unit.LegalEntityId,
                unit.Address == null ? null : new AddressDto(unit.Address.Line1, unit.Address.Line2, unit.Address.City, unit.Address.StateCode, unit.Address.CountryCode, unit.Address.PostalCode),
                unit.TimeZone, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveLocationRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Locations, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.LegalEntities, body.LegalEntityId, "company", ct);
                await EnsureAddressAsync(lookups, body.Address, ct);

                Location location = Location.Create(body.Code, body.Name, body.LegalEntityId, ToAddress(body.Address), body.TimeZone);
                db.Locations.Add(location);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/locations/{location.Id}", new { location.Id });
            })
            .WithName("CreateLocation")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveLocationRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Location location = await OrgEndpointKit.FindAsync(db.Locations, id, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.LegalEntities, body.LegalEntityId, "company", ct);
                await EnsureAddressAsync(lookups, body.Address, ct);

                location.Rename(body.Name);
                location.Update(body.LegalEntityId, ToAddress(body.Address), body.TimeZone);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateLocation")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Grades and job levels ------------------------------------------------------------

    private static void MapRanked(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder grades = Group(endpoints, "grades", "Organization");

        OrgEndpointKit.MapStandardRoutes(grades, "Grades", db => db.Grades,
            unit => new RankedItem(unit.Id, unit.Code, unit.Name, unit.Rank, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        grades.MapPost("/", async (SaveRankedRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Grades, body.Code, null, ct);
                Grade grade = Grade.Create(body.Code, body.Name, body.Rank);
                db.Grades.Add(grade);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/grades/{grade.Id}", new { grade.Id });
            })
            .WithName("CreateGrade")
            .RequirePermission(Permissions.Organization.Manage);

        grades.MapPut("/{id:guid}", async (Guid id, SaveRankedRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Grade grade = await OrgEndpointKit.FindAsync(db.Grades, id, ct);
                grade.Rename(body.Name);
                grade.SetRank(body.Rank);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateGrade")
            .RequirePermission(Permissions.Organization.Manage);

        RouteGroupBuilder levels = Group(endpoints, "job-levels", "Organization");

        OrgEndpointKit.MapStandardRoutes(levels, "JobLevels", db => db.JobLevels,
            unit => new RankedItem(unit.Id, unit.Code, unit.Name, unit.Rank, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        levels.MapPost("/", async (SaveRankedRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.JobLevels, body.Code, null, ct);
                JobLevel level = JobLevel.Create(body.Code, body.Name, body.Rank);
                db.JobLevels.Add(level);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/job-levels/{level.Id}", new { level.Id });
            })
            .WithName("CreateJobLevel")
            .RequirePermission(Permissions.Organization.Manage);

        levels.MapPut("/{id:guid}", async (Guid id, SaveRankedRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                JobLevel level = await OrgEndpointKit.FindAsync(db.JobLevels, id, ct);
                level.Rename(body.Name);
                level.SetRank(body.Rank);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateJobLevel")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Departments ------------------------------------------------------------------------

    private static void MapDepartments(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "departments", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Departments", db => db.Departments,
            unit => new DepartmentItem(
                unit.Id, unit.Code, unit.Name, unit.ParentDepartmentId, unit.DivisionId, unit.HeadEmployeeId,
                unit.CostCenterId, unit.ProfitCenterId, unit.LocationId, unit.EffectiveFrom, unit.EffectiveTo, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.DepartmentManage);

        group.MapGet("/tree", async (AppDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                List<Department> departments = await db.Departments.AsNoTracking().OrderBy(unit => unit.Name).ToListAsync(ct);

                IReadOnlyDictionary<Guid, int> headcounts = await employees.CountByDepartmentAsync(departments.Select(unit => unit.Id), ct);

                ILookup<Guid?, Department> byParent = departments.ToLookup(unit => unit.ParentDepartmentId);

                IReadOnlyList<DepartmentNode> Build(Guid? parentId) =>
                [
                    .. byParent[parentId].Select(unit => new DepartmentNode(
                        unit.Id, unit.Code, unit.Name, unit.HeadEmployeeId, headcounts.GetValueOrDefault(unit.Id), unit.IsActive, Build(unit.Id))),
                ];

                return TypedResults.Ok(Build(null));
            })
            .WithName("GetDepartmentTree")
            .RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, TimeProvider time, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Departments, body.Code, null, ct);

                Department department = Department.Create(
                    body.Code, body.Name, body.EffectiveFrom ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
                await PlaceAsync(department, body, db, employees, ct);

                db.Departments.Add(department);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/departments/{department.Id}", new { department.Id });
            })
            .WithName("CreateDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPut("/{id:guid}", async (Guid id, SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Department department = await OrgEndpointKit.FindAsync(db.Departments, id, ct);
                department.Rename(body.Name);
                await PlaceAsync(department, body, db, employees, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPost("/{id:guid}/close", async (Guid id, CloseRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Department department = await OrgEndpointKit.FindAsync(db.Departments, id, ct);

                if (await db.Departments.AnyAsync(unit => unit.ParentDepartmentId == id && unit.IsActive, ct))
                {
                    throw new DomainRuleException("DEPARTMENT_HAS_CHILDREN", "Close or move the departments inside this one first.");
                }

                if ((await employees.CountByDepartmentAsync([id], ct)).GetValueOrDefault(id) > 0)
                {
                    throw new DomainRuleException("DEPARTMENT_HAS_EMPLOYEES", "Transfer this department's employees before closing it.");
                }

                department.Close(body.EffectiveTo);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("CloseDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        static async Task PlaceAsync(Department department, SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureActiveAsync(db.Departments, body.ParentDepartmentId, "parent department", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Divisions, body.DivisionId, "division", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.CostCenters, body.CostCenterId, "cost center", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.ProfitCenters, body.ProfitCenterId, "profit center", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Locations, body.LocationId, "location", ct);
            await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

            if (body.ParentDepartmentId is { } parentId)
            {
                await EnsureNoCycleAsync(db, department.Id, parentId, ct);
            }

            department.Place(body.ParentDepartmentId, body.DivisionId, body.HeadEmployeeId, body.CostCenterId, body.ProfitCenterId, body.LocationId);
        }

        // Walks up from the proposed parent; meeting the department itself means the move would
        // make it its own ancestor.
        static async Task EnsureNoCycleAsync(AppDbContext db, Guid departmentId, Guid parentId, CancellationToken ct)
        {
            Dictionary<Guid, Guid?> parents = await db.Departments.AsNoTracking()
                .ToDictionaryAsync(unit => unit.Id, unit => unit.ParentDepartmentId, ct);

            Guid? current = parentId;
            var seen = new HashSet<Guid>();

            while (current is { } id && seen.Add(id))
            {
                if (id == departmentId)
                {
                    throw new DomainRuleException("DEPARTMENT_CYCLE", "A department cannot be placed inside one of its own sub-departments.");
                }

                current = parents.GetValueOrDefault(id);
            }
        }
    }

    // ----- Jobs ---------------------------------------------------------------------------------

    private static void MapJobs(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "jobs", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Jobs", db => db.Jobs,
            unit => new JobItem(
                unit.Id, unit.Code, unit.Name, unit.JobFamilyCode, unit.JobLevelId, unit.DepartmentId,
                unit.MinimumExperienceYears, unit.MaximumExperienceYears, unit.Description, unit.RequiredSkills, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveJobRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Jobs, body.Code, null, ct);
                Job job = Job.Create(body.Code, body.Name);
                await DefineAsync(job, body, db, lookups, ct);

                db.Jobs.Add(job);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/jobs/{job.Id}", new { job.Id });
            })
            .WithName("CreateJob")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveJobRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Job job = await OrgEndpointKit.FindAsync(db.Jobs, id, ct);
                job.Rename(body.Name);
                await DefineAsync(job, body, db, lookups, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateJob")
            .RequirePermission(Permissions.Organization.Manage);

        static async Task DefineAsync(Job job, SaveJobRequest body, AppDbContext db, IMasterDataLookup lookups, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.JobFamily, body.JobFamilyCode, "job family", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.JobLevels, body.JobLevelId, "job level", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Departments, body.DepartmentId, "department", ct);

            job.Define(body.JobFamilyCode, body.JobLevelId, body.DepartmentId, body.MinimumExperienceYears, body.MaximumExperienceYears, body.Description, body.RequiredSkills);
        }
    }

    // ----- Positions ----------------------------------------------------------------------------

    private static void MapPositions(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "positions", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Positions", db => db.Positions,
            unit => new PositionItem(
                unit.Id, unit.Code, unit.Name, unit.JobId, unit.DepartmentId, unit.LocationId, unit.GradeId,
                unit.ReportsToPositionId, unit.IsBudgeted, unit.Headcount, unit.EffectiveFrom, unit.EffectiveTo, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapGet("/occupancy", async (Guid[]? ids, AppDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                IQueryable<Position> positions = db.Positions.AsNoTracking().Where(position => position.IsActive);

                if (ids is { Length: > 0 })
                {
                    positions = positions.Where(position => ids.Contains(position.Id));
                }

                var seats = await positions.Select(position => new { position.Id, position.Headcount }).ToListAsync(ct);
                IReadOnlyDictionary<Guid, int> occupied = await employees.CountByPositionAsync(seats.Select(seat => seat.Id), ct);

                IReadOnlyList<PositionOccupancy> result =
                [
                    .. seats.Select(seat =>
                    {
                        int taken = occupied.GetValueOrDefault(seat.Id);
                        return new PositionOccupancy(seat.Id, seat.Headcount, taken, Math.Max(0, seat.Headcount - taken));
                    }),
                ];

                return TypedResults.Ok(result);
            })
            .WithName("GetPositionOccupancy")
            .RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (SavePositionRequest body, AppDbContext db, TimeProvider time, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Positions, body.Code, null, ct);
                await ValidateAsync(body, null, db, ct);

                Position position = Position.Create(
                    body.Code, body.Name, body.JobId, body.DepartmentId, body.EffectiveFrom ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
                position.Configure(body.JobId, body.DepartmentId, body.LocationId, body.GradeId, body.ReportsToPositionId, body.IsBudgeted, body.Headcount ?? 1);

                db.Positions.Add(position);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/positions/{position.Id}", new { position.Id });
            })
            .WithName("CreatePosition")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SavePositionRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Position position = await OrgEndpointKit.FindAsync(db.Positions, id, ct);
                await ValidateAsync(body, id, db, ct);

                int headcount = body.Headcount ?? position.Headcount;
                IReadOnlyDictionary<Guid, int> occupied = await employees.CountByPositionAsync([id], ct);
                if (occupied.GetValueOrDefault(id) > headcount)
                {
                    throw new DomainRuleException("POSITION_HEADCOUNT_BELOW_OCCUPANCY", "More people hold this position than the new headcount allows.");
                }

                position.Rename(body.Name);
                position.Configure(body.JobId, body.DepartmentId, body.LocationId, body.GradeId, body.ReportsToPositionId, body.IsBudgeted, headcount);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdatePosition")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPost("/{id:guid}/close", async (Guid id, CloseRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Position position = await OrgEndpointKit.FindAsync(db.Positions, id, ct);

                IReadOnlyDictionary<Guid, int> occupied = await employees.CountByPositionAsync([id], ct);
                if (occupied.GetValueOrDefault(id) > 0)
                {
                    throw new DomainRuleException("POSITION_OCCUPIED", "Move the people holding this position before closing it.");
                }

                position.Close(body.EffectiveTo);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("ClosePosition")
            .RequirePermission(Permissions.Organization.Manage);

        static async Task ValidateAsync(SavePositionRequest body, Guid? positionId, AppDbContext db, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureActiveAsync(db.Jobs, body.JobId, "job", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Departments, body.DepartmentId, "department", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Locations, body.LocationId, "location", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Grades, body.GradeId, "grade", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Positions, body.ReportsToPositionId, "reports-to position", ct);

            if (positionId is { } id && body.ReportsToPositionId is { } managerPositionId)
            {
                // Same walk as departments: a position cannot report up into its own reports.
                Dictionary<Guid, Guid?> reportsTo = await db.Positions.AsNoTracking()
                    .ToDictionaryAsync(position => position.Id, position => position.ReportsToPositionId, ct);

                Guid? current = managerPositionId;
                var seen = new HashSet<Guid>();
                while (current is { } step && seen.Add(step))
                {
                    if (step == id)
                    {
                        throw new DomainRuleException("POSITION_CYCLE", "A position cannot report to one of its own reports.");
                    }

                    current = reportsTo.GetValueOrDefault(step);
                }
            }
        }
    }
}
