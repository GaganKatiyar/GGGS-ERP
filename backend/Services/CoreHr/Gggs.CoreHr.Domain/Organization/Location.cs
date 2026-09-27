using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>Where within a branch someone actually works - a floor, a site, or nowhere physical at all.</summary>
public enum LocationType
{
    Office = 1,
    Site = 2,
    Remote = 3,
}

/// <summary>
/// A place inside a branch. Distinct from <see cref="Branch"/> because one registered branch often
/// has several floors or client sites, and "remote" has to be representable without inventing a
/// branch that does not exist.
/// </summary>
public sealed class Location : TenantAggregateRoot
{
    private Location()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public Guid BranchId { get; private set; }

    public LocationType LocationType { get; private set; }

    public static Location Create(string code, string name, Guid branchId, LocationType locationType)
    {
        Guard.NotEmpty(branchId);

        return new Location
        {
            Code = OrganizationCode.Normalize(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
            BranchId = branchId,
            LocationType = locationType,
        };
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
}
