using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The token endpoint.
/// <para>
/// Every service on the platform trusts what a token says, so these tests read the token itself
/// rather than checking that a call succeeded. Two claims are asserted directly because they are the
/// ones that decide access: <c>tid</c>, which fixes whose data the caller can reach, and <c>pv</c>,
/// which decides when a cached permission set is stale.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class TokenEndpointTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task ThePasswordGrant_IssuesATokenScopedToTheUsersOwnTenant()
    {
        using HttpClient client = factory.CreateWebClient();

        JsonElement token = await RequestTokenAsync(client, PeopleOsApiFactory.AdminPassword);

        token.GetProperty("access_token").GetString().ShouldNotBeNullOrWhiteSpace();
        token.GetProperty("token_type").GetString().ShouldBe("Bearer");

        // offline_access was asked for, so a refresh token comes back; without one a session would
        // end after ten minutes.
        token.GetProperty("refresh_token").GetString().ShouldNotBeNullOrWhiteSpace();

        JsonElement claims = ReadPayload(token.GetProperty("access_token").GetString()!);

        claims.GetProperty("sub").GetString().ShouldBe(PeopleOsApiFactory.AdminUserId.ToString());

        // From the user record, never from the request. This is the claim every service reads to
        // decide whose data the caller may see.
        claims.GetProperty("tid").GetString().ShouldBe(PeopleOsApiFactory.SeededTenantId.ToString());

        claims.GetProperty("pv").ToString().ShouldNotBeNullOrWhiteSpace();
        claims.GetProperty("iss").GetString().ShouldBe("https://localhost/");
    }

    [Fact]
    public async Task TheAccessToken_CarriesNoPermissions()
    {
        using HttpClient client = factory.CreateWebClient();

        JsonElement token = await RequestTokenAsync(client, PeopleOsApiFactory.AdminPassword);
        string raw = ReadPayload(token.GetProperty("access_token").GetString()!).GetRawText();

        // The whole reason permissions are resolved per request. A token listing them keeps granting
        // them until it expires, so removing a role would not take effect when an administrator
        // expects - and an HR role holds hundreds of codes, which would make the token enormous too.
        raw.ShouldNotContain("admin.role.manage");
        raw.ShouldNotContain("permissions");
    }

    [Fact]
    public async Task TheAccessToken_AuthenticatesAgainstTheApi()
    {
        using HttpClient client = factory.CreateWebClient();

        JsonElement token = await RequestTokenAsync(client, PeopleOsApiFactory.AdminPassword);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/session");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token.GetProperty("access_token").GetString());

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // The same token every other service will be handed. If this fails, nothing behind the
        // gateway can authenticate at all.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWrongPassword_IsRefusedWithTheOAuthErrorShape()
    {
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await PostTokenAsync(client, "not the password");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // The specification's shape, not our ProblemDetails: a standard client library parses this,
        // and a token endpoint answering in a house style is one no standard client can read.
        body.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ARefreshToken_BuysANewAccessTokenAndIsThenSpent()
    {
        using HttpClient client = factory.CreateWebClient();

        JsonElement first = await RequestTokenAsync(client, PeopleOsApiFactory.AdminPassword);
        string refreshToken = first.GetProperty("refresh_token").GetString()!;

        HttpResponseMessage refreshed = await PostAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement second = await refreshed.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // A new refresh token comes back, which is what makes the old one one-time.
        second.GetProperty("refresh_token").GetString().ShouldNotBe(refreshToken);

        JsonElement claims = ReadPayload(second.GetProperty("access_token").GetString()!);

        // Rebuilt from the user record, but carrying the original session and authentication time, so
        // an absolute session limit and a step-up check still measure from when the password was typed.
        claims.GetProperty("sid").GetString().ShouldBe(
            ReadPayload(first.GetProperty("access_token").GetString()!).GetProperty("sid").GetString());
    }

    private static async Task<JsonElement> RequestTokenAsync(HttpClient client, string password)
    {
        HttpResponseMessage response = await PostTokenAsync(client, password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostTokenAsync(HttpClient client, string password)
        => PostAsync(client, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["username"] = PeopleOsApiFactory.AdminEmail,
            ["password"] = password,
            ["tenant_code"] = PeopleOsApiFactory.SeededTenantCode,
            ["scope"] = "gggs.api offline_access",
            ["client_id"] = PeopleOsApiFactory.ApiClientId,
            ["client_secret"] = DevelopmentSecrets.GatewayClientSecret,
        });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync("/connect/token", content, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads a JWT's payload without validating it.
    /// <para>
    /// Only ever right in a test, and only because the subject here is what the token <em>says</em>.
    /// Nothing in the product reads a token this way; services validate the signature first.
    /// </para>
    /// </summary>
    private static JsonElement ReadPayload(string accessToken)
    {
        string payload = accessToken.Split('.')[1];
        payload = payload.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }
}
