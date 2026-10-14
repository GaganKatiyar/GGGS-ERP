using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Identity.Domain.Users;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// Password rules, including the defaults. The defaults matter as much as the validation: most
/// tenants will never change them, so whatever ships is what most customers get.
/// </summary>
public sealed class PasswordPolicyTests
{
    [Fact]
    public void TheDefaultsFollowCurrentGuidanceRatherThanOldHabit()
    {
        PasswordPolicy policy = PasswordPolicy.CreateDefault();

        // Length resists offline cracking; character classes mostly produce P@ssw0rd.
        policy.MinimumLength.ShouldBe(12);
        policy.RequireComplexity.ShouldBeFalse();

        // Forced rotation reliably produces Password1, Password2, Password3.
        policy.MaximumAgeDays.ShouldBe(0);

        // A breached password is compromised however long it is.
        policy.RejectBreachedPasswords.ShouldBeTrue();
    }

    [Fact]
    public void AShortPasswordIsRefused()
    {
        PasswordValidationResult result = PasswordPolicy.CreateDefault()
            .Validate("short", "Priya Sharma", "priya@acme.com");

        result.IsValid.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();
    }

    [Fact]
    public void ALongPassphraseIsAccepted_WithoutSymbols()
    {
        // The point of dropping the complexity requirement: this is a good password and the old
        // rules would have refused it.
        PasswordValidationResult result = PasswordPolicy.CreateDefault()
            .Validate("correct horse battery staple", "Priya Sharma", "priya@acme.com");

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void APasswordContainingTheUsersOwnName_IsRefused()
    {
        // Guessable by anyone with a staff directory, whatever its length.
        PasswordValidationResult result = PasswordPolicy.CreateDefault()
            .Validate("SharmaSharmaSharma", "Priya Sharma", "priya@acme.com");

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void APasswordContainingTheEmailLocalPart_IsRefused()
    {
        PasswordValidationResult result = PasswordPolicy.CreateDefault()
            .Validate("priya-is-my-password", "Priya Sharma", "priya@acme.com");

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EveryFailureIsReportedAtOnce()
    {
        // Otherwise a user fixes one problem, resubmits, and is told about the next.
        PasswordPolicy policy = PasswordPolicy.CreateDefault();
        policy.Update(
            minimumLength: 16,
            requireComplexity: true,
            rejectBreachedPasswords: true,
            historyCount: 5,
            maximumAgeDays: 0,
            lockoutThreshold: 5,
            lockoutDuration: TimeSpan.FromMinutes(15),
            idleTimeout: TimeSpan.FromMinutes(30),
            absoluteSessionLifetime: TimeSpan.FromHours(12),
            mfaRequirement: MfaRequirement.AdministratorsOnly);

        PasswordValidationResult result = policy.Validate("priya", "Priya Sharma", "priya@acme.com");

        result.Failures.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void ATenantCannotConfigureItsWayBelowTheFloor()
    {
        // A customer must not be able to choose passwords this platform would be embarrassed to
        // be storing.
        Should.Throw<DomainRuleException>(() => PasswordPolicy.CreateDefault().Update(
            minimumLength: 4,
            requireComplexity: false,
            rejectBreachedPasswords: false,
            historyCount: 0,
            maximumAgeDays: 0,
            lockoutThreshold: 5,
            lockoutDuration: TimeSpan.FromMinutes(15),
            idleTimeout: TimeSpan.FromMinutes(30),
            absoluteSessionLifetime: TimeSpan.FromHours(12),
            mfaRequirement: MfaRequirement.Off))
            .ErrorCode.ShouldBe("PASSWORD_POLICY_LENGTH_INVALID");
    }

    [Fact]
    public void ALockoutCannotBeEffectivelyPermanent()
    {
        // A lockout measured in days is indistinguishable from a permanent one and just generates
        // support tickets.
        Should.Throw<DomainRuleException>(() => PasswordPolicy.CreateDefault().Update(
            minimumLength: 12,
            requireComplexity: false,
            rejectBreachedPasswords: true,
            historyCount: 5,
            maximumAgeDays: 0,
            lockoutThreshold: 5,
            lockoutDuration: TimeSpan.FromDays(30),
            idleTimeout: TimeSpan.FromMinutes(30),
            absoluteSessionLifetime: TimeSpan.FromHours(12),
            mfaRequirement: MfaRequirement.Off))
            .ErrorCode.ShouldBe("PASSWORD_POLICY_LOCKOUT_DURATION_INVALID");
    }

    [Fact]
    public void ASessionCannotExpireOnIdleAfterItHasAlreadyExpiredAbsolutely()
    {
        Should.Throw<DomainRuleException>(() => PasswordPolicy.CreateDefault().Update(
            minimumLength: 12,
            requireComplexity: false,
            rejectBreachedPasswords: true,
            historyCount: 5,
            maximumAgeDays: 0,
            lockoutThreshold: 5,
            lockoutDuration: TimeSpan.FromMinutes(15),
            idleTimeout: TimeSpan.FromHours(24),
            absoluteSessionLifetime: TimeSpan.FromHours(12),
            mfaRequirement: MfaRequirement.Off))
            .ErrorCode.ShouldBe("PASSWORD_POLICY_SESSION_INVALID");
    }

    [Fact]
    public void PasswordsNeverExpireWhenRotationIsOff()
    {
        PasswordPolicy policy = PasswordPolicy.CreateDefault();
        DateTime changed = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        policy.IsExpired(changed, new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc)).ShouldBeFalse();
    }
}
