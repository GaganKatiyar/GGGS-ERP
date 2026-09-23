using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Platform.IntegrationTests;

/// <summary>
/// The platform console over HTTP, with the real authorization pipeline in place.
/// <para>
/// Half of these assert that something is refused. An endpoint that works is easy to notice when
/// it breaks; an endpoint that quietly stopped refusing is not.
/// </para>
/// </summary>
[Collection(nameof(PlatformApiCollection))]
public sealed class TenantEndpointTests(PlatformApiFactory factory)
{
    private static Guid TenantId => PlatformApiFactory.SeededTenantId;

    [Fact]
    public async Task ListingTenants_WithoutCredentials_IsRefused()
    {
        using HttpClient client = factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/tenants",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListingTenants_WithoutThePlatformPermission_IsRefused()
    {
        // Platform staff, but holding only a tenant-administration permission. Being inside the
        // platform boundary is not the same as being allowed to list customers.
        using HttpClient client = factory.CreatePlatformClient(Permissions.Admin.UserManage);

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/tenants",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListingTenants_WithThePlatformPermission_Succeeds()
    {
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/tenants",
            TestContext.Current.CancellationToken);

        // Called with no query string at all, which is how a client calls a list endpoint the
        // first time. Binding PageRequest directly made "page" a required parameter and broke this.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProvisioningATenant_RequiresManageNotJustView()
    {
        // Reading the customer list and creating a customer are different privileges.
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/tenants",
            NewTenantBody("perm-check"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendingATenant_RequiresItsOwnPermission()
    {
        // Cutting a paying customer off is the most disruptive thing this console does, so holding
        // general management rights is deliberately not enough.
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{Guid.CreateVersion7()}/suspend",
            new { Reason = "Non-payment" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnUnknownTenantReturnsNotFound()
    {
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/tenants/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATenantWithAnInvalidCode_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantManage);

        // "api" would collide with our own routes and hostnames.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/tenants",
            NewTenantBody("api"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);

        // The client switches on errorCode, never on the message text.
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("TENANT_CODE_RESERVED");
    }

    [Fact]
    public async Task HealthProbesAnswerWithoutCredentials()
    {
        // A load balancer has no token. If this ever returns 401 again, every instance is marked
        // unhealthy and the service disappears from rotation.
        using HttpClient client = factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadingTenantSettings_RequiresTheAdministratorPermission()
    {
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/settings",
            TestContext.Current.CancellationToken);

        // A platform permission does not grant access to a tenant's own configuration either.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ASettingValueThatIsNotJson_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(TenantId, Permissions.Admin.SettingManage);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/v1/settings/leave/WorkingDays",
            new { ValueJson = "not json at all" },
            TestContext.Current.CancellationToken);

        // Caught on the way in, rather than breaking the screen that reads it weeks later.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    private static object NewTenantBody(string code) => new
    {
        Code = code,
        Name = "Test Customer",
        CountryCode = "IN",
        DataRegion = "centralindia",
        DefaultTimeZone = "Asia/Kolkata",
        DefaultCurrency = "INR",
        DefaultLocale = "en-IN",
        PlanCode = "PRO",
        AdministratorEmail = "admin@example.com",
    };
}
