using Shouldly;

namespace Gggs.Gateway.IntegrationTests;

/// <summary>
/// The headers that constrain the browser.
/// <para>
/// Asserted rather than assumed, because a missing header fails silently: the application works
/// perfectly without a content-security policy right up to the day it matters.
/// </para>
/// </summary>
[Collection(nameof(GatewayCollection))]
public sealed class SecurityHeaderTests(GatewayFactory factory)
{
    [Fact]
    public async Task EveryResponse_CarriesTheHardeningHeaders()
    {
        using HttpClient browser = factory.CreateBrowser();

        // An unauthenticated request: the headers must be there before anyone signs in, since the
        // sign-in page is the one that handles a password.
        HttpResponseMessage response = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);

        string policy = response.Headers.GetValues("Content-Security-Policy").Single();

        policy.ShouldContain("default-src 'self'");
        policy.ShouldContain("script-src 'self'");

        // The two that carry the most weight: no plugin embedding, and no other site may frame us.
        policy.ShouldContain("object-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");

        // 'unsafe-eval' would let an injected string become code, which is most of what a policy is
        // meant to prevent. Angular's production build never needs it.
        policy.ShouldNotContain("unsafe-eval");

        // Inline scripts stay forbidden even though inline styles are allowed.
        policy.ShouldNotContain("script-src 'self' 'unsafe-inline'");

        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Permissions-Policy").Single().ShouldContain("camera=()");
    }

    [Fact]
    public async Task TheShippedConfiguration_PinsTheBrowserToHttps()
    {
        using HttpClient browser = factory.CreateBrowser();

        HttpResponseMessage response = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);

        string hsts = response.Headers.GetValues("Strict-Transport-Security").Single();

        hsts.ShouldContain("max-age=31536000");
        hsts.ShouldContain("includeSubDomains");

        // The code default is off and appsettings.json turns it on, so a developer serving over plain
        // HTTP can switch it back off. A year of pinned HTTPS for a host is close to irreversible,
        // which is why it is a setting at all rather than always on.
    }
}
