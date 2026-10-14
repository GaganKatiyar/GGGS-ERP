using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff.Events;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// Account rules for platform staff - the mirror of <see cref="UserTests"/>. Pinned for the same
/// reason: everything here decides whether one of our own can get into a system that reaches every
/// tenant's data.
/// </summary>
public sealed class PlatformStaffMemberTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnInvitedMember_HasNoPasswordAndCannotYetBeUsedNormally()
    {
        PlatformStaffMember member = Invite();

        member.PasswordHash.ShouldBeNull();
        member.Status.ShouldBe(PlatformStaffStatus.Invited);
        member.DomainEvents.OfType<PlatformStaffInvitedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void CompletingAnInvitation_ActivatesTheAccount()
    {
        PlatformStaffMember member = Invite();

        member.CompleteInvitation("hash-1", Now);

        member.Status.ShouldBe(PlatformStaffStatus.Active);
        member.PasswordHash.ShouldBe("hash-1");
    }

    [Fact]
    public void AnInvitationCannotBeCompletedTwice()
    {
        PlatformStaffMember member = Active();

        Should.Throw<DomainRuleException>(() => member.CompleteInvitation("hash-2", Now))
            .ErrorCode.ShouldBe("PLATFORM_STAFF_NOT_INVITED");
    }

    [Fact]
    public void RepeatedFailures_LockTheAccountForALimitedTime()
    {
        PlatformStaffMember member = Active();

        // The threshold is a fixed constant here, not a configurable policy - there is no tenant to
        // own one - so the test drives it past any reasonable number rather than reading it back.
        for (int attempt = 0; attempt < 10 && !member.IsLockedOut(Now); attempt++)
        {
            member.RecordFailedSignIn(Now);
        }

        member.IsLockedOut(Now).ShouldBeTrue();
        member.DomainEvents.OfType<PlatformStaffLockedOutDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ASuccessfulSignIn_ClearsAccumulatedFailures()
    {
        PlatformStaffMember member = Active();

        member.RecordFailedSignIn(Now);
        member.RecordFailedSignIn(Now);
        member.RecordSuccessfulSignIn(Now);

        member.AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public void APasswordShorterThanTheFixedMinimum_IsRejected()
    {
        // Fourteen, not the tenant default of twelve - a compromised platform account reaches every
        // tenant's commercial data, not one customer's.
        IReadOnlyList<string> failures = PlatformStaffMember.Validate("short-pass1", null, null);

        failures.ShouldNotBeEmpty();
    }

    [Fact]
    public void APasswordContainingTheAccountsOwnEmail_IsRejected()
    {
        IReadOnlyList<string> failures = PlatformStaffMember.Validate(
            "opssupport-team-secret",
            "Ops Support",
            "opssupport@gggshr.example");

        failures.ShouldNotBeEmpty();
    }

    [Fact]
    public void ALongEnoughUnrelatedPassword_IsAccepted()
    {
        IReadOnlyList<string> failures = PlatformStaffMember.Validate(
            "correct horse battery staple",
            "Ops Support",
            "opssupport@gggshr.example");

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void DisablingRotatesTheSecurityStamp_SoExistingSessionsEnd()
    {
        PlatformStaffMember member = Active();
        string before = member.SecurityStamp;

        member.Disable();

        member.SecurityStamp.ShouldNotBe(before);
        member.CanSignIn.ShouldBeFalse();
        member.DomainEvents.OfType<PlatformStaffDisabledDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ADisabledAccountsPasswordCannotBeChanged()
    {
        PlatformStaffMember member = Active();
        member.Disable();

        Should.Throw<DomainRuleException>(() => member.SetPassword("new-hash", Now))
            .ErrorCode.ShouldBe("PLATFORM_STAFF_DISABLED");
    }

    private static PlatformStaffMember Invite() => PlatformStaffMember.Invite("ops@gggshr.example", "Ops Support", Now);

    private static PlatformStaffMember Active()
    {
        PlatformStaffMember member = Invite();
        member.CompleteInvitation("initial-hash", Now);
        return member;
    }
}
