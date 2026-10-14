using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Keeping a browser session honest while it stays open.
/// <para>
/// The session holds the signed-in principal's claims, rebuilt from the account record every
/// revalidation interval - the monolith's replacement for the old gateway's ten-minute token
/// refresh. It has two jobs: a session in good standing must keep working through revalidation, and
/// one whose account can no longer sign in must stop.
/// </para>
/// <para>
/// These tests set the interval to zero so every request revalidates. Waiting five real minutes
/// would be the alternative, and a test nobody runs proves nothing.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SessionRevalidationTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task ASessionSurvivesRepeatedRevalidation()
    {
        using WebApplicationFactory<Program> host = AlwaysRevalidating();
        using HttpClient browser = await SignedInBrowserAsync(host, PeopleOsApiFactory.AdminEmail, PeopleOsApiFactory.AdminPassword);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage response = await browser.GetAsync("/api/v1/me/session", TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"request {attempt + 1} after a revalidation");
        }
    }

    [Fact]
    public async Task ParallelRequests_AllSucceedAndLeaveTheSessionUsable()
    {
        using WebApplicationFactory<Program> host = AlwaysRevalidating();
        using HttpClient browser = await SignedInBrowserAsync(host, PeopleOsApiFactory.AdminEmail, PeopleOsApiFactory.AdminPassword);

        // What a dashboard does on load: several requests at once, all due for revalidation.
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => browser.GetAsync("/api/v1/me/session", TestContext.Current.CancellationToken)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        HttpResponseMessage after = await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisablingTheAccount_EndsItsOpenSessionOnTheNextRequest()
    {
        const string email = "revalidation-victim@testco.example";
        const string password = "a passphrase for revalidation";
        await factory.SeedUserAsync(email, password);

        using WebApplicationFactory<Program> host = AlwaysRevalidating();
        using HttpClient browser = await SignedInBrowserAsync(host, email, password);

        (await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(PeopleOsApiFactory.SeededTenantId);
            AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            User user = await context.Set<User>().SingleAsync(
                candidate => candidate.NormalizedEmail == User.NormalizeEmailForLookup(email),
                TestContext.Current.CancellationToken);
            user.Disable();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The cookie still names a live session record, but the account behind it no longer
        // qualifies: revalidation ends the session rather than letting it run to its timeout.
        (await browser.GetAsync("/bff/user", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.GetAsync("/api/v1/me/session", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A host whose sessions revalidate on every request.</summary>
    private WebApplicationFactory<Program> AlwaysRevalidating()
        => factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gggs:Bff:Session:RevalidateInterval"] = "00:00:00",
            })));

    private static async Task<HttpClient> SignedInBrowserAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        HttpClient browser = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            HandleCookies = true,
        });

        browser.DefaultRequestHeaders.Add(PeopleOsApiFactory.CsrfHeader, "1");

        using HttpResponseMessage response = await browser.PostAsJsonAsync(
            "/bff/login",
            new { Email = email, Password = password, TenantCode = PeopleOsApiFactory.SeededTenantCode },
            TestContext.Current.CancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The test sign-in was refused: {response.StatusCode}.");
        }

        return browser;
    }
}
