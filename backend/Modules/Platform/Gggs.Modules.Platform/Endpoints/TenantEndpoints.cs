using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Endpoints;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Endpoints;

/// <summary>
/// The platform console: managing customers.
/// <para>
/// Every endpoint here needs a <c>platform.*</c> permission, which only our own staff hold. A
/// tenant administrator, however senior, can reach none of it - these are commercial and lifecycle
/// operations, not tenant configuration.
/// </para>
/// </summary>
public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/tenants").WithTags("Tenants");

        group.MapGet("/", ListAsync)
            .WithName("ListTenants")
            .RequirePermission(Permissions.Platform.TenantView);

        group.MapGet("/{tenantId:guid}", GetAsync)
            .WithName("GetTenant")
            .RequirePermission(Permissions.Platform.TenantView);

        group.MapPost("/", ProvisionAsync)
            .WithName("ProvisionTenant")
            .RequirePermission(Permissions.Platform.TenantManage);

        group.MapPost("/{tenantId:guid}/activate", ActivateAsync)
            .WithName("ActivateTenant")
            .RequirePermission(Permissions.Platform.TenantManage);

        // Suspension is separated from general management: cutting a paying customer off is the
        // most disruptive thing this console can do, so it gets its own permission.
        group.MapPost("/{tenantId:guid}/suspend", SuspendAsync)
            .WithName("SuspendTenant")
            .RequirePermission(Permissions.Platform.TenantSuspend);

        group.MapPost("/{tenantId:guid}/reactivate", ReactivateAsync)
            .WithName("ReactivateTenant")
            .RequirePermission(Permissions.Platform.TenantSuspend);

        group.MapPost("/{tenantId:guid}/begin-offboarding", BeginOffboardingAsync)
            .WithName("BeginTenantOffboarding")
            .RequirePermission(Permissions.Platform.TenantManage);

        endpoints.MapGet("/api/v1/plans", ListPlansAsync)
            .WithName("ListPlans")
            .WithTags("Plans")
            .RequirePermission(Permissions.Platform.SubscriptionManage);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<TenantListItem>>> ListAsync(
        [AsParameters] PageQuery page,
        [FromQuery] TenantStatus? status,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();
        string? search = request.Search;

        IQueryable<Tenant> query = database.Tenants.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(tenant => tenant.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(tenant => tenant.Code.Contains(term) || tenant.Name.Contains(term));
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<TenantListItem> items = await query
            .OrderBy(tenant => tenant.Code)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(tenant => new TenantListItem(
                tenant.Id,
                tenant.Code,
                tenant.Name,
                tenant.Status,
                tenant.IsolationTier,
                tenant.CountryCode,
                tenant.DataRegion,
                tenant.CreatedDate))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Ok<TenantDetail>> GetAsync(
        Guid tenantId,
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Tenant tenant = await database.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        SubscriptionSummary? subscription = await BuildSubscriptionSummaryAsync(
            database, tenantId, timeProvider, cancellationToken);

        return TypedResults.Ok(new TenantDetail(
            tenant.Id,
            tenant.Code,
            tenant.Name,
            tenant.LegalName,
            tenant.Status,
            tenant.IsolationTier,
            tenant.CountryCode,
            tenant.DataRegion,
            tenant.DefaultTimeZone,
            tenant.DefaultCurrency,
            tenant.DefaultLocale,
            tenant.ActivatedDate,
            tenant.SuspendedDate,
            tenant.SuspensionReason,
            tenant.PurgeEligibleDate,
            subscription,
            tenant.CreatedDate));
    }

    private static async Task<Created<TenantDetail>> ProvisionAsync(
        ProvisionTenantBody body,
        TenantProvisioningService provisioning,
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        ProvisionTenantResult result = await provisioning.ProvisionAsync(
            new ProvisionTenantRequest
            {
                Code = body.Code,
                Name = body.Name,
                LegalName = body.LegalName,
                CountryCode = body.CountryCode,
                DataRegion = body.DataRegion,
                DefaultTimeZone = body.DefaultTimeZone,
                DefaultCurrency = body.DefaultCurrency,
                DefaultLocale = body.DefaultLocale,
                PlanCode = body.PlanCode,
                IsolationTier = body.IsolationTier,
                TrialDays = body.TrialDays,
                SeatLimit = body.SeatLimit,
                BillingReference = body.BillingReference,
                AdministratorEmail = body.AdministratorEmail,
            },
            cancellationToken);

        Ok<TenantDetail> created = await GetAsync(result.TenantId, database, timeProvider, cancellationToken);
        return TypedResults.Created($"/api/v1/tenants/{result.TenantId}", created.Value!);
    }

    private static async Task<NoContent> ActivateAsync(
        Guid tenantId,
        TenantProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        await provisioning.ActivateAsync(tenantId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SuspendAsync(
        Guid tenantId,
        SuspendTenantBody body,
        TenantProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await provisioning.SuspendAsync(tenantId, body.Reason, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ReactivateAsync(
        Guid tenantId,
        TenantProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        await provisioning.ReactivateAsync(tenantId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> BeginOffboardingAsync(
        Guid tenantId,
        BeginOffboardingBody body,
        TenantProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await provisioning.BeginOffboardingAsync(
            tenantId,
            TimeSpan.FromDays(body.GraceDays),
            cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<PlanListItem>>> ListPlansAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        List<Plan> plans = await database.Plans
            .AsNoTracking()
            .OrderBy(plan => plan.Edition)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PlanListItem> items =
        [
            .. plans.Select(plan => new PlanListItem(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.Edition.ToString(),
                plan.MaxEmployees,
                plan.IsPubliclyAvailable,
                plan.IsActive,
                [.. plan.FeatureCodes]))
        ];

        return TypedResults.Ok(items);
    }

    private static async Task<SubscriptionSummary?> BuildSubscriptionSummaryAsync(
        AppDbContext database,
        Guid tenantId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await database.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId
                    && (candidate.Status == SubscriptionStatus.Trial
                        || candidate.Status == SubscriptionStatus.Active
                        || candidate.Status == SubscriptionStatus.PastDue),
                cancellationToken);

        if (subscription is null)
        {
            return null;
        }

        Plan? plan = await database.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == subscription.PlanId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return new SubscriptionSummary(
            subscription.Id,
            plan.Code,
            plan.Name,
            subscription.Status.ToString(),
            subscription.StartDate,
            subscription.EndDate,
            subscription.TrialEndsOn,
            subscription.SeatLimit,
            [.. subscription.ResolveFeatures(plan, today)]);
    }
}
