using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Single sign-on through an organisation's OpenID Connect provider, against a fake provider: the
/// round trip as a browser makes it (no CSRF header, no automatic redirects), and every way a
/// returning browser can be turned away.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SsoTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A real browser navigating: cookies kept, redirects visible, no SPA headers.</summary>
    private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    private async Task<string> ProviderAsync(string? allowedDomains = null)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string key = $"idp-{Guid.NewGuid():N}"[..20];
        JsonElement created = await OkAsync(await admin.PostAsJsonAsync("/api/v1/admin/identity-providers", new
        {
            Key = key,
            DisplayName = "Test Directory",
            Authority = FakeOidcProvider.Authority,
            ClientId = FakeOidcProvider.ClientId,
            ClientSecret = "s3cret",
            AllowedDomains = allowedDomains,
            Enabled = true,
        }, Ct));
        created.GetProperty("hasClientSecret").GetBoolean().ShouldBeTrue();
        created.TryGetProperty("clientSecret", out _).ShouldBeFalse();
        return key;
    }

    private static async Task<Uri> StartAsync(HttpClient browser, string key, string returnUrl = "/projects")
    {
        HttpResponseMessage start = await browser.GetAsync($"/bff/sso/start?tenant={PeopleOsApiFactory.SeededTenantCode}&provider={key}&returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Uri location = start.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe($"{FakeOidcProvider.Authority}/authorize");
        location.Query.ShouldContain("code_challenge_method=S256");
        location.Query.ShouldContain($"client_id={FakeOidcProvider.ClientId}");
        return location;
    }

    private static string StateOf(Uri authorize) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorize.Query)["state"].ToString();

    /// <summary>The provider sends the browser back; returns where the landing page moves on to.</summary>
    private static async Task<string> ReturnAsync(HttpClient browser, string state, string code)
    {
        HttpResponseMessage callback = await browser.GetAsync($"/bff/sso/callback?state={Uri.EscapeDataString(state)}&code={code}", Ct);
        callback.StatusCode.ShouldBe(HttpStatusCode.OK);
        string html = await callback.Content.ReadAsStringAsync(Ct);
        int at = html.IndexOf("url=", StringComparison.Ordinal) + 4;
        return WebUtility.HtmlDecode(html[at..html.IndexOf('"', at)]);
    }

    [Fact]
    public async Task A_person_signs_in_through_their_organisations_provider_and_lands_where_they_were_going()
    {
        string key = await ProviderAsync("testco.example");
        string email = await factory.SeedUserAsync(Email("sso"), "a long enough passphrase");

        using HttpClient anonymous = factory.CreateRawClient();
        (await OkAsync(await anonymous.GetAsync($"/bff/sso/providers?tenant={PeopleOsApiFactory.SeededTenantCode}", Ct)))
            .EnumerateArray().ShouldContain(option => option.GetProperty("key").GetString() == key);
        (await OkAsync(await anonymous.GetAsync("/bff/sso/providers?tenant=no-such-customer", Ct))).GetArrayLength().ShouldBe(0);

        using HttpClient browser = Browser();
        Uri authorize = await StartAsync(browser, key);
        string code = factory.Oidc.Grant(authorize, $"sub-{Guid.NewGuid():N}", email.ToUpperInvariant());

        (await ReturnAsync(browser, StateOf(authorize), code)).ShouldBe("/projects");
        JsonElement me = await OkAsync(await browser.GetAsync("/bff/user", Ct));
        me.ToString().ShouldContain(email, Case.Insensitive);
    }

    [Fact]
    public async Task Once_linked_the_account_follows_the_providers_subject_not_the_address()
    {
        string key = await ProviderAsync();
        string email = await factory.SeedUserAsync(Email("sso"), "a long enough passphrase");
        string subject = $"sub-{Guid.NewGuid():N}";

        using (HttpClient browser = Browser())
        {
            Uri authorize = await StartAsync(browser, key);
            (await ReturnAsync(browser, StateOf(authorize), factory.Oidc.Grant(authorize, subject, email))).ShouldBe("/projects");
        }

        using HttpClient other = Browser();
        Uri again = await StartAsync(other, key);
        (await ReturnAsync(other, StateOf(again), factory.Oidc.Grant(again, "someone-else", email))).ShouldBe("/sign-in?ssoError=SSO_ACCOUNT_MISMATCH");
        (await other.GetAsync("/bff/user", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_unverified_or_out_of_domain_addresses_get_no_session()
    {
        string key = await ProviderAsync("testco.example");
        string email = await factory.SeedUserAsync(Email("sso"), "a long enough passphrase");

        foreach ((string address, bool verified) in new[] { (Email("nobody"), true), (email, false), ("someone@elsewhere.example", true) })
        {
            using HttpClient browser = Browser();
            Uri authorize = await StartAsync(browser, key);
            (await ReturnAsync(browser, StateOf(authorize), factory.Oidc.Grant(authorize, $"sub-{Guid.NewGuid():N}", address, verified))).ShouldBe("/sign-in?ssoError=SSO_NO_ACCOUNT");
            (await browser.GetAsync("/bff/user", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Forged_tokens_wrong_nonces_and_foreign_state_are_refused()
    {
        string key = await ProviderAsync();
        string email = await factory.SeedUserAsync(Email("sso"), "a long enough passphrase");

        using (HttpClient browser = Browser())
        {
            Uri authorize = await StartAsync(browser, key);
            (await ReturnAsync(browser, StateOf(authorize), factory.Oidc.Grant(authorize, "s1", email, nonceOverride: "replayed"))).ShouldBe("/sign-in?ssoError=SSO_TOKEN_INVALID");
        }

        using (HttpClient browser = Browser())
        {
            Uri authorize = await StartAsync(browser, key);
            factory.Oidc.SignWithUnknownKey = true;
            try
            {
                (await ReturnAsync(browser, StateOf(authorize), factory.Oidc.Grant(authorize, "s1", email))).ShouldBe("/sign-in?ssoError=SSO_TOKEN_INVALID");
            }
            finally
            {
                factory.Oidc.SignWithUnknownKey = false;
            }
        }

        // A code and state started in someone else's browser: this one has no matching state cookie.
        using HttpClient victim = Browser();
        using HttpClient attacker = Browser();
        Uri started = await StartAsync(attacker, key);
        (await ReturnAsync(victim, StateOf(started), factory.Oidc.Grant(started, "s1", email))).ShouldBe("/sign-in?ssoError=SSO_STATE_INVALID");
        (await victim.GetAsync("/bff/user", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_return_address_cannot_point_off_site_and_unknown_providers_go_back_to_sign_in()
    {
        string key = await ProviderAsync();
        string email = await factory.SeedUserAsync(Email("sso"), "a long enough passphrase");

        using HttpClient browser = Browser();
        Uri authorize = await StartAsync(browser, key, "//evil.example/steal");
        (await ReturnAsync(browser, StateOf(authorize), factory.Oidc.Grant(authorize, $"sub-{Guid.NewGuid():N}", email))).ShouldBe("/");

        HttpResponseMessage unknown = await browser.GetAsync($"/bff/sso/start?tenant={PeopleOsApiFactory.SeededTenantCode}&provider=nope", Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        unknown.Headers.Location!.ToString().ShouldBe("/sign-in?ssoError=SSO_PROVIDER_UNKNOWN");
    }

    [Fact]
    public async Task Only_administrators_manage_providers()
    {
        using HttpClient person = await factory.CreateScopedUserClientAsync(Email("sso"), null, (Gggs.BuildingBlocks.Security.Permissions.Self.ProfileView, Gggs.BuildingBlocks.Security.DataScope.Own));
        (await person.GetAsync("/api/v1/admin/identity-providers", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using HttpClient admin = await factory.CreateAdminClientAsync();
        string key = await ProviderAsync();
        JsonElement row = (await OkAsync(await admin.GetAsync("/api/v1/admin/identity-providers", Ct))).EnumerateArray().Single(provider => provider.GetProperty("key").GetString() == key);
        Guid id = row.GetProperty("id").GetGuid();
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/admin/identity-providers", new { Key = key, DisplayName = "Again", Authority = FakeOidcProvider.Authority, ClientId = "x", Enabled = true }, Ct))).ShouldBe("SSO_KEY_TAKEN");
        (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync($"/api/v1/admin/identity-providers/{id}", new { Key = key, DisplayName = "Plain", Authority = "http://idp.example", ClientId = "x", Enabled = true }, Ct))).ShouldBe("SSO_AUTHORITY_INVALID");

        JsonElement updated = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/admin/identity-providers/{id}", new { Key = key, DisplayName = "Renamed", Authority = FakeOidcProvider.Authority, ClientId = FakeOidcProvider.ClientId, Enabled = false }, Ct));
        updated.GetProperty("hasClientSecret").GetBoolean().ShouldBeTrue();
        using HttpClient anonymous = factory.CreateRawClient();
        (await OkAsync(await anonymous.GetAsync($"/bff/sso/providers?tenant={PeopleOsApiFactory.SeededTenantCode}", Ct)))
            .EnumerateArray().ShouldNotContain(option => option.GetProperty("key").GetString() == key);
        (await admin.DeleteAsync($"/api/v1/admin/identity-providers/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
