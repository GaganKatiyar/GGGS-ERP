using System.Text;
using Gggs.Modules.Identity.Application.Authentication;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// The authenticator-code algorithm against the RFC 6238 appendix B vectors (SHA-1, truncated to the
/// six digits every authenticator app shows), plus the replay and drift rules sign-in relies on.
/// </summary>
public sealed class TotpTests
{
    private static readonly string RfcSecret = Totp.ToBase32(Encoding.ASCII.GetBytes("12345678901234567890"));

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Codes_match_the_rfc_test_vectors(long unixSeconds, string expected)
        => Totp.Code(RfcSecret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).ShouldBe(expected);

    [Fact]
    public void Base32_round_trips_and_matches_the_reference_encoding()
    {
        RfcSecret.ShouldBe("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        byte[] random = [1, 2, 3, 250, 251, 252, 7];
        Totp.FromBase32(Totp.ToBase32(random)).ShouldBe(random);
        Totp.FromBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq").ShouldBe(Encoding.ASCII.GetBytes("12345678901234567890"));
    }

    [Fact]
    public void One_step_of_clock_drift_is_tolerated_but_no_more()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        long step = Totp.StepAt(now);

        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, 0).ShouldBe(step - 1);
        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 1), now, 0).ShouldBe(step + 1);
        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now, 0).ShouldBeNull();
        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 2), now, 0).ShouldBeNull();
    }

    [Fact]
    public void A_code_cannot_be_used_twice()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        long step = Totp.StepAt(now);
        string code = Totp.Code(RfcSecret, step);

        Totp.Verify(RfcSecret, code, now, step - 1).ShouldBe(step);
        Totp.Verify(RfcSecret, code, now, step).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public void Malformed_codes_are_refused(string? code)
        => Totp.Verify(RfcSecret, code, DateTimeOffset.UtcNow, 0).ShouldBeNull();

    [Fact]
    public void Spaces_in_a_typed_code_are_ignored()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        string code = Totp.Code(RfcSecret, Totp.StepAt(now));
        Totp.Verify(RfcSecret, $"{code[..3]} {code[3..]}", now, 0).ShouldNotBeNull();
    }

    [Fact]
    public void Recovery_codes_are_unique_readable_and_hashed_regardless_of_formatting()
    {
        IReadOnlyList<string> codes = Totp.NewRecoveryCodes();
        codes.Count.ShouldBe(10);
        codes.Distinct().Count().ShouldBe(10);
        codes.ShouldAllBe(code => code.Length == 9 && code[4] == '-');

        string code = codes[0];
        Totp.HashRecoveryCode(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)).ShouldBe(Totp.HashRecoveryCode(code));
        Totp.HashRecoveryCode(code).ShouldNotContain(code[..4]);
    }

    [Fact]
    public void The_provisioning_link_is_what_authenticator_apps_expect()
    {
        string uri = Totp.ProvisioningUri("PeopleOS", "a.b@acme.example", "ABC234");
        uri.ShouldStartWith("otpauth://totp/PeopleOS:a.b%40acme.example?secret=ABC234");
        uri.ShouldContain("issuer=PeopleOS");
        uri.ShouldContain("digits=6");
        uri.ShouldContain("period=30");
    }
}
