using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// A stand-in OpenID Connect provider behind the "sso" HTTP client: discovery, keys and a token
/// endpoint that checks PKCE and issues signed id_tokens for codes the test granted.
/// </summary>
public sealed class FakeOidcProvider : HttpMessageHandler
{
    public const string Authority = "https://idp.test";
    public const string ClientId = "peopleos-test";

    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly ConcurrentDictionary<string, IssuedCode> grants = new();

    /// <summary>Signs with a key the provider never published - for the forged-token case.</summary>
    public bool SignWithUnknownKey { get; set; }

    /// <summary>
    /// What the provider does once the person has signed in there: a code for the browser to bring back.
    /// The nonce and PKCE challenge come from the authorization request the app sent the browser to.
    /// </summary>
    public string Grant(Uri authorizationRequest, string subject, string email, bool emailVerified = true, string? nonceOverride = null, params string[] amr)
    {
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(authorizationRequest.Query);
        string code = Guid.NewGuid().ToString("N");
        grants[code] = new IssuedCode(query["code_challenge"].ToString(), nonceOverride ?? query["nonce"].ToString(), query["redirect_uri"].ToString(), subject, email, emailVerified, amr);
        return code;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/.well-known/openid-configuration")
        {
            return Json(new
            {
                issuer = Authority,
                authorization_endpoint = $"{Authority}/authorize",
                token_endpoint = $"{Authority}/token",
                jwks_uri = $"{Authority}/keys",
            });
        }

        if (path == "/keys")
        {
            JsonWebKey jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
            return Json(new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
        }

        if (path == "/token" && request.Method == HttpMethod.Post)
        {
            Dictionary<string, Microsoft.Extensions.Primitives.StringValues> form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (!grants.TryRemove(form["code"].ToString(), out IssuedCode? grant)
                || form["client_id"] != ClientId
                || form["redirect_uri"] != grant.RedirectUri
                || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString()))) != grant.Challenge)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
            }

            return Json(new { access_token = "unused", token_type = "Bearer", id_token = IdToken(grant) });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private string IdToken(IssuedCode grant)
    {
        List<Claim> claims =
        [
            new("sub", grant.Subject),
            new("email", grant.Email),
            new("email_verified", grant.EmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
            new("nonce", grant.Nonce),
            new("name", "Federated Person"),
        ];
        claims.AddRange(grant.Amr.Select(method => new Claim("amr", method)));
        SecurityKey signing = SignWithUnknownKey ? new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" } : key;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = ClientId,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signing, SecurityAlgorithms.RsaSha256),
        });
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private sealed record IssuedCode(string Challenge, string Nonce, string RedirectUri, string Subject, string Email, bool EmailVerified, string[] Amr);
}
