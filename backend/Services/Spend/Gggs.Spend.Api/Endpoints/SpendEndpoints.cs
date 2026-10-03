using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Spend.Api.Contracts;
using Gggs.Spend.Application.Expenses;
using Gggs.Spend.Application.Travel;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Travel;
using Gggs.Spend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Spend.Api.Endpoints;

/// <summary>
/// Expense categories and claims - an employee's own claim (create, add lines, submit) through
/// <see cref="Permissions.Self.ExpenseClaimCreate"/>, and admin review (view anyone's, approve,
/// reject) through <see cref="Permissions.Spend.ClaimView"/>/<see cref="Permissions.Spend.ClaimApprove"/>.
/// No Workflow integration, multi-currency or receipts in this v1 slice - see
/// <c>ExpenseClaim</c>'s own remarks. Also travel requests, advances and settlements - the same
/// self-service/admin split, with advances and settlements always an admin/finance action - see
/// <c>TravelAdvance</c>'s and <c>TravelSettlement</c>'s own remarks.
/// </summary>
public static class SpendEndpoints
{
    public static IEndpointRouteBuilder MapSpendEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapCategoryEndpoints(endpoints);
        MapClaimEndpoints(endpoints);
        MapTravelEndpoints(endpoints);
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

        ExpenseClaim entity = await service.CreateAsync(employeeId, body.ClaimNo, body.Title, body.Currency, body.TravelRequestId, ct);
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

    private static ExpenseCategoryResponse ToResponse(ExpenseCategory entity) => new(entity.Id, entity.Code, entity.Name, entity.IsActive);

    private static ExpenseClaimResponse ToResponse(ExpenseClaim entity) => new(
        entity.Id, entity.ClaimNo, entity.EmployeeId, entity.Title, entity.Currency, entity.Total, entity.Status,
        entity.SubmittedDate, entity.DecidedDate, entity.TravelRequestId, entity.WorkflowInstanceId);

    private static ExpenseLineResponse ToResponse(ExpenseLine entity) => new(
        entity.Id, entity.CategoryId, entity.ExpenseDate, entity.Amount, entity.Merchant);

    private static void MapTravelEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/spend/travel-requests").WithTags("Spend");

        group.MapGet("/me", ListMyTravelRequestsAsync).WithName("ListMyTravelRequests").RequirePermission(Permissions.Self.TravelRequestCreate);

        group.MapGet("/{id:guid}", GetTravelRequestAsync)
            .WithName("GetTravelRequest").RequirePermission(Permissions.Self.TravelRequestCreate, Permissions.Spend.TravelView);

        group.MapPost("/", CreateTravelRequestAsync).WithName("CreateTravelRequest").RequirePermission(Permissions.Self.TravelRequestCreate);
        group.MapPost("/{id:guid}/submit", SubmitTravelRequestAsync)
            .WithName("SubmitTravelRequest").RequirePermission(Permissions.Self.TravelRequestCreate);

        group.MapGet("/{id:guid}/advances", ListAdvancesAsync)
            .WithName("ListTravelAdvances").RequirePermission(Permissions.Self.TravelRequestCreate, Permissions.Spend.TravelView);
        group.MapPost("/{id:guid}/advances", RequestAdvanceAsync)
            .WithName("RequestTravelAdvance").RequirePermission(Permissions.Spend.TravelAdvanceManage);

        group.MapGet("/{id:guid}/settlement", GetSettlementAsync)
            .WithName("GetTravelSettlement").RequirePermission(Permissions.Self.TravelRequestCreate, Permissions.Spend.TravelView);
        group.MapPost("/{id:guid}/settlement/build", BuildSettlementAsync)
            .WithName("BuildTravelSettlement").RequirePermission(Permissions.Spend.TravelAdvanceManage);

        endpoints.MapGet("/api/v1/spend/employees/{employeeId:guid}/travel-requests", ListEmployeeTravelRequestsAsync)
            .WithName("ListEmployeeTravelRequests").WithTags("Spend").RequirePermission(Permissions.Spend.TravelView);

        endpoints.MapPost("/api/v1/spend/travel-advances/{id:guid}/disburse", DisburseAdvanceAsync)
            .WithName("DisburseTravelAdvance").WithTags("Spend").RequirePermission(Permissions.Spend.TravelAdvanceManage);

        endpoints.MapPost("/api/v1/spend/travel-settlements/{id:guid}/settle", SettleSettlementAsync)
            .WithName("SettleTravelSettlement").WithTags("Spend").RequirePermission(Permissions.Spend.TravelAdvanceManage);
    }

    private static async Task<Ok<IReadOnlyList<TravelRequestResponse>>> ListMyTravelRequestsAsync(
        ICurrentUser currentUser, SpendDbContext db, CancellationToken ct)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return TypedResults.Ok<IReadOnlyList<TravelRequestResponse>>([]);
        }

        return TypedResults.Ok(await ListTravelRequestsForEmployeeAsync(employeeId, db, ct));
    }

    private static async Task<Ok<IReadOnlyList<TravelRequestResponse>>> ListEmployeeTravelRequestsAsync(
        Guid employeeId, SpendDbContext db, CancellationToken ct)
        => TypedResults.Ok(await ListTravelRequestsForEmployeeAsync(employeeId, db, ct));

    private static async Task<IReadOnlyList<TravelRequestResponse>> ListTravelRequestsForEmployeeAsync(
        Guid employeeId, SpendDbContext db, CancellationToken ct)
    {
        List<TravelRequestResponse> items = await db.TravelRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return items;
    }

    /// <summary>Same "own record vs admin view" shape as <see cref="GetClaimAsync"/>, against
    /// <see cref="Permissions.Spend.TravelView"/> instead of ClaimView.</summary>
    private static async Task<Results<Ok<TravelRequestResponse>, NotFound, ForbidHttpResult>> GetTravelRequestAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, CancellationToken ct)
    {
        TravelRequest? request = await db.TravelRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.EmployeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.TravelView))
            {
                return TypedResults.Forbid();
            }
        }

        return TypedResults.Ok(ToResponse(request));
    }

    /// <summary>Same "own record unless also an admin" shape <see cref="CreateClaimAsync"/> uses
    /// for expense claims.</summary>
    private static async Task<Results<Created<TravelRequestResponse>, ForbidHttpResult>> CreateTravelRequestAsync(
        CreateTravelRequestBody body, ICurrentUser currentUser, IPermissionProvider permissions, TravelRequestService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid employeeId = body.EmployeeId;
        if (employeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.TravelView))
            {
                return TypedResults.Forbid();
            }
        }

        TravelRequest entity = await service.CreateAsync(
            employeeId, body.RequestNo, body.Purpose, body.FromCity, body.ToCity, body.FromDate, body.ToDate, body.EstimatedCost, body.Currency, ct);
        return TypedResults.Created($"/api/v1/spend/travel-requests/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Results<Ok<TravelRequestResponse>, NotFound, ForbidHttpResult>> SubmitTravelRequestAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, SpendDbContext db, TravelRequestService service, CancellationToken ct)
    {
        TravelRequest? request = await db.TravelRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.EmployeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Spend.TravelView))
            {
                return TypedResults.Forbid();
            }
        }

        return TypedResults.Ok(ToResponse(await service.SubmitAsync(id, ct)));
    }

    private static async Task<Ok<IReadOnlyList<TravelAdvanceResponse>>> ListAdvancesAsync(Guid id, SpendDbContext db, CancellationToken ct)
    {
        List<TravelAdvanceResponse> items = await db.TravelAdvances.AsNoTracking()
            .Where(x => x.TravelRequestId == id)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<TravelAdvanceResponse>>(items);
    }

    private static async Task<Created<TravelAdvanceResponse>> RequestAdvanceAsync(
        Guid id, RequestTravelAdvanceBody body, TravelAdvanceService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        TravelAdvance entity = await service.RequestAsync(id, body.Amount, body.Currency, ct);
        return TypedResults.Created($"/api/v1/spend/travel-requests/{id}/advances/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<TravelAdvanceResponse>> DisburseAdvanceAsync(Guid id, TravelAdvanceService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DisburseAsync(id, ct)));

    private static async Task<Results<Ok<TravelSettlementResponse>, NotFound>> GetSettlementAsync(Guid id, SpendDbContext db, CancellationToken ct)
    {
        TravelSettlement? settlement = await db.TravelSettlements.AsNoTracking().FirstOrDefaultAsync(x => x.TravelRequestId == id, ct);
        return settlement is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(settlement));
    }

    private static async Task<Ok<TravelSettlementResponse>> BuildSettlementAsync(Guid id, TravelSettlementService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.BuildAsync(id, ct)));

    private static async Task<Ok<TravelSettlementResponse>> SettleSettlementAsync(Guid id, TravelSettlementService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.SettleAsync(id, ct)));

    private static TravelRequestResponse ToResponse(TravelRequest entity) => new(
        entity.Id, entity.RequestNo, entity.EmployeeId, entity.Purpose, entity.FromCity, entity.ToCity, entity.FromDate,
        entity.ToDate, entity.EstimatedCost, entity.Currency, entity.Status, entity.SubmittedDate, entity.DecidedDate,
        entity.WorkflowInstanceId);

    private static TravelAdvanceResponse ToResponse(TravelAdvance entity) => new(
        entity.Id, entity.TravelRequestId, entity.Amount, entity.Currency, entity.Status, entity.DisbursedDate);

    private static TravelSettlementResponse ToResponse(TravelSettlement entity) => new(
        entity.Id, entity.TravelRequestId, entity.AdvanceTotal, entity.ExpenseTotal, entity.NetPayable, entity.NetRecoverable,
        entity.Status, entity.SettledDate);
}
