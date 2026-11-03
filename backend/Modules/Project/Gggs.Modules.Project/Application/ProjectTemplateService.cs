using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Application;

public sealed record ProjectTemplateItem(Guid Id, string Code, string Name, ProjectBillingType BillingType, TemplateDefinition Definition);

public sealed record ProjectTemplateBody(string Name, IReadOnlyList<string>? Phases, IReadOnlyList<TemplateColumn>? Columns, IReadOnlyList<DefaultActivity>? Activities);

/// <summary>Project templates: a starting point for each engagement model, and laying one out on a new project.</summary>
public sealed class ProjectTemplateService(AppDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ProjectTemplateItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<ProjectTemplate> rows = await dbContext.ProjectTemplates.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken);
        return [.. rows.Select(Item)];
    }

    public async Task<ProjectTemplateItem> SaveAsync(Guid id, ProjectTemplateBody body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectTemplate template = await dbContext.ProjectTemplates.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectTemplate), id);
        template.Update(body.Name, new TemplateDefinition(body.Phases ?? [], body.Columns ?? [], body.Activities ?? []));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(template);
    }

    /// <summary>Adds any catalog template this organisation has never had; ones it changed or deleted stay as they are.</summary>
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> existing = [.. await dbContext.ProjectTemplates.IgnoreQueryFilters([QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        List<ProjectTemplate> missing = [.. ProjectTemplateCatalog.Defaults().Where(template => !existing.Contains(template.Code))];
        if (missing.Count == 0)
        {
            return 0;
        }

        dbContext.ProjectTemplates.AddRange(missing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return missing.Count;
    }

    /// <summary>The template a new project asked for, which must be for the model it is being sold under.</summary>
    public async Task<TemplateDefinition?> DefinitionForAsync(Guid? templateId, ProjectBillingType billingType, CancellationToken cancellationToken = default)
    {
        if (templateId is not { } id)
        {
            return null;
        }

        ProjectTemplate template = await dbContext.ProjectTemplates.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new DomainRuleException("PROJECT_TEMPLATE_UNKNOWN", "That project template does not exist.");
        return template.BillingType == billingType
            ? template.Definition
            : throw new DomainRuleException("PROJECT_TEMPLATE_MODEL", $"{template.Name} is for a different engagement model.");
    }

    /// <summary>Lays a template out on a new project: its board, its phases, and shared work anyone on the project can book to.</summary>
    public void Apply(ProjectEntity project, TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definition);

        int order = 0;
        List<BoardColumn> columns = [];
        foreach (TemplateColumn column in definition.Columns)
        {
            BoardColumn added = BoardColumn.Create(project.Id, column.Name, column.Category, order++, null);
            dbContext.BoardColumns.Add(added);
            columns.Add(added);
        }

        order = 0;
        foreach (string phase in definition.Phases)
        {
            dbContext.ProjectPhases.Add(ProjectPhase.Create(project.Id, new PhasePlan(phase, order++, null, null, null, null, PhaseStatus.NotStarted, 0)));
        }

        BoardColumn start = columns.FirstOrDefault(column => column.Category == ColumnCategory.Todo) ?? columns.First(column => column.Category == ColumnCategory.Backlog);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        double rank = 0;
        ProjectTask Add(TaskType type, Guid? parent, string title)
        {
            ProjectTask task = ProjectTask.Create(
                project.Id, project.NextTaskNumber(), project.Code,
                new TaskDetails(type, parent, null, null, null, title, null, null, WorkPriority.Medium, null, null, null, null, null, 0, Billable: false, OpenToTeam: true),
                start, rank += 1000, null, now);
            dbContext.ProjectTasks.Add(task);
            return task;
        }

        foreach (DefaultActivity activity in definition.Activities)
        {
            Guid epic = Add(TaskType.Epic, null, activity.Name).Id;
            foreach (string task in activity.Tasks)
            {
                Add(TaskType.Task, epic, task);
            }
        }
    }

    private static ProjectTemplateItem Item(ProjectTemplate template) => new(template.Id, template.Code, template.Name, template.BillingType, template.Definition);
}
