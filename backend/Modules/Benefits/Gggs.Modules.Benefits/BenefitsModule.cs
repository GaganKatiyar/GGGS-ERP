using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Benefits.Application;
using Gggs.Modules.Benefits.Contracts;
using Gggs.Modules.Benefits.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Benefits;

/// <summary>Benefits (spec §24).</summary>
public sealed class BenefitsModule : IModule
{
    public string Name => "benefits";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<BenefitService>();
        services.AddScoped<IBenefitCharges>(provider => provider.GetRequiredService<BenefitService>());
        services.AddScoped<BenefitSeeder>();
        services.AddWorkflowSubject(BenefitService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedBenefitsOnTenantProvisioned>();
        services.AddGggsEventConsumer<EmployeeHiredIntegrationEvent, AutoEnrollOnHire>();
        services.AddGggsEventConsumer<EmployeeExitedIntegrationEvent, EndBenefitsOnExit>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, DecideEnrollmentOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, DecideEnrollmentOnRejection>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/benefits").WithTags("Benefits");
        me.MapGet("/", async (BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.MineAsync(ct)))
            .WithName("MyBenefits").RequirePermission(Permissions.Self.BenefitManage);
        me.MapPost("/", async (EnrollBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.EnrollMeAsync(body, ct)))
            .WithName("EnrollMeInBenefit").RequirePermission(Permissions.Self.BenefitManage);
        me.MapPut("/{id:guid}/dependents", async (Guid id, DependentsBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.UpdateMyDependentsAsync(id, body.DependentIds, ct)))
            .WithName("UpdateMyBenefitDependents").RequirePermission(Permissions.Self.BenefitManage);
        me.MapPost("/{id:guid}/cancel", async (Guid id, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.CancelMineAsync(id, ct)))
            .WithName("CancelMyBenefit").RequirePermission(Permissions.Self.BenefitManage);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/benefits").WithTags("Benefits");
        group.MapGet("/plans", async (bool? includeInactive, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.PlansAsync(includeInactive ?? false, ct)))
            .WithName("ListBenefitPlans").RequirePermission(Permissions.Benefit.PlanView);
        group.MapPost("/plans", async (PlanBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.SavePlanAsync(null, body, ct)))
            .WithName("CreateBenefitPlan").RequirePermission(Permissions.Benefit.PlanManage);
        group.MapPut("/plans/{id:guid}", async (Guid id, PlanBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.SavePlanAsync(id, body, ct)))
            .WithName("UpdateBenefitPlan").RequirePermission(Permissions.Benefit.PlanManage);
        group.MapPost("/plans/{id:guid}/activate", async (Guid id, BenefitService benefits, CancellationToken ct) =>
            {
                await benefits.SetPlanActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateBenefitPlan").RequirePermission(Permissions.Benefit.PlanManage);
        group.MapPost("/plans/{id:guid}/deactivate", async (Guid id, BenefitService benefits, CancellationToken ct) =>
            {
                await benefits.SetPlanActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateBenefitPlan").RequirePermission(Permissions.Benefit.PlanManage);

        group.MapGet("/enrollments", async (Guid? planId, EnrollmentStatus? status, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.EnrollmentsAsync(planId, status, ct)))
            .WithName("ListBenefitEnrollments").RequirePermission(Permissions.Benefit.EnrollmentView);
        group.MapPost("/enrollments", async (EnrollOnBehalfBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.EnrollOnBehalfAsync(body, ct)))
            .WithName("EnrollInBenefit").RequirePermission(Permissions.Benefit.EnrollmentManage);
        group.MapPost("/enrollments/{id:guid}/end", async (Guid id, EndBody body, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.EndAsync(id, body.LastDay, ct)))
            .WithName("EndBenefitEnrollment").RequirePermission(Permissions.Benefit.EnrollmentManage);
        group.MapGet("/employees/{employeeId:guid}", async (Guid employeeId, BenefitService benefits, CancellationToken ct) => TypedResults.Ok(await benefits.ForEmployeeAsync(employeeId, ct)))
            .WithName("EmployeeBenefits").RequirePermission(Permissions.Benefit.EnrollmentView);
    }
}
