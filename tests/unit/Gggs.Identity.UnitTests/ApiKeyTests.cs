using Gggs.Modules.Identity.Domain.ApiKeys;
using Shouldly;

namespace Gggs.Identity.UnitTests;

public sealed class ApiKeyTests
{
    [Fact]
    public void AGeneratedKey_ParsesBackIntoItsParts()
    {
        (string key, string prefix, string secret) = ApiKeyFormat.Generate("Acme");
        key.ShouldStartWith("pk_acme_");
        ApiKeyFormat.Parse(key).ShouldBe(new ApiKeyParts("acme", prefix, secret));
    }

    [Fact]
    public void AnOrganisationCodeWithUnderscores_StillParses()
    {
        (string key, string prefix, _) = ApiKeyFormat.Generate("north_star");
        ApiKeyParts parts = ApiKeyFormat.Parse(key)!;
        parts.TenantCode.ShouldBe("north_star");
        parts.Prefix.ShouldBe(prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("pk_acme_short_secret")]
    [InlineData("sk_acme_abcdefgh_0123456789012345678901234567890123456789012345678901234567890123")]
    public void AnythingElse_IsNotAKey(string? value) => ApiKeyFormat.Parse(value).ShouldBeNull();

    [Fact]
    public void TheSecret_IsCheckedAgainstItsHash_AndRevokedOrExpiredKeysStopWorking()
    {
        (_, string prefix, string secret) = ApiKeyFormat.Generate("acme");
        var now = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        ApiKey key = ApiKey.Create("Payroll export", prefix, ApiKeyFormat.Hash(secret), Guid.NewGuid(), new DateOnly(2026, 12, 31), null);

        key.Matches(secret).ShouldBeTrue();
        key.Matches(secret[..^1] + "0").ShouldBe(secret.EndsWith('0'));
        key.IsUsableOn(now).ShouldBeTrue();
        key.IsUsableOn(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ShouldBeFalse("it expired on 31 December");

        key.Revoke(now);
        key.IsUsableOn(now).ShouldBeFalse();
    }

    [Fact]
    public void Use_IsRecordedAtMostOnceAMinute()
    {
        ApiKey key = ApiKey.Create("Sync", "abcdefgh", ApiKeyFormat.Hash("x"), Guid.NewGuid(), null, null);
        var now = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        key.Touch(now).ShouldBeTrue();
        key.Touch(now.AddSeconds(30)).ShouldBeFalse();
        key.Touch(now.AddSeconds(61)).ShouldBeTrue();
    }
}
