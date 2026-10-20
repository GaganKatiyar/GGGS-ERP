using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>An employee's work profile - never identity documents, bank details or pay - within the caller's employee access.</summary>
internal sealed class GetEmployeeTool(AppDbContext dbContext, EmployeeScope scope, IOrgStructure organization) : IAiTool
{
    public string Name => "get_employee";

    public string Description => "Looks up employees by name, employee code or email and returns their work profile: designation, department, manager, location, joining date, status and work mode.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("query", "string", "Name, employee code or official email", true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Employee.View;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string term = (AiJson.Text(input, "query") ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return AiToolOutput.Problem("Give at least two characters of a name or code.");
        }

        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);
        var people = await visible
            .Where(row => row.DisplayName.Contains(term) || row.EmployeeCode.Contains(term) || (row.OfficialEmail != null && row.OfficialEmail.Contains(term)))
            .OrderBy(row => row.DisplayName).Take(5)
            .Select(row => new { row.Id, row.DisplayName, row.EmployeeCode, row.Designation, row.DepartmentId, row.LocationId, row.ManagerEmployeeId, row.JoiningDate, row.Status, row.WorkModeCode, row.OfficialEmail })
            .ToListAsync(cancellationToken);
        if (people.Count == 0)
        {
            return AiToolOutput.Problem($"No employee you can see matches '{term}'.");
        }

        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, people.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await organization.GetNamesAsync(OrgUnitKind.Location, people.Where(row => row.LocationId != null).Select(row => row.LocationId!.Value), cancellationToken);
        List<Guid> managerIds = [.. people.Where(row => row.ManagerEmployeeId != null).Select(row => row.ManagerEmployeeId!.Value)];
        Dictionary<Guid, string> managers = await dbContext.Employees.AsNoTracking().Where(row => managerIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        return AiToolOutput.Data(people.Select(row => new
        {
            Name = row.DisplayName,
            Code = row.EmployeeCode,
            row.Designation,
            Department = row.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
            Location = row.LocationId is { } location ? locations.GetValueOrDefault(location) : null,
            Manager = row.ManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager) : null,
            Joined = row.JoiningDate,
            Status = row.Status == EmployeeStatus.OnNotice ? "On notice" : row.Status.ToString(),
            WorkMode = row.WorkModeCode,
            Email = row.OfficialEmail,
        }));
    }
}
