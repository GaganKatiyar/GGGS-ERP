using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Organization;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

public sealed class BranchTests
{
    [Fact]
    public void SettingAGeoFenceWithoutCoordinates_IsRejected()
    {
        Branch branch = Branch.Create("HQ", "Head Office", Guid.NewGuid(), "IN", "Asia/Kolkata");

        // A radius with nothing to measure it from is not a fence, it is a number nobody can act on.
        Should.Throw<DomainRuleException>(() => branch.SetGeoFence(null, null, 100))
            .ErrorCode.ShouldBe("GEO_FENCE_LOCATION_MISSING");
    }

    [Fact]
    public void ATinyGeoFenceRadius_IsRejected()
    {
        Branch branch = Branch.Create("HQ", "Head Office", Guid.NewGuid(), "IN", "Asia/Kolkata");

        Should.Throw<DomainRuleException>(() => branch.SetGeoFence(19.0760, 72.8777, 5))
            .ErrorCode.ShouldBe("GEO_FENCE_RADIUS_TOO_SMALL");
    }

    [Fact]
    public void GeoFencingIsOffByDefault()
    {
        // Off by default is the point: geo-fencing is a surveillance feature, and a tenant switches
        // it on knowingly rather than discovering it running.
        Branch branch = Branch.Create("HQ", "Head Office", Guid.NewGuid(), "IN", "Asia/Kolkata");
        branch.GeoFenceMeters.ShouldBeNull();
    }

    [Fact]
    public void AValidGeoFence_IsAccepted()
    {
        Branch branch = Branch.Create("HQ", "Head Office", Guid.NewGuid(), "IN", "Asia/Kolkata");

        branch.SetGeoFence(19.0760, 72.8777, 100);

        branch.GeoFenceMeters.ShouldBe(100);
        branch.GeoLatitude.ShouldBe(19.0760);
    }

    [Fact]
    public void ClearingAGeoFence_RemovesTheRadiusAndCoordinates()
    {
        Branch branch = Branch.Create("HQ", "Head Office", Guid.NewGuid(), "IN", "Asia/Kolkata");
        branch.SetGeoFence(19.0760, 72.8777, 100);

        branch.SetGeoFence(null, null, null);

        branch.GeoFenceMeters.ShouldBeNull();
        branch.GeoLatitude.ShouldBeNull();
        branch.GeoLongitude.ShouldBeNull();
    }
}
