using Gggs.Gateway.Bff;
using Gggs.Identity.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Gggs.Gateway.IntegrationTests;

/// <summary>
/// Hosts the gateway in-process with a real Identity service behind it.
/// <para>
/// Both hosts are real, and the only thing substituted is the network between them: the gateway's
/// HTTP client and YARP's forwarder are pointed at Identity's in-memory test server. Everything that
/// matters is therefore genuine - the cookie Identity issues, the credential the gateway stores, the
/// header the proxy attaches - because those are exactly the joints a mocked Identity would hide.
/// </para>
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<GatewayHost>, IAsyncLifetime
{
    private readonly IdentityApiFactory _identity = new();

    /// <summary>The Identity fixture, so tests can reuse its seeded accounts.</summary>
    public IdentityApiFactory Identity => _identity;

    public async ValueTask InitializeAsync()
    {
        await _identity.InitializeAsync();

        // Forces the gateway host to build now rather than on first request, so a wiring mistake
        // fails here instead of inside an unrelated test.
        _ = Services;
    }

    /// <summary>
    /// A client that looks like a browser on an HTTPS origin.
    /// <para>
    /// HTTPS is not cosmetic: the session cookie carries the <c>__Host-</c> prefix and the
    /// <c>Secure</c> attribute, so an http client would silently discard it and every test would
    /// fail as though sign-in were broken. Cookies are handled by the test handler, as a browser
    /// would.
    /// </para>
    /// </summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
        HandleCookies = true,
    });

    /// <summary>A client that keeps no cookies, for asserting on the Set-Cookie header itself.</summary>
    public HttpClient CreateRawClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
        HandleCookies = false,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gggs:Platform:DeploymentProfile"] = "OnPremise",
                ["Gggs:Services:Identity"] = "https://localhost",

                // No Redis: sessions live in this process, which is what a single test host wants.
                ["ConnectionStrings:Redis"] = string.Empty,
            });
        });

        builder.ConfigureServices(services =>
        {
            // The gateway's own calls to Identity.
            services.AddHttpClient<Gggs.Gateway.Bff.Infrastructure.IdentityClient>(client =>
                {
                    client.BaseAddress = new Uri("https://localhost/");
                })
                .ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler());

            // And the calls YARP forwards, which go through a different pipeline entirely - missing
            // this is how a test can pass while every proxied request in production fails.
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new TestServerForwarderFactory(_identity));
        });

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _identity.Dispose();
        }
    }

    /// <summary>Hands YARP a client that reaches the in-process Identity host.</summary>
    private sealed class TestServerForwarderFactory(IdentityApiFactory identity) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(identity.Server.CreateHandler());
    }
}

[CollectionDefinition(nameof(GatewayCollection))]
public sealed class GatewayCollection : ICollectionFixture<GatewayFactory>;
