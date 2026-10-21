using Gggs.Data;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

/// <summary>A leaver's unfinished tasks, and handing them on.</summary>
internal sealed class ProjectTasks(AppDbContext dbContext) : IProjectTasks
{
    public async Task<IReadOnlyList<OpenProjectTask>> OpenForAssigneeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.AssigneeEmployeeId == employeeId && task.CompletedDate == null)
            .OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate).ThenBy(task => task.Code)
            .Select(task => new OpenProjectTask(task.Id, task.ProjectId, task.Code, task.Title, task.DueDate))
            .Take(500)
            .ToListAsync(cancellationToken);

    public async Task<int> ReassignOpenAsync(Guid fromEmployeeId, Guid toEmployeeId, CancellationToken cancellationToken = default)
    {
        List<ProjectTask> open = await dbContext.ProjectTasks.Where(task => task.AssigneeEmployeeId == fromEmployeeId && task.CompletedDate == null).ToListAsync(cancellationToken);
        foreach (ProjectTask task in open)
        {
            task.AssignTo(toEmployeeId);
        }

        return open.Count;
    }
}
