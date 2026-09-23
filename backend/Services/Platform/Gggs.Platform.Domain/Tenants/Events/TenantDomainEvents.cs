using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Events;

namespace Gggs.Platform.Domain.Tenants.Events;

/*
 * Domain events raised by the Tenant aggregate.
 *
 * These stay inside the Platform service. The ones other services react to - "a tenant exists,
 * seed your defaults", "a tenant is suspended, stop serving it" - are published as integration
 * events by the application layer, so the domain stays free of the wire contract.
 */

public sealed record TenantRegisteredDomainEvent(
    Guid TenantId,
    string Code,
    string DataRegion,
    TenantIsolationTier IsolationTier) : DomainEvent;

public sealed record TenantActivatedDomainEvent(Guid TenantId, string Code) : DomainEvent;

public sealed record TenantSuspendedDomainEvent(Guid TenantId, string Code, string Reason) : DomainEvent;

public sealed record TenantOffboardingStartedDomainEvent(
    Guid TenantId,
    string Code,
    DateTime PurgeEligibleDate) : DomainEvent;

public sealed record TenantPurgedDomainEvent(Guid TenantId, string Code) : DomainEvent;

public sealed record TenantIsolationTierChangedDomainEvent(
    Guid TenantId,
    string Code,
    TenantIsolationTier PreviousTier,
    TenantIsolationTier NewTier) : DomainEvent;
