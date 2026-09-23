namespace Gggs.BuildingBlocks.Domain.Abstractions;

/*
 * Tenant lifecycle vocabulary.
 *
 * Defined here, in the layer that depends on nothing, because both the Platform domain (which owns
 * the transitions) and the shared tenant context (which every service reads) need the same words.
 * They were briefly declared in both places, and the constant aliasing that required was a sign
 * the duplication was wrong rather than a problem to work around: two enums that must always
 * agree are one enum.
 */

/// <summary>Where a tenant is in its life (architecture §3.4).</summary>
public enum TenantStatus
{
    /// <summary>Created, but its databases and seed data are still being set up.</summary>
    Provisioning = 1,

    Active = 2,

    /// <summary>Billing lapsed or the customer asked us to pause. Data intact, nobody can sign in.</summary>
    Suspended = 3,

    /// <summary>Leaving. Read-only while their export is prepared and the grace period runs.</summary>
    Offboarding = 4,

    /// <summary>Exported and grace period over. Data still present, awaiting purge.</summary>
    Archived = 5,

    /// <summary>Data destroyed. The row remains so the code can never be reused.</summary>
    Purged = 6,
}

/// <summary>Whether a tenant shares a database or has its own (ADR-01).</summary>
public enum TenantIsolationTier
{
    Pooled = 1,
    Dedicated = 2,
}
