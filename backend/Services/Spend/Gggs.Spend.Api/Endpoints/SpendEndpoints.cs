using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Spend.Api.Contracts;
using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Spend.Api.Endpoints;

/// <summary>
/// Expense categories and claims - an employee's own claim (create, add lines, submit) through
/// <see cref="Permissions.Self.ExpenseClaimCreate"/>, and admin review (view anyone's, approve,
/// reject) through <see cref="Permissions.Spend.ClaimView"/>/<see cref="Permissions.Spend.ClaimApprove"/>.
/// No Workflow integration, multi-currency or receipts in this v1 slice - see
/// <c>ExpenseClaim</c>'s own remarks.
/// </summary>
public static class SpendEndpoints
{
    public static IEndpointRouteBuilder MapSpendEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapCategoryEndpoints(endpoints);
        MapClaimEndpoints(endpoints);
        return endpoints;
    }

    private static void MapCategoryEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/spend/categories").WithTags("Spend");

        group.MapGet("/", ListCategoriesAsync).WithName("ListExpenseCategories").RequirePermission(Permissions.Spend.CategoryManage);
        group.MapPost("/", CreateCategoryAsync).WithName("CreateExpenseCategory").RequirePermission(Permissions.Spend.CategoryManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateCategoryAsync)
            .WithName("DeactivateExpenseCategory").RequirePermission(Permissions.Spend.CategoryManage);
        group.MapPost("/{id:guid}/reactivate", ReactivateCategoryAsync)
            .WithName("ReactivateExpenseCategory").RequirePermission(Permissions.Spend.CategoryManage);
    }

    private static async Task<Ok<IReadOnlyList<ExpenseCategoryResponse>>> ListCategoriesAsync(SpendDbContext db, CancellationToken ct)
    {
        List<ExpenseCategoryResponse> items = await db.ExpenseCategories.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<ExpenseCategoryResponse>>(items);
    }

    private static async Task<Created<ExpenseCategoryResponse>> CreateCategoryAsync(
        CreateExpenseCategoryBody body, ExpenseCategoryService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        ExpenseCategory entity = await service.CreateAsync(body.Code, body.Name, ct);
        return TypedResults.Created($"/api/v1/spend/categories/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<ExpenseCategoryResponse>> DeactivateCategoryAsync(Guid id, ExpenseCategoryService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)));

    private static async Task<Ok<ExpenseCategoryResponse>> ReactivateCategoryAsync(Guid id, ExpenseCategoryService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)));

    private static void MapClaimEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/spend/claims").WithTags("Spend");

        group.MapGet("/me", ListMyClaimsAsync).WithName("ListMyExpenseClaims").RequirePermission(Permissions.Self.ExpenseClaimCreate);

        // Either permission reaches the handler; which claim a caller may actually read is decided
        // inside it, against the claim's own EmployeeId - the same "own record" shape Payroll's
        // payslip endpoints use.
        group.MapGet("/{id:guid}", GetClaimAsync)
            .WithName("GetExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate, Permissions.Spend.ClaimView);

        group.MapPost("/", CreateClaimAsync).WithName("CreateExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        group.MapPost("/{id:guid}/lines", AddLineAsync).WithName("AddExpenseLine").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        group.MapGet("/{id:guid}/lines", ListLinesAsync)
            .WithName("ListExpenseLines").RequirePermission(Permissions.Self.ExpenseClaimCreate, Permissions.Spend.ClaimView);
        group.MapPost("/{id:guid}/submit", SubmitClaimAsync).WithName("SubmitExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);

        group.MapPost("/{id:guid}/approve", ApproveClaimAsync).WithName("ApproveExpenseClaim").RequirePermission(Permissions.Spend.ClaimApprove);
        group.MapPost("/{id:guid}/reject", RejectClaimAsync).WithName("RejectExpenseClaim").RequirePermission(Permissions.Spend.ClaimApprove);

        endpoints.MapGet("/api/v1/spend/employees/{employeeId:guid}/claims", ListEmployeeClaimsAsync)
            .WithName("ListEmployeeExpenseClaims").WithTags("Spend").RequirePermission(Permissions.Spend.ClaimView);
    }

    private static async Task<Ok<IReadOnlyList<ExpenseClaimResponse>>> ListMyClaimsAsync(
        ICurrentUser currentUser, SpendDbContext db, CancellationToken ct)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return TypedResults.Ok<IReadOnlyList<ExpenseClaimResponse>>([]);
        }

        return TypedResults.Ok(await ListClaimsForEmployeeAsync(employeeId, db, ct));
    }

    private static async Task<Ok<IReadOnlyList<ExpenseClaimResponse>>> ListEmployeeClaimsAsync(
        Guid employeeId, SpendDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ListClaimsForEmployeeAsync(employeeId, db, ct));

    private static async Task<IReadOnlyList<ExpenseClaimResponse>> ListClaimsForEmployeeAsync(
        Guid employeeId, SpendDbContext db, CancellationToken ct)
    {
        List<ExpenseClaimResponse> items = await db.ExpenseClaims.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return items;
    }

    /// <summary>
    /// A caller with just <see cref="Permissions.Self.ExpenseClaimCreate"/> may only reach their
    /// own claim, checked against the claim's own <see cref="ExpenseClaim.EmployeeId"/>, not a
    /// blanket grant - the same "own record" shape self-service permissions elsewhere in this
    /// catalog use. A caller who also holds <see cref="Permissions.Spend.ClaimView"/> may reach
    /// anyone's.
    /// </summary>
    private static async Task<Results<Ok<ExpenseClaimResponse>, NotFound, ForbidHttpResult>> GetClaimAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, CancellationToken ct)
    {
        ExpenseClaim? claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (claim is null)
        {
            return TypedResults.NotFound();
        }

        if (claim.EmployeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.ClaimView))
            {
                return TypedResults.Forbid();
            }
        }

        return TypedResults.Ok(ToResponse(claim));
    }

    private static async Task<Results<Ok<IReadOnlyList<ExpenseLineResponse>>, NotFound, ForbidHttpResult>> ListLinesAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, CancellationToken ct)
    {
        ExpenseClaim? claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (claim is null)
        {
            return TypedResults.NotFound();
        }

        if (claim.EmployeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.ClaimView))
            {
                return TypedResults.Forbid();
            }
        }

        List<ExpenseLineResponse> items = await db.ExpenseLines.AsNoTracking()
            .Where(x => x.ClaimId == id)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<ExpenseLineResponse>>(items);
    }

    /// <summary>
    /// A caller who does not also hold <see cref="Permissions.Spend.ClaimView"/> may only create a
    /// claim for themselves - the body's EmployeeId is ignored for them, and the signed-in
    /// employee is used instead, so "own record" cannot be bypassed by naming someone else's id.
    /// An admin holding ClaimView may create on behalf of the employee named in the body.
    /// </summary>
    private static async Task<Results<Created<ExpenseClaimResponse>, ForbidHttpResult>> CreateClaimAsync(
        CreateExpenseClaimBody body, ICurrentUser currentUser, IPermissionProvider permissions, ExpenseClaimService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid employeeId = body.EmployeeId;
        if (employeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.ClaimView))
            {
                return TypedResults.Forbid();
            }
        }

        ExpenseClaim entity = await service.CreateAsync(employeeId, body.ClaimNo, body.Title, body.Currency, ct);
        return TypedResults.Created($"/api/v1/spend/claims/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Results<Created<ExpenseLineResponse>, NotFound, ForbidHttpResult>> AddLineAsync(
        Guid id, AddExpenseLineBody body, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db,
        ExpenseClaimService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        ClaimAccess access = await CheckClaimAccessAsync(id, currentUser, permissions, db, ct);
        if (access != ClaimAccess.Allowed)
        {
            return ToAccessResult<Created<ExpenseLineResponse>>(access);
        }

        ExpenseLine entity = await service.AddLineAsync(id, body.CategoryId, body.ExpenseDate, body.Amount, body.Merchant, ct);
        return TypedResults.Created($"/api/v1/spend/claims/{id}/lines/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Results<Ok<ExpenseClaimResponse>, NotFound, ForbidHttpResult>> SubmitClaimAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, ExpenseClaimService service, CancellationToken ct)
    {
        ClaimAccess access = await CheckClaimAccessAsync(id, currentUser, permissions, db, ct);
        if (access != ClaimAccess.Allowed)
        {
            return ToAccessResult<Ok<ExpenseClaimResponse>>(access);
        }

        return TypedResults.Ok(ToResponse(await service.SubmitAsync(id, ct)));
    }

    private enum ClaimAccess
    {
        Allowed,
        NotFound,
        Forbidden,
    }

    /// <summary>
    /// Shared by <see cref="AddLineAsync"/> and <see cref="SubmitClaimAsync"/>, the two write
    /// actions a caller with just <see cref="Permissions.Self.ExpenseClaimCreate"/> may take
    /// against a claim - both need the same "is this my own claim" check <see cref="GetClaimAsync"/>
    /// already does for reads.
    /// </summary>
    private static async Task<ClaimAccess> CheckClaimAccessAsync(
        Guid claimId, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, CancellationToken ct)
    {
        ExpenseClaim? claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(x => x.Id == claimId, ct);
        if (claim is null)
        {
            return ClaimAccess.NotFound;
        }

        if (claim.EmployeeId == currentUser.EmployeeId)
        {
            return ClaimAccess.Allowed;
        }

        PermissionSet held = await permissions.GetCurrentAsync(ct);
        return held.Has(Permissions.Spend.ClaimView) ? ClaimAccess.Allowed : ClaimAccess.Forbidden;
    }

    private static Results<TSuccess, NotFound, ForbidHttpResult> ToAccessResult<TSuccess>(ClaimAccess access)
        where TSuccess : IResult
        => access == ClaimAccess.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();

    private static async Task<Ok<ExpenseClaimResponse>> ApproveClaimAsync(Guid id, ExpenseClaimService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ApproveAsync(id, ct)));

    private static async Task<Ok<ExpenseClaimResponse>> RejectClaimAsync(Guid id, ExpenseClaimService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.RejectAsync(id, ct)));

    private static ExpenseCategoryResponse ToResponse(ExpenseCategory entity) => new(entity.Id, entity.Code, entity.Name, entity.IsActive);

    private static ExpenseClaimResponse ToResponse(ExpenseClaim entity) => new(
        entity.Id, entity.ClaimNo, entity.EmployeeId, entity.Title, entity.Currency, entity.Total, entity.Status,
        entity.SubmittedDate, entity.DecidedDate);

    private static ExpenseLineResponse ToResponse(ExpenseLine entity) => new(
        entity.Id, entity.CategoryId, entity.ExpenseDate, entity.Amount, entity.Merchant);
}
