using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Background verification from offer to hire: an accepted offer starts it, each check gets its own result,
/// reports are kept with it, and the hiring rule decides whether an unfinished verification stops the hire.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class BackgroundCheckFlowTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task An_accepted_offer_starts_verification_and_the_hire_waits_as_the_rule_says()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement original = await admin.GetFromJsonAsync<JsonElement>("/api/v1/recruitment/settings", Ct);
        try
        {
            await SaveRulesAsync(admin, original, "Required", startOnAccept: true, ["Identity", "Education"]);
            (Guid candidate, Guid offer) = await AcceptedOfferAsync(admin);

            // The accepted offer started verification with the default checks.
            JsonElement check = await LatestCheckAsync(admin, candidate);
            check.GetProperty("status").GetString().ShouldBe("Initiated");
            check.GetProperty("results").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(["Identity", "Education"]);
            Guid checkId = check.GetProperty("id").GetGuid();

            (await CoreHrApi.ErrorCodeAsync(await HireAsync(admin, offer, acknowledge: false))).ShouldBe("BGV_REQUIRED");

            // One check cleared, a report kept for the other.
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/background-checks/{checkId}/result", new { Status = "Clear", Check = "Identity" }, Ct));
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("University confirms the degree was awarded in 2019."));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            using (var form = new MultipartFormDataContent { { file, "file", "degree-confirmation.txt" }, { new StringContent("Education"), "check" } })
            {
                await OkAsync(await admin.PostAsync($"/api/v1/recruitment/background-checks/{checkId}/reports", form, Ct));
            }

            check = await LatestCheckAsync(admin, candidate);
            check.GetProperty("status").GetString().ShouldBe("InProgress");
            JsonElement report = check.GetProperty("reports").EnumerateArray().ShouldHaveSingleItem();
            report.GetProperty("checkName").GetString().ShouldBe("Education");
            (await admin.GetStringAsync($"/api/v1/recruitment/background-checks/{checkId}/reports/{report.GetProperty("index").GetInt32()}", Ct)).ShouldContain("awarded in 2019");

            // Under "Warn", an unfinished verification asks first; acknowledging is the person's call.
            await SaveRulesAsync(admin, original, "Warn", startOnAccept: true, ["Identity", "Education"]);
            (await CoreHrApi.ErrorCodeAsync(await HireAsync(admin, offer, acknowledge: false))).ShouldBe("BGV_PENDING");

            // The last check finds a discrepancy: the verification is complete, and a required one lets the hire go ahead.
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/background-checks/{checkId}/result", new { Status = "Discrepancy", Remarks = "Graduation year given as 2018", Check = "Education" }, Ct));
            check = await LatestCheckAsync(admin, candidate);
            check.GetProperty("status").GetString().ShouldBe("Discrepancy");
            check.GetProperty("remarks").GetString().ShouldBe("Education: Graduation year given as 2018");

            await SaveRulesAsync(admin, original, "Required", startOnAccept: false, null);
            (await HireAsync(admin, offer, acknowledge: false)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/v1/recruitment/settings", original, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static async Task SaveRulesAsync(HttpClient admin, JsonElement current, string rule, bool startOnAccept, string[]? checks)
    {
        HttpResponseMessage saved = await admin.PutAsJsonAsync("/api/v1/recruitment/settings", new
        {
            ReferralBonus = current.GetProperty("referralBonus").GetDecimal(),
            ReferralBonusAfterMonths = current.GetProperty("referralBonusAfterMonths").GetInt32(),
            OfferValidDays = current.GetProperty("offerValidDays").GetInt32(),
            Currency = current.GetProperty("currency").GetString(),
            ScorecardCriteria = current.GetProperty("scorecardCriteria").EnumerateArray().Select(item => item.GetString()).ToArray(),
            BgvHireRule = rule,
            BgvStartOnOfferAccepted = startOnAccept,
            BgvDefaultChecks = checks,
        }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<(Guid Candidate, Guid Offer)> AcceptedOfferAsync(HttpClient admin)
    {
        Guid requisition = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/requisitions", new
        {
            Title = "Verified analyst " + CoreHrApi.Unique("R"),
            DepartmentId = await CoreHrApi.DepartmentAsync(admin),
            Openings = 1,
            EmploymentTypeCode = "FULL_TIME",
            WorkModeCode = "HYBRID",
            SalaryMin = 800_000m,
            SalaryMax = 1_200_000m,
            Skills = new[] { "SQL" },
        }, Ct))).GetProperty("id").GetGuid();
        Guid candidate = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new
        {
            Details = new { FirstName = CoreHrApi.Unique("Kavya"), LastName = "Menon", Email = $"{Guid.NewGuid():N}@bgv.example", Skills = new[] { "SQL" } },
            Source = "Direct",
            RequisitionId = requisition,
        }, Ct))).GetProperty("candidate").GetProperty("id").GetGuid();
        Guid application = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/applications?requisitionId={requisition}", Ct))!.ShouldHaveSingleItem().GetProperty("id").GetGuid();

        Guid offer = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/offers", new
        {
            ApplicationId = application,
            Terms = new { AnnualCtc = 1_000_000m, Currency = "INR", JoiningDate = Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Designation = "Analyst" },
            Submit = true,
        }, Ct))).GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/offers/{offer}/send", null, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offer}/respond", new { Accepted = true }, Ct));
        return (candidate, offer);
    }

    private static async Task<JsonElement> LatestCheckAsync(HttpClient admin, Guid candidate)
        => (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/recruitment/candidates/{candidate}", Ct)).GetProperty("backgroundChecks").EnumerateArray().First();

    private static async Task<HttpResponseMessage> HireAsync(HttpClient admin, Guid offer, bool acknowledge)
        => await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offer}/hire", new { AcknowledgeBgv = acknowledge }, Ct);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
