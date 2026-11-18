using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Creates Core HR records through the API, as a user would. Codes carry a random suffix because the
/// suite shares one database and tests run in any order.
/// </summary>
internal static class CoreHrApi
{
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();

    public static async Task<Guid> CreateAsync(HttpClient client, string path, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {text}");

        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    public static Task<Guid> DepartmentAsync(HttpClient client, Guid? parentId = null, string? code = null)
        => CreateAsync(client, "/api/v1/organizations/departments", new { Code = code ?? Unique("DEPT"), Name = "Department " + Unique("N"), ParentDepartmentId = parentId });

    public static Task<Guid> AnyRoleAsync(HttpClient client)
        => CreateAsync(client, "/api/v1/organizations/roles", new { Code = Unique("ROLE"), Name = "Engineer", JobFamilyCode = "ENGINEERING", RequiredSkills = new[] { ".NET" } });

    /// <summary>A role of one department; it gets a designation for every active level.</summary>
    public static Task<Guid> RoleAsync(HttpClient client, Guid departmentId, string name = "Business Analyst")
        => CreateAsync(client, "/api/v1/organizations/roles", new { Code = Unique("R"), Name = name, DepartmentId = departmentId });

    public static Task<Guid> LevelAsync(HttpClient client, int rank = 3)
        => CreateAsync(client, "/api/v1/organizations/levels", new { Code = Unique("L"), Name = "Level " + rank, Rank = rank });

    public static Task<Guid> DesignationAsync(HttpClient client, string name, Guid? levelId = null)
        => CreateAsync(client, "/api/v1/organizations/designations", new { Code = Unique("DES"), Name = name, LevelId = levelId });

    public static async Task<Guid> HireAsync(HttpClient client, Guid departmentId, Guid? managerId = null, string? email = null)
    {
        HttpResponseMessage response = await HireRawAsync(client, departmentId, managerId, email);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"hire failed: {text}");

        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> HireRawAsync(HttpClient client, Guid? departmentId, Guid? managerId = null, string? email = null)
        => client.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Test",
            LastName = Unique("Person"),
            OfficialEmail = email,
            JoiningDate = "2024-01-15",
            Assignment = new
            {
                DepartmentId = departmentId,
                ManagerEmployeeId = managerId,
                EmploymentTypeCode = "FULL_TIME",
                WorkModeCode = "HYBRID",
                Designation = "Engineer",
            },
        }, TestContext.Current.CancellationToken);

    public static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("code").GetString()!;
    }
}
