using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.ProjectFinancials.Application;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.ProjectFinancials;

/// <summary>Project financials (spec §74-77, §125).</summary>
public sealed class ProjectFinancialsModule : IModule
{
    public string Name => "project-financials";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<FinanceService>();
        services.AddScoped<BillingService>();
        services.AddScoped<IBilledHours, BilledHoursLookup>();
        services.AddWorkflowSubject(FinanceService.InvoiceSubject);
        services.AddScoped<IMetricSource, FinanceMetrics>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<FinanceSnapshotSweep>();
        services.AddScoped<FinanceSnapshotJob>();
        services.AddHostedService<FinanceJobRegistration>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, SendInvoiceOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, ReturnInvoiceOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnInvoiceOnSendBack>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/project-financials").WithTags("Project financials");

        group.MapGet("/", async (FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.PortfolioAsync(ct)))
            .WithName("PortfolioFinancials").RequirePermission(Permissions.Project.FinancialsView);
        group.MapGet("/{projectId:guid}", async (Guid projectId, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.ProjectAsync(projectId, ct)))
            .WithName("ProjectFinancials").RequirePermission(Permissions.Project.FinancialsView);
        group.MapPut("/{projectId:guid}/budget", async (Guid projectId, BudgetBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveBudgetAsync(projectId, body, ct)))
            .WithName("SaveProjectBudget").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPut("/{projectId:guid}/profile", async (Guid projectId, ProfileBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveProfileAsync(projectId, body, ct)))
            .WithName("SaveProjectFinanceProfile").RequirePermission(Permissions.Project.FinancialsManage);

        group.MapPost("/{projectId:guid}/rates", async (Guid projectId, RateBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveRateAsync(projectId, null, body, ct)))
            .WithName("AddProjectRate").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPut("/{projectId:guid}/rates/{rateId:guid}", async (Guid projectId, Guid rateId, RateBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveRateAsync(projectId, rateId, body, ct)))
            .WithName("UpdateProjectRate").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapDelete("/{projectId:guid}/rates/{rateId:guid}", async (Guid projectId, Guid rateId, FinanceService finance, CancellationToken ct) =>
            {
                await finance.DeleteRateAsync(projectId, rateId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteProjectRate").RequirePermission(Permissions.Project.FinancialsManage);

        group.MapPost("/{projectId:guid}/costs", async (Guid projectId, CostDetails body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveCostAsync(projectId, null, body, ct)))
            .WithName("AddProjectCost").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPut("/{projectId:guid}/costs/{costId:guid}", async (Guid projectId, Guid costId, CostDetails body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveCostAsync(projectId, costId, body, ct)))
            .WithName("UpdateProjectCost").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapDelete("/{projectId:guid}/costs/{costId:guid}", async (Guid projectId, Guid costId, FinanceService finance, CancellationToken ct) =>
            {
                await finance.DeleteCostAsync(projectId, costId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteProjectCost").RequirePermission(Permissions.Project.FinancialsManage);

        group.MapGet("/portfolio/dashboard", async (FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.PortfolioDashboardAsync(ct)))
            .WithName("PortfolioDashboard").RequirePermission(Permissions.Project.FinancialsView);
        group.MapGet("/{projectId:guid}/outcome-units", async (Guid projectId, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.OutcomeUnitsAsync(projectId, ct)))
            .WithName("ProjectOutcomeUnits").RequirePermission(Permissions.Project.FinancialsView);
        group.MapPost("/{projectId:guid}/outcome-units", async (Guid projectId, OutcomeUnitsBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.AcceptOutcomeUnitsAsync(projectId, body, ct)))
            .WithName("AcceptOutcomeUnits").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapDelete("/{projectId:guid}/outcome-units/{unitsId:guid}", async (Guid projectId, Guid unitsId, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.RemoveOutcomeUnitsAsync(projectId, unitsId, ct)))
            .WithName("RemoveOutcomeUnits").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapGet("/billing/run", async (DateOnly month, BillingService billing, CancellationToken ct) => TypedResults.Ok(await billing.RunAsync(month, ct)))
            .WithName("BillingRun").RequirePermission(Permissions.Project.FinancialsView);
        group.MapGet("/billing/export", async ([AsParameters] ExportQuery query, BillingService billing, CancellationToken ct) =>
            {
                ExportFile file = await billing.ExportAsync(query, ct);
                return Results.File(file.Content, file.ContentType, file.FileName);
            })
            .WithName("ExportInvoices").RequirePermission(Permissions.Project.FinancialsView);
        group.MapGet("/{projectId:guid}/billing/preview", async (Guid projectId, DateOnly from, DateOnly to, BillingService billing, CancellationToken ct) =>
                TypedResults.Ok(await billing.PreviewAsync(projectId, from, to, ct)))
            .WithName("PreviewProjectBilling").RequirePermission(Permissions.Project.FinancialsView);
        group.MapPost("/{projectId:guid}/invoices/prepare", async (Guid projectId, PrepareInvoiceBody body, BillingService billing, CancellationToken ct) =>
                TypedResults.Ok(await billing.PrepareAsync(projectId, body, ct)))
            .WithName("PrepareProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapDelete("/{projectId:guid}/invoices/{invoiceId:guid}/lines/{lineId:guid}", async (Guid projectId, Guid invoiceId, Guid lineId, BillingService billing, CancellationToken ct) =>
                TypedResults.Ok(await billing.RemoveLineAsync(projectId, invoiceId, lineId, ct)))
            .WithName("RemoveProjectInvoiceLine").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPost("/{projectId:guid}/invoices/{invoiceId:guid}/credit", async (Guid projectId, Guid invoiceId, CreditBody body, BillingService billing, CancellationToken ct) =>
                TypedResults.Ok(await billing.CreditAsync(projectId, invoiceId, body, ct)))
            .WithName("CreditProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapGet("/{projectId:guid}/credit-notes", async (Guid projectId, BillingService billing, CancellationToken ct) =>
                TypedResults.Ok(await billing.CreditNotesAsync(projectId, ct)))
            .WithName("ProjectCreditNotes").RequirePermission(Permissions.Project.FinancialsView);
        group.MapPost("/{projectId:guid}/invoices", async (Guid projectId, InvoiceBody body, FinanceService finance, CancellationToken ct) => TypedResults.Ok(await finance.SaveInvoiceAsync(projectId, null, body, ct)))
            .WithName("RaiseProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPut("/{projectId:guid}/invoices/{invoiceId:guid}", async (Guid projectId, Guid invoiceId, InvoiceBody body, FinanceService finance, CancellationToken ct) =>
                TypedResults.Ok(await finance.SaveInvoiceAsync(projectId, invoiceId, body, ct)))
            .WithName("UpdateProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPost("/{projectId:guid}/invoices/{invoiceId:guid}/send", async (Guid projectId, Guid invoiceId, FinanceService finance, CancellationToken ct) =>
                TypedResults.Ok(await finance.SendInvoiceAsync(projectId, invoiceId, ct)))
            .WithName("SendProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPost("/{projectId:guid}/invoices/{invoiceId:guid}/paid", async (Guid projectId, Guid invoiceId, PaidOnBody body, FinanceService finance, CancellationToken ct) =>
                TypedResults.Ok(await finance.InvoicePaidAsync(projectId, invoiceId, body.PaidOn, ct)))
            .WithName("ProjectInvoicePaid").RequirePermission(Permissions.Project.FinancialsManage);
        group.MapPost("/{projectId:guid}/invoices/{invoiceId:guid}/cancel", async (Guid projectId, Guid invoiceId, FinanceService finance, CancellationToken ct) =>
                TypedResults.Ok(await finance.CancelInvoiceAsync(projectId, invoiceId, ct)))
            .WithName("CancelProjectInvoice").RequirePermission(Permissions.Project.FinancialsManage);
    }
}
