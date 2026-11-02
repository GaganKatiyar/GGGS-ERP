using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class EmployeePhotoTests(PeopleOsApiFactory factory)
{
    /// <summary>A PNG signature and a little body: the API judges the type by the first bytes only.</summary>
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    private static readonly byte[] WebpBytes = [.. "RIFF"u8, 8, 0, 0, 0, .. "WEBPVP8 "u8];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmployee_SetsReplacesAndRemovesTheirOwnPhoto_AndColleaguesOutOfScopeCannotSeeIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);
        Guid other = await CoreHrApi.HireAsync(admin, department);

        using HttpClient self = await factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email("photo"), employee, (Permissions.Self.ProfileView, DataScope.Own), (Permissions.Self.ProfileEdit, DataScope.Own));

        (await HasPhotoAsync(self)).ShouldBeFalse();
        (await self.GetAsync("/api/v1/me/employee/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        Guid first = await UploadAsync(self, PngBytes);
        (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/employee/avatar", Ct)).GetProperty("photoId").GetGuid().ShouldBe(first);
        (await self.GetFromJsonAsync<JsonElement>("/api/v1/me/employee", Ct)).GetProperty("photoId").GetGuid().ShouldBe(first);

        using (HttpResponseMessage photo = await self.GetAsync($"/api/v1/me/employee/photo?v={first}", Ct))
        {
            photo.StatusCode.ShouldBe(HttpStatusCode.OK);
            photo.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            photo.Headers.CacheControl!.Private.ShouldBeTrue();
            photo.Headers.CacheControl.MaxAge.ShouldNotBeNull();
            (await photo.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(PngBytes);
        }

        // Without the current id in the URL the browser must check back, or an old photo would stick.
        using (HttpResponseMessage unversioned = await self.GetAsync("/api/v1/me/employee/photo", Ct))
        {
            unversioned.Headers.CacheControl!.NoCache.ShouldBeTrue();
        }

        Guid second = await UploadAsync(self, WebpBytes);
        second.ShouldNotBe(first);
        using (HttpResponseMessage replaced = await self.GetAsync("/api/v1/me/employee/photo", Ct))
        {
            replaced.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        }

        // HR sees it through the employee routes; a colleague whose view reaches only themselves does not.
        (await admin.GetAsync($"/api/v1/employees/{employee}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (HttpClient colleague = await factory.CreateScopedUserClientAsync(PlatformServicesApi.Email("colleague"), other, (Permissions.Employee.View, DataScope.Own)))
        {
            (await colleague.GetAsync($"/api/v1/employees/{employee}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await self.DeleteAsync("/api/v1/me/employee/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await self.GetAsync("/api/v1/me/employee/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await HasPhotoAsync(self)).ShouldBeFalse();
    }

    [Fact]
    public async Task APhoto_IsJudgedByItsBytes_AndChangingItNeedsTheEditPermission()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);

        using HttpClient self = await factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email("photo"), employee, (Permissions.Self.ProfileView, DataScope.Own), (Permissions.Self.ProfileEdit, DataScope.Own));

        // A script named like a picture is still a script.
        HttpResponseMessage disguised = await PutAsync(self, Encoding.ASCII.GetBytes("<svg onload=alert(1)>"), "me.png");
        disguised.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(disguised)).ShouldBe("PHOTO_TYPE_NOT_ALLOWED");

        HttpResponseMessage tooLarge = await PutAsync(self, [.. PngBytes, .. new byte[2 * 1024 * 1024]], "big.png");
        (await CoreHrApi.ErrorCodeAsync(tooLarge)).ShouldBe("PHOTO_TOO_LARGE");

        Guid other = await CoreHrApi.HireAsync(admin, department);
        using HttpClient viewOnly = await factory.CreateScopedUserClientAsync(PlatformServicesApi.Email("viewer"), other, (Permissions.Self.ProfileView, DataScope.Own));
        (await PutAsync(viewOnly, PngBytes, "me.png")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewOnly.DeleteAsync("/api/v1/me/employee/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>The API leaves null properties out, so "no photo" is a missing or null id.</summary>
    private static async Task<bool> HasPhotoAsync(HttpClient client)
    {
        JsonElement info = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/employee/avatar", Ct);
        return info.TryGetProperty("photoId", out JsonElement id) && id.ValueKind != JsonValueKind.Null;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] bytes)
    {
        HttpResponseMessage response = await PutAsync(client, bytes, "me.png");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("photoId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await client.PutAsync("/api/v1/me/employee/photo", form, Ct);
    }
}
