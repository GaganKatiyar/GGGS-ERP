using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Attendance.Domain;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class AttendanceModeTests
{
    [Fact]
    public void A_profile_needs_at_least_one_known_mode()
    {
        AttendanceProfile profile = AttendanceProfile.For(Guid.NewGuid(), AttendanceModes.Device | AttendanceModes.Mobile);
        profile.Modes.HasFlag(AttendanceModes.Web).ShouldBeFalse();

        Should.Throw<DomainRuleException>(() => profile.Set(AttendanceModes.None));
        Should.Throw<DomainRuleException>(() => profile.Set((AttendanceModes)64));
    }

    [Fact]
    public void Without_a_profile_web_and_device_are_allowed_as_before()
        => AttendanceProfile.Default.ShouldBe(AttendanceModes.Web | AttendanceModes.Device);
}
