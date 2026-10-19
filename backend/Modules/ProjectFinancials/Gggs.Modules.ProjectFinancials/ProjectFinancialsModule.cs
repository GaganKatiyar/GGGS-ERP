using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.ProjectFinancials.Application;
using Gggs.Modules.ProjectFinancials.Domain;

namespace Gggs.Modules.ProjectFinancials;

/// <summary>Project financials (spec §74-77, §125).</summary>
public sealed class ProjectFinancialsModule : IModule
{
    public string Name => "project-financials";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<FinanceService>();
        services.AddScoped<IMetricSource, FinanceMetrics>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<FinanceSnapshotSweep>();
        services.AddScoped<FinanceSnapshotJob>();
        services.AddHostedService<FinanceJobRegistration>();
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
