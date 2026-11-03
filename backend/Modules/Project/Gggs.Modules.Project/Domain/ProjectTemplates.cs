using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Project.Domain;

public sealed record TemplateColumn(string Name, ColumnCategory Category);

/// <summary>What a project starts with: its phases, its board, and the shared work anyone on it can book to.</summary>
public sealed record TemplateDefinition(IReadOnlyList<string> Phases, IReadOnlyList<TemplateColumn> Columns, IReadOnlyList<DefaultActivity> Activities);

/// <summary>A starting point for projects under one engagement model. Each organisation can change its own.</summary>
public sealed class ProjectTemplate : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ProjectTemplate()
    {
        Code = null!;
        Name = null!;
        DefinitionJson = "{}";
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public ProjectBillingType BillingType { get; private set; }

    public string DefinitionJson { get; private set; }

    public TemplateDefinition Definition => JsonSerializer.Deserialize<TemplateDefinition>(DefinitionJson, Json) ?? new TemplateDefinition([], [], []);

    public static ProjectTemplate Create(string code, string name, ProjectBillingType billingType, TemplateDefinition definition)
    {
        var template = new ProjectTemplate { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32), BillingType = billingType };
        template.Update(name, definition);
        return template;
    }

    public void Update(string name, TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        DefinitionJson = JsonSerializer.Serialize(Validated(definition), Json);
    }

    private static TemplateDefinition Validated(TemplateDefinition definition)
    {
        List<TemplateColumn> columns = [.. definition.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Name)).Select(column => column with { Name = column.Name.Trim() })];
        if (columns.Any(column => !Enum.IsDefined(column.Category)))
        {
            throw new DomainRuleException("PROJECT_TEMPLATE_COLUMNS", "A board column has an unknown category.");
        }

        if (!columns.Any(column => column.Category == ColumnCategory.Done) || !columns.Any(column => column.Category is ColumnCategory.Backlog or ColumnCategory.Todo))
        {
            throw new DomainRuleException("PROJECT_TEMPLATE_COLUMNS", "A board needs a column for new work and a column for finished work.");
        }

        if (columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            throw new DomainRuleException("PROJECT_TEMPLATE_COLUMNS", "Two board columns have the same name.");
        }

        return new TemplateDefinition(
            [.. definition.Phases.Where(phase => !string.IsNullOrWhiteSpace(phase)).Select(phase => phase.Trim())],
            columns,
            [.. definition.Activities.Where(activity => !string.IsNullOrWhiteSpace(activity.Name))
                .Select(activity => new DefaultActivity(activity.Name.Trim(), [.. activity.Tasks.Where(task => !string.IsNullOrWhiteSpace(task)).Select(task => task.Trim())]))]);
    }
}

/// <summary>The templates a new organisation starts with, one for each engagement model.</summary>
public static class ProjectTemplateCatalog
{
    private static readonly IReadOnlyList<TemplateColumn> Delivery =
        [.. BoardColumn.Defaults.Select(column => new TemplateColumn(column.Name, column.Category))];

    private static readonly IReadOnlyList<TemplateColumn> Support =
    [
        new("New", ColumnCategory.Backlog),
        new("Triage", ColumnCategory.Todo),
        new("In Progress", ColumnCategory.InProgress),
        new("Waiting on client", ColumnCategory.InProgress),
        new("Resolved", ColumnCategory.Done),
    ];

    private static readonly DefaultActivity Meetings = new("Meetings", ["Stand-up meetings", "Client meetings", "Planned meetings"]);
    private static readonly DefaultActivity Management = new("Project management", ["Planning", "Status reporting"]);

    public static IReadOnlyList<ProjectTemplate> Defaults() =>
    [
        ProjectTemplate.Create("FIXED_PRICE", "Fixed price delivery", ProjectBillingType.FixedPrice, new TemplateDefinition(["Discovery", "Design", "Build", "Test", "UAT", "Go-live"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("MILESTONE", "Milestone delivery", ProjectBillingType.Milestone, new TemplateDefinition(["Discovery", "Design", "Build", "Test", "UAT", "Go-live"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("TIME_MATERIAL", "Time and material", ProjectBillingType.TimeAndMaterial, new TemplateDefinition(["Onboarding", "Delivery", "Handover"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("DEDICATED_TEAM", "Dedicated team", ProjectBillingType.DedicatedTeam, new TemplateDefinition(["Onboarding", "Delivery"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("OUTCOME", "Outcome based", ProjectBillingType.OutcomeBased, new TemplateDefinition(["Discovery", "Delivery", "Acceptance"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("RETAINER", "Retainer", ProjectBillingType.Retainer, new TemplateDefinition(["Onboarding", "Ongoing"], Delivery, [Meetings, Management])),
        ProjectTemplate.Create("MANAGED_SERVICE", "Managed service", ProjectBillingType.ManagedService, new TemplateDefinition(["Transition", "Steady state"], Support, [Meetings, new("Service management", ["Monitoring", "Service reviews"])])),
    ];
}
