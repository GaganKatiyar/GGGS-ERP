using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// A company registered inside a tenant.
/// <para>
/// A tenant is a customer; a legal entity is one of the companies that customer runs. A group with
/// three registered companies is one tenant with three legal entities, and the distinction matters
/// from here on: payroll, statutory registration, payslip branding and bank files are all per legal
/// entity, never per tenant, because that is the boundary the law actually draws.
/// </para>
/// </summary>
public sealed class LegalEntity : TenantAggregateRoot
{
    private LegalEntity()
    {
        Code = null!;
        Name = null!;
        CountryCode = null!;
        Currency = null!;
        Address = Address.Empty;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public string CountryCode { get; private set; }

    public string Currency { get; private set; }

    public Address Address { get; private set; }

    /// <summary>
    /// Which compliance pack this entity's payroll runs under. Free text for now: the pack registry
    /// belongs to the Payroll service, which does not exist yet, and a code that means nothing until
    /// then is honest about that rather than pretending a validated reference exists.
    /// </summary>
    public string? CompliancePackCode { get; private set; }

    public static LegalEntity Create(
        string code,
        string name,
        string countryCode,
        string currency,
        string? registrationNumber = null)
    {
        return new LegalEntity
        {
            Code = OrganizationCode.Normalize(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            CountryCode = NormalizeCountryCode(countryCode),
            Currency = NormalizeCurrency(currency),
            RegistrationNumber = Normalize(registrationNumber, 100),
            Address = Address.Empty,
        };
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void SetRegistrationNumber(string? registrationNumber) => RegistrationNumber = Normalize(registrationNumber, 100);

    public void SetAddress(Address address) => Address = Guard.NotNull(address);

    public void SetCompliancePack(string? compliancePackCode) => CompliancePackCode = Normalize(compliancePackCode, 50);

    private static string NormalizeCountryCode(string countryCode)
    {
        string normalized = Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant();

        Guard.Against(normalized.Length != 2, "COUNTRY_CODE_INVALID", "Country code must be a two-letter ISO code.");

        return normalized;
    }

    private static string NormalizeCurrency(string currency)
    {
        string normalized = Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant();

        Guard.Against(normalized.Length != 3, "CURRENCY_CODE_INVALID", "Currency must be a three-letter ISO code.");

        return normalized;
    }

    private static string? Normalize(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}
