using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Platform.Application.Usage;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The platform console's organisation endpoints, served by their own host (Gggs.PlatformApi) against
/// the HR application's database, with the real authorization pipeline in place.
/// <para>
/// Half of these assert that something is refused. An endpoint that works is easy to notice when it
/// breaks; an endpoint that quietly stopped refusing is not.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlatformConsoleTests(PeopleOsApiFactory factory)
{
    private PlatformConsoleFactory Console => factory.PlatformConsole;

    [Fact]
    public async Task Organisations_WithoutCredentials_AreRefused()
    {
        using HttpClient client = Console.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/platform/organisations", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Organisations_WithoutThePlatformPermission_AreRefused()
    {
        // Platform staff, but holding only a tenant-administration permission.
        using HttpClient client = Console.CreatePlatformClient(Permissions.Admin.UserManage);

        HttpResponseMessage response = await client.GetAsync("/api/v1/platform/organisations", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organisations_WithThePlatformPermission_ListTheHrApplicationsTenants()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync("/api/v1/platform/organisations", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement rows = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        rows.EnumerateArray().Select(row => row.GetProperty("code").GetString()).ShouldContain(PeopleOsApiFactory.SeededTenantCode);
    }

    [Fact]
    public async Task TheHrApi_NoLongerServesTheConsoleEndpoints()
    {
        using HttpClient client = factory.CreatePlatformClient(
            Permissions.Platform.TenantView, Permissions.Platform.TenantManage, Permissions.Platform.StaffView);

        foreach (string path in new[] { "/api/v1/tenants", "/api/v1/platform/organisations", "/api/v1/platform/staff", "/api/v1/platform-staff" })
        {
            HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }

    [Fact]
    public async Task TheConsole_ServesNoHrEndpoints()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync("/api/v1/employees", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingAnOrganisation_RequiresManageNotJustView()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/platform/organisations", NewOrganisation("perm-check"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendingAnOrganisation_RequiresItsOwnPermission()
    {
        // Cutting a paying customer off is the most disruptive thing the console does.
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/platform/organisations/{Guid.CreateVersion7()}/suspend",
            new { Reason = "Non-payment" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnUnknownOrganisation_IsNotFound()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/platform/organisations/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnOrganisationWithAReservedCode_IsRejectedWithItsErrorCode()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/platform/organisations", NewOrganisation("api"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("TENANT_CODE_RESERVED");
    }

    [Fact]
    public async Task HealthProbesAnswerWithoutCredentials()
    {
        using HttpClient client = Console.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadingTenantSettings_StillRequiresTheAdministratorPermission()
    {
        using HttpClient client = factory.CreatePlatformClient(Permissions.Platform.TenantView);

        HttpResponseMessage response = await client.GetAsync("/api/v1/settings", TestContext.Current.CancellationToken);

        // A platform permission does not grant access to a tenant's own configuration.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ASettingValueThatIsNotJson_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(PeopleOsApiFactory.SeededTenantId, Permissions.Admin.SettingManage);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/v1/settings/leave/WorkingDays", new { ValueJson = "not json at all" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task CreatingAnOrganisation_ActivatesIt_RecordsTheChange_AndInvitesItsAdministrator()
    {
        string code = $"co-{Guid.NewGuid():N}"[..20];
        using HttpClient client = Console.CreatePlatformClient(
            Permissions.Platform.TenantView, Permissions.Platform.TenantManage, Permissions.Platform.SubscriptionManage, Permissions.Platform.FeatureManage);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/platform/organisations", NewOrganisation(code), TestContext.Current.CancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement organisation = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Guid id = organisation.GetProperty("id").GetGuid();
        organisation.GetProperty("status").GetString().ShouldBe("Active");

        // Every console change is in the platform audit trail, against the organisation.
        await using (AppDbContext database = factory.CreateContext())
        {
            (await database.PlatformAuditEntries.CountAsync(entry => entry.TenantId == id, TestContext.Current.CancellationToken)).ShouldBeGreaterThan(0);
        }

        // The worker seeds the new tenant from the provisioning event: roles, and an administrator
        // whose invitation is emailed rather than handed to platform staff.
        await factory.DeliverEventsAsync();

        JsonElement administrators = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform/organisations/{id}/administrators", TestContext.Current.CancellationToken);
        JsonElement administrator = administrators.EnumerateArray().Single();
        administrator.GetProperty("email").GetString().ShouldBe($"owner@{code}.example");
        administrator.GetProperty("status").GetString().ShouldBe("Invited");
        administrator.GetProperty("invitationExpiresDate").ValueKind.ShouldBe(JsonValueKind.String);

        // Re-sending works while nobody has used the account, and never returns the token.
        HttpResponseMessage resent = await client.PostAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/administrators/resend",
            new { Email = $"owner@{code}.example" },
            TestContext.Current.CancellationToken);
        resent.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("token", Case.Insensitive);

        // A second administrator, invited from the console, lands in that organisation only.
        HttpResponseMessage added = await client.PostAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/administrators",
            new { Email = $"deputy@{code}.example", DisplayName = "Deputy" },
            TestContext.Current.CancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement afterAdding = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform/organisations/{id}/administrators", TestContext.Current.CancellationToken);
        afterAdding.GetArrayLength().ShouldBe(2);

        JsonElement seededTenantAdministrators = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform/organisations/{PeopleOsApiFactory.SeededTenantId}/administrators", TestContext.Current.CancellationToken);
        seededTenantAdministrators.EnumerateArray()
            .Select(row => row.GetProperty("email").GetString())
            .ShouldNotContain($"deputy@{code}.example");
    }

    [Fact]
    public async Task ChangingModulesAndTheSubscription_IsReflectedInTheOrganisation()
    {
        string code = $"mod-{Guid.NewGuid():N}"[..20];
        using HttpClient client = Console.CreatePlatformClient(
            Permissions.Platform.TenantView, Permissions.Platform.TenantManage, Permissions.Platform.SubscriptionManage, Permissions.Platform.FeatureManage);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/platform/organisations", NewOrganisation(code), TestContext.Current.CancellationToken);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // The test plan has Core HR, Time and Payroll: switch Payroll off and add Learning on.
        HttpResponseMessage modules = await client.PutAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/modules",
            new { Enabled = new[] { Features.CoreHr, Features.Time, Features.Learning } },
            TestContext.Current.CancellationToken);
        modules.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement detail = await modules.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Dictionary<string, bool> enabled = detail.GetProperty("modules").EnumerateArray()
            .ToDictionary(m => m.GetProperty("code").GetString()!, m => m.GetProperty("enabled").GetBoolean());
        enabled[Features.Payroll].ShouldBeFalse();
        enabled[Features.Learning].ShouldBeTrue();
        enabled[Features.CoreHr].ShouldBeTrue();

        DateOnly renews = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        HttpResponseMessage subscription = await client.PutAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/subscription",
            new { PlanCode = "PRO", SeatLimit = 50, Paid = true, TrialEndsOn = (DateOnly?)null, RenewsOn = renews, BillingReference = "PO-77" },
            TestContext.Current.CancellationToken);
        subscription.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement sub = (await subscription.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("subscription");
        sub.GetProperty("seatLimit").GetInt32().ShouldBe(50);
        sub.GetProperty("status").GetString().ShouldBe("Active");
        sub.GetProperty("renewsOn").GetString().ShouldBe(renews.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        sub.GetProperty("billingReference").GetString().ShouldBe("PO-77");

        // A seat limit above the plan's maximum is refused.
        HttpResponseMessage tooMany = await client.PutAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/subscription",
            new { PlanCode = "PRO", SeatLimit = 5000, Paid = true, TrialEndsOn = (DateOnly?)null, RenewsOn = renews, BillingReference = "PO-77" },
            TestContext.Current.CancellationToken);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task TheUsageMeasurement_CountsInsideTheTenant_AndTheConsoleShowsOnlyTheCounts()
    {
        // As the hourly job does for each tenant: every module's probe, inside that tenant.
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            var reading = new TenantUsageReading();
            foreach (ITenantUsageProbe probe in services.GetServices<ITenantUsageProbe>())
            {
                await probe.ReadAsync(reading, DateTime.UtcNow.Date, TestContext.Current.CancellationToken);
            }

            reading.Users.ShouldNotBeNull();
            reading.Users.Value.ShouldBeGreaterThan(0);
            reading.Employees.ShouldNotBeNull();
            reading.SignIn.ShouldNotBeNullOrWhiteSpace();
            reading.EmailChannel.ShouldNotBeNullOrWhiteSpace();

            await services.GetRequiredService<PlatformUsageRecorder>()
                .SaveAsync(PeopleOsApiFactory.SeededTenantId, reading, DateTime.UtcNow, TestContext.Current.CancellationToken);
        });

        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView);
        JsonElement detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform/organisations/{PeopleOsApiFactory.SeededTenantId}", TestContext.Current.CancellationToken);

        JsonElement usage = detail.GetProperty("usage");
        usage.ValueKind.ShouldBe(JsonValueKind.Object);
        usage.GetProperty("users").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task TheDashboardAndHealth_Answer()
    {
        using HttpClient client = Console.CreatePlatformClient(Permissions.Platform.TenantView, Permissions.Platform.HealthView);

        (await client.GetAsync("/api/v1/platform/dashboard", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/platform/plans", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/platform/audit", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/platform/health", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DedicatedDatabaseDetails_AreSavedEncrypted_TestedAndRevealedOnlyWithAReason()
    {
        string code = $"db-{Guid.NewGuid():N}"[..20];
        using HttpClient manager = Console.CreatePlatformClient(Permissions.Platform.TenantView, Permissions.Platform.TenantManage);
        HttpResponseMessage created = await manager.PostAsJsonAsync("/api/v1/platform/organisations", NewOrganisation(code), TestContext.Current.CancellationToken);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // Managing organisations is not enough: the password opens the organisation's data.
        (await manager.GetAsync($"/api/v1/platform/organisations/{id}/database", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using HttpClient admin = Console.CreatePlatformClient(Permissions.Platform.DatabaseManage);
        JsonElement before = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/platform/organisations/{id}/database", TestContext.Current.CancellationToken);
        before.GetProperty("inUse").GetString().ShouldBe("Shared");
        before.GetProperty("shared").GetProperty("databaseName").GetString().ShouldBe(factory.DatabaseName);
        before.TryGetProperty("dedicated", out _).ShouldBeFalse(); // null values are left out of responses

        // The test run's own database, reached with integrated sign-in: connects, schema up to date.
        var server = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(factory.ConnectionString).DataSource;
        HttpResponseMessage tested = await admin.PostAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/database/test",
            new { Server = server, DatabaseName = factory.DatabaseName, Authentication = "Integrated", Encryption = "TrustCertificate" },
            TestContext.Current.CancellationToken);
        JsonElement result = await tested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        result.GetProperty("succeeded").GetBoolean().ShouldBeTrue(result.GetProperty("message").GetString());
        result.GetProperty("schema").GetString()!.ShouldStartWith("Up to date");

        // A SQL login: the password is stored encrypted and never comes back on a read.
        const string Secret = "S3cret-db-pass!";
        HttpResponseMessage saved = await admin.PutAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/database",
            new { Server = "sql-test.example.com,1433", DatabaseName = "nb_test", Authentication = "SqlLogin", UserName = "nb_app", Password = Secret, Encryption = "Strict" },
            TestContext.Current.CancellationToken);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        string savedBody = await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        savedBody.ShouldNotContain(Secret);
        JsonElement dedicated = JsonDocument.Parse(savedBody).RootElement.GetProperty("dedicated");
        dedicated.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
        dedicated.GetProperty("connectionStringPreview").GetString()!.ShouldContain("Password=********");

        await using (AppDbContext database = factory.CreateContext())
        {
            string? ciphertext = await database.Set<Gggs.Modules.Platform.Domain.Console.DedicatedDatabase>()
                .Where(row => row.TenantId == id).Select(row => row.PasswordCiphertext).SingleAsync(TestContext.Current.CancellationToken);
            ciphertext.ShouldNotBeNull();
            ciphertext.ShouldNotContain(Secret);
        }

        // Saving again without a password keeps the stored one.
        (await admin.PutAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/database",
            new { Server = "sql-test.example.com,1433", DatabaseName = "nb_test2", Authentication = "SqlLogin", UserName = "nb_app", Encryption = "Strict" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revealing needs a reason, and is recorded with it.
        (await admin.PostAsJsonAsync($"/api/v1/platform/organisations/{id}/database/reveal", new { Reason = " " }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        HttpResponseMessage revealed = await admin.PostAsJsonAsync(
            $"/api/v1/platform/organisations/{id}/database/reveal", new { Reason = "Ticket 2041" }, TestContext.Current.CancellationToken);
        (await revealed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("password").GetString().ShouldBe(Secret);

        await using (AppDbContext database = factory.CreateContext())
        {
            (await database.PlatformAuditEntries.AnyAsync(
                entry => entry.TenantId == id && entry.Action == "database.password-revealed" && entry.Reason == "Ticket 2041",
                TestContext.Current.CancellationToken)).ShouldBeTrue();
        }

        // Removing the details leaves the organisation as it was: in the shared database.
        (await admin.DeleteAsync($"/api/v1/platform/organisations/{id}/database", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement after = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/platform/organisations/{id}/database", TestContext.Current.CancellationToken);
        after.TryGetProperty("dedicated", out _).ShouldBeFalse();
    }

    private static object NewOrganisation(string code) => new
    {
        Code = code,
        Name = $"Customer {code}",
        CountryCode = "IN",
        DataRegion = "centralindia",
        DefaultTimeZone = "Asia/Kolkata",
        DefaultCurrency = "INR",
        DefaultLocale = "en-IN",
        PlanCode = "PRO",
        TrialDays = 30,
        AdministratorEmail = $"owner@{code}.example",
    };
}
