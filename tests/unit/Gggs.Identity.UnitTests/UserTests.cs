using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Identity.Domain.Users;
using Gggs.Identity.Domain.Users.Events;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// Account rules. Everything here decides whether someone can get in, or is kept out, so the rules
/// are pinned rather than left to whichever caller happens to invoke them.
/// </summary>
public sealed class UserTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnInvitedUser_HasNoPasswordAndCannotYetBeUsedNormally()
    {
        User user = InviteUser();

        // An administrator setting the first password would know it, and it would usually travel
        // to the user in plain text.
        user.PasswordHash.ShouldBeNull();
        user.Status.ShouldBe(UserStatus.Invited);
        user.DomainEvents.OfType<UserInvitedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void CompletingAnInvitation_ActivatesTheAccount()
    {
        User user = InviteUser();

        user.CompleteInvitation("hash-1", Now);

        user.Status.ShouldBe(UserStatus.Active);
        user.PasswordHash.ShouldBe("hash-1");
        user.PasswordChangedDate.ShouldBe(Now);
    }

    [Fact]
    public void AnInvitationCannotBeCompletedTwice()
    {
        User user = ActiveUser();

        Should.Throw<DomainRuleException>(() => user.CompleteInvitation("hash-2", Now))
            .ErrorCode.ShouldBe("USER_NOT_INVITED");
    }

    [Fact]
    public void RepeatedFailures_LockTheAccountForALimitedTime()
    {
        User user = ActiveUser();
        PasswordPolicy policy = PasswordPolicy.CreateDefault();

        for (int attempt = 0; attempt < policy.LockoutThreshold; attempt++)
        {
            user.RecordFailedSignIn(policy, Now);
        }

        user.Status.ShouldBe(UserStatus.Locked);
        user.IsLockedOut(Now).ShouldBeTrue();
        user.DomainEvents.OfType<UserLockedOutDomainEvent>().ShouldHaveSingleItem();

        // Timed, not permanent: a permanent lockout hands anyone who knows an email address a way
        // to lock that person out at will.
        user.IsLockedOut(Now.Add(policy.LockoutDuration).AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void ASuccessfulSignIn_ClearsAccumulatedFailures()
    {
        // Without this, occasional typos spread over months would eventually lock out someone who
        // has been signing in successfully the whole time.
        User user = ActiveUser();
        PasswordPolicy policy = PasswordPolicy.CreateDefault();

        user.RecordFailedSignIn(policy, Now);
        user.RecordFailedSignIn(policy, Now);
        user.RecordSuccessfulSignIn(Now);

        user.AccessFailedCount.ShouldBe(0);
        user.LastLoginDate.ShouldBe(Now);
    }

    [Fact]
    public void ChangingThePassword_EndsExistingSessions()
    {
        // Whoever prompted the change - the user, or an administrator after a compromise - expects
        // existing sessions to stop working.
        User user = ActiveUser();
        string before = user.SecurityStamp;

        user.SetPassword("hash-2", PasswordPolicy.CreateDefault(), Now);

        user.SecurityStamp.ShouldNotBe(before);
    }

    [Fact]
    public void ChangingThePassword_ClearsALockout()
    {
        // Someone who has just proved they can reset their password should not still be locked out
        // by the failures that led them to reset it.
        User user = ActiveUser();
        PasswordPolicy policy = PasswordPolicy.CreateDefault();

        for (int attempt = 0; attempt < policy.LockoutThreshold; attempt++)
        {
            user.RecordFailedSignIn(policy, Now);
        }

        user.SetPassword("hash-new", policy, Now);

        user.Status.ShouldBe(UserStatus.Active);
        user.AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public void PasswordHistoryIsKeptAndTrimmedToThePolicy()
    {
        User user = ActiveUser();
        PasswordPolicy policy = PasswordPolicy.CreateDefault();

        for (int index = 2; index <= 9; index++)
        {
            user.SetPassword($"hash-{index}", policy, Now.AddDays(index));
        }

        user.PasswordHistory.Count.ShouldBe(policy.HistoryCount);
        user.RecentPasswordHashes(policy.HistoryCount).ShouldContain("hash-9");
        user.RecentPasswordHashes(policy.HistoryCount).ShouldNotContain("hash-2");
    }

    [Fact]
    public void DisablingAUser_EndsTheirSessionsImmediately()
    {
        // A departing employee should not keep working until their token expires.
        User user = ActiveUser();
        string before = user.SecurityStamp;

        user.Disable();

        user.Status.ShouldBe(UserStatus.Disabled);
        user.CanSignIn.ShouldBeFalse();
        user.SecurityStamp.ShouldNotBe(before);
    }

    [Fact]
    public void ADisabledUserCannotSignIn()
    {
        User user = ActiveUser();
        user.Disable();

        Should.Throw<DomainRuleException>(() => user.RecordSuccessfulSignIn(Now))
            .ErrorCode.ShouldBe("USER_CANNOT_SIGN_IN");
    }

    [Fact]
    public void ReEnablingAUserWhoNeverSetAPassword_ReturnsThemToInvited()
    {
        // Otherwise they land on a sign-in page they have no credential for.
        User user = InviteUser();
        user.Disable();

        user.Enable();

        user.Status.ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public void ChangingRoles_BumpsThePermissionVersion()
    {
        // The token carries this and the permission cache is keyed by it, so a revoked role takes
        // effect on the next request rather than when the token expires.
        User user = ActiveUser();
        int before = user.PermissionVersion;

        user.BumpPermissionVersion();

        user.PermissionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public void EmailsAreNormalizedForLookupButPreservedForDisplay()
    {
        User user = User.Invite("  Priya.Sharma@Acme.COM ", "Priya Sharma", UserType.TenantUser, null, Now);

        // People expect their own capitalisation back; lookup must not depend on it.
        user.Email.ShouldBe("Priya.Sharma@Acme.COM");
        user.NormalizedEmail.ShouldBe("PRIYA.SHARMA@ACME.COM");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("   ")]
    public void AnInvalidEmailIsRejected(string email)
    {
        Should.Throw<Exception>(() => User.Invite(email, "Someone", UserType.TenantUser, null, Now));
    }

    [Fact]
    public void AFederatedLoginCanBeLinkedToAnExistingAccount()
    {
        // Present before federation ships, so a user gains a federated identity later without
        // losing their account, roles or history.
        User user = ActiveUser();

        user.LinkExternalLogin("EntraId", "external-object-id", "Acme Entra", Now);

        user.ExternalLogins.ShouldHaveSingleItem().Provider.ShouldBe("EntraId");
    }

    [Fact]
    public void LinkingTheSameFederatedLoginTwice_IsANoOp()
    {
        User user = ActiveUser();

        user.LinkExternalLogin("EntraId", "external-object-id", null, Now);
        user.LinkExternalLogin("EntraId", "external-object-id", null, Now);

        user.ExternalLogins.Count.ShouldBe(1);
    }

    [Fact]
    public void RemovingTheLastCredential_IsRefused()
    {
        // A federated user with no password, whose only login is unlinked, would be locked out of
        // an account that still looks active.
        User user = User.Invite("sso@acme.com", "SSO User", UserType.TenantUser, null, Now);
        user.LinkExternalLogin("EntraId", "object-id", null, Now);

        Should.Throw<DomainRuleException>(() => user.UnlinkExternalLogin("EntraId", "object-id"))
            .ErrorCode.ShouldBe("USER_WOULD_HAVE_NO_CREDENTIAL");
    }

    private static User InviteUser() => User.Invite(
        "priya@acme.com", "Priya Sharma", UserType.TenantUser, employeeId: null, Now);

    private static User ActiveUser()
    {
        User user = InviteUser();
        user.CompleteInvitation("hash-1", Now);
        return user;
    }
}
