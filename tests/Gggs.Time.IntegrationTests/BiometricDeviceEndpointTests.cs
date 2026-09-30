using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Snapshots;
using Gggs.Time.Infrastructure;
using Shouldly;

namespace Gggs.Time.IntegrationTests;

/// <summary><c>/api/v1/attendance/devices</c> (admin CRUD, ordinary authenticated requests) and
/// <c>/api/v1/attendance/device-ingest</c> (the device's own call, authenticated by API key rather
/// than a user token) over HTTP. The ingest tests are the real proof that
/// <c>DeviceApiKeyAuthenticator</c> correctly resolves and sets the tenant from nothing but the key -
/// see its own remarks on why that has to happen before any tenant-owned table can be read.</summary>
[Collection(nameof(TimeApiCollection))]
public sealed class BiometricDeviceEndpointTests(TimeApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task RegisteringADevice_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/devices",
            new RegisterBiometricDeviceBody($"SN-{Guid.NewGuid():N}", "Lobby terminal", Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegisteringADevice_ReturnsAnApiKey_ThatIsNeverRepeated()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.DeviceManage);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/devices",
            new RegisterBiometricDeviceBody($"SN-{Guid.NewGuid():N}", "Lobby terminal", Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        BiometricDeviceRegisteredResponse? body = await response.Content.ReadFromJsonAsync<BiometricDeviceRegisteredResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.ApiKey.ShouldNotBeNullOrWhiteSpace();
        body.Device.Status.ShouldBe(BiometricDeviceStatus.Active);

        HttpResponseMessage getResponse = await client.GetAsync("/api/v1/attendance/devices", TestContext.Current.CancellationToken);
        List<BiometricDeviceResponse>? devices = await getResponse.Content.ReadFromJsonAsync<List<BiometricDeviceResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);
        devices.ShouldNotBeNull();
        devices.ShouldContain(d => d.Id == body.Device.Id);
    }

    [Fact]
    public async Task RegisteringADevice_WithADuplicateSerialNumber_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.DeviceManage);
        string serialNumber = $"SN-{Guid.NewGuid():N}";
        var body = new RegisterBiometricDeviceBody(serialNumber, "Lobby terminal", Guid.NewGuid());

        (await client.PostAsJsonAsync("/api/v1/attendance/devices", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/attendance/devices", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RotatingAnApiKey_InvalidatesTheOldOne_ForDeviceIngest()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Attendance.DeviceManage);
        (Guid deviceId, string originalApiKey) = await RegisterDeviceAsync(client);
        Guid employeeId = await SeedEmployeeAndMapAsync(client, deviceId);

        HttpResponseMessage rotateResponse = await client.PostAsync(
            $"/api/v1/attendance/devices/{deviceId}/rotate-key", content: null, TestContext.Current.CancellationToken);
        rotateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        BiometricDeviceRegisteredResponse? rotated = await rotateResponse.Content.ReadFromJsonAsync<BiometricDeviceRegisteredResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        rotated.ShouldNotBeNull();
        rotated.ApiKey.ShouldNotBe(originalApiKey);

        using HttpClient anonymousClient = factory.CreateClient();
        HttpRequestMessage oldKeyRequest = new(HttpMethod.Post, "/api/v1/attendance/device-ingest")
        {
            Content = JsonContent.Create(new DeviceIngestPunchBody("EMP-CODE-1", DateTime.UtcNow, PunchDirection.In, null)),
        };
        oldKeyRequest.Headers.Add("X-Device-Api-Key", originalApiKey);

        HttpResponseMessage oldKeyResponse = await anonymousClient.SendAsync(oldKeyRequest, TestContext.Current.CancellationToken);
        oldKeyResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        HttpRequestMessage newKeyRequest = new(HttpMethod.Post, "/api/v1/attendance/device-ingest")
        {
            Content = JsonContent.Create(new DeviceIngestPunchBody("EMP-CODE-1", DateTime.UtcNow, PunchDirection.In, null)),
        };
        newKeyRequest.Headers.Add("X-Device-Api-Key", rotated.ApiKey);

        HttpResponseMessage newKeyResponse = await anonymousClient.SendAsync(newKeyRequest, TestContext.Current.CancellationToken);
        newKeyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        _ = employeeId;
    }

    [Fact]
    public async Task DeviceIngest_WithNoApiKey_IsUnauthorized()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/device-ingest",
            new DeviceIngestPunchBody("EMP-CODE-1", DateTime.UtcNow, PunchDirection.In, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeviceIngest_WithAnUnknownApiKey_IsUnauthorized()
    {
        using HttpClient client = factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/attendance/device-ingest")
        {
            Content = JsonContent.Create(new DeviceIngestPunchBody("EMP-CODE-1", DateTime.UtcNow, PunchDirection.In, null)),
        };
        request.Headers.Add("X-Device-Api-Key", "gggs_dev_not-a-real-key");

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeviceIngest_WithAnUnmappedDeviceUserCode_IsRejected()
    {
        using HttpClient managementClient = factory.CreateClientFor(Permissions.Attendance.DeviceManage);
        (_, string apiKey) = await RegisterDeviceAsync(managementClient);

        using HttpClient anonymousClient = factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/attendance/device-ingest")
        {
            Content = JsonContent.Create(new DeviceIngestPunchBody("UNMAPPED-CODE", DateTime.UtcNow, PunchDirection.In, null)),
        };
        request.Headers.Add("X-Device-Api-Key", apiKey);

        HttpResponseMessage response = await anonymousClient.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeviceIngest_WithAValidKeyAndMappedUser_RecordsAPunch_ResolvingTheTenantFromTheKeyAlone()
    {
        using HttpClient managementClient = factory.CreateClientFor(Permissions.Attendance.DeviceManage);
        (Guid deviceId, string apiKey) = await RegisterDeviceAsync(managementClient);
        await SeedEmployeeAndMapAsync(managementClient, deviceId, deviceUserCode: "FP-042");

        // No X-Test-Tenant header at all here - this client never authenticates as a user. If the
        // tenant were not resolved purely from the API key, this call would 401/403/exception
        // instead of succeeding.
        using HttpClient deviceClient = factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/attendance/device-ingest")
        {
            Content = JsonContent.Create(new DeviceIngestPunchBody("FP-042", DateTime.UtcNow, PunchDirection.In, $"ext-{Guid.NewGuid():N}")),
        };
        request.Headers.Add("X-Device-Api-Key", apiKey);

        HttpResponseMessage response = await deviceClient.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        AttendancePunchResponse? punch = await response.Content.ReadFromJsonAsync<AttendancePunchResponse>(JsonOptions, TestContext.Current.CancellationToken);
        punch.ShouldNotBeNull();
        punch.Source.ShouldBe(PunchSource.Biometric);
    }

    private static async Task<(Guid DeviceId, string ApiKey)> RegisterDeviceAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/attendance/devices",
            new RegisterBiometricDeviceBody($"SN-{Guid.NewGuid():N}", "Lobby terminal", Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        BiometricDeviceRegisteredResponse? body = await response.Content.ReadFromJsonAsync<BiometricDeviceRegisteredResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        return (body!.Device.Id, body.ApiKey);
    }

    private async Task<Guid> SeedEmployeeAndMapAsync(HttpClient client, Guid deviceId, string deviceUserCode = "EMP-CODE-1")
    {
        Guid employeeId = Guid.NewGuid();

        using TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Device Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/attendance/devices/{deviceId}/user-maps",
            new CreateDeviceUserMapBody(deviceUserCode, employeeId),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return employeeId;
    }
}
