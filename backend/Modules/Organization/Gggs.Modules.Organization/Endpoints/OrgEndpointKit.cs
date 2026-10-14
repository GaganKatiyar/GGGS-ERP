using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Organization.Endpoints;

/// <summary>A list query for organization records: paging, search on code and name, and an active filter.</summary>
public sealed record OrgListQuery
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    public string? Search { get; init; }

    /// <summary>Null for all, true for active only, false for inactive only.</summary>
    public bool? Active { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Sort = Sort, Search = Search };
}

/// <summary>
/// What every organization endpoint shares: the standard routes, and the checks every write makes
/// before it touches anything - code free, references real, employees active, lookup codes known.
/// </summary>
internal static class OrgEndpointKit
{
    /// <summary>Maps list, get, activate and deactivate for one kind. Create and update are the kind's own.</summary>
    public static void MapStandardRoutes<T, TItem>(
        RouteGroupBuilder group,
        string name,
        Func<AppDbContext, DbSet<T>> set,
        Expression<Func<T, TItem>> select,
        string viewPermission,
        string managePermission)
        where T : OrgUnit
    {
        group.MapGet("/", async ([AsParameters] OrgListQuery query, AppDbContext database, CancellationToken cancellationToken) =>
            {
                PageRequest page = query.ToPageRequest();
                IQueryable<T> rows = set(database).AsNoTracking();

                if (!string.IsNullOrWhiteSpace(page.Search))
                {
                    string term = page.Search.Trim();
                    rows = rows.Where(unit => unit.Code.Contains(term) || unit.Name.Contains(term));
                }

                if (query.Active is not null)
                {
                    rows = rows.Where(unit => unit.IsActive == query.Active.Value);
                }

                PagedResult<TItem> result = await rows
                    .ApplySort(page.Sort, SortableColumns<T>(), "code")
                    .ToPagedResultAsync(page, select, cancellationToken);

                return TypedResults.Ok(result);
            })
            .WithName($"List{name}")
            .RequirePermission(viewPermission);

        group.MapGet("/{id:guid}", async Task<Results<Ok<TItem>, NotFound>> (Guid id, AppDbContext database, CancellationToken cancellationToken) =>
            {
                TItem? item = await set(database).AsNoTracking().Where(unit => unit.Id == id).Select(select).FirstOrDefaultAsync(cancellationToken);
                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            })
            .WithName($"Get{name}")
            .RequirePermission(viewPermission);

        group.MapPost("/{id:guid}/activate", async (Guid id, AppDbContext database, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
            {
                T unit = await FindAsync(set(database), id, cancellationToken);
                unit.Activate();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName($"Activate{name}")
            .RequirePermission(managePermission);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, AppDbContext database, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
            {
                T unit = await FindAsync(set(database), id, cancellationToken);
                unit.Deactivate();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName($"Deactivate{name}")
            .RequirePermission(managePermission);
    }

    public static async Task<T> FindAsync<T>(IQueryable<T> set, Guid id, CancellationToken cancellationToken)
        where T : OrgUnit
        => await set.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken)
            ?? throw new NotFoundException(typeof(T).Name, id);

    /// <summary>Refuses a code another live record of the same kind already uses.</summary>
    public static async Task EnsureCodeFreeAsync<T>(IQueryable<T> set, string code, Guid? exceptId, CancellationToken cancellationToken)
        where T : OrgUnit
    {
        string normalized = OrgUnit.NormalizeCode(code);

        if (await set.AnyAsync(unit => unit.Code == normalized && unit.Id != exceptId, cancellationToken))
        {
            throw new DomainRuleException("ORG_CODE_IN_USE", $"The code '{normalized}' is already used by another {Describe<T>()}.");
        }
    }

    /// <summary>Refuses a reference to a record that does not exist in this tenant or is inactive.</summary>
    public static async Task EnsureActiveAsync<T>(IQueryable<T> set, Guid? id, string field, CancellationToken cancellationToken)
        where T : OrgUnit
    {
        if (id is null)
        {
            return;
        }

        // The tenant filter applies here, so another tenant's id is simply "not found".
        if (!await set.AnyAsync(unit => unit.Id == id && unit.IsActive, cancellationToken))
        {
            throw new DomainRuleException("ORG_REFERENCE_INVALID", $"The selected {field} does not exist or is inactive.") { Context = new Dictionary<string, object?> { ["field"] = field } };
        }
    }

    public static async Task EnsureEmployeeAsync(IEmployeeDirectory employees, Guid? employeeId, string field, CancellationToken cancellationToken)
    {
        if (employeeId is not null && !await employees.IsActiveAsync(employeeId.Value, cancellationToken))
        {
            throw new DomainRuleException("ORG_EMPLOYEE_INVALID", $"The selected {field} is not an active employee.") { Context = new Dictionary<string, object?> { ["field"] = field } };
        }
    }

    public static async Task EnsureLookupAsync(IMasterDataLookup lookups, string listCode, string? code, string field, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(code) && !await lookups.IsValidAsync(listCode, code, cancellationToken))
        {
            throw new DomainRuleException("LOOKUP_CODE_INVALID", $"'{code}' is not a valid {field}.") { Context = new Dictionary<string, object?> { ["field"] = field } };
        }
    }

    private static Dictionary<string, Expression<Func<T, object?>>> SortableColumns<T>()
        where T : OrgUnit
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = unit => unit.Code,
            ["name"] = unit => unit.Name,
            ["active"] = unit => unit.IsActive,
            ["created"] = unit => unit.CreatedDate,
        };

    private static string Describe<T>() => typeof(T).Name switch
    {
        nameof(LegalEntity) => "company",
        nameof(BusinessUnit) => "business unit",
        nameof(CostCenter) => "cost center",
        nameof(ProfitCenter) => "profit center",
        nameof(JobLevel) => "job level",
        string other => other.ToLowerInvariant(),
    };
}
