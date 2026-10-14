using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.MasterData.Application;
using Gggs.Modules.MasterData.Domain;
using Gggs.Modules.MasterData.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.MasterData.Endpoints;

public sealed record LookupListItem(string Code, string Name, string? ParentList, bool TenantExtensible);

public sealed record LookupValueItem(Guid Id, string Code, string Label, string? ParentCode, int SortOrder, bool IsActive, bool IsSystem);

public sealed record CreateLookupValueRequest(string Code, string Label, string? ParentCode, int? SortOrder);

public sealed record UpdateLookupValueRequest(string Label, int SortOrder);

/// <summary>
/// Lookup lists: every dropdown in the product reads from here (spec §113). Reading is open to
/// anyone signed in with master-data view - every form needs it; changing a list is administration.
/// </summary>
public static class MasterDataEndpoints
{
    public static IEndpointRouteBuilder MapMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/master-data").WithTags("Master data");

        group.MapGet("/", ListListsAsync).WithName("ListLookupLists").RequirePermission(Permissions.MasterData.View);
        group.MapGet("/{listCode}", ListValuesAsync).WithName("ListLookupValues").RequirePermission(Permissions.MasterData.View);
        group.MapPost("/{listCode}", CreateAsync).WithName("CreateLookupValue").RequirePermission(Permissions.MasterData.Manage);
        group.MapPut("/{listCode}/{id:guid}", UpdateAsync).WithName("UpdateLookupValue").RequirePermission(Permissions.MasterData.Manage);
        group.MapPost("/{listCode}/{id:guid}/activate", ActivateAsync).WithName("ActivateLookupValue").RequirePermission(Permissions.MasterData.Manage);
        group.MapPost("/{listCode}/{id:guid}/deactivate", DeactivateAsync).WithName("DeactivateLookupValue").RequirePermission(Permissions.MasterData.Manage);

        return endpoints;
    }

    private static Ok<IReadOnlyList<LookupListItem>> ListListsAsync()
        => TypedResults.Ok<IReadOnlyList<LookupListItem>>(
            [.. LookupCatalog.Lists.Select(list => new LookupListItem(list.Code, list.Name, list.ParentList, list.TenantExtensible))]);

    private static async Task<Ok<IReadOnlyList<LookupValueItem>>> ListValuesAsync(
        string listCode,
        string? parent,
        bool? includeInactive,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        LookupListDefinition list = RequireList(listCode);

        IQueryable<LookupValue> query = database.LookupValues.AsNoTracking().Where(value => value.ListCode == list.Code);

        if (includeInactive != true)
        {
            query = query.Where(value => value.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(parent))
        {
            string parentCode = LookupValue.NormalizeCode(parent);
            query = query.Where(value => value.ParentCode == parentCode);
        }

        List<LookupValueItem> values = await query
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Label)
            .Select(value => new LookupValueItem(value.Id, value.Code, value.Label, value.ParentCode, value.SortOrder, value.IsActive, value.IsSystem))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<LookupValueItem>>(values);
    }

    private static async Task<Created<LookupValueItem>> CreateAsync(
        string listCode,
        CreateLookupValueRequest body,
        AppDbContext database,
        IUnitOfWork unitOfWork,
        IGggsCache cache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        LookupListDefinition list = RequireList(listCode);

        if (!list.TenantExtensible)
        {
            // Countries, currencies and blood groups are standards, not preferences.
            throw new DomainRuleException("LOOKUP_LIST_NOT_EXTENSIBLE", $"Values cannot be added to '{list.Name}'.");
        }

        string code = LookupValue.NormalizeCode(body.Code);

        if (await database.LookupValues.AnyAsync(value => value.ListCode == list.Code && value.Code == code, cancellationToken))
        {
            throw new DomainRuleException("LOOKUP_CODE_IN_USE", $"'{code}' already exists in '{list.Name}'.");
        }

        if (list.ParentList is not null)
        {
            string parentCode = string.IsNullOrWhiteSpace(body.ParentCode)
                ? throw new DomainRuleException("LOOKUP_PARENT_REQUIRED", $"Values in '{list.Name}' need a parent from '{list.ParentList}'.")
                : LookupValue.NormalizeCode(body.ParentCode);

            if (!await database.LookupValues.AnyAsync(value => value.ListCode == list.ParentList && value.Code == parentCode, cancellationToken))
            {
                throw new DomainRuleException("LOOKUP_PARENT_UNKNOWN", $"'{parentCode}' is not a value of '{list.ParentList}'.");
            }
        }

        int sortOrder = body.SortOrder
            ?? ((await database.LookupValues.Where(value => value.ListCode == list.Code).MaxAsync(value => (int?)value.SortOrder, cancellationToken)) ?? 0) + 10;

        LookupValue created = LookupValue.Create(list.Code, code, body.Label, list.ParentList is null ? null : body.ParentCode, sortOrder);
        database.LookupValues.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAreaAsync(CacheAreas.Lookups, cancellationToken);

        return TypedResults.Created($"/api/v1/master-data/{list.Code}/{created.Id}", ToItem(created));
    }

    private static async Task<Ok<LookupValueItem>> UpdateAsync(
        string listCode,
        Guid id,
        UpdateLookupValueRequest body,
        AppDbContext database,
        IUnitOfWork unitOfWork,
        IGggsCache cache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        LookupValue value = await FindAsync(database, listCode, id, cancellationToken);

        value.Update(body.Label, body.SortOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAreaAsync(CacheAreas.Lookups, cancellationToken);

        return TypedResults.Ok(ToItem(value));
    }

    private static async Task<Ok<LookupValueItem>> ActivateAsync(
        string listCode, Guid id, AppDbContext database, IUnitOfWork unitOfWork, IGggsCache cache, CancellationToken cancellationToken)
        => await SetActiveAsync(listCode, id, active: true, database, unitOfWork, cache, cancellationToken);

    private static async Task<Ok<LookupValueItem>> DeactivateAsync(
        string listCode, Guid id, AppDbContext database, IUnitOfWork unitOfWork, IGggsCache cache, CancellationToken cancellationToken)
        => await SetActiveAsync(listCode, id, active: false, database, unitOfWork, cache, cancellationToken);

    /// <summary>
    /// Deactivating hides a value from new selections; records already holding its code keep it and
    /// still show its label. That is why there is no delete.
    /// </summary>
    private static async Task<Ok<LookupValueItem>> SetActiveAsync(
        string listCode, Guid id, bool active, AppDbContext database, IUnitOfWork unitOfWork, IGggsCache cache, CancellationToken cancellationToken)
    {
        LookupValue value = await FindAsync(database, listCode, id, cancellationToken);

        if (active)
        {
            value.Activate();
        }
        else
        {
            value.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAreaAsync(CacheAreas.Lookups, cancellationToken);
        return TypedResults.Ok(ToItem(value));
    }

    private static LookupListDefinition RequireList(string listCode)
        => LookupCatalog.Find(listCode) ?? throw new NotFoundException("LookupList", listCode);

    private static async Task<LookupValue> FindAsync(AppDbContext database, string listCode, Guid id, CancellationToken cancellationToken)
    {
        LookupListDefinition list = RequireList(listCode);
        return await database.LookupValues.FirstOrDefaultAsync(value => value.Id == id && value.ListCode == list.Code, cancellationToken)
            ?? throw new NotFoundException(nameof(LookupValue), id);
    }

    private static LookupValueItem ToItem(LookupValue value)
        => new(value.Id, value.Code, value.Label, value.ParentCode, value.SortOrder, value.IsActive, value.IsSystem);
}
