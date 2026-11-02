using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Platform.Endpoints;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary><c>/api/v1/about</c>: the build and health shown on Settings > Platform > About Nuts and Bolts.</summary>
[Collection(nameof(ApiCollection))]
public sealed class AboutEndpointTests(PeopleOsApiFactory factory)
{
    [Fact]
    public async Task About_WithoutCredentials_IsRefused()
    {
        using HttpClient client = factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/about", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task About_WithoutTheSettingsPermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Audit.LogView);

        HttpResponseMessage response = await client.GetAsync("/api/v1/about", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task About_ReportsTheBuildTheDatabaseAndEveryModule()
    {
        using HttpClient client = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Admin.SettingView);

        AboutResponse? about = await client.GetFromJsonAsync<AboutResponse>("/api/v1/about", TestContext.Current.CancellationToken);

        about.ShouldNotBeNull();
        about.Product.ShouldBe("Nuts and Bolts");
        about.Version.ShouldNotBeNullOrWhiteSpace();
        about.DatabaseReachable.ShouldBeTrue();
        about.Modules.ShouldContain("platform");
        about.Modules.ShouldContain("employee");
        about.Modules.Count.ShouldBe(Gggs.Composition.ModuleCatalog.All.Count);
    }
}
