using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Adding a candidate from a CV: the server reads it, the form is filled, and the CV stays with the candidate.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CandidateCvTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_CV_is_read_into_fields_and_kept_with_the_candidate()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string email = $"{Guid.NewGuid():N}@cv.example";
        string cv = $"""
            Riya Sharma
            Senior QA Automation Engineer
            Pune | {email} | +91 98765 43210

            EXPERIENCE
            Infosys Ltd — Senior QA Automation Engineer
            Jun 2021 – Present

            SKILLS
            Selenium, Java, SQL

            Current CTC: 12 LPA | Expected CTC: 16 LPA | Notice period: 60 days
            """;

        JsonElement read = await ReadAsync(admin, cv, "riya-sharma.txt");
        read.GetProperty("hasText").GetBoolean().ShouldBeTrue();
        JsonElement fields = read.GetProperty("fields");
        fields.GetProperty("firstName").GetString().ShouldBe("Riya");
        fields.GetProperty("email").GetString().ShouldBe(email);
        fields.GetProperty("currentCompany").GetString().ShouldBe("Infosys Ltd");
        fields.GetProperty("currentCtc").GetDecimal().ShouldBe(1_200_000m);
        fields.GetProperty("noticePeriodDays").GetInt32().ShouldBe(60);
        fields.GetProperty("skills").EnumerateArray().Select(skill => skill.GetString()).ShouldContain("SQL");

        // The recruiter saves the form with the CV attached.
        JsonElement added = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new
        {
            Details = new { FirstName = "Riya", LastName = "Sharma", Email = email, Mobile = "+91 98765 43210", Skills = new[] { "SQL" } },
            Source = "Direct",
            CvKey = read.GetProperty("cvKey").GetString(),
            CvFileName = read.GetProperty("fileName").GetString(),
        }, Ct));
        JsonElement candidate = added.GetProperty("candidate");
        candidate.GetProperty("cvFileName").GetString().ShouldBe("riya-sharma.txt");

        HttpResponseMessage download = await admin.GetAsync($"/api/v1/recruitment/candidates/{candidate.GetProperty("id").GetGuid()}/cv", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await download.Content.ReadAsStringAsync(Ct)).ShouldContain("Infosys Ltd");
    }

    [Fact]
    public async Task Only_PDF_Word_and_text_CVs_are_taken_and_only_ones_this_feature_stored_can_be_attached()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();

        using var image = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "photo.jpg" } };
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync("/api/v1/recruitment/cv/read", image, Ct))).ShouldBe("CV_FILE_TYPE");

        HttpResponseMessage forged = await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new
        {
            Details = new { FirstName = "Mallory", Email = $"{Guid.NewGuid():N}@cv.example", Skills = Array.Empty<string>() },
            Source = "Direct",
            CvKey = "documents/someone-else/contract.pdf",
            CvFileName = "contract.pdf",
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(forged)).ShouldBe("CV_MISSING");
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string text, string fileName)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await OkAsync(await client.PostAsync("/api/v1/recruitment/cv/read", form, Ct));
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
