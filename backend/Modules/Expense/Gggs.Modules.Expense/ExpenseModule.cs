using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Expense.Application;
using Gggs.Modules.Expense.Contracts;
using Gggs.Modules.Expense.Domain;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Expense;

/// <summary>Expenses and travel (spec: expense and travel).</summary>
public sealed class ExpenseModule : IModule
{
    public string Name => "expense";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<ExpenseService>();
        services.AddScoped<ExpenseSeeder>();
        services.AddScoped<IExpenseLedger, ExpenseLedger>();
        services.AddScoped<IDocumentOwnerAccess, ClaimDocumentAccess>();
        services.AddWorkflowSubject(ExpenseService.ClaimSubject);
        services.AddWorkflowSubject(ExpenseService.TravelSubject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedExpenseOnTenantProvisioned>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveExpenseOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectExpenseOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnExpenseOnSendBack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/expenses").WithTags("Expenses");

        group.MapGet("/dashboard", async (ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("ExpenseDashboard").RequirePermission(Permissions.Spend.ClaimView);
        group.MapGet("/categories", async (ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.CategoriesAsync(ct)))
            .WithName("ListExpenseCategories").RequirePermission(Permissions.Spend.ClaimView, Permissions.Self.ExpenseClaimCreate);
        group.MapPost("/categories", async (CategoryBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(null, body, ct)))
            .WithName("CreateExpenseCategory").RequirePermission(Permissions.Spend.CategoryManage);
        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(id, body, ct)))
            .WithName("UpdateExpenseCategory").RequirePermission(Permissions.Spend.CategoryManage);

        string[] claimAccess = [Permissions.Spend.ClaimView, Permissions.Self.ExpenseClaimCreate, Permissions.Workflow.TaskAct];
        group.MapGet("/claims", async ([AsParameters] ClaimQuery query, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.ClaimsAsync(query, ct)))
            .WithName("ListExpenseClaims").RequirePermission(Permissions.Spend.ClaimView);
        group.MapGet("/claims/{id:guid}", async (Guid id, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.ClaimAsync(id, ct)))
            .WithName("GetExpenseClaim").RequirePermission(claimAccess);
        group.MapPost("/claims/{id:guid}/pay", async (Guid id, PayBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.PayAsync(id, body, ct)))
            .WithName("PayExpenseClaim").RequirePermission(Permissions.Spend.ClaimPay);

        group.MapGet("/travel", async (TravelStatus? status, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.TripsAsync(status, ct)))
            .WithName("ListTravelRequests").RequirePermission(Permissions.Spend.TravelView);
        group.MapGet("/travel/{id:guid}", async (Guid id, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.TripAsync(id, ct)))
            .WithName("GetTravelRequest").RequirePermission(Permissions.Spend.TravelView, Permissions.Self.TravelRequestCreate, Permissions.Workflow.TaskAct);
        group.MapPost("/travel/{id:guid}/advance", async (Guid id, AdvanceBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.PayAdvanceAsync(id, body.Amount, ct)))
            .WithName("PayTravelAdvance").RequirePermission(Permissions.Spend.TravelAdvanceManage);
        group.MapPost("/travel/{id:guid}/settle", async (Guid id, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.SettleAsync(id, ct)))
            .WithName("SettleTravel").RequirePermission(Permissions.Spend.TravelAdvanceManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Expenses");
        me.MapGet("/expenses", async (ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.MyClaimsAsync(ct)))
            .WithName("MyExpenseClaims").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapPost("/expenses", async (ClaimBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.DraftAsync(body, ct)))
            .WithName("DraftExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapPut("/expenses/{id:guid}", async (Guid id, ClaimBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, body, ct)))
            .WithName("UpdateExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapPut("/expenses/{id:guid}/lines", async (Guid id, IReadOnlyList<ClaimLineInput> body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.SaveLinesAsync(id, body, ct)))
            .WithName("SaveExpenseLines").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapPost("/expenses/{id:guid}/submit", async (Guid id, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.SubmitAsync(id, ct)))
            .WithName("SubmitExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapPost("/expenses/{id:guid}/cancel", async (Guid id, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.CancelAsync(id, ct)))
            .WithName("CancelExpenseClaim").RequirePermission(Permissions.Self.ExpenseClaimCreate);
        me.MapGet("/travel", async (ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.MyTripsAsync(ct)))
            .WithName("MyTravelRequests").RequirePermission(Permissions.Self.TravelRequestCreate);
        me.MapPost("/travel", async (TravelBody body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.RequestTripAsync(body, ct)))
            .WithName("RequestTravel").RequirePermission(Permissions.Self.TravelRequestCreate);
        me.MapPut("/travel/{id:guid}", async (Guid id, TravelDetails body, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateTripAsync(id, body, ct)))
            .WithName("UpdateTravelRequest").RequirePermission(Permissions.Self.TravelRequestCreate);
        me.MapPost("/travel/{id:guid}/{action:regex(^(submit|cancel)$)}", async (Guid id, string action, ExpenseService service, CancellationToken ct) => TypedResults.Ok(await service.TripActionAsync(id, action, ct)))
            .WithName("ChangeTravelRequest").RequirePermission(Permissions.Self.TravelRequestCreate);
    }
}

internal sealed class SeedExpenseOnTenantProvisioned(ExpenseSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal static class ExpenseDecisions
{
    public static Task ApplyAsync(ExpenseService expenses, string? subjectType, Guid? subjectId, bool? approved, CancellationToken cancellationToken)
        => (subjectType, subjectId) switch
        {
            (ExpenseService.ClaimSubjectType, { } id) => expenses.DecideClaimAsync(id, approved, cancellationToken),
            (ExpenseService.TravelSubjectType, { } id) => expenses.DecideTripAsync(id, approved, cancellationToken),
            _ => Task.CompletedTask,
        };
}

internal sealed class ApproveExpenseOnApproval(ExpenseService expenses) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => ExpenseDecisions.ApplyAsync(expenses, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: true, cancellationToken);
}

internal sealed class RejectExpenseOnRejection(ExpenseService expenses) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => ExpenseDecisions.ApplyAsync(expenses, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: false, cancellationToken);
}

internal sealed class ReturnExpenseOnSendBack(ExpenseService expenses) : IIntegrationEventConsumer<WorkflowSentBackIntegrationEvent>
{
    public Task HandleAsync(WorkflowSentBackIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => ExpenseDecisions.ApplyAsync(expenses, integrationEvent?.SubjectType, integrationEvent?.SubjectId, approved: null, cancellationToken);
}
