using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Identity.IntegrationTests;
using Shouldly;

namespace Gggs.Gateway.IntegrationTests;

/// <summary>
/// Proxying, and what the gateway refuses to forward.
/// <para>
/// The proxy is the part that has to be boring. It adds a credential and passes the request on; if it
/// started making decisions about who may do what, those decisions would be bypassable by anything
/// that reached a service another way.
/// </para>
/// </summary>
[Collection(nameof(GatewayCollection))]
public sealed class BffProxyTests(GatewayFactory factory)
{
    [Fact]
    public async Task AProxiedCall_CarriesTheSessionCredentialWithoutTheBrowserHavingOne()
    {
        using HttpClient browser = await SignedInBrowserAsync();

        // The browser sends only its session cookie. If this returns 200, the gateway attached the
        // upstream credential on the way through - which is the entire proxy contract.
        HttpResponseMessage response = await browser.GetAsync(
            "/api/v1/me/session",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement session = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        session.GetProperty("email").GetString().ShouldBe(IdentityApiFactory.AdminEmail);
    }

    [Fact]
    public async Task AProxiedCall_WithoutASession_ReachesTheServiceAndIsRefusedThere()
    {
        using HttpClient browser = factory.CreateBrowser();

        HttpResponseMessage response = await browser.GetAsync(
            "/api/v1/me/session",
            TestContext.Current.CancellationToken);

        // 401 from the service, not from the gateway. The gateway does not pre-judge authentication:
        // the service that owns the data decides, so there is one answer rather than two.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASmuggledAuthorizationHeader_DoesNotReachTheService()
    {
        using HttpClient browser = await SignedInBrowserAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/session");
        request.Headers.Add("Authorization", "Bearer a.token.the-client-made-up");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        // Stripped on the way in, so the session credential is still what authenticated the call.
        // Had it been forwarded, the service would have tried to validate a token instead.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWriteWithoutTheCsrfHeader_IsRefusedBeforeItReachesAnything()
    {
        using HttpClient browser = await SignedInBrowserAsync();

        HttpResponseMessage response = await browser.PostAsJsonAsync(
            "/api/v1/users/invite",
            new { Email = "someone@testco.example", DisplayName = "Someone", RoleIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        problem.GetProperty("errorCode").GetString().ShouldBe("CSRF_HEADER_REQUIRED");
    }

    [Fact]
    public async Task AWriteWithTheCsrfHeader_IsForwardedAndAuthorizedByTheService()
    {
        using HttpClient browser = await SignedInBrowserAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/invite")
        {
            Content = JsonContent.Create(new
            {
                Email = $"proxied-{Guid.NewGuid():N}@testco.example",
                DisplayName = "Invited Through The Gateway",
                RoleIds = Array.Empty<Guid>(),
            }),
        };

        request.Headers.Add("X-CSRF", "1");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        // The administrator holds admin.user.invite, so the service allows it. The gateway checked
        // nothing about permissions and did not need to.
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AGetRequest_NeedsNoCsrfHeader()
    {
        using HttpClient browser = await SignedInBrowserAsync();

        HttpResponseMessage response = await browser.GetAsync(
            "/api/v1/users",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpClient> SignedInBrowserAsync()
    {
        HttpClient browser = factory.CreateBrowser();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                Email = IdentityApiFactory.AdminEmail,
                Password = IdentityApiFactory.AdminPassword,
                TenantCode = IdentityApiFactory.SeededTenantCode,
            }),
        };

        request.Headers.Add("X-CSRF", "1");

        HttpResponseMessage response = await request.SendThrough(browser);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The gateway refused the test sign-in: {response.StatusCode}.");
        }

        return browser;
    }
}

internal static class RequestExtensions
{
    /// <summary>Sends a request on a client, honouring the ambient test cancellation token.</summary>
    public static Task<HttpResponseMessage> SendThrough(this HttpRequestMessage request, HttpClient client)
        => client.SendAsync(request, TestContext.Current.CancellationToken);
}
