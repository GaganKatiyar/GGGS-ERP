using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// A registered office or site of a legal entity. Attendance geo-fencing, statutory registration
/// (professional tax varies by Indian state, for instance) and shift defaults all key off the branch,
/// which is why it carries its own time zone rather than inheriting the legal entity's.
/// </summary>
public sealed class Branch : TenantAggregateRoot
{
    private Branch()
    {
        Code = null!;
        Name = null!;
        CountryCode = null!;
        TimeZone = null!;
        Address = Address.Empty;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Address Address { get; private set; }

    public string CountryCode { get; private set; }

    public string? StateCode { get; private set; }

    /// <summary>IANA id, e.g. <c>Asia/Kolkata</c>. Every attendance timestamp for this branch is interpreted against it.</summary>
    public string TimeZone { get; private set; }

    public double? GeoLatitude { get; private set; }

    public double? GeoLongitude { get; private set; }

    /// <summary>
    /// Radius for attendance geo-fencing. Null means geo-fencing is off for this branch - a
    /// surveillance feature a tenant switches on knowingly, not a default.
    /// </summary>
    public int? GeoFenceMeters { get; private set; }

    public static Branch Create(
        string code,
        string name,
        Guid legalEntityId,
        string countryCode,
        string timeZone)
    {
        Guard.NotEmpty(legalEntityId);

        return new Branch
        {
            Code = OrganizationCode.Normalize(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            LegalEntityId = legalEntityId,
            CountryCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant(), 2),
            TimeZone = Guard.MaxLength(Guard.NotNullOrWhiteSpace(timeZone).Trim(), 64),
            Address = Address.Empty,
        };
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void SetAddress(Address address, string? stateCode)
    {
        Address = Guard.NotNull(address);
        StateCode = string.IsNullOrWhiteSpace(stateCode) ? null : Guard.MaxLength(stateCode.Trim(), 10);
    }

    public void SetTimeZone(string timeZone) => TimeZone = Guard.MaxLength(Guard.NotNullOrWhiteSpace(timeZone).Trim(), 64);

    public void SetGeoFence(double? latitude, double? longitude, int? radiusMeters)
    {
        Guard.Against(
            radiusMeters is not null && (latitude is null || longitude is null),
            "GEO_FENCE_LOCATION_MISSING",
            "A geo-fence radius needs a latitude and longitude to measure it from.");

        Guard.Against(radiusMeters is < 10, "GEO_FENCE_RADIUS_TOO_SMALL", "A geo-fence radius must be at least 10 metres.");

        GeoLatitude = latitude;
        GeoLongitude = longitude;
        GeoFenceMeters = radiusMeters;
    }
}
