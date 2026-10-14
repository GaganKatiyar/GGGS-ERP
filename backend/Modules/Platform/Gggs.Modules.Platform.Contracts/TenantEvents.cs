using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Platform.Contracts;

/*
 * The Platform service's published contract.
 *
 * Other services reference this package and nothing else of ours. Payloads carry ids plus the few
 * fields a consumer needs to act; anything more is fetched from the API, which applies its own
 * permissions. Once a consumer exists, these shapes may only be added to - a changed or removed
 * property is a new version of the event.
 */

/// <summary>
/// A tenant record exists and its databases are ready. Every service seeds its defaults on this:
/// roles, leave types, workflow definitions, notification templates.
/// <para>
/// Consumers must be idempotent. Delivery is at least once, and provisioning is re-run by hand when
/// a step fails, so "seed the defaults" will sometimes arrive twice for the same tenant.
/// </para>
/// </summary>
[MessageType("gggs.platform.tenant.provisioned")]
public sealed record TenantProvisionedIntegrationEvent(
    Guid TenantId,
    string Code,
    string Name,
    string CountryCode,
    string DefaultTimeZone,
    string DefaultCurrency,
    string DefaultLocale,
    string DataRegion,

    /// <summary>
    /// Who the first administrator invitation goes to. Carried on the event because Identity is
    /// the service that creates accounts and it has no other way to learn this - the address was
    /// given for exactly this purpose and reaches one consumer.
    /// </summary>
    string AdministratorEmail) : IntegrationEvent;

/// <summary>
/// The tenant is live. Sent after every service has reported its seeding complete, so anything
/// that should only happen once the customer can actually sign in hangs off this rather than
/// <see cref="TenantProvisionedIntegrationEvent"/>.
/// </summary>
[MessageType("gggs.platform.tenant.activated")]
public sealed record TenantActivatedIntegrationEvent(Guid TenantId, string Code) : IntegrationEvent;

/// <summary>
/// The tenant may no longer be served. Services drop cached tenant data and stop background work
/// for it; nothing is deleted, because suspension is reversible and usually commercial.
/// </summary>
[MessageType("gggs.platform.tenant.suspended")]
public sealed record TenantSuspendedIntegrationEvent(Guid TenantId, string Code, string Reason) : IntegrationEvent;

/// <summary>The tenant is served again. Paired with the suspension event so caches can be rebuilt.</summary>
[MessageType("gggs.platform.tenant.reactivated")]
public sealed record TenantReactivatedIntegrationEvent(Guid TenantId, string Code) : IntegrationEvent;

/// <summary>
/// The customer is leaving. Services switch the tenant to read-only and prepare their part of the
/// data export; the grace period runs until <paramref name="PurgeEligibleDate"/>.
/// </summary>
[MessageType("gggs.platform.tenant.offboarding-started")]
public sealed record TenantOffboardingStartedIntegrationEvent(
    Guid TenantId,
    string Code,
    DateTime PurgeEligibleDate) : IntegrationEvent;

/// <summary>
/// Destroy this tenant's data. Irreversible, and every service is expected to act on it: rows,
/// documents, cached entries and search indexes.
/// </summary>
[MessageType("gggs.platform.tenant.purge-requested")]
public sealed record TenantPurgeRequestedIntegrationEvent(Guid TenantId, string Code) : IntegrationEvent;

/// <summary>
/// The tenant's entitlements changed - a new plan, or an override added or withdrawn. Services
/// holding a cached feature list refresh it; a module that just disappeared must stop being served.
/// </summary>
[MessageType("gggs.platform.tenant.entitlements-changed")]
public sealed record TenantEntitlementsChangedIntegrationEvent(
    Guid TenantId,
    IReadOnlyList<string> EnabledFeatures) : IntegrationEvent;
