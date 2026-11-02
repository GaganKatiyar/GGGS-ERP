using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Education, prior work experience and the employee's own employment history on My profile.</summary>
[Collection(nameof(ApiCollection))]
public sealed class EmployeeBackgroundTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmployee_AddsEditsAndRemovesTheirEducation_AndHrSeesIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);
        using HttpClient self = await SelfAsync(employee, "education");

        HttpResponseMessage added = await self.PostAsJsonAsync(
            "/api/v1/me/employee/education",
            new { Degree = "B.Tech", FieldOfStudy = "Computer Science", Institution = "IIT Kanpur", StartYear = 2012, EndYear = 2016, Grade = "8.4 CGPA" },
            Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Ct));
        Guid id = (await added.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        (await self.PutAsJsonAsync($"/api/v1/me/employee/education/{id}", new { Degree = "B.Tech", Institution = "IIT Kanpur", StartYear = 2012, EndYear = 2016, Grade = "8.6 CGPA" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement[] mine = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/employee/education", Ct))!;
        mine.Length.ShouldBe(1);
        mine[0].GetProperty("grade").GetString().ShouldBe("8.6 CGPA");
        mine[0].TryGetProperty("fieldOfStudy", out _).ShouldBeFalse("an edit replaces the whole entry, and empty values are left out");

        JsonElement[] seenByHr = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/employees/{employee}/education", Ct))!;
        seenByHr.Single().GetProperty("institution").GetString().ShouldBe("IIT Kanpur");

        HttpResponseMessage reversed = await self.PostAsJsonAsync("/api/v1/me/employee/education", new { Degree = "MBA", Institution = "IIM", StartYear = 2020, EndYear = 2018 }, Ct);
        (await CoreHrApi.ErrorCodeAsync(reversed)).ShouldBe("EDUCATION_YEARS_REVERSED");

        (await self.DeleteAsync($"/api/v1/me/employee/education/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/employee/education", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task WorkExperience_IsKeptNewestFirst_AndCannotStartInTheFuture()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);
        using HttpClient self = await SelfAsync(employee, "experience");

        (await self.PostAsJsonAsync("/api/v1/me/employee/work-experience", new { Company = "Infosys", Title = "Engineer", FromDate = "2016-07-01", ToDate = "2019-03-31" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await self.PostAsJsonAsync("/api/v1/me/employee/work-experience", new { Company = "TCS", Title = "Senior Engineer", Location = "Pune", FromDate = "2019-04-01", ToDate = "2024-01-31" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement[] jobs = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/employee/work-experience", Ct))!;
        jobs.Select(job => job.GetProperty("company").GetString()).ShouldBe(["TCS", "Infosys"]);

        HttpResponseMessage future = await self.PostAsJsonAsync("/api/v1/me/employee/work-experience", new { Company = "Later Ltd", Title = "Lead", FromDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd") }, Ct);
        (await CoreHrApi.ErrorCodeAsync(future)).ShouldBe("EXPERIENCE_IN_FUTURE");

        // HR corrects an entry for someone in their scope.
        Guid tcs = jobs[0].GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/employees/{employee}/work-experience/{tcs}", new { Company = "Tata Consultancy Services", Title = "Senior Engineer", Location = "Pune", FromDate = "2019-04-01", ToDate = "2024-01-31" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/employee/work-experience", Ct))![0].GetProperty("company").GetString().ShouldBe("Tata Consultancy Services");
    }

    [Fact]
    public async Task SomeoneOutOfScope_CannotReadOrChangeAnotherEmployeesBackground()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);
        Guid other = await CoreHrApi.HireAsync(admin, department);
        using HttpClient self = await SelfAsync(employee, "owner");
        Guid id = (await (await self.PostAsJsonAsync("/api/v1/me/employee/education", new { Degree = "B.Com", Institution = "Delhi University" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        using HttpClient colleague = await factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email("colleague"), other,
            (Permissions.Employee.View, DataScope.Own), (Permissions.Employee.Edit, DataScope.Own),
            (Permissions.Self.ProfileView, DataScope.Own), (Permissions.Self.ProfileEdit, DataScope.Own));

        (await colleague.GetAsync($"/api/v1/employees/{employee}/education", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.DeleteAsync($"/api/v1/employees/{employee}/education/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Their own routes reach only their own record, so another person's entry id is simply not found.
        (await colleague.DeleteAsync($"/api/v1/me/employee/education/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/employee/education", Ct))!.Length.ShouldBe(1);
    }

    [Fact]
    public async Task MyHistory_MatchesTheEmploymentRecordsHrSees()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid employee = await CoreHrApi.HireAsync(admin, department);
        using HttpClient self = await SelfAsync(employee, "history");

        string mine = await self.GetStringAsync("/api/v1/me/employee/history", Ct);
        string hr = await admin.GetStringAsync($"/api/v1/employees/{employee}/history", Ct);
        mine.ShouldBe(hr);
        JsonDocument.Parse(mine).RootElement.GetArrayLength().ShouldBeGreaterThan(0);
    }

    private Task<HttpClient> SelfAsync(Guid employee, string label)
        => factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email(label), employee, (Permissions.Self.ProfileView, DataScope.Own), (Permissions.Self.ProfileEdit, DataScope.Own));
}
