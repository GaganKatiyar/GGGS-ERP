using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Application;
using Gggs.Modules.Organization.Application.StructureImport;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Organization.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Endpoints;

public sealed record AddressDto(string? Line1, string? Line2, string? City, string? StateCode, string? CountryCode, string? PostalCode);

public sealed record CompanyItem(
    Guid Id, string Code, string Name, string? LegalName, string CountryCode, string CurrencyCode,
    AddressDto? Address, string? Gstin, string? Pan, string? Cin, string? Tan, bool IsActive);

public sealed record SaveCompanyRequest(
    string Code, string Name, string? LegalName, string CountryCode, string CurrencyCode,
    AddressDto? Address, string? Gstin, string? Pan, string? Cin, string? Tan);

public sealed record BusinessUnitItem(Guid Id, string Code, string Name, Guid? CompanyId, Guid? HeadEmployeeId, bool IsActive);

public sealed record SaveBusinessUnitRequest(string Code, string Name, Guid? CompanyId, Guid? HeadEmployeeId);

/// <summary>A cost center: the company name an office works under.</summary>
public sealed record CostCenterItem(
    Guid Id, string Code, string Name, Guid? CompanyId, bool IsDefault, Guid? OwnerEmployeeId,
    string? Gstin, string? Pan, AddressDto? Address, bool IsActive);

public sealed record SaveCostCenterRequest(
    string Code, string Name, Guid? CompanyId, Guid? OwnerEmployeeId, string? Gstin, string? Pan, AddressDto? Address, bool? IsDefault);

public sealed record LocationItem(Guid Id, string Code, string Name, Guid? CompanyId, Guid? CostCenterId, AddressDto? Address, string? TimeZone, bool IsActive);

public sealed record SaveLocationRequest(string Code, string Name, Guid? CompanyId, Guid? CostCenterId, AddressDto? Address, string? TimeZone);

public sealed record LevelItem(Guid Id, string Code, string Name, int Rank, bool IsActive);

public sealed record SaveLevelRequest(string Code, string Name, int Rank);

/// <summary>A title: a role at a level when <see cref="RoleId"/> is set, otherwise free-standing.</summary>
public sealed record DesignationItem(Guid Id, string Code, string Name, Guid? LevelId, Guid? RoleId, bool IsActive);

public sealed record SaveDesignationRequest(string Code, string Name, Guid? LevelId, Guid? RoleId = null);

/// <summary>A department, or a function when <see cref="ParentDepartmentId"/> names its department.</summary>
public sealed record DepartmentItem(
    Guid Id, string Code, string Name, Guid? ParentDepartmentId, Guid? BusinessUnitId, Guid? HeadEmployeeId,
    Guid? CostCenterId, Guid? LocationId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

/// <summary>Leave <see cref="ParentDepartmentId"/> empty for a department; set it to make a function of that department.</summary>
public sealed record SaveDepartmentRequest(
    string Code, string Name, Guid? ParentDepartmentId, Guid? BusinessUnitId, Guid? HeadEmployeeId,
    Guid? CostCenterId, Guid? LocationId, DateOnly? EffectiveFrom);

public sealed record CloseRequest(DateOnly EffectiveTo);

/// <summary>A department in the tree with its functions as <see cref="Children"/>.</summary>
public sealed record DepartmentNode(
    Guid Id, string Code, string Name, Guid? BusinessUnitId, Guid? HeadEmployeeId, Guid? LocationId, Guid? CostCenterId, int Headcount, bool IsActive,
    IReadOnlyList<DepartmentNode> Children);

public sealed record OrgRoleItem(
    Guid Id, string Code, string Name, string? JobFamilyCode, Guid? LevelId, Guid? DepartmentId,
    decimal? MinimumExperienceYears, decimal? MaximumExperienceYears, string? Description, IReadOnlyList<string> RequiredSkills, bool IsActive);

public sealed record SaveOrgRoleRequest(
    string Code, string Name, string? JobFamilyCode, Guid? LevelId, Guid? DepartmentId,
    decimal? MinimumExperienceYears, decimal? MaximumExperienceYears, string? Description, IReadOnlyList<string>? RequiredSkills);

/// <summary>An org-structure file's text: CSV, or a Markdown table.</summary>
public sealed record StructureImportRequest(string? Content);

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

        MapCompanies(endpoints);
        MapBusinessUnits(endpoints);
        MapCostCenters(endpoints);
        MapLocations(endpoints);
        MapLevels(endpoints);
        MapDesignations(endpoints);
        MapDepartments(endpoints);
        MapJobs(endpoints);
        MapStructureImport(endpoints);

        return endpoints;
    }

    // ----- Structure import -------------------------------------------------------------------

    /// <summary>
    /// Departments, functions and roles from a file (CSV or a Markdown table): preview shows what
    /// would change, apply re-plans the same file and makes the changes in one save.
    /// </summary>
    private static void MapStructureImport(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "structure-imports", "Organization");

        group.MapPost("/preview", async (StructureImportRequest body, AppDbContext db, CancellationToken ct) =>
            {
                StructurePlan plan = await PlanAsync(body, db, ct);
                return TypedResults.Ok(plan);
            })
            .WithName("PreviewStructureImport")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPost("/apply", async (StructureImportRequest body, AppDbContext db, TimeProvider time, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                StructurePlan plan = await PlanAsync(body, db, ct);
                StructureImportResult result = await StructureImporter.ApplyAsync(db, plan, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Ok(result);
            })
            .WithName("ApplyStructureImport")
            .RequirePermission(Permissions.Organization.Manage);

        static async Task<StructurePlan> PlanAsync(StructureImportRequest body, AppDbContext db, CancellationToken ct)
        {
            StructureParseResult parsed = StructureImportParser.Parse(body.Content ?? string.Empty);
            StructurePlan plan = StructureImportPlanner.Plan(parsed.Rows, await StructureImporter.SnapshotAsync(db, ct));
            return parsed.Errors.Count == 0 ? plan : plan with { Errors = [.. parsed.Errors, .. plan.Errors] };
        }
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

    private static void MapCompanies(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "companies", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Companies", db => db.Companies,
            unit => new CompanyItem(
                unit.Id, unit.Code, unit.Name, unit.LegalName, unit.CountryCode, unit.CurrencyCode,
                unit.Address == null ? null : new AddressDto(unit.Address.Line1, unit.Address.Line2, unit.Address.City, unit.Address.StateCode, unit.Address.CountryCode, unit.Address.PostalCode),
                unit.Gstin, unit.Pan, unit.Cin, unit.Tan, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.CompanyManage);

        group.MapPost("/", async (SaveCompanyRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Companies, body.Code, null, ct);
                await ValidateAsync(body, lookups, ct);

                Company entity = Company.Create(body.Code, body.Name, body.CountryCode, body.CurrencyCode);
                entity.UpdateDetails(body.Name, body.LegalName, body.CountryCode, body.CurrencyCode, ToAddress(body.Address));
                entity.UpdateRegistration(body.Gstin, body.Pan, body.Cin, body.Tan);
                db.Companies.Add(entity);

                // Every company starts with a default cost center carrying its own name.
                if (!await db.CostCenters.AnyAsync(unit => unit.Code == entity.Code, ct))
                {
                    db.CostCenters.Add(CostCenter.DefaultFor(entity));
                }

                await unitOfWork.SaveChangesAsync(ct);

                return TypedResults.Created($"{Base}/companies/{entity.Id}", new { entity.Id });
            })
            .WithName("CreateCompany")
            .RequirePermission(Permissions.Organization.CompanyManage);

        group.MapPut("/{id:guid}", async (Guid id, SaveCompanyRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Company entity = await OrgEndpointKit.FindAsync(db.Companies, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Companies, entity, body.Code, ct);
                await ValidateAsync(body, lookups, ct);

                entity.UpdateDetails(body.Name, body.LegalName, body.CountryCode, body.CurrencyCode, ToAddress(body.Address));
                entity.UpdateRegistration(body.Gstin, body.Pan, body.Cin, body.Tan);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateCompany")
            .RequirePermission(Permissions.Organization.CompanyManage);

        static async Task ValidateAsync(SaveCompanyRequest body, IMasterDataLookup lookups, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.Country, body.CountryCode, "country", ct);
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.Currency, body.CurrencyCode, "currency", ct);
            await EnsureAddressAsync(lookups, body.Address, ct);
        }
    }

    // ----- Business units -----------------------------------------------------------------------

    private static void MapBusinessUnits(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "business-units", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "BusinessUnits", db => db.BusinessUnits,
            unit => new BusinessUnitItem(unit.Id, unit.Code, unit.Name, unit.CompanyId, unit.HeadEmployeeId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveBusinessUnitRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.BusinessUnits, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Companies, body.CompanyId, "company", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                BusinessUnit unit = BusinessUnit.Create(body.Code, body.Name, body.CompanyId, body.HeadEmployeeId);
                db.BusinessUnits.Add(unit);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/business-units/{unit.Id}", new { unit.Id });
            })
            .WithName("CreateBusinessUnit")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveBusinessUnitRequest body, AppDbContext db, IEmployeeDirectory employees, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                BusinessUnit unit = await OrgEndpointKit.FindAsync(db.BusinessUnits, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.BusinessUnits, unit, body.Code, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Companies, body.CompanyId, "company", ct);
                await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

                unit.Rename(body.Name);
                unit.Assign(body.CompanyId, body.HeadEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateBusinessUnit")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Cost centers -------------------------------------------------------------------------

    private static void MapCostCenters(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder costs = Group(endpoints, "cost-centers", "Organization");

        OrgEndpointKit.MapStandardRoutes(costs, "CostCenters", db => db.CostCenters,
            unit => new CostCenterItem(
                unit.Id, unit.Code, unit.Name, unit.CompanyId, unit.IsDefault, unit.OwnerEmployeeId, unit.Gstin, unit.Pan,
                unit.Address == null ? null : new AddressDto(unit.Address.Line1, unit.Address.Line2, unit.Address.City, unit.Address.StateCode, unit.Address.CountryCode, unit.Address.PostalCode),
                unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage,
            beforeDeactivate: unit =>
            {
                if (unit.IsDefault)
                {
                    throw new DomainRuleException("COST_CENTER_DEFAULT", "This is the company's default cost center. Make another one the default first.");
                }
            });

        costs.MapPost("/", async (SaveCostCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.CostCenters, body.Code, null, ct);
                await ValidateAsync(body, db, employees, lookups, ct);

                CostCenter center = CostCenter.Create(body.Code, body.Name, body.CompanyId, body.OwnerEmployeeId);
                center.UpdateRegistration(body.Gstin, body.Pan, ToAddress(body.Address));
                db.CostCenters.Add(center);
                await SetDefaultAsync(center, body.IsDefault, db, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/cost-centers/{center.Id}", new { center.Id });
            })
            .WithName("CreateCostCenter")
            .RequirePermission(Permissions.Organization.Manage);

        costs.MapPut("/{id:guid}", async (Guid id, SaveCostCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                CostCenter center = await OrgEndpointKit.FindAsync(db.CostCenters, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.CostCenters, center, body.Code, ct);
                await ValidateAsync(body, db, employees, lookups, ct);

                if (center.IsDefault && body.IsDefault == false)
                {
                    throw new DomainRuleException("COST_CENTER_DEFAULT", "A company always has a default cost center. Make another one the default instead.");
                }

                center.Rename(body.Name);
                center.Update(body.CompanyId, body.OwnerEmployeeId);
                center.UpdateRegistration(body.Gstin, body.Pan, ToAddress(body.Address));
                await SetDefaultAsync(center, body.IsDefault, db, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateCostCenter")
            .RequirePermission(Permissions.Organization.Manage);

        static async Task ValidateAsync(SaveCostCenterRequest body, AppDbContext db, IEmployeeDirectory employees, IMasterDataLookup lookups, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureActiveAsync(db.Companies, body.CompanyId, "company", ct);
            await OrgEndpointKit.EnsureEmployeeAsync(employees, body.OwnerEmployeeId, "owner", ct);
            await EnsureAddressAsync(lookups, body.Address, ct);
        }

        // One default per company: making this one the default hands the flag over.
        static async Task SetDefaultAsync(CostCenter center, bool? makeDefault, AppDbContext db, CancellationToken ct)
        {
            if (makeDefault != true || center.IsDefault)
            {
                return;
            }

            if (center.CompanyId is null)
            {
                throw new DomainRuleException("COST_CENTER_DEFAULT_NEEDS_COMPANY", "Choose the company this cost center is the default for.");
            }

            List<CostCenter> others = await db.CostCenters
                .Where(unit => unit.IsDefault && unit.CompanyId == center.CompanyId && unit.Id != center.Id)
                .ToListAsync(ct);
            others.ForEach(unit => unit.ClearDefault());
            center.MakeDefault();
            center.Activate();
        }
    }

    // ----- Locations --------------------------------------------------------------------------

    private static void MapLocations(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "locations", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Locations", db => db.Locations,
            unit => new LocationItem(
                unit.Id, unit.Code, unit.Name, unit.CompanyId, unit.CostCenterId,
                unit.Address == null ? null : new AddressDto(unit.Address.Line1, unit.Address.Line2, unit.Address.City, unit.Address.StateCode, unit.Address.CountryCode, unit.Address.PostalCode),
                unit.TimeZone, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveLocationRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Locations, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Companies, body.CompanyId, "company", ct);
                await OrgEndpointKit.EnsureActiveAsync(db.CostCenters, body.CostCenterId, "cost center", ct);
                await EnsureAddressAsync(lookups, body.Address, ct);

                Location location = Location.Create(body.Code, body.Name, body.CompanyId, ToAddress(body.Address), body.TimeZone, body.CostCenterId);
                db.Locations.Add(location);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/locations/{location.Id}", new { location.Id });
            })
            .WithName("CreateLocation")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveLocationRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Location location = await OrgEndpointKit.FindAsync(db.Locations, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Locations, location, body.Code, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Companies, body.CompanyId, "company", ct);
                if (body.CostCenterId != location.CostCenterId)
                {
                    await OrgEndpointKit.EnsureActiveAsync(db.CostCenters, body.CostCenterId, "cost center", ct);
                }

                await EnsureAddressAsync(lookups, body.Address, ct);

                location.Rename(body.Name);
                location.Update(body.CompanyId, ToAddress(body.Address), body.TimeZone, body.CostCenterId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateLocation")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Levels -----------------------------------------------------------------------------

    private static void MapLevels(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder levels = Group(endpoints, "levels", "Organization");

        // Every role has a designation per active level, so adding, retiring or recoding a level
        // brings every role's designations along.
        OrgEndpointKit.MapStandardRoutes(levels, "Levels", db => db.Levels,
            unit => new LevelItem(unit.Id, unit.Code, unit.Name, unit.Rank, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage,
            activeChanged: async (db, _, ct) => await RoleDesignations.SyncAllAsync(db, ct));

        levels.MapPost("/", async (SaveLevelRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Levels, body.Code, null, ct);
                Level level = Level.Create(body.Code, body.Name, body.Rank);
                db.Levels.Add(level);
                await RoleDesignations.SyncAllAsync(db, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/levels/{level.Id}", new { level.Id });
            })
            .WithName("CreateLevel")
            .RequirePermission(Permissions.Organization.Manage);

        levels.MapPut("/{id:guid}", async (Guid id, SaveLevelRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Level level = await OrgEndpointKit.FindAsync(db.Levels, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Levels, level, body.Code, ct);
                level.Rename(body.Name);
                level.SetRank(body.Rank);
                await RoleDesignations.SyncAllAsync(db, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateLevel")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Designations -----------------------------------------------------------------------

    private static void MapDesignations(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "designations", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Designations", db => db.Designations,
            unit => new DesignationItem(unit.Id, unit.Code, unit.Name, unit.LevelId, unit.RoleId, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage);

        group.MapPost("/", async (SaveDesignationRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Designations, body.Code, null, ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Levels, body.LevelId, "level", ct);
                await OrgEndpointKit.EnsureActiveAsync(db.Roles, body.RoleId, "role", ct);
                Designation designation = Designation.Create(body.Code, body.Name, body.LevelId, body.RoleId);
                db.Designations.Add(designation);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/designations/{designation.Id}", new { designation.Id });
            })
            .WithName("CreateDesignation")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveDesignationRequest body, AppDbContext db, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Designation designation = await OrgEndpointKit.FindAsync(db.Designations, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Designations, designation, body.Code, ct);
                if (body.LevelId != designation.LevelId)
                {
                    await OrgEndpointKit.EnsureActiveAsync(db.Levels, body.LevelId, "level", ct);
                }

                if (body.RoleId != designation.RoleId)
                {
                    await OrgEndpointKit.EnsureActiveAsync(db.Roles, body.RoleId, "role", ct);
                }

                designation.Rename(body.Name);
                designation.SetLevel(body.LevelId);
                designation.SetRole(body.RoleId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateDesignation")
            .RequirePermission(Permissions.Organization.Manage);
    }

    // ----- Departments ------------------------------------------------------------------------

    private static void MapDepartments(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "departments", "Organization");

        OrgEndpointKit.MapStandardRoutes(group, "Departments", db => db.Departments,
            unit => new DepartmentItem(
                unit.Id, unit.Code, unit.Name, unit.ParentDepartmentId, unit.BusinessUnitId, unit.HeadEmployeeId,
                unit.CostCenterId, unit.LocationId, unit.EffectiveFrom, unit.EffectiveTo, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.DepartmentManage);

        group.MapGet("/tree", async (AppDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                List<Department> departments = await db.Departments.AsNoTracking().OrderBy(unit => unit.Name).ToListAsync(ct);

                IReadOnlyDictionary<Guid, int> headcounts = await employees.CountByDepartmentAsync(departments.Select(unit => unit.Id), ct);

                ILookup<Guid?, Department> byParent = departments.ToLookup(unit => unit.ParentDepartmentId);

                IReadOnlyList<DepartmentNode> Build(Guid? parentId) =>
                [
                    .. byParent[parentId].Select(unit => new DepartmentNode(
                        unit.Id, unit.Code, unit.Name, unit.BusinessUnitId, unit.HeadEmployeeId, unit.LocationId, unit.CostCenterId,
                        headcounts.GetValueOrDefault(unit.Id), unit.IsActive, Build(unit.Id))),
                ];

                return TypedResults.Ok(Build(null));
            })
            .WithName("GetDepartmentTree")
            .RequirePermission(Permissions.Organization.View);

        group.MapPost("/", async (SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, IIntegrationEventPublisher publisher, TimeProvider time, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Departments, body.Code, null, ct);

                Department department = Department.Create(
                    body.Code, body.Name, body.EffectiveFrom ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
                await PlaceAsync(department, body, db, employees, ct);

                db.Departments.Add(department);
                PublishHeadChange(publisher, department.Id, null, department.HeadEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/departments/{department.Id}", new { department.Id });
            })
            .WithName("CreateDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPut("/{id:guid}", async (Guid id, SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, IIntegrationEventPublisher publisher, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Department department = await OrgEndpointKit.FindAsync(db.Departments, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Departments, department, body.Code, ct);
                Guid? previousHead = department.HeadEmployeeId;
                department.Rename(body.Name);
                await PlaceAsync(department, body, db, employees, ct);
                PublishHeadChange(publisher, department.Id, previousHead, department.HeadEmployeeId);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        group.MapPost("/{id:guid}/close", async (Guid id, CloseRequest body, AppDbContext db, IEmployeeDirectory employees, IIntegrationEventPublisher publisher, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Department department = await OrgEndpointKit.FindAsync(db.Departments, id, ct);

                if (await db.Departments.AnyAsync(unit => unit.ParentDepartmentId == id && unit.IsActive, ct))
                {
                    throw new DomainRuleException("DEPARTMENT_HAS_CHILDREN", "Close or move this department's functions first.");
                }

                if ((await employees.CountByDepartmentAsync([id], ct)).GetValueOrDefault(id) > 0)
                {
                    throw new DomainRuleException("DEPARTMENT_HAS_EMPLOYEES", "Transfer this department's employees before closing it.");
                }

                department.Close(body.EffectiveTo);
                // A closed department has nobody heading it any more.
                PublishHeadChange(publisher, department.Id, department.HeadEmployeeId, null);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("CloseDepartment")
            .RequirePermission(Permissions.Organization.DepartmentManage);

        // Published with the change, in the same transaction, so a head is never moved without it.
        static void PublishHeadChange(IIntegrationEventPublisher publisher, Guid departmentId, Guid? previous, Guid? current)
        {
            if (previous != current)
            {
                publisher.Publish(new DepartmentHeadChangedIntegrationEvent(departmentId, previous, current));
            }
        }

        static async Task PlaceAsync(Department department, SaveDepartmentRequest body, AppDbContext db, IEmployeeDirectory employees, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureActiveAsync(db.Departments, body.ParentDepartmentId, "department", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.BusinessUnits, body.BusinessUnitId, "business unit", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.CostCenters, body.CostCenterId, "cost center", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Locations, body.LocationId, "location", ct);
            await OrgEndpointKit.EnsureEmployeeAsync(employees, body.HeadEmployeeId, "head", ct);

            Guid? businessUnitId = body.BusinessUnitId;

            if (body.ParentDepartmentId is { } parentId)
            {
                await EnsureNoCycleAsync(db, department.Id, parentId, ct);

                // Two levels only: a function belongs to a department, never to another function,
                // and a department that already has functions cannot become one.
                Department parent = await db.Departments.AsNoTracking().FirstAsync(unit => unit.Id == parentId, ct);
                if (parent.IsFunction)
                {
                    throw new DomainRuleException("FUNCTION_IN_FUNCTION", $"{parent.Name} is a function. Choose the department it belongs to instead.");
                }

                if (await db.Departments.AnyAsync(unit => unit.ParentDepartmentId == department.Id && unit.IsActive, ct))
                {
                    throw new DomainRuleException("DEPARTMENT_HAS_FUNCTIONS", $"{department.Name} has functions of its own, so it cannot become a function. Move its functions first.");
                }

                // A function sits in its department's business unit.
                businessUnitId = parent.BusinessUnitId;
            }

            department.Place(body.ParentDepartmentId, businessUnitId, body.HeadEmployeeId, body.CostCenterId, body.LocationId);

            // Moving a department to another business unit moves its functions with it.
            if (!department.IsFunction)
            {
                List<Department> functions = await db.Departments.Where(unit => unit.ParentDepartmentId == department.Id).ToListAsync(ct);
                foreach (Department function in functions.Where(function => function.BusinessUnitId != businessUnitId))
                {
                    function.Place(function.ParentDepartmentId, businessUnitId, function.HeadEmployeeId, function.CostCenterId, function.LocationId);
                }
            }
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

    // ----- Roles -------------------------------------------------------------------------------

    private static void MapJobs(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = Group(endpoints, "roles", "Organization");

        // A role's designations (one per level) switch on and off with it.
        OrgEndpointKit.MapStandardRoutes(group, "OrgRoles", db => db.Roles,
            unit => new OrgRoleItem(
                unit.Id, unit.Code, unit.Name, unit.JobFamilyCode, unit.LevelId, unit.DepartmentId,
                unit.MinimumExperienceYears, unit.MaximumExperienceYears, unit.Description, unit.RequiredSkills, unit.IsActive),
            Permissions.Organization.View, Permissions.Organization.Manage,
            activeChanged: async (db, role, ct) => await RoleDesignations.SyncAsync(db, [role], ct));

        group.MapPost("/", async (SaveOrgRoleRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                await OrgEndpointKit.EnsureCodeFreeAsync(db.Roles, body.Code, null, ct);
                Role role = Role.Create(body.Code, body.Name);
                await DefineAsync(role, body, db, lookups, ct);

                db.Roles.Add(role);
                await RoleDesignations.SyncAsync(db, [role], ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Created($"{Base}/roles/{role.Id}", new { role.Id });
            })
            .WithName("CreateOrgRole")
            .RequirePermission(Permissions.Organization.Manage);

        group.MapPut("/{id:guid}", async (Guid id, SaveOrgRoleRequest body, AppDbContext db, IMasterDataLookup lookups, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                Role role = await OrgEndpointKit.FindAsync(db.Roles, id, ct);
                await OrgEndpointKit.ChangeCodeAsync(db.Roles, role, body.Code, ct);
                role.Rename(body.Name);
                await DefineAsync(role, body, db, lookups, ct);
                await RoleDesignations.SyncAsync(db, [role], ct);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("UpdateOrgRole")
            .RequirePermission(Permissions.Organization.Manage);

        static async Task DefineAsync(Role role, SaveOrgRoleRequest body, AppDbContext db, IMasterDataLookup lookups, CancellationToken ct)
        {
            await OrgEndpointKit.EnsureLookupAsync(lookups, LookupLists.JobFamily, body.JobFamilyCode, "job family", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Levels, body.LevelId, "level", ct);
            await OrgEndpointKit.EnsureActiveAsync(db.Departments, body.DepartmentId, "department", ct);

            // A role belongs to a department and is open to all of its functions.
            if (body.DepartmentId is { } departmentId
                && await db.Departments.AnyAsync(unit => unit.Id == departmentId && unit.ParentDepartmentId != null, ct))
            {
                throw new DomainRuleException("ROLE_DEPARTMENT_IS_FUNCTION", "Choose the department, not one of its functions. A role is open to every function of its department.");
            }

            role.Define(body.JobFamilyCode, body.LevelId, body.DepartmentId, body.MinimumExperienceYears, body.MaximumExperienceYears, body.Description, body.RequiredSkills);
        }
    }
}
