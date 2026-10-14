using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class DocumentTests(PeopleOsApiFactory factory)
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUploadedDocument_KeepsEveryVersion_AndServesEachBack()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid type = await TypeAsync(admin, "PAN");

        HttpResponseMessage uploaded = await UploadAsync(admin, org.Requester, type, "PAN card", "pan.pdf", PdfBytes);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(Ct));
        JsonElement created = await uploaded.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Guid document = created.GetProperty("document").GetProperty("id").GetGuid();
        created.GetProperty("document").GetProperty("currentVersion").GetInt32().ShouldBe(1);

        using var second = new MultipartFormDataContent { { File("pan-v2.pdf", PdfBytes), "file", "pan-v2.pdf" }, { new StringContent("Rescanned"), "note" } };
        HttpResponseMessage versioned = await admin.PostAsync($"/api/v1/documents/{document}/versions", second, Ct);
        versioned.StatusCode.ShouldBe(HttpStatusCode.OK, await versioned.Content.ReadAsStringAsync(Ct));
        JsonElement detail = await versioned.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("versions").GetArrayLength().ShouldBe(2);

        HttpResponseMessage first = await admin.GetAsync($"/api/v1/documents/{document}/versions/1/content", Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await first.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(PdfBytes);

        JsonElement listed = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents?ownerType=Employee&ownerId={org.Requester}", Ct))!;
        listed.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldContain(document);
    }

    [Fact]
    public async Task FilesAreChecked_ByExtensionAndByContent()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid type = await TypeAsync(admin, "OTHER");

        HttpResponseMessage program = await UploadAsync(admin, org.Requester, type, "Tool", "tool.exe", [0x4D, 0x5A, 0x90, 0x00]);
        (await CoreHrApi.ErrorCodeAsync(program)).ShouldBe("DOCUMENT_TYPE_NOT_ALLOWED");

        HttpResponseMessage disguised = await UploadAsync(admin, org.Requester, type, "Not a PDF", "fake.pdf", Encoding.ASCII.GetBytes("<script>alert(1)</script>"));
        (await CoreHrApi.ErrorCodeAsync(disguised)).ShouldBe("DOCUMENT_CONTENT_MISMATCH");

        Guid passport = await TypeAsync(admin, "PASSPORT");
        HttpResponseMessage noExpiry = await UploadAsync(admin, org.Requester, passport, "Passport", "passport.pdf", PdfBytes);
        (await CoreHrApi.ErrorCodeAsync(noExpiry)).ShouldBe("DOCUMENT_EXPIRY_REQUIRED");
    }

    [Fact]
    public async Task AManagersDocumentAccess_ReachesTheirTeamOnly()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid type = await TypeAsync(admin, "EDUCATION");

        Guid teamDocument = await UploadedIdAsync(admin, org.Requester, type, "Degree");
        Guid otherDocument = await UploadedIdAsync(admin, org.Finance, type, "Degree");

        using HttpClient manager = await factory.CreateScopedUserClientAsync(
            Email("doc-manager"), org.Manager, (Permissions.Document.View, DataScope.DirectReports));

        (await manager.GetAsync($"/api/v1/documents/{teamDocument}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/v1/documents/{otherDocument}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await manager.GetAsync($"/api/v1/documents/{otherDocument}/versions/1/content", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await manager.GetAsync("/api/v1/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the whole library needs a tenant-wide grant");
    }

    [Fact]
    public async Task AGeneratedLetter_IsFilledIn_AndTheEmployeeCanReadAndSignIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        JsonElement template = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/documents/templates", Ct))!
            .First(row => row.GetProperty("code").GetString() == "EMPLOYMENT_CERTIFICATE");

        HttpResponseMessage generated = await admin.PostAsJsonAsync(
            $"/api/v1/documents/templates/{template.GetProperty("id").GetGuid()}/generate",
            new { EmployeeId = org.Requester, SignerEmployeeId = org.Requester },
            Ct);
        generated.StatusCode.ShouldBe(HttpStatusCode.Created, await generated.Content.ReadAsStringAsync(Ct));
        JsonElement created = await generated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Guid document = created.GetProperty("document").GetProperty("id").GetGuid();
        created.GetProperty("signatures")[0].GetProperty("status").GetString().ShouldBe("Requested");

        using HttpClient employee = await PersonAsync(factory, org.Requester);
        JsonElement[] mine = (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/documents", Ct))!;
        mine.Select(row => row.GetProperty("id").GetGuid()).ShouldContain(document);

        string letter = await employee.GetStringAsync($"/api/v1/me/documents/{document}/versions/1/content", Ct);
        letter.ShouldContain("Employment certificate");
        letter.ShouldContain("Engineer", Case.Sensitive, "the designation is filled in");
        letter.ShouldNotContain("{{");

        JsonElement request = (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/signature-requests", Ct))!
            .First(row => row.GetProperty("documentId").GetGuid() == document);
        Guid requestId = request.GetProperty("id").GetGuid();

        using HttpClient colleague = await PersonAsync(factory, org.Finance);
        (await colleague.PostAsync($"/api/v1/me/signature-requests/{requestId}/sign", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.GetAsync($"/api/v1/me/documents/{document}/versions/1/content", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await employee.PostAsync($"/api/v1/me/signature-requests/{requestId}/sign", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement detail = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{document}", Ct))!;
        detail.GetProperty("signatures")[0].GetProperty("status").GetString().ShouldBe("Signed");
    }

    [Fact]
    public async Task AnArchivedDocument_LeavesTheListButIsKept()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        Guid document = await UploadedIdAsync(admin, org.Requester, await TypeAsync(admin, "OTHER"), "Old form");

        (await admin.PostAsync($"/api/v1/documents/{document}/archive", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement listed = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents?ownerType=Employee&ownerId={org.Requester}", Ct))!;
        listed.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldNotContain(document);

        JsonElement withArchived = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents?ownerType=Employee&ownerId={org.Requester}&includeArchived=true", Ct))!;
        withArchived.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldContain(document);
    }

    private static async Task<Guid> TypeAsync(HttpClient admin, string code)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/documents/types", Ct))!
            .First(row => row.GetProperty("code").GetString() == code)
            .GetProperty("id").GetGuid();

    private static async Task<Guid> UploadedIdAsync(HttpClient client, Guid employee, Guid type, string title)
    {
        HttpResponseMessage response = await UploadAsync(client, employee, type, title, "file.pdf", PdfBytes);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("document").GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid employee, Guid type, string title, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Employee"), "ownerType" },
            { new StringContent(employee.ToString()), "ownerId" },
            { new StringContent(type.ToString()), "documentTypeId" },
            { new StringContent(title), "title" },
            { File(fileName, bytes), "file", fileName },
        };

        return await client.PostAsync("/api/v1/documents", form, Ct);
    }

    private static ByteArrayContent File(string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "application/octet-stream");
        return content;
    }
}
