using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authentication;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Multi-factor sign-in end to end: enrol an authenticator, sign in with a code or a recovery code,
/// refuse replays and stale challenges, and re-authenticate (step up) before sensitive actions.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class MfaTests(PeopleOsApiFactory factory)
{
    private const string Password = "a scoped test passphrase";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CodeAt(string secret, int offset = 0) => Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + offset);

    private async Task<(HttpClient Client, string Email)> UserAsync()
    {
        string email = Email("mfa");
        HttpClient client = await factory.CreateScopedUserClientAsync(email, null, (Permissions.Self.ProfileView, DataScope.Own));
        return (client, email);
    }

    private static async Task<(string Secret, string[] RecoveryCodes)> EnrolAsync(HttpClient client)
    {
        JsonElement setup = await OkAsync(await client.PostAsync("/api/v1/me/security/mfa/setup", null, Ct));
        string secret = setup.GetProperty("secret").GetString()!;
        setup.GetProperty("provisioningUri").GetString()!.ShouldStartWith("otpauth://totp/");
        JsonElement confirmed = await OkAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/confirm", new { Code = CodeAt(secret) }, Ct));
        return (secret, [.. confirmed.GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!)]);
    }

    private async Task<JsonElement> LoginAsync(HttpClient browser, string email)
        => await OkAsync(await browser.PostAsJsonAsync("/bff/login", new { Email = email, Password, TenantCode = PeopleOsApiFactory.SeededTenantCode }, Ct));

    [Fact]
    public async Task Enrolling_needs_a_correct_code_and_hands_out_recovery_codes_once()
    {
        (HttpClient user, _) = await UserAsync();
        using HttpClient client = user;
        (await OkAsync(await client.GetAsync("/api/v1/me/security/mfa", Ct))).GetProperty("enabled").GetBoolean().ShouldBeFalse();
        (await CoreHrApi.ErrorCodeAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/confirm", new { Code = "123456" }, Ct))).ShouldBe("MFA_NO_ENROLMENT");

        JsonElement setup = await OkAsync(await client.PostAsync("/api/v1/me/security/mfa/setup", null, Ct));
        string secret = setup.GetProperty("secret").GetString()!;
        string wrong = CodeAt(secret) == "000000" ? "111111" : "000000";
        (await CoreHrApi.ErrorCodeAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/confirm", new { Code = wrong }, Ct))).ShouldBe(MfaService.InvalidCode);

        JsonElement confirmed = await OkAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/confirm", new { Code = CodeAt(secret) }, Ct));
        confirmed.GetProperty("recoveryCodes").GetArrayLength().ShouldBe(10);

        JsonElement status = await OkAsync(await client.GetAsync("/api/v1/me/security/mfa", Ct));
        status.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        status.GetProperty("recoveryCodesLeft").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task Signing_in_takes_a_second_step_that_refuses_wrong_and_replayed_codes()
    {
        (HttpClient user, string email) = await UserAsync();
        using HttpClient client = user;
        (string secret, _) = await EnrolAsync(client);

        using HttpClient browser = factory.CreateWebClient();
        JsonElement first = await LoginAsync(browser, email);
        first.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
        first.GetProperty("errorCode").GetString().ShouldBe(SignInErrorCodes.MfaRequired);
        string challenge = first.GetProperty("mfaChallenge").GetString()!;

        // Not signed in yet: the password alone opens nothing.
        (await browser.GetAsync("/api/v1/me/security/mfa", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The code that confirmed enrolment has been used; it cannot be replayed.
        JsonElement replay = await OkAsync(await browser.PostAsJsonAsync("/bff/login/mfa", new { Challenge = challenge, Code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) }, Ct));
        replay.GetProperty("succeeded").GetBoolean().ShouldBeFalse();

        JsonElement tampered = await OkAsync(await browser.PostAsJsonAsync("/bff/login/mfa", new { Challenge = challenge + "x", Code = CodeAt(secret, 1) }, Ct));
        tampered.GetProperty("errorCode").GetString().ShouldBe(MfaService.ChallengeExpired);

        JsonElement done = await OkAsync(await browser.PostAsJsonAsync("/bff/login/mfa", new { Challenge = challenge, Code = CodeAt(secret, 1) }, Ct));
        done.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        (await browser.GetAsync("/api/v1/me/security/mfa", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_only()
    {
        (HttpClient user, string email) = await UserAsync();
        using HttpClient client = user;
        (_, string[] recovery) = await EnrolAsync(client);

        using HttpClient browser = factory.CreateWebClient();
        string challenge = (await LoginAsync(browser, email)).GetProperty("mfaChallenge").GetString()!;
        (await OkAsync(await browser.PostAsJsonAsync("/bff/login/mfa", new { Challenge = challenge, Code = recovery[0].ToLowerInvariant() }, Ct)))
            .GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        (await OkAsync(await browser.GetAsync("/api/v1/me/security/mfa", Ct))).GetProperty("recoveryCodesLeft").GetInt32().ShouldBe(9);

        using HttpClient again = factory.CreateWebClient();
        string second = (await LoginAsync(again, email)).GetProperty("mfaChallenge").GetString()!;
        JsonElement reused = await OkAsync(await again.PostAsJsonAsync("/bff/login/mfa", new { Challenge = second, Code = recovery[0] }, Ct));
        reused.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
        reused.GetProperty("errorCode").GetString().ShouldBe(MfaService.InvalidCode);
    }

    [Fact]
    public async Task Step_up_takes_the_password_without_an_authenticator_and_a_code_with_one()
    {
        (HttpClient user, _) = await UserAsync();
        using HttpClient client = user;
        (await CoreHrApi.ErrorCodeAsync(await client.PostAsJsonAsync("/bff/step-up", new { Password = "not it" }, Ct))).ShouldBe(MfaService.InvalidCode);
        (await client.PostAsJsonAsync("/bff/step-up", new { Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (string secret, _) = await EnrolAsync(client);
        (await CoreHrApi.ErrorCodeAsync(await client.PostAsJsonAsync("/bff/step-up", new { Password }, Ct))).ShouldBe(MfaService.InvalidCode);
        (await client.PostAsJsonAsync("/bff/step-up", new { Code = CodeAt(secret, 1) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpClient anonymous = factory.CreateWebClient();
        (await anonymous.PostAsJsonAsync("/bff/step-up", new { Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Switching_off_needs_a_code_and_then_the_password_is_enough_again()
    {
        (HttpClient user, string email) = await UserAsync();
        using HttpClient client = user;
        (string secret, string[] recovery) = await EnrolAsync(client);

        JsonElement regenerated = await OkAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/recovery-codes", new { Code = CodeAt(secret, 1) }, Ct));
        regenerated.GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()).ShouldNotContain(recovery[0]);

        (await CoreHrApi.ErrorCodeAsync(await client.PostAsJsonAsync("/api/v1/me/security/mfa/disable", new { Code = recovery[0] }, Ct))).ShouldBe(MfaService.InvalidCode);
        string fresh = regenerated.GetProperty("recoveryCodes")[0].GetString()!;
        (await client.PostAsJsonAsync("/api/v1/me/security/mfa/disable", new { Code = fresh }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpClient browser = factory.CreateWebClient();
        (await LoginAsync(browser, email)).GetProperty("succeeded").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_administrator_without_an_authenticator_is_told_to_set_one_up()
    {
        using HttpClient browser = factory.CreateWebClient();
        JsonElement body = await OkAsync(await browser.PostAsJsonAsync("/bff/login", new { Email = PeopleOsApiFactory.AdminEmail, Password = PeopleOsApiFactory.AdminPassword, TenantCode = PeopleOsApiFactory.SeededTenantCode }, Ct));
        body.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        body.GetProperty("mfaEnrollmentRequired").GetBoolean().ShouldBeTrue();
    }
}
