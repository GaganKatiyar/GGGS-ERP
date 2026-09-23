using Gggs.BuildingBlocks.Domain.Abstractions;

namespace Gggs.Platform.Api.Contracts;

/*
 * Request and response shapes for the platform console.
 *
 * None of them carries a TenantId for the acting tenant: platform endpoints name the tenant being
 * operated on in the route, and tenant-scoped endpoints take it from the token. An architecture
 * test enforces the second part.
 */

/// <summary>Brings a customer onto the platform.</summary>
public sealed record ProvisionTenantBody
{
    /// <summary>Short code for login discovery and subdomains. Permanent once issued.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? LegalName { get; init; }

    /// <summary>ISO 3166-1 alpha-2, e.g. <c>IN</c>.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Region the data lives in. Fixed at provisioning for residency.</summary>
    public required string DataRegion { get; init; }

    /// <summary>IANA time zone, e.g. <c>Asia/Kolkata</c>.</summary>
    public required string DefaultTimeZone { get; init; }

    /// <summary>ISO 4217, e.g. <c>INR</c>.</summary>
    public required string DefaultCurrency { get; init; }

    public required string DefaultLocale { get; init; }

    public required string PlanCode { get; init; }

    public TenantIsolationTier IsolationTier { get; init; } = TenantIsolationTier.Pooled;

    public int? TrialDays { get; init; }

    public int? SeatLimit { get; init; }

    public string? BillingReference { get; init; }

    /// <summary>Address the first administrator invitation is sent to.</summary>
    public required string AdministratorEmail { get; init; }
}

/// <summary>Why a tenant is being suspended. Required: somebody will have to explain it.</summary>
public sealed record SuspendTenantBody(string Reason);

/// <summary>Grace period before any data may be destroyed. Minimum seven days.</summary>
public sealed record BeginOffboardingBody(int GraceDays = 30);

public sealed record TenantListItem(
    Guid Id,
    string Code,
    string Name,
    TenantStatus Status,
    TenantIsolationTier IsolationTier,
    string CountryCode,
    string DataRegion,
    DateTime CreatedDate);

public sealed record TenantDetail(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    TenantStatus Status,
    TenantIsolationTier IsolationTier,
    string CountryCode,
    string DataRegion,
    string DefaultTimeZone,
    string DefaultCurrency,
    string DefaultLocale,
    DateTime? ActivatedDate,
    DateTime? SuspendedDate,
    string? SuspensionReason,
    DateTime? PurgeEligibleDate,
    SubscriptionSummary? Subscription,
    DateTime CreatedDate);

public sealed record SubscriptionSummary(
    Guid Id,
    string PlanCode,
    string PlanName,
    string Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? TrialEndsOn,
    int? SeatLimit,
    IReadOnlyList<string> EnabledFeatures);

public sealed record PlanListItem(
    Guid Id,
    string Code,
    string Name,
    string Edition,
    int? MaxEmployees,
    bool IsPubliclyAvailable,
    bool IsActive,
    IReadOnlyList<string> FeatureCodes);

/// <summary>A tenant's own configuration, read and written by their administrators.</summary>
public sealed record TenantSettingItem(string Module, string Key, string ValueJson);

public sealed record UpsertTenantSettingBody(string ValueJson);
