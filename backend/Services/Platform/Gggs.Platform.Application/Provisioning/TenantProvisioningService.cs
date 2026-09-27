using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Platform.Contracts;
using Gggs.Platform.Domain.Subscriptions;
using Gggs.Platform.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Gggs.Platform.Application.Provisioning;

/// <summary>Everything needed to bring a customer onto the platform.</summary>
public sealed record ProvisionTenantRequest
{
    /// <summary>Short code used for login discovery and subdomains. Permanent once issued.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? LegalName { get; init; }

    public required string CountryCode { get; init; }

    /// <summary>
    /// Region the data lives in. Fixed at provisioning, because residency is promised per region
    /// and moving a tenant later is a migration project.
    /// </summary>
    public required string DataRegion { get; init; }

    public required string DefaultTimeZone { get; init; }

    public required string DefaultCurrency { get; init; }

    public required string DefaultLocale { get; init; }

    public required string PlanCode { get; init; }

    public TenantIsolationTier IsolationTier { get; init; } = TenantIsolationTier.Pooled;

    /// <summary>Trial length, or null to start a paid subscription immediately.</summary>
    public int? TrialDays { get; init; }

    public int? SeatLimit { get; init; }

    public string? BillingReference { get; init; }

    /// <summary>Who receives the first administrator invitation.</summary>
    public required string AdministratorEmail { get; init; }
}

public sealed record ProvisionTenantResult(Guid TenantId, string Code, TenantStatus Status);

/// <summary>
/// Creates a tenant and announces it.
/// <para>
/// Deliberately not a distributed transaction. The tenant and its subscription commit locally
/// together with the message that tells every other service to seed its defaults; the relay
/// delivers it afterwards. If a downstream service fails to seed, the tenant stays in
/// <see cref="TenantStatus.Provisioning"/> and is visible as incomplete rather than half-usable.
/// </para>
/// <para>
/// Activation is therefore a separate step, not the end of this method: a tenant becomes Active
/// once seeding has actually happened, which is the difference between a customer signing in to a
/// working system and signing in to an empty one.
/// </para>
/// </summary>
public sealed class TenantProvisioningService(
    ITenantRepository tenants,
    IPlanRepository plans,
    IIntegrationEventPublisher publisher,
    ITenantRegistry registry,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<TenantProvisioningService> logger)
{
    public async Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        // Checked before creating anything, so a duplicate request fails cleanly rather than
        // leaving a half-built tenant behind. The unique index is still the real guarantee.
        if (await tenants.CodeExistsAsync(request.Code, cancellationToken))
        {
            throw new DomainRuleException(
                "TENANT_CODE_IN_USE",
                $"Tenant code '{request.Code}' is already in use.");
        }

        Plan plan = await plans.GetByCodeAsync(request.PlanCode, cancellationToken)
            ?? throw new NotFoundException(nameof(Plan), request.PlanCode);

        if (!plan.IsActive)
        {
            throw new DomainRuleException(
                "PLAN_RETIRED",
                $"Plan '{request.PlanCode}' is retired and cannot be sold to a new tenant.");
        }

        Tenant tenant = Tenant.Register(
            request.Code,
            request.Name,
            request.CountryCode,
            request.DataRegion,
            request.DefaultTimeZone,
            request.DefaultCurrency,
            request.DefaultLocale,
            request.IsolationTier,
            now);

        if (request.LegalName is not null)
        {
            tenant.Rename(request.Name, request.LegalName);
        }

        DateOnly today = DateOnly.FromDateTime(now);

        Subscription subscription = Subscription.Start(
            tenant.Id,
            plan.Id,
            today,
            request.TrialDays is null ? null : today.AddDays(request.TrialDays.Value),
            request.SeatLimit ?? plan.MaxEmployees,
            request.BillingReference);

        tenants.Add(tenant);
        tenants.AddSubscription(subscription);

        // Staged, not sent: it commits with the rows above, and the relay publishes it after.
        publisher.Publish(new TenantProvisionedIntegrationEvent(
            tenant.Id,
            tenant.Code,
            tenant.Name,
            tenant.CountryCode,
            tenant.DefaultTimeZone,
            tenant.DefaultCurrency,
            tenant.DefaultLocale,
            tenant.DataRegion,
            request.AdministratorEmail));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Tenant {TenantCode} ({TenantId}) provisioned on plan {PlanCode} in {DataRegion}; awaiting seeding.",
            tenant.Code,
            tenant.Id,
            plan.Code,
            tenant.DataRegion);

        return new ProvisionTenantResult(tenant.Id, tenant.Code, tenant.Status);
    }

    /// <summary>
    /// Marks provisioning complete and lets the customer in. Called once the seeding services have
    /// reported success, or by an operator finishing a failed provisioning by hand.
    /// </summary>
    public async Task ActivateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        Tenant tenant = await tenants.GetAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        tenant.CompleteProvisioning(timeProvider.GetUtcNow().UtcDateTime);

        publisher.Publish(new TenantActivatedIntegrationEvent(tenant.Id, tenant.Code));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The registry caches tenant status, and a stale entry here means a customer told they are
        // live still cannot sign in.
        await registry.InvalidateAsync(tenant.Id, cancellationToken);

        logger.LogInformation("Tenant {TenantCode} ({TenantId}) is now active.", tenant.Code, tenant.Id);
    }

    public async Task SuspendAsync(Guid tenantId, string reason, CancellationToken cancellationToken = default)
    {
        Tenant tenant = await tenants.GetAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        tenant.Suspend(reason, timeProvider.GetUtcNow().UtcDateTime);

        publisher.Publish(new TenantSuspendedIntegrationEvent(tenant.Id, tenant.Code, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidated immediately: a suspension that takes five minutes to bite is a suspension
        // that let five more minutes of access through.
        await registry.InvalidateAsync(tenant.Id, cancellationToken);

        logger.LogWarning("Tenant {TenantCode} suspended: {Reason}", tenant.Code, reason);
    }

    public async Task ReactivateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        Tenant tenant = await tenants.GetAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        tenant.Reactivate(timeProvider.GetUtcNow().UtcDateTime);

        publisher.Publish(new TenantReactivatedIntegrationEvent(tenant.Id, tenant.Code));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await registry.InvalidateAsync(tenant.Id, cancellationToken);

        logger.LogInformation("Tenant {TenantCode} reactivated.", tenant.Code);
    }

    /// <summary>
    /// Starts offboarding. The grace period is the safety net: until it elapses nothing is
    /// destroyed, and the tenant can be reactivated by an operator.
    /// </summary>
    public async Task BeginOffboardingAsync(
        Guid tenantId,
        TimeSpan gracePeriod,
        CancellationToken cancellationToken = default)
    {
        Tenant tenant = await tenants.GetAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        tenant.BeginOffboarding(gracePeriod, now);

        publisher.Publish(new TenantOffboardingStartedIntegrationEvent(
            tenant.Id,
            tenant.Code,
            tenant.PurgeEligibleDate!.Value));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await registry.InvalidateAsync(tenant.Id, cancellationToken);

        logger.LogWarning(
            "Tenant {TenantCode} is offboarding; data may be purged after {PurgeEligibleDate:u}.",
            tenant.Code,
            tenant.PurgeEligibleDate);
    }
}

/// <summary>Persistence for the tenant registry, kept as a port so the service stays testable.</summary>
public interface ITenantRepository
{
    Task<Tenant?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    void Add(Tenant tenant);

    void AddSubscription(Subscription subscription);
}

public interface IPlanRepository
{
    Task<Plan?> GetByCodeAsync(string planCode, CancellationToken cancellationToken = default);
}
