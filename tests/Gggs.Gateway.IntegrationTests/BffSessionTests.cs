using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Identity.IntegrationTests;
using Shouldly;

namespace Gggs.Gateway.IntegrationTests;

/// <summary>
/// Signing in through the gateway.
/// <para>
/// The claim these tests exist to check is the one the whole design rests on: after a successful
/// sign-in the browser holds a session cookie and nothing else - no token, no upstream cookie, no
/// credential anywhere a script could read it. That is asserted on the wire rather than reasoned
/// about, because it is the kind of property a refactor breaks quietly.
/// </para>
/// </summary>
[Collection(nameof(GatewayCollection))]
public sealed class BffSessionTests(GatewayFactory factory)
{
    private const string SessionCookieName = "__Host-gggs-session";

    [Fact]
    public async Task User_WithoutASession_IsUnauthorizedRatherThanAnError()
    {
        using HttpClient browser = factory.CreateBrowser();

        HttpResponseMessage response = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);

        // The SPA calls this on every load to find out whether anyone is signed in, so 401 is the
        // normal answer for a visitor, not a fault.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_SetsAHardenedSessionCookieAndLeavesNoCredentialInTheResponse()
    {
        using HttpClient client = factory.CreateRawClient();

        HttpResponseMessage response = await LoginAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Identity's own cookie is the credential the gateway now holds. If its name or the password
        // that was posted ever appears in a response body, it has been handed back to the browser.
        body.ShouldNotContain("__Host-gggs-identity");
        body.ShouldNotContain(IdentityApiFactory.AdminPassword);

        string cookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionCookieName, StringComparison.Ordinal));

        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/", Case.Insensitive);

        // A __Host- cookie may not carry a domain, and an expiry would outlive the server-side
        // record the key points at.
        cookie.ShouldNotContain("domain=", Case.Insensitive);
        cookie.ShouldNotContain("expires=", Case.Insensitive);

        // Only one cookie reaches the browser: the upstream Set-Cookie is dropped, not relayed.
        response.Headers.GetValues("Set-Cookie").Count().ShouldBe(1);
    }

    [Fact]
    public async Task Login_ThenUser_ReturnsTheSessionWithPermissionsAndNoToken()
    {
        using HttpClient browser = factory.CreateBrowser();

        await LoginAsync(browser);

        HttpResponseMessage response = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement session = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        session.GetProperty("email").GetString().ShouldBe(IdentityApiFactory.AdminEmail);
        session.GetProperty("tenant").GetProperty("code").GetString().ShouldBe(IdentityApiFactory.SeededTenantCode);

        // Permissions come from real role grants in the database, which is what the shell filters
        // its navigation with.
        session.GetProperty("permissions").EnumerateArray().ShouldNotBeEmpty();

        string raw = session.GetRawText();
        raw.ShouldNotContain("credential", Case.Insensitive);
        raw.ShouldNotContain("__Host-gggs-identity");
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_SaysNothingAboutWhyAndSetsNoCookie()
    {
        using HttpClient client = factory.CreateRawClient();

        HttpResponseMessage response = await PostLoginAsync(
            client,
            IdentityApiFactory.AdminEmail,
            "not the password",
            IdentityApiFactory.SeededTenantCode);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        body.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
        body.GetProperty("errorCode").GetString().ShouldBe("INVALID_CREDENTIALS");

        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).ShouldBeFalse();
        cookies.ShouldBeNull();
    }

    [Fact]
    public async Task Login_ForAnUnknownTenant_LooksExactlyLikeAWrongPassword()
    {
        using HttpClient client = factory.CreateRawClient();

        HttpResponseMessage response = await PostLoginAsync(
            client,
            IdentityApiFactory.AdminEmail,
            IdentityApiFactory.AdminPassword,
            "no-such-customer");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Distinguishable answers here would let anyone enumerate which companies are on the
        // platform, and which are suspended.
        body.GetProperty("errorCode").GetString().ShouldBe("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Logout_EndsTheSessionSoTheSameCookieCannotBeReplayed()
    {
        using HttpClient raw = factory.CreateRawClient();

        HttpResponseMessage login = await LoginAsync(raw);

        string sessionCookie = login.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionCookieName, StringComparison.Ordinal))
            .Split(';')[0];

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logout.Headers.Add("Cookie", sessionCookie);
        logout.Headers.Add("X-CSRF", "1");

        HttpResponseMessage logoutResponse = await raw.SendAsync(logout, TestContext.Current.CancellationToken);
        logoutResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Replayed afterwards. The record is gone from the store, so holding the cookie is not
        // enough - which is the difference between revoking a session and asking a browser to forget.
        using var replay = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        replay.Headers.Add("Cookie", sessionCookie);

        HttpResponseMessage replayed = await raw.SendAsync(replay, TestContext.Current.CancellationToken);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASessionKey_PresentedFromADifferentBrowser_IsRefused()
    {
        using HttpClient raw = factory.CreateRawClient();

        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                Email = IdentityApiFactory.AdminEmail,
                Password = IdentityApiFactory.AdminPassword,
                TenantCode = IdentityApiFactory.SeededTenantCode,
            }),
        };

        login.Headers.Add("X-CSRF", "1");
        login.Headers.UserAgent.ParseAdd("Mozilla/5.0 (the browser that signed in)");

        HttpResponseMessage response = await raw.SendAsync(login, TestContext.Current.CancellationToken);

        string sessionCookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionCookieName, StringComparison.Ordinal))
            .Split(';')[0];

        using var stolen = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        stolen.Headers.Add("Cookie", sessionCookie);
        stolen.Headers.UserAgent.ParseAdd("Mozilla/5.0 (somewhere else entirely)");

        HttpResponseMessage replayed = await raw.SendAsync(stolen, TestContext.Current.CancellationToken);

        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signs in as the Identity fixture's administrator.</summary>
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) => PostLoginAsync(
        client,
        IdentityApiFactory.AdminEmail,
        IdentityApiFactory.AdminPassword,
        IdentityApiFactory.SeededTenantCode);

    /// <summary>
    /// Posts to the login endpoint with the CSRF header the SPA sends.
    /// <para>
    /// Always through here rather than <c>PostAsJsonAsync</c>: a test that forgets the header is
    /// refused by the guard and fails looking like a credential problem.
    /// </para>
    /// </summary>
    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        string tenantCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { Email = email, Password = password, TenantCode = tenantCode }),
        };

        request.Headers.Add("X-CSRF", "1");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
