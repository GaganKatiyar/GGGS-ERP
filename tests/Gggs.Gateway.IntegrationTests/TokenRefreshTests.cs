using System.Net;
using System.Net.Http.Json;
using Gggs.Gateway.Bff;
using Gggs.Identity.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Gggs.Gateway.IntegrationTests;

/// <summary>
/// Keeping a session alive across token expiry.
/// <para>
/// Access tokens live ten minutes and cannot be revoked, which is the point of them being short. The
/// cost is that the gateway has to exchange a refresh token several times an hour, on a live browser
/// session, without the user noticing - and refresh tokens are one-time, so getting it wrong signs
/// people out rather than failing visibly.
/// </para>
/// <para>
/// These tests force every request to refresh by setting the margin wider than the token's lifetime.
/// Waiting ten real minutes would be the alternative, and a test nobody runs proves nothing.
/// </para>
/// </summary>
[Collection(nameof(GatewayCollection))]
public sealed class TokenRefreshTests(GatewayFactory factory)
{
    [Fact]
    public async Task ASessionSurvivesRepeatedRefreshes()
    {
        using WebApplicationFactory<GatewayHost> host = AlwaysRefreshing();
        using HttpClient browser = await SignedInBrowserAsync(host);

        // Three round trips, each one exchanging the refresh token for a new pair. The second would
        // fail if the gateway kept re-presenting the spent token, and the third if it failed to
        // persist the new one.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage response = await browser.GetAsync(
                "/api/v1/me/session",
                TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"request {attempt + 1} after a refresh");
        }
    }

    [Fact]
    public async Task ParallelRequests_DoNotSpendTheRefreshTokenTwice()
    {
        using WebApplicationFactory<GatewayHost> host = AlwaysRefreshing();
        using HttpClient browser = await SignedInBrowserAsync(host);

        // What a dashboard does on load. Without the single-flight lock these would each refresh with
        // the same one-time token; the second would be treated as a replay, the whole chain revoked,
        // and the user signed out by their own page loading.
        IEnumerable<Task<HttpResponseMessage>> calls = Enumerable.Range(0, 6)
            .Select(_ => browser.GetAsync("/api/v1/me/session", TestContext.Current.CancellationToken));

        HttpResponseMessage[] responses = await Task.WhenAll(calls);

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        // And the session is still usable afterwards, rather than having survived the burst only to
        // be left holding a token nothing will accept.
        HttpResponseMessage after = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A gateway whose refresh margin is wider than an access token's whole lifetime, so every
    /// request finds its token due for renewal.
    /// </summary>
    private WebApplicationFactory<GatewayHost> AlwaysRefreshing()
        => factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gggs:Bff:Session:RefreshMargin"] = "00:20:00",
            })));

    private static async Task<HttpClient> SignedInBrowserAsync(WebApplicationFactory<GatewayHost> host)
    {
        HttpClient browser = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            HandleCookies = true,
        });

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

        using HttpResponseMessage response = await browser.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The gateway refused the test sign-in: {response.StatusCode}.");
        }

        return browser;
    }
}
