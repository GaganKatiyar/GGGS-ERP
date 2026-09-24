using System.Net;
using System.Net.Http.Json;
using Gggs.Identity.Domain.Users;
using Shouldly;

namespace Gggs.Identity.IntegrationTests;

/// <summary>
/// Sign-in end to end against a real database.
/// <para>
/// The interesting assertions are about what a caller is <em>not</em> told. A sign-in page that
/// distinguishes "no such user" from "wrong password" is a way to discover who works at a company,
/// and one that answers faster for unknown addresses is the same thing measured with a stopwatch.
/// </para>
/// </summary>
[Collection(nameof(IdentityApiCollection))]
public sealed class SignInTests(IdentityApiFactory factory)
{
    [Fact]
    public async Task CorrectCredentials_SignTheUserIn()
    {
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { Email = IdentityApiFactory.SeededUserEmail, Password = IdentityApiFactory.SeededPassword, TenantCode = IdentityApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        SignInBody? body = await response.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken);
        body!.Succeeded.ShouldBeTrue();

        // The session is a cookie, so nothing on the page can read the credential.
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).ShouldBeTrue();
        cookies!.ShouldContain(cookie => cookie.Contains("__Host-gggs-identity", StringComparison.Ordinal));
        cookies!.ShouldContain(cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AWrongPasswordIsRefused()
    {
        using HttpClient client = factory.CreateWebClient();

        SignInBody body = await SignInAsync(client, IdentityApiFactory.SeededUserEmail, "not-the-password");

        body.Succeeded.ShouldBeFalse();
        body.ErrorCode.ShouldBe("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task AnUnknownAddressIsRefusedIdenticallyToAWrongPassword()
    {
        // The whole point: an attacker must not be able to tell which of the two happened.
        using HttpClient client = factory.CreateWebClient();

        SignInBody unknown = await SignInAsync(client, "nobody@testco.example", "any-password");
        SignInBody wrongPassword = await SignInAsync(client, IdentityApiFactory.SeededUserEmail, "wrong");

        unknown.ErrorCode.ShouldBe(wrongPassword.ErrorCode);
        unknown.Succeeded.ShouldBe(wrongPassword.Succeeded);
    }

    [Fact]
    public async Task AnUnknownTenantIsRefusedIdenticallyToo()
    {
        // Otherwise the sign-in page reveals which companies are on the platform.
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { Email = IdentityApiFactory.SeededUserEmail, Password = IdentityApiFactory.SeededPassword, TenantCode = "no-such-customer" },
            TestContext.Current.CancellationToken);

        SignInBody? body = await response.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken);

        body!.Succeeded.ShouldBeFalse();
        body.ErrorCode.ShouldBe("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task TheRightPasswordInTheWrongTenantIsRefused()
    {
        // Users are tenant-owned. Knowing a valid credential for one customer must not open an
        // account at another, even when the address is the same.
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new
            {
                Email = IdentityApiFactory.SeededUserEmail,
                Password = IdentityApiFactory.SeededPassword,
                TenantCode = IdentityApiFactory.OtherTenantCode,
            },
            TestContext.Current.CancellationToken);

        SignInBody? body = await response.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken);

        body!.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task RepeatedWrongPasswords_LockTheAccountAndSayWhenItLifts()
    {
        using HttpClient client = factory.CreateWebClient();
        string email = await factory.SeedUserAsync("lockout-probe@testco.example", "Correct Password 123");

        SignInBody? last = null;
        for (int attempt = 0; attempt < PasswordPolicy.CreateDefault().LockoutThreshold; attempt++)
        {
            last = await SignInAsync(client, email, "wrong-password");
        }

        last!.ErrorCode.ShouldBe("ACCOUNT_LOCKED");

        // Telling the user when it lifts stops them retrying and extending it.
        last.LockoutEndDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task ALockedAccountRefusesEvenTheCorrectPassword()
    {
        using HttpClient client = factory.CreateWebClient();
        string email = await factory.SeedUserAsync("locked-probe@testco.example", "Correct Password 123");

        for (int attempt = 0; attempt < PasswordPolicy.CreateDefault().LockoutThreshold; attempt++)
        {
            await SignInAsync(client, email, "wrong-password");
        }

        SignInBody body = await SignInAsync(client, email, "Correct Password 123");

        body.Succeeded.ShouldBeFalse();
        body.ErrorCode.ShouldBe("ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task AnInvitedUserWhoHasNotSetAPassword_CannotSignIn()
    {
        using HttpClient client = factory.CreateWebClient();
        string email = await factory.SeedInvitedUserAsync("invited-probe@testco.example");

        SignInBody body = await SignInAsync(client, email, "anything-at-all");

        body.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task CurrentUserRequiresASession()
    {
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        // An API, so a status code rather than a redirect to a login page that does not exist here.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AfterSigningIn_TheSessionIdentifiesTheUserAndTheirTenant()
    {
        using HttpClient client = factory.CreateWebClient();

        await SignInAsync(client, IdentityApiFactory.SeededUserEmail, IdentityApiFactory.SeededPassword);

        CurrentUserBody? me = await client.GetFromJsonAsync<CurrentUserBody>(
            "/api/v1/auth/me",
            TestContext.Current.CancellationToken);

        me!.Email.ShouldBe(IdentityApiFactory.SeededUserEmail);
        me.TenantId.ShouldNotBeNullOrWhiteSpace();
        me.UserType.ShouldBe(nameof(UserType.TenantUser));
    }

    private async Task<SignInBody> SignInAsync(HttpClient client, string email, string password)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { Email = email, Password = password, TenantCode = IdentityApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<SignInBody>(TestContext.Current.CancellationToken))!;
    }

    private sealed record SignInBody(bool Succeeded, string? ErrorCode, bool MustChangePassword, DateTime? LockoutEndDate);

    private sealed record CurrentUserBody(string UserId, string DisplayName, string Email, string? TenantId, string? EmployeeId, string UserType);
}
