using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Helpers for the delivery tests: clients, contracts, projects and the people on them.</summary>
internal static class DeliveryApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> ClientAsync(HttpClient admin, string? status = null)
        => await OkAsync(
            await admin.PostAsJsonAsync("/api/v1/clients", new
            {
                Code = CoreHrApi.Unique("CL"),
                Name = "Client " + CoreHrApi.Unique("N"),
                IndustryCode = "IT_SERVICES",
                City = "Bengaluru",
                Email = "accounts@client.example",
                Status = status,
                PrimaryContact = new { Name = "Asha Rao", Designation = "CTO", Email = "asha@client.example" },
            }, Ct),
            HttpStatusCode.Created);

    public static object Contract(Guid clientId, string type = "Msa", Guid? parentId = null, DateOnly? start = null, DateOnly? end = null, string currency = "INR", Guid? ownerId = null, DateOnly? renewal = null)
        => new
        {
            Number = (string?)null,
            ClientId = clientId,
            Title = $"{type} agreement",
            Type = type,
            ParentContractId = parentId,
            StartDate = Iso(start ?? Today.AddDays(-30)),
            EndDate = end is null ? null : Iso(end.Value),
            Currency = currency,
            Value = 1_000_000m,
            BillingModel = "TimeAndMaterial",
            PaymentTermsDays = 30,
            RenewalDate = renewal is null ? null : Iso(renewal.Value),
            AutoRenew = false,
            OwnerEmployeeId = ownerId,
        };

    public static object Project(Guid? clientId = null, Guid? contractId = null, Guid? managerId = null, DateOnly? start = null, DateOnly? end = null, string code = "")
        => new
        {
            Code = code.Length == 0 ? "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : code,
            Name = "Project " + CoreHrApi.Unique("N"),
            Type = "TimeAndMaterial",
            ClientId = clientId,
            ContractId = contractId,
            ProjectManagerEmployeeId = managerId,
            StartDate = Iso(start ?? Today.AddDays(-30)),
            EndDate = Iso(end ?? Today.AddDays(60)),
            Priority = "High",
            BillingType = "TimeAndMaterial",
            Currency = "INR",
            Budget = 500_000m,
            Confidentiality = "Internal",
            Methodology = "Agile",
        };

    public static async Task<JsonElement> CreateProjectAsync(HttpClient client, object body)
        => (await OkAsync(await client.PostAsJsonAsync("/api/v1/projects", body, Ct), HttpStatusCode.Created)).GetProperty("project");

    public static async Task<Guid> RoleAsync(HttpClient client, string code)
        => (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/projects/roles", Ct))!
            .First(role => role.GetProperty("code").GetString() == code)
            .GetProperty("id").GetGuid();

    public static async Task<JsonElement> AddMemberAsync(HttpClient admin, Guid projectId, Guid employeeId, string role = "DEV", int allocation = 50, DateOnly? end = null)
        => await OkAsync(await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/team", new
        {
            EmployeeId = employeeId,
            ProjectRoleId = await RoleAsync(admin, role),
            StartDate = Iso(Today.AddDays(-10)),
            EndDate = end is null ? null : Iso(end.Value),
            AllocationPercent = allocation,
            Billable = true,
            BillingRate = 2500m,
            CostRate = 1200m,
        }, Ct));

    public static object NewTask(string title, string type = "Task", Guid? parentId = null, Guid? assigneeId = null, int? points = null, Guid? sprintId = null, string priority = "Medium", DateOnly? due = null)
        => new
        {
            Type = type,
            ParentTaskId = parentId,
            SprintId = sprintId,
            Title = title,
            AssigneeEmployeeId = assigneeId,
            Priority = priority,
            StoryPoints = points,
            DueDate = due is null ? null : Iso(due.Value),
        };

    public static async Task<JsonElement> CreateTaskAsync(HttpClient client, Guid projectId, object body)
        => await OkAsync(await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", body, Ct));

    public static async Task<Guid> ColumnAsync(HttpClient client, Guid projectId, string category)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/board", Ct)).GetProperty("columns").EnumerateArray()
            .First(column => column.GetProperty("category").GetString() == category)
            .GetProperty("id").GetGuid();

    /// <summary>A team member as the Employee role makes them: own projects only, working on tasks and the RAID log.</summary>
    public static Task<HttpClient> MemberAsync(PeopleOsApiFactory factory, Guid employeeId)
        => factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email("member"),
            employeeId,
            (Permissions.Project.View, DataScope.Own),
            (Permissions.Project.TaskView, DataScope.Own),
            (Permissions.Project.TaskManage, DataScope.Own),
            (Permissions.Project.MilestoneView, DataScope.Own),
            (Permissions.Project.SprintView, DataScope.Own),
            (Permissions.Project.ChangeRequestView, DataScope.Own),
            (Permissions.Project.RaidView, DataScope.Own),
            (Permissions.Project.RaidManage, DataScope.Own),
            (Permissions.Document.View, DataScope.Own),
            (Permissions.Document.Upload, DataScope.Own));
}
