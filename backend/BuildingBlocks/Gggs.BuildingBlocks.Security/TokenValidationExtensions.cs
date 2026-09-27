using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Gggs.BuildingBlocks.Security;

/// <summary>How a service validates the tokens the Identity service issues.</summary>
public sealed class TokenValidationOptions
{
    public const string SectionName = "Gggs:Authentication";

    /// <summary>
    /// The Identity service's public address, used to fetch its signing keys.
    /// <para>
    /// Public, not internal: it has to match the <c>iss</c> claim in the tokens, and a token issued
    /// as <c>https://identity.example.com</c> will not validate against a key set fetched from
    /// <c>http://identity</c>.
    /// </para>
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// The audience this service accepts. Checked, so a token minted for something else that happens
    /// to trust the same signing key is refused here.
    /// </summary>
    public string Audience { get; set; } = "gggs.api";

    /// <summary>
    /// Allows the key set to be fetched over plain HTTP. Development only: on a real network it makes
    /// the key set - and therefore what this service will trust - substitutable by anyone in the path.
    /// </summary>
    public bool AllowHttpMetadata { get; set; }
}

/// <summary>
/// Bearer-token authentication for every service except Identity.
/// <para>
/// Standard JWT bearer rather than OpenIddict's own validation handler, deliberately: it keeps the
/// OIDC server's library out of eleven services that only need to check a signature, and it means a
/// future move to a different issuer is a configuration change here rather than a dependency change
/// everywhere.
/// </para>
/// </summary>
public static class TokenValidationExtensions
{
    public static IServiceCollection AddGggsTokenValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        TokenValidationOptions options =
            configuration.GetSection(TokenValidationOptions.SectionName).Get<TokenValidationOptions>()
            ?? new TokenValidationOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.Authority = options.Authority;
                bearer.Audience = options.Audience;
                bearer.RequireHttpsMetadata = !options.AllowHttpMetadata;

                // Claims are left exactly as issued. The default mapping renames sub, email and the
                // rest to long WS-* URIs, which would quietly break every place that reads 'tid' or
                // 'sub' by name - and those names are the contract UserPrincipalFactory publishes.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Thirty seconds, not the five-minute default. Access tokens live ten minutes, and
                    // five minutes of clock tolerance on a ten-minute token is half its lifetime spent
                    // accepting expired ones.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = "name",
                    RoleClaimType = "role",
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Tells the client to refresh rather than to sign in again, which is the
                        // difference between a smooth ten-minute token rotation and a user being
                        // bounced to the login page every ten minutes.
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers["X-Token-Expired"] = "true";
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Points this service's permission checks at the Identity service.
    /// <para>
    /// Every service except Identity needs this. Without it the permission handler falls back to
    /// reading claims from the token, and the token deliberately carries no permissions - so every
    /// check would quietly deny.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGggsRemotePermissions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient<IPermissionProvider, HttpPermissionProvider>(client =>
        {
            client.BaseAddress = new Uri(configuration["Gggs:Services:Identity"] ?? "http://identity");

            // Short: this is on the path of every authorized request, so a slow Identity should fail
            // fast and be retried rather than hold requests open across the whole platform.
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
