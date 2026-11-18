using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Hire drafts: the Hire form saved part-way. What was typed comes back exactly as saved but is encrypted at
/// rest; files wait with the draft; only people who may hire see drafts; deleting one removes everything.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class HireDraftTests(PeopleOsApiFactory factory)
{
    private const string Aadhaar = "234567890124";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_comes_back_as_saved_but_is_encrypted_at_rest()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string name = CoreHrApi.Unique("Draft");
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/employees/hire-drafts", new
        {
            DisplayName = name,
            Step = 4,
            Payload = new { form = new { person = new { firstName = name }, ids = new { aadhaar = Aadhaar } }, modes = new[] { "Web" } },
        }, Ct));
        Guid id = saved.GetProperty("id").GetGuid();

        try
        {
            JsonElement opened = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/hire-drafts/{id}", Ct);
            opened.GetProperty("step").GetInt32().ShouldBe(4);
            opened.GetProperty("payload").GetProperty("form").GetProperty("ids").GetProperty("aadhaar").GetString().ShouldBe(Aadhaar);

            (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/employees/hire-drafts", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == id && row.GetProperty("displayName").GetString() == name);

            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            {
                HireDraft row = await provider.GetRequiredService<AppDbContext>().Set<HireDraft>().AsNoTracking().SingleAsync(draft => draft.Id == id, Ct);
                row.PayloadCiphertext.ShouldNotContain(Aadhaar);
                row.PayloadCiphertext.ShouldNotContain("aadhaar");
            });

            // Saving again replaces what was there.
            await OkAsync(await admin.PutAsJsonAsync($"/api/v1/employees/hire-drafts/{id}", new { DisplayName = name, Step = 6, Payload = new { form = new { person = new { firstName = "Changed" } } } }, Ct));
            JsonElement again = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/hire-drafts/{id}", Ct);
            again.GetProperty("step").GetInt32().ShouldBe(6);
            again.GetProperty("payload").GetProperty("form").GetProperty("person").GetProperty("firstName").GetString().ShouldBe("Changed");
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/employees/hire-drafts/{id}", Ct);
        }

        (await admin.GetAsync($"/api/v1/employees/hire-drafts/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_draft_keeps_a_photo_and_documents_until_it_is_deleted()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid id = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/employees/hire-drafts", new { DisplayName = "With files", Step = 0, Payload = new { } }, Ct))).GetProperty("id").GetGuid();

        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%test\n");

        (await PutFileAsync(admin, id, "photo", png, "image/png", "me.png")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutFileAsync(admin, id, "AADHAAR", pdf, "application/pdf", "aadhaar.pdf")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Judged by its bytes, never its name: a PDF is no photo, and a renamed script is nothing.
        (await CoreHrApi.ErrorCodeAsync(await PutFileAsync(admin, id, "photo", pdf, "image/png", "fake.png"))).ShouldBe("HIRE_DRAFT_FILE_TYPE");
        (await CoreHrApi.ErrorCodeAsync(await PutFileAsync(admin, id, "PAN", Encoding.ASCII.GetBytes("MZ not a document"), "application/pdf", "pan.pdf"))).ShouldBe("HIRE_DRAFT_FILE_TYPE");
        (await PutFileAsync(admin, id, "SALARY_SLIP", pdf, "application/pdf", "x.pdf")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        JsonElement opened = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/hire-drafts/{id}", Ct);
        opened.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("slot").GetString()).ShouldBe(["photo", "AADHAAR"], ignoreOrder: true);

        HttpResponseMessage download = await admin.GetAsync($"/api/v1/employees/hire-drafts/{id}/files/AADHAAR", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(pdf);

        (await admin.DeleteAsync($"/api/v1/employees/hire-drafts/{id}/files/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/employees/hire-drafts/{id}/files/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.DeleteAsync($"/api/v1/employees/hire-drafts/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/employees/hire-drafts/{id}/files/AADHAAR", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_someone_who_may_hire_sees_drafts()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient viewer = await factory.CreateScopedUserClientAsync(Email("draft-viewer"), person, (Permissions.Employee.View, DataScope.Tenant));

        (await viewer.GetAsync("/api/v1/employees/hire-drafts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/employees/hire-drafts", new { DisplayName = "Nope", Step = 0, Payload = new { } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/employees/hire-drafts", new { DisplayName = "Empty", Step = 0, Payload = "not an object" }, Ct))).ShouldBe("HIRE_DRAFT_PAYLOAD");
    }

    private static async Task<HttpResponseMessage> PutFileAsync(HttpClient client, Guid draft, string slot, byte[] bytes, string contentType, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PutAsync($"/api/v1/employees/hire-drafts/{draft}/files/{slot}", form, Ct);
    }
}
