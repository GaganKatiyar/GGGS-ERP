using System.Net;
using System.Net.Http.Json;
using Gggs.Data;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Platform.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>Recent links: the menu pages each person opened lately, for their own home screen.</summary>
[Collection(nameof(ApiCollection))]
public sealed class RecentPagesTests(PeopleOsApiFactory factory)
{
    private const string Path = "/api/v1/me/recent-pages";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task VisitAsync(HttpClient client, string route)
        => (await client.PostAsJsonAsync(Path, new { Route = route }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private static async Task<string[]> ListAsync(HttpClient client)
        => (await client.GetFromJsonAsync<string[]>(Path, Ct))!;

    private async Task<(HttpClient First, HttpClient Second)> TwoPeopleAsync()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        return (await PersonAsync(factory, org.Manager), await PersonAsync(factory, org.Requester));
    }

    [Fact]
    public async Task SomeoneNew_HasNoRecentPages()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            (await ListAsync(first)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task PagesComeBackNewestFirst_AndARepeatVisitMovesToTheTopWithoutAddingARow()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            await VisitAsync(first, "/payroll/runs");
            await VisitAsync(first, "/calendar");
            await VisitAsync(first, "/me/leave");
            (await ListAsync(first)).ShouldBe(["/me/leave", "/calendar", "/payroll/runs"]);

            await VisitAsync(first, "/payroll/runs");
            (await ListAsync(first)).ShouldBe(["/payroll/runs", "/me/leave", "/calendar"]);
        }
    }

    [Fact]
    public async Task OnlyTheNewestAreKept_AndOlderOnesAreRemoved()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            for (int page = 1; page <= RecentPage.KeptPerUser + 3; page++)
            {
                await VisitAsync(first, $"/page-{page}");
            }

            string[] routes = await ListAsync(first);
            routes.Length.ShouldBe(RecentPage.KeptPerUser);
            routes[0].ShouldBe($"/page-{RecentPage.KeptPerUser + 3}");
            routes.ShouldNotContain("/page-3");
            routes.ShouldContain("/page-4");

            // Removed from the table, not just left out of the answer.
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
            {
                List<string> kept = await services.GetRequiredService<AppDbContext>().Set<RecentPage>()
                    .Where(page => page.Route.StartsWith("/page-"))
                    .Select(page => page.Route)
                    .ToListAsync(Ct);
                kept.ShouldNotContain("/page-1");
                kept.ShouldNotContain("/page-3");
            });
        }
    }

    [Fact]
    public async Task OnePersonNeverSeesAnothersPages()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            await VisitAsync(first, "/payroll/salaries");
            await VisitAsync(second, "/me/profile");

            (await ListAsync(first)).ShouldBe(["/payroll/salaries"]);
            (await ListAsync(second)).ShouldBe(["/me/profile"]);
        }
    }

    [Fact]
    public async Task AnotherOrganisationNeverSeesThem_EvenForTheSameUserId()
    {
        // These clients are the same test user in two tenants, so only the tenant keeps them apart.
        using HttpClient here = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId);
        using HttpClient there = factory.CreateClientFor(PeopleOsApiFactory.OtherTenantId);

        await VisitAsync(here, "/only-in-this-organisation");

        (await ListAsync(here)).ShouldContain("/only-in-this-organisation");
        (await ListAsync(there)).ShouldNotContain("/only-in-this-organisation");
    }

    [Theory]
    [InlineData("")]
    [InlineData("payroll/runs")]
    [InlineData("/payroll/runs?employee=42")]
    [InlineData("/payroll/runs#top")]
    [InlineData("/payroll runs")]
    [InlineData("https://example.com/")]
    [InlineData("/<script>")]
    public async Task AnythingButAPlainPath_IsRefusedAndNotSaved(string route)
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            HttpResponseMessage response = await first.PostAsJsonAsync(Path, new { Route = route }, Ct);

            response.IsSuccessStatusCode.ShouldBeFalse();
            ((int)response.StatusCode).ShouldBeLessThan(500);
            (await ListAsync(first)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task ATooLongAddress_IsRefused()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            string route = "/" + new string('a', RecentPage.MaxRouteLength);
            ((int)(await first.PostAsJsonAsync(Path, new { Route = route }, Ct)).StatusCode).ShouldBeInRange(400, 499);
        }
    }

    [Fact]
    public async Task SignedOut_GetsNothing()
    {
        using HttpClient anonymous = factory.CreateRawClient();

        (await anonymous.GetAsync(Path, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Path, new { Route = "/calendar" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OpeningPages_LeavesNothingInTheAuditLog()
    {
        (HttpClient first, HttpClient second) = await TwoPeopleAsync();
        using (first)
        using (second)
        {
            await VisitAsync(first, "/calendar");
            await VisitAsync(first, "/me/leave");
            await VisitAsync(first, "/calendar");
            await factory.DeliverEventsAsync();

            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
            {
                bool audited = await services.GetRequiredService<AppDbContext>().Set<AuditLog>()
                    .AnyAsync(entry => entry.EntityType == nameof(RecentPage), Ct);
                audited.ShouldBeFalse();
            });
        }
    }
}
