using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The hire wizard's backend: a hire carries the whole record (personal details, addresses, Aadhaar,
/// PAN, passport, bank, previous experience) in one transaction, from the directory or from an accepted
/// offer; attendance modes gate the web punch; HR can set a new hire's photo.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class HireDetailsTests(PeopleOsApiFactory factory)
{
    private const string ValidAadhaar = "234567890124";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Details(string? aadhaar = ValidAadhaar) => new
    {
        GenderCode = "FEMALE",
        DateOfBirth = "1996-04-12",
        MaritalStatusCode = "MARRIED",
        BloodGroupCode = "O_POS",
        PresentAddress = new { Line1 = "12 MG Road", City = "Bengaluru", StateCode = "IN-KA", CountryCode = "IN", PostalCode = "560001" },
        PermanentAddress = new { Line1 = "4 FC Road", City = "Pune", StateCode = "IN-MH", CountryCode = "IN", PostalCode = "411004" },
        Aadhaar = aadhaar,
        Pan = "ABCDE1234F",
        PassportNumber = "K1234567",
        PassportExpiry = "2034-01-31",
        BankName = "HDFC Bank",
        BankIfsc = "HDFC0001234",
        BankAccountHolder = "Test Person",
        BankAccountNumber = "123456789012",
        PreviousExperienceYears = 4.5m,
    };

    [Fact]
    public async Task A_direct_hire_saves_the_whole_record_at_once()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Kavya",
            LastName = CoreHrApi.Unique("Rao"),
            JoiningDate = Iso(Today),
            Assignment = new { DepartmentId = department, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Engineer" },
            Details = Details(),
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Guid id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        JsonElement profile = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{id}", Ct);
        profile.GetProperty("maritalStatus").GetProperty("code").GetString().ShouldBe("MARRIED");
        profile.GetProperty("bloodGroup").GetProperty("code").GetString().ShouldBe("O_POS");
        profile.GetProperty("currentAddress").GetProperty("city").GetString().ShouldBe("Bengaluru");
        profile.GetProperty("permanentAddress").GetProperty("city").GetString().ShouldBe("Pune");
        JsonElement sensitive = profile.GetProperty("sensitive");
        sensitive.GetProperty("nationalIdHint").GetString()!.ShouldEndWith("0124");
        sensitive.GetProperty("panHint").GetString().ShouldNotBeNullOrWhiteSpace();
        sensitive.GetProperty("passportHint").GetString().ShouldNotBeNullOrWhiteSpace();
        sensitive.GetProperty("bankIfsc").GetString().ShouldBe("HDFC0001234");
        sensitive.GetProperty("previousExperienceYears").GetDecimal().ShouldBe(4.5m);

        JsonElement revealed = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{id}/statutory", Ct);
        revealed.GetProperty("passportNumber").GetString().ShouldBe("K1234567");
        revealed.GetProperty("nationalId").GetString().ShouldBe(ValidAadhaar);
    }

    [Fact]
    public async Task An_invalid_aadhaar_stops_the_hire_and_nothing_is_saved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        string lastName = CoreHrApi.Unique("Bad");

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Wrong",
            LastName = lastName,
            JoiningDate = Iso(Today),
            Assignment = new { DepartmentId = department, EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID" },
            Details = Details(aadhaar: "234567890120"),
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Aadhaar");
        JsonElement list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees?search={lastName}", Ct);
        list.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task An_accepted_offer_is_hireable_once_and_its_hire_carries_the_details()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid requisition = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/requisitions", new
        {
            Title = "Analyst " + CoreHrApi.Unique("R"),
            DepartmentId = department,
            Openings = 1,
            EmploymentTypeCode = "FULL_TIME",
            WorkModeCode = "HYBRID",
            SalaryMin = 800_000m,
            SalaryMax = 1_200_000m,
            Skills = new[] { "SQL" },
        }, Ct))).GetProperty("id").GetGuid();
        string first = CoreHrApi.Unique("Nisha");
        Guid candidate = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/candidates", new
        {
            Details = new { FirstName = first, LastName = "Iyer", Email = Email("nisha"), ExperienceYears = 6m, Skills = new[] { "SQL" } },
            Source = "Direct",
            RequisitionId = requisition,
        }, Ct))).GetProperty("candidate").GetProperty("id").GetGuid();
        Guid application = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/recruitment/applications?requisitionId={requisition}", Ct))!.ShouldHaveSingleItem().GetProperty("id").GetGuid();

        JsonElement offer = await OkAsync(await admin.PostAsJsonAsync("/api/v1/recruitment/offers", new
        {
            ApplicationId = application,
            Terms = new { AnnualCtc = 1_000_000m, Currency = "INR", JoiningDate = Iso(Today), Designation = "Analyst" },
            Submit = true,
        }, Ct));
        Guid offerId = offer.GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/recruitment/offers/{offerId}/send", null, Ct));

        (await HireableAsync(admin)).ShouldNotContain(row => row.GetProperty("offerId").GetGuid() == offerId, "not accepted yet");
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/respond", new { Accepted = true }, Ct));

        JsonElement hireable = (await HireableAsync(admin)).Single(row => row.GetProperty("offerId").GetGuid() == offerId);
        hireable.GetProperty("candidateId").GetGuid().ShouldBe(candidate);
        hireable.GetProperty("departmentId").GetGuid().ShouldBe(department);
        hireable.GetProperty("experienceYears").GetDecimal().ShouldBe(6m);
        hireable.GetProperty("annualCtc").GetDecimal().ShouldBe(1_000_000m);

        Guid employee = (await OkAsync(await admin.PostAsJsonAsync($"/api/v1/recruitment/offers/{offerId}/hire", new { Details = new { Aadhaar = ValidAadhaar, BloodGroupCode = "B_POS" } }, Ct)))
            .GetProperty("employeeId").GetGuid();

        JsonElement profile = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{employee}", Ct);
        profile.GetProperty("bloodGroup").GetProperty("code").GetString().ShouldBe("B_POS");
        profile.GetProperty("sensitive").GetProperty("previousExperienceYears").GetDecimal().ShouldBe(6m, "taken from the candidate when the form leaves it out");
        (await HireableAsync(admin)).ShouldNotContain(row => row.GetProperty("offerId").GetGuid() == offerId, "hired now");
    }

    [Fact]
    public async Task Without_web_mode_the_browser_punch_is_refused()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("modes"), person, (Permissions.Self.AttendancePunch, DataScope.Own));

        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/employees/{person}/modes", Ct)).GetProperty("modes").EnumerateArray().Select(mode => mode.GetString())
            .ShouldBe(["Web", "Device"], "the default when HR never chose");

        JsonElement set = await OkAsync(await admin.PutAsJsonAsync($"/api/v1/attendance/employees/{person}/modes", new { Modes = new[] { "Device", "Mobile" } }, Ct));
        set.GetProperty("modes").EnumerateArray().Select(mode => mode.GetString()).ShouldBe(["Device", "Mobile"]);

        HttpResponseMessage punch = await self.PostAsync("/api/v1/me/attendance/punch", null, Ct);
        punch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CoreHrApi.ErrorCodeAsync(punch)).ShouldBe("ATTENDANCE_WEB_PUNCH_OFF");

        (await admin.PutAsJsonAsync($"/api/v1/attendance/employees/{person}/modes", new { Modes = Array.Empty<string>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task HR_can_set_a_new_hires_photo()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        JsonElement info = await OkAsync(await admin.PutAsync($"/api/v1/employees/{person}/photo", form, Ct));
        info.ToString().ShouldNotBeNullOrWhiteSpace();
        (await admin.GetAsync($"/api/v1/employees/{person}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<JsonElement[]> HireableAsync(HttpClient admin)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/recruitment/offers/hireable", Ct))!;
}
