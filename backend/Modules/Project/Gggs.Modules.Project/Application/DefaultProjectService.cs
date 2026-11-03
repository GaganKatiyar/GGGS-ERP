using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

/// <summary>A row of Project settings: the organisation or a department, and the default project its people book to.</summary>
public sealed record DefaultProjectRow(
    Guid? DepartmentId,
    string Name,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    bool IsInherited,
    Guid? ManagerEmployeeId,
    string? ManagerName);

/// <summary>Creates default projects, and works out which one each person books to on a given day.</summary>
public sealed class DefaultProjectService(AppDbContext dbContext, IOrgStructure org, IEmployeeDirectory employees, ITenantClock clock, TimeProvider timeProvider)
{
    /// <summary>The default projects the employee could book to in the window: more than one only if they changed department in it.</summary>
    public async Task<IReadOnlyCollection<Guid>> ForEmployeeAsync(Guid employeeId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        List<OpenDefault> open = await OpenDefaultsAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Guid> departments = await employees.DepartmentsDuringAsync(employeeId, firstDay, lastDay, cancellationToken);
        Dictionary<Guid, Guid?> parents = (await org.DepartmentsAsync(cancellationToken)).ToDictionary(department => department.Id, department => department.ParentDepartmentId);
        Dictionary<Guid, Guid> byDepartment = open.Where(row => row.DepartmentId is not null).ToDictionary(row => row.DepartmentId!.Value, row => row.ProjectId);
        Guid? organization = open.FirstOrDefault(row => row.DepartmentId is null)?.ProjectId;

        if (departments.Count == 0)
        {
            return organization is { } fallback ? [fallback] : [];
        }

        return [.. departments.Select(department => DefaultProjects.Resolve(department, parents, byDepartment, organization)).OfType<Guid>().Distinct()];
    }

    public async Task<IReadOnlyList<DefaultProjectRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<OpenDefault> open = await OpenDefaultsAsync(cancellationToken);
        Dictionary<Guid, OpenDefault> byProject = open.ToDictionary(row => row.ProjectId);
        Dictionary<Guid, Guid> byDepartment = open.Where(row => row.DepartmentId is not null).ToDictionary(row => row.DepartmentId!.Value, row => row.ProjectId);
        Guid? organization = open.FirstOrDefault(row => row.DepartmentId is null)?.ProjectId;
        IReadOnlyList<DepartmentNode> departments = await org.DepartmentsAsync(cancellationToken);
        Dictionary<Guid, Guid?> parents = departments.ToDictionary(department => department.Id, department => department.ParentDepartmentId);
        Dictionary<Guid, string> managers = (await employees.GetManyAsync(open.Select(row => row.ManagerEmployeeId).OfType<Guid>(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        DefaultProjectRow Row(Guid? departmentId, string name)
        {
            Guid? projectId = DefaultProjects.Resolve(departmentId, parents, byDepartment, organization);
            OpenDefault? project = projectId is { } id ? byProject.GetValueOrDefault(id) : null;
            return new DefaultProjectRow(
                departmentId, name, project?.ProjectId, project?.Code, project?.Name, project is not null && project.DepartmentId != departmentId, project?.ManagerEmployeeId,
                project?.ManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager) : null);
        }

        return [Row(null, "Whole organisation"), .. departments.Where(department => department.IsActive).Select(department => Row(department.Id, department.Name))];
    }

    /// <summary>
    /// Gives the organisation and every open department a default project if it has never had one. One
    /// that was closed or deleted is left that way: someone chose to, and its people fall back up the tree.
    /// </summary>
    public async Task<int> EnsureAllAsync(CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Projects.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Select(project => new { project.Code, project.Type, project.DepartmentId })
            .ToListAsync(cancellationToken);
        HashSet<string> taken = [.. existing.Select(project => project.Code)];
        HashSet<Guid?> covered = [.. existing.Where(project => project.Type == ProjectType.DepartmentDefault).Select(project => project.DepartmentId)];
        DateOnly today = await clock.TodayAsync(cancellationToken);
        int created = 0;

        if (!covered.Contains(null))
        {
            string code = taken.Contains(DefaultProjects.OrganizationCode) ? DefaultProjects.CodeFor(DefaultProjects.OrganizationCode, taken) : DefaultProjects.OrganizationCode;
            taken.Add(code);
            Create(code, "General (default)", null, null, today);
            created++;
        }

        foreach (DepartmentNode department in (await org.DepartmentsAsync(cancellationToken)).Where(department => department.IsActive && !covered.Contains(department.Id)))
        {
            string code = DefaultProjects.CodeFor(department.Code, taken);
            taken.Add(code);
            Create(code, $"{department.Name} (default)", department.Id, await org.GetDepartmentHeadAsync(department.Id, cancellationToken), today);
            created++;
        }

        if (created > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    private void Create(string code, string name, Guid? departmentId, Guid? managerEmployeeId, DateOnly today)
    {
        var project = ProjectEntity.Create(
            code,
            new ProjectProfile(
                name, ProjectType.DepartmentDefault, null, null, null, null, departmentId, managerEmployeeId, null, null, new DateOnly(today.Year, 1, 1), DefaultProjects.OpenEnded,
                null, WorkPriority.Medium, ProjectBillingType.NonBillable, "INR", null, null, null, "Time that belongs to no other project: meetings, administration, learning and time between projects.",
                null, null, Confidentiality.Internal, DeliveryMethodology.Agile),
            ProjectStage.Execution);
        dbContext.Projects.Add(project);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        int order = 0;
        BoardColumn? todo = null;
        foreach ((string column, ColumnCategory category) in BoardColumn.Defaults)
        {
            BoardColumn added = BoardColumn.Create(project.Id, column, category, order++, null);
            dbContext.BoardColumns.Add(added);
            todo ??= category == ColumnCategory.Todo ? added : null;
        }

        double rank = 0;
        ProjectTask Add(TaskType type, Guid? parentId, string title)
        {
            ProjectTask task = ProjectTask.Create(
                project.Id, project.NextTaskNumber(), project.Code,
                new TaskDetails(type, parentId, null, null, null, title, null, null, WorkPriority.Medium, null, null, null, null, null, 0, Billable: false, OpenToTeam: true),
                todo!, rank += 1000, null, now);
            dbContext.ProjectTasks.Add(task);
            return task;
        }

        foreach (DefaultActivity activity in DefaultProjects.Activities)
        {
            Guid epic = Add(TaskType.Epic, null, activity.Name).Id;
            foreach (string task in activity.Tasks)
            {
                Add(TaskType.Task, epic, task);
            }
        }
    }

    private Task<List<OpenDefault>> OpenDefaultsAsync(CancellationToken cancellationToken)
        => dbContext.Projects.AsNoTracking()
            .Where(project => project.Type == ProjectType.DepartmentDefault && (project.Status == ProjectStatus.Active || project.Status == ProjectStatus.OnHold))
            .Select(project => new OpenDefault(project.Id, project.Code, project.Name, project.DepartmentId, project.ProjectManagerEmployeeId))
            .ToListAsync(cancellationToken);

    private sealed record OpenDefault(Guid ProjectId, string Code, string Name, Guid? DepartmentId, Guid? ManagerEmployeeId);
}
