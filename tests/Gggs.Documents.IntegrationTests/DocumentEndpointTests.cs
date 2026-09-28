using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Documents.Api.Contracts;
using Gggs.Documents.Domain.Documents;
using Gggs.Documents.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.Documents.IntegrationTests;

/// <summary>
/// Upload, list, get, download and delete over HTTP, against a real database and a real local
/// storage folder.
/// </summary>
[Collection(nameof(DocumentsApiCollection))]
public sealed class DocumentEndpointTests(DocumentsApiFactory factory)
{
    // The server writes enums as strings (AddGggsJson); System.Net.Http.Json's own default options
    // do not know that, so reading a DocumentResponse back needs the same wire format applied here.
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task UploadingAFile_CreatesADocumentWhoseBytesRoundTripThroughDownload()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Document.TypeManage, Permissions.Document.Upload, Permissions.Document.View);

        string typeCode = await CreateDocumentTypeAsync(client, ["pdf", "jpg"], maxSizeMb: 5);

        Guid ownerId = Guid.CreateVersion7();
        byte[] fileBytes = "%PDF-1.4 fake pdf content for a round-trip test"u8.ToArray();

        HttpResponseMessage uploadResponse = await UploadAsync(
            client, typeCode, DocumentOwnerType.Employee, ownerId, "resume.pdf", "application/pdf", fileBytes);

        uploadResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        DocumentResponse? uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        uploaded.ShouldNotBeNull();
        uploaded.FileName.ShouldBe("resume.pdf");
        uploaded.SizeBytes.ShouldBe(fileBytes.LongLength);
        uploaded.ScanStatus.ShouldBe(DocumentScanStatus.Skipped);

        HttpResponseMessage downloadResponse = await client.GetAsync(
            $"/api/v1/documents/{uploaded.Id}/download", TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        byte[] downloaded = await downloadResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        downloaded.ShouldBe(fileBytes);
    }

    [Fact]
    public async Task UploadingAFile_WithADisallowedExtension_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Document.TypeManage, Permissions.Document.Upload);

        string typeCode = await CreateDocumentTypeAsync(client, ["pdf"], maxSizeMb: 5);

        HttpResponseMessage response = await UploadAsync(
            client, typeCode, DocumentOwnerType.Employee, Guid.CreateVersion7(),
            "malware.exe", "application/octet-stream", "not a pdf"u8.ToArray());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UploadingAFile_LargerThanTheTypesLimit_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Document.TypeManage, Permissions.Document.Upload);

        string typeCode = await CreateDocumentTypeAsync(client, ["pdf"], maxSizeMb: 1);
        byte[] tooLarge = new byte[2 * 1024 * 1024];

        HttpResponseMessage response = await UploadAsync(
            client, typeCode, DocumentOwnerType.Employee, Guid.CreateVersion7(),
            "big.pdf", "application/pdf", tooLarge);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UploadingAFile_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Document.View);

        HttpResponseMessage response = await UploadAsync(
            client, "ANY", DocumentOwnerType.Employee, Guid.CreateVersion7(),
            "file.pdf", "application/pdf", "content"u8.ToArray());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListingByOwner_ReturnsOnlyThatOwnersDocuments()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Document.TypeManage, Permissions.Document.Upload, Permissions.Document.View);

        string typeCode = await CreateDocumentTypeAsync(client, ["pdf"], maxSizeMb: 5);
        Guid ownerId = Guid.CreateVersion7();
        Guid otherOwnerId = Guid.CreateVersion7();

        await UploadAsync(client, typeCode, DocumentOwnerType.Employee, ownerId, "a.pdf", "application/pdf", "a"u8.ToArray());
        await UploadAsync(client, typeCode, DocumentOwnerType.Employee, ownerId, "b.pdf", "application/pdf", "b"u8.ToArray());
        await UploadAsync(client, typeCode, DocumentOwnerType.Employee, otherOwnerId, "c.pdf", "application/pdf", "c"u8.ToArray());

        List<DocumentResponse>? items = await client.GetFromJsonAsync<List<DocumentResponse>>(
            $"/api/v1/documents?ownerType=Employee&ownerId={ownerId}", JsonOptions, TestContext.Current.CancellationToken);

        items.ShouldNotBeNull();
        items.Count.ShouldBe(2);
        items.ShouldAllBe(item => item.OwnerId == ownerId);
    }

    [Fact]
    public async Task DeletingADocument_HidesItFromReadsButLeavesTheRowSoftDeleted()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Document.TypeManage, Permissions.Document.Upload,
            Permissions.Document.View, Permissions.Document.Delete);

        string typeCode = await CreateDocumentTypeAsync(client, ["pdf"], maxSizeMb: 5);
        HttpResponseMessage uploadResponse = await UploadAsync(
            client, typeCode, DocumentOwnerType.Employee, Guid.CreateVersion7(),
            "delete-me.pdf", "application/pdf", "content"u8.ToArray());
        DocumentResponse uploaded = (await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/v1/documents/{uploaded.Id}", TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/documents/{uploaded.Id}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using DocumentDbContext db = factory.CreateContext();
        Domain.Documents.Document? stillThere = await db.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == uploaded.Id, TestContext.Current.CancellationToken);
        stillThere.ShouldNotBeNull();
        stillThere.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task GettingAnUnknownDocument_ReturnsNotFound()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Document.View);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/documents/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string> CreateDocumentTypeAsync(HttpClient client, string[] extensions, int maxSizeMb)
    {
        string code = $"TYPE-{Guid.CreateVersion7():N}"[..20];

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/documents/types",
            new
            {
                Code = code,
                Name = "Test Document Type",
                Category = DocumentCategory.Employee,
                AllowedExtensions = extensions,
                MaxSizeMb = maxSizeMb,
                IsEmployeeUploadable = true,
                IsSensitive = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return code;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string documentTypeCode,
        DocumentOwnerType ownerType,
        Guid ownerId,
        string fileName,
        string contentType,
        byte[] fileBytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(documentTypeCode, Encoding.UTF8), "documentTypeCode");
        content.Add(new StringContent(ownerType.ToString(), Encoding.UTF8), "ownerType");
        content.Add(new StringContent(ownerId.ToString(), Encoding.UTF8), "ownerId");

        return await client.PostAsync("/api/v1/documents", content, TestContext.Current.CancellationToken);
    }
}
