using System.Security.Cryptography.X509Certificates;
using Gggs.BuildingBlocks.Application;
using Gggs.Identity.Api.Endpoints;
using Gggs.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gggs.Identity.Api.Infrastructure;

/// <summary>Token lifetimes and the clients allowed to ask for one.</summary>
public sealed class TokenOptions
{
    public const string SectionName = "Gggs:Identity:Tokens";

    /// <summary>
    /// Short by design. An access token cannot be revoked once issued, so its lifetime is the window
    /// in which a removed role or a disabled account still works. Ten minutes bounds that.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a session can be refreshed for. Matches the gateway's absolute session ceiling: the
    /// two are the same limit measured in two places, and the shorter of them wins.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// The public address this service issues tokens as, which becomes the <c>iss</c> claim.
    /// <para>
    /// Must be set wherever the service sits behind a gateway, and that is every real deployment.
    /// Without it the issuer is inferred from whatever host the request arrived on, so a token minted
    /// through the public address would fail to validate on a call that arrived over the internal
    /// one - the same token, rejected because it was asked for from a different direction.
    /// </para>
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>The gateway's client id.</summary>
    public string GatewayClientId { get; set; } = "gggs-bff";

    /// <summary>
    /// The gateway's client secret.
    /// <para>
    /// Required outside development. A confidential client with no secret is a public client, and a
    /// public client using the password grant means anyone who can reach the token endpoint can try
    /// credentials against it at their own pace.
    /// </para>
    /// </summary>
    public string? GatewayClientSecret { get; set; }

    /// <summary>
    /// Thumbprint of the signing certificate in the machine store, for the on-premise profile.
    /// Left empty in the cloud, where the certificate comes from Key Vault.
    /// </summary>
    public string? SigningCertificateThumbprint { get; set; }

    /// <summary>Encryption certificate thumbprint, used for refresh tokens.</summary>
    public string? EncryptionCertificateThumbprint { get; set; }
}

/// <summary>Wires the OIDC server (ADR-04) and registers the clients that may use it.</summary>
public static class OpenIddictSetup
{
    public static IServiceCollection AddGggsOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<TokenOptions>()
            .Bind(configuration.GetSection(TokenOptions.SectionName))
            .Validate(
                options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.GatewayClientSecret),
                "Gggs:Identity:Tokens:GatewayClientSecret is required outside development.")
            .Validate(
                options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.Issuer),
                "Gggs:Identity:Tokens:Issuer is required outside development.")
            .ValidateOnStart();

        TokenOptions tokens = configuration.GetSection(TokenOptions.SectionName).Get<TokenOptions>() ?? new TokenOptions();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<IdentityDbContext>())

            .AddServer(options =>
            {
                options.SetTokenEndpointUris("connect/token");

                // Revocation, so signing out actually ends the session upstream rather than leaving a
                // refresh token alive for its full twelve hours.
                options.SetRevocationEndpointUris("connect/revoke");

                // Discovery and the key set, which is how every other service learns what to trust.
                // Named explicitly rather than left to defaults, because these two addresses are a
                // published contract: changing them breaks every service that cached the metadata.
                options.SetConfigurationEndpointUris(".well-known/openid-configuration");
                options.SetJsonWebKeySetEndpointUris(".well-known/jwks");

                // Only what is used. Every enabled endpoint and grant is attack surface, and an
                // authorization-code flow nobody drives is a flow nobody notices is misconfigured.
                options.AllowPasswordFlow();
                options.AllowRefreshTokenFlow();

                options.RegisterScopes(TokenEndpoints.ApiScope, Scopes.OfflineAccess);

                // One-time refresh tokens. A refresh token presented twice means either a bug or a
                // stolen token, and OpenIddict's reuse detection revokes the whole chain - so a thief
                // gets one use and the real session ends, rather than both continuing side by side.
                //
                // The leeway is not zero, and that is a considered trade-off rather than laxness. A
                // browser loading a dashboard fires several requests at once; if two gateway replicas
                // refresh in the same instant, a zero leeway would revoke the chain and sign the user
                // out of their own page load. Thirty seconds tolerates that race while still catching a
                // token replayed minutes later, which is what theft actually looks like. The gateway
                // also serialises refreshes within a replica, so this covers only the cross-replica gap.
                options.SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(30));

                // Access tokens are plain JWTs so any service can validate them from the published
                // signing keys. Refresh tokens stay encrypted: they are long-lived bearer credentials
                // that only this service ever needs to read.
                options.DisableAccessTokenEncryption();

                ConfigureCertificates(options, tokens, environment);

                var aspNetCore = options.UseAspNetCore()
                    .EnableTokenEndpointPassthrough();

                if (environment.IsDevelopment())
                {
                    // Development only, same reasoning as the signing certificate and the gateway
                    // secret above: nobody runs TLS on their own machine, and OpenIddict refuses
                    // every request outright without this. Outside development the real requirement
                    // stands - HTTPS termination is the platform's job (Front Door/reverse proxy),
                    // never something a service opts out of on its own.
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })

            // Identity validates its own tokens, so its API accepts the same bearer token every other
            // service does rather than only its sign-in cookie.
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // Issuer and lifetimes are applied here rather than inside the builder above, because the
        // builder runs while the host is being composed and reads configuration as it stands at that
        // moment. Anything layered on afterwards - a test host, an on-premise override file, a
        // container's environment variables - would be invisible to it, and the symptom is the worst
        // kind: a service that quietly falls back to inferring its issuer and then rejects its own
        // tokens the first time it sits behind a gateway.
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<IOptions<TokenOptions>>((server, configured) =>
            {
                TokenOptions value = configured.Value;

                if (!string.IsNullOrWhiteSpace(value.Issuer))
                {
                    server.Issuer = new Uri(value.Issuer, UriKind.Absolute);
                }

                server.AccessTokenLifetime = value.AccessTokenLifetime;
                server.RefreshTokenLifetime = value.RefreshTokenLifetime;
            });

        // The same issuer on the validation side. UseLocalServer shares the signing keys but leaves
        // the issuer to be inferred from the incoming request, so without this a token minted through
        // the public address is rejected when it arrives on the internal one.
        services.AddOptions<OpenIddictValidationOptions>()
            .Configure<IOptions<TokenOptions>>((validation, configured) =>
            {
                if (!string.IsNullOrWhiteSpace(configured.Value.Issuer))
                {
                    validation.Issuer = new Uri(configured.Value.Issuer, UriKind.Absolute);
                }
            });

        services.AddHostedService<ClientRegistration>();

        return services;
    }

    private static void ConfigureCertificates(
        OpenIddictServerBuilder options,
        TokenOptions tokens,
        IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(tokens.SigningCertificateThumbprint))
        {
            options.AddSigningCertificate(FindCertificate(tokens.SigningCertificateThumbprint));

            if (!string.IsNullOrWhiteSpace(tokens.EncryptionCertificateThumbprint))
            {
                options.AddEncryptionCertificate(FindCertificate(tokens.EncryptionCertificateThumbprint));
            }

            return;
        }

        if (!environment.IsDevelopment())
        {
            // Refused rather than silently generating a key. A development certificate in production
            // would mean tokens that stop validating on every restart, and a signing key nobody
            // rotates, backs up or knows exists.
            throw new InvalidOperationException(
                "Gggs:Identity:Tokens:SigningCertificateThumbprint is required outside development.");
        }

        // Development only: regenerated per machine, and every restart invalidates outstanding tokens.
        options.AddDevelopmentSigningCertificate();
        options.AddDevelopmentEncryptionCertificate();
    }

    private static X509Certificate2 FindCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);

        X509Certificate2Collection found = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: false);

        return found.Count > 0
            ? found[0]
            : throw new InvalidOperationException(
                $"No certificate with thumbprint '{thumbprint}' was found in the local machine store.");
    }

    /// <summary>
    /// Registers the gateway as a client at startup.
    /// <para>
    /// In code rather than in a seed script because the client list is part of the trust model: which
    /// clients exist, and what each may ask for, should be reviewable in a diff.
    /// </para>
    /// </summary>
    private sealed class ClientRegistration(
        IServiceProvider serviceProvider,
        IOptions<TokenOptions> options,
        IHostEnvironment environment,
        ILogger<ClientRegistration> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

            IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogError(
                    "The identity database is unreachable, so the OAuth client was not registered. " +
                    "Nobody will be able to sign in until this host is restarted with the database available.");
                return;
            }

            // Migrations are a pre-deployment step, so the tables are normally already there. When
            // they are not - an on-premise upgrade that started the service before running them - this
            // logs loudly and lets the host start, because a crash loop is harder to diagnose from a
            // customer's server than a message saying exactly what is missing.
            IEnumerable<string> applied = await database.Database.GetAppliedMigrationsAsync(cancellationToken);

            if (!applied.Any(migration => migration.EndsWith("AddOpenIddictStores", StringComparison.Ordinal)))
            {
                logger.LogError(
                    "The OIDC tables are missing, so the OAuth client was not registered. Run the " +
                    "database migrations and restart this service; sign-in will fail until then.");
                return;
            }

            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            TokenOptions tokens = options.Value;

            object? existing = await manager.FindByClientIdAsync(tokens.GatewayClientId, cancellationToken);

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = tokens.GatewayClientId,
                ClientSecret = string.IsNullOrWhiteSpace(tokens.GatewayClientSecret)
                    ? (environment.IsDevelopment() ? DevelopmentSecrets.GatewayClientSecret : null)
                    : tokens.GatewayClientSecret,
                ClientType = ClientTypes.Confidential,
                DisplayName = "GGGS HRMS web gateway",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.Password,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.Scope + TokenEndpoints.ApiScope,
                    Permissions.Scopes.Profile,
                },
            };

            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancellationToken);
                logger.LogInformation("Registered the OIDC client {ClientId}.", tokens.GatewayClientId);
                return;
            }

            // Updated rather than left alone, so a permission removed from the descriptor above is
            // actually removed from a deployment that already has the client.
            await manager.UpdateAsync(existing, descriptor, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
