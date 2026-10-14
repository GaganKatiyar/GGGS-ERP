using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Platform.Domain.Tenants.Events;

namespace Gggs.Modules.Platform.Domain.Tenants;

/// <summary>
/// A customer organization: the isolation boundary the whole platform is built around.
/// <para>
/// A tenant is not a company. A group with three registered companies is one tenant with three
/// legal entities, because payroll, statutory filing and branding are per legal entity while
/// users, roles and configuration are per customer.
/// </para>
/// <para>
/// Platform-owned: this is the registry every other database's <c>TenantId</c> points at, so it
/// cannot itself be tenant-filtered.
/// </para>
/// </summary>
public sealed class Tenant : AggregateRoot, IPlatformOwned, IAuditable, IConcurrencyAware
{
    private Tenant()
    {
        Code = null!;
        Name = null!;
        DataRegion = null!;
        CountryCode = null!;
        DefaultTimeZone = null!;
        DefaultCurrency = null!;
        DefaultLocale = null!;
    }

    /// <summary>Short code used for login discovery and subdomains. Never reused, even after purge.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? LegalName { get; private set; }

    public TenantStatus Status { get; private set; }

    public TenantIsolationTier IsolationTier { get; private set; }

    /// <summary>
    /// Fixed at provisioning. Residency commitments are made per region, and moving a tenant later
    /// is a migration project, not a settings change.
    /// </summary>
    public string DataRegion { get; private set; }

    public string CountryCode { get; private set; }

    public string DefaultTimeZone { get; private set; }

    public string DefaultCurrency { get; private set; }

    public string DefaultLocale { get; private set; }

    public DateTime? ActivatedDate { get; private set; }

    public DateTime? SuspendedDate { get; private set; }

    public string? SuspensionReason { get; private set; }

    /// <summary>When the archived data may be destroyed. Set when offboarding starts.</summary>
    public DateTime? PurgeEligibleDate { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public byte[]? RowVersion { get; private set; }

    /// <summary>Can this tenant serve requests? Only Active does.</summary>
    public bool IsOperational => Status == TenantStatus.Active;

    public static Tenant Register(
        string code,
        string name,
        string countryCode,
        string dataRegion,
        string defaultTimeZone,
        string defaultCurrency,
        string defaultLocale,
        TenantIsolationTier isolationTier,
        DateTime now)
    {
        var tenant = new Tenant
        {
            Id = SequentialGuid.Create(),
            Code = NormalizeCode(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            CountryCode = NormalizeCountry(countryCode),
            DataRegion = Guard.MaxLength(Guard.NotNullOrWhiteSpace(dataRegion).Trim().ToLowerInvariant(), 32),
            DefaultTimeZone = Guard.MaxLength(Guard.NotNullOrWhiteSpace(defaultTimeZone).Trim(), 64),
            DefaultCurrency = NormalizeCurrency(defaultCurrency),
            DefaultLocale = Guard.MaxLength(Guard.NotNullOrWhiteSpace(defaultLocale).Trim(), 10),
            IsolationTier = isolationTier,

            // Never born Active: databases, seed data, roles and the first admin all have to exist
            // first, and a tenant that could sign in mid-provisioning would see a half-built system.
            Status = TenantStatus.Provisioning,
        };

        tenant.Raise(new TenantRegisteredDomainEvent(tenant.Id, tenant.Code, tenant.DataRegion, tenant.IsolationTier));
        return tenant;
    }

    /// <summary>Called by the provisioning saga once every step has succeeded.</summary>
    public void CompleteProvisioning(DateTime now)
    {
        Guard.Against(
            Status != TenantStatus.Provisioning,
            "TENANT_NOT_PROVISIONING",
            $"Only a provisioning tenant can be activated; this one is {Status}.");

        Status = TenantStatus.Active;
        ActivatedDate = now;
        Raise(new TenantActivatedDomainEvent(Id, Code));
    }

    /// <summary>
    /// Suspends access without touching data. Used for non-payment and at customer request; the
    /// data stays exactly as it is so reactivating is instant.
    /// </summary>
    public void Suspend(string reason, DateTime now)
    {
        Guard.NotNullOrWhiteSpace(reason);
        Guard.Against(
            Status != TenantStatus.Active,
            "TENANT_NOT_ACTIVE",
            $"Only an active tenant can be suspended; this one is {Status}.");

        Status = TenantStatus.Suspended;
        SuspendedDate = now;
        SuspensionReason = Guard.MaxLength(reason.Trim(), 500);
        Raise(new TenantSuspendedDomainEvent(Id, Code, SuspensionReason));
    }

    public void Reactivate(DateTime now)
    {
        Guard.Against(
            Status != TenantStatus.Suspended,
            "TENANT_NOT_SUSPENDED",
            $"Only a suspended tenant can be reactivated; this one is {Status}.");

        Status = TenantStatus.Active;
        SuspendedDate = null;
        SuspensionReason = null;
        ActivatedDate = now;
        Raise(new TenantActivatedDomainEvent(Id, Code));
    }

    /// <summary>
    /// Begins offboarding. The grace period is the point: a customer who leaves by mistake, or
    /// whose renewal is stuck in procurement, must be recoverable without a database restore.
    /// </summary>
    public void BeginOffboarding(TimeSpan gracePeriod, DateTime now)
    {
        Guard.Against(
            Status is not (TenantStatus.Active or TenantStatus.Suspended),
            "TENANT_CANNOT_OFFBOARD",
            $"Only an active or suspended tenant can be offboarded; this one is {Status}.");

        Guard.Against(
            gracePeriod < TimeSpan.FromDays(7),
            "TENANT_GRACE_TOO_SHORT",
            "The offboarding grace period must be at least seven days.");

        Status = TenantStatus.Offboarding;
        PurgeEligibleDate = now.Add(gracePeriod);
        Raise(new TenantOffboardingStartedDomainEvent(Id, Code, PurgeEligibleDate.Value));
    }

    /// <summary>Their export is delivered and the grace period has run; data may now be destroyed.</summary>
    public void Archive(DateTime now)
    {
        Guard.Against(
            Status != TenantStatus.Offboarding,
            "TENANT_NOT_OFFBOARDING",
            $"Only an offboarding tenant can be archived; this one is {Status}.");

        Guard.Against(
            PurgeEligibleDate is not null && now < PurgeEligibleDate.Value,
            "TENANT_GRACE_NOT_ELAPSED",
            $"The grace period runs until {PurgeEligibleDate:u}; the tenant cannot be archived before then.");

        Status = TenantStatus.Archived;
    }

    /// <summary>
    /// Records that the data has been destroyed. Terminal: the row stays so the code can never be
    /// handed to another customer, which would let old links and cached references resolve to them.
    /// </summary>
    public void MarkPurged(DateTime now)
    {
        Guard.Against(
            Status != TenantStatus.Archived,
            "TENANT_NOT_ARCHIVED",
            $"Only an archived tenant can be purged; this one is {Status}.");

        Status = TenantStatus.Purged;
        Raise(new TenantPurgedDomainEvent(Id, Code));
    }

    public void Rename(string name, string? legalName)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        LegalName = legalName is null ? null : Guard.MaxLength(legalName.Trim(), 300);
    }

    /// <summary>
    /// Moves the tenant between the pooled and dedicated tiers. The record changes here; the data
    /// move is a separate operation the platform team runs (ADR-01).
    /// </summary>
    public void ChangeIsolationTier(TenantIsolationTier tier)
    {
        Guard.Against(
            Status is not (TenantStatus.Active or TenantStatus.Suspended),
            "TENANT_CANNOT_CHANGE_TIER",
            $"A tenant in {Status} cannot change isolation tier.");

        if (IsolationTier == tier)
        {
            return;
        }

        TenantIsolationTier previous = IsolationTier;
        IsolationTier = tier;
        Raise(new TenantIsolationTierChangedDomainEvent(Id, Code, previous, tier));
    }

    /// <summary>
    /// Codes are lowercase, alphanumeric with hyphens, and become part of a hostname. Reserved
    /// words are refused so a tenant cannot claim a code that collides with our own routes.
    /// </summary>
    private static string NormalizeCode(string code)
    {
        string normalized = Guard.NotNullOrWhiteSpace(code).Trim().ToLowerInvariant();

        Guard.Against(
            normalized.Length is < 3 or > 32,
            "TENANT_CODE_LENGTH",
            "Tenant code must be between 3 and 32 characters.");

        Guard.Against(
            !normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'),
            "TENANT_CODE_CHARACTERS",
            "Tenant code may contain only letters, digits and hyphens.");

        Guard.Against(
            normalized.StartsWith('-') || normalized.EndsWith('-'),
            "TENANT_CODE_CHARACTERS",
            "Tenant code may not start or end with a hyphen.");

        Guard.Against(
            ReservedCodes.Contains(normalized),
            "TENANT_CODE_RESERVED",
            $"'{normalized}' is reserved and cannot be used as a tenant code.");

        return normalized;
    }

    private static readonly HashSet<string> ReservedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "admin", "portal", "login", "auth", "identity", "static", "cdn",
        "mail", "smtp", "status", "support", "help", "docs", "blog", "about", "careers",
        "platform", "system", "internal", "test", "staging", "demo",
    };

    private static string NormalizeCountry(string countryCode)
    {
        string normalized = Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant();

        Guard.Against(
            normalized.Length != 2,
            "TENANT_COUNTRY_INVALID",
            "Country must be a two-letter ISO 3166-1 code.");

        return normalized;
    }

    private static string NormalizeCurrency(string currency)
    {
        string normalized = Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant();

        Guard.Against(
            normalized.Length != 3,
            "TENANT_CURRENCY_INVALID",
            "Currency must be a three-letter ISO 4217 code.");

        return normalized;
    }
}
