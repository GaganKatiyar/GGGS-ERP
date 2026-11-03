using System.Globalization;
using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Project.Domain;

namespace Gggs.Modules.Project.Application;

/// <summary>Finding the one project a tool was asked about, among those the person can see.</summary>
internal static class ToolProjects
{
    public static async Task<ProjectItem> FindAsync(ProjectService projects, string? query, CancellationToken cancellationToken)
    {
        string wanted = (query ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            throw new DomainRuleException("AI_PROJECT", "Say which project.");
        }

        var page = await projects.ListAsync(new ProjectListQuery { Search = wanted, PageSize = 20 }, cancellationToken);
        return page.Items.FirstOrDefault(item => string.Equals(item.Code, wanted, StringComparison.OrdinalIgnoreCase))
            ?? (page.Items.Count == 1 ? page.Items[0] : null)
            ?? throw new DomainRuleException("AI_PROJECT", page.Items.Count == 0 ? $"No project you can see matches '{wanted}'." : $"'{wanted}' matches several projects: {string.Join(", ", page.Items.Select(item => item.Code))}.");
    }
}

/// <summary>The week on a project, from which the assistant writes a status report for a person to use.</summary>
internal sealed class ProjectWeekTool(ProjectService projects, TaskViewsService views, InsightService insight) : IAiTool
{
    public string Name => "project_week";

    public string Description => "Returns what happened on a project in the last 7 days and what is coming in the next 14: tasks finished, tasks late, milestones, risks and health. Use this to draft a weekly status report. The report you write is a draft for the person to edit and send; say so.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("project", "string", "Project code or name", true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Project.TaskView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        try
        {
            ProjectItem project = await ToolProjects.FindAsync(projects, AiJson.Text(input, "project"), cancellationToken);
            return AiToolOutput.Data(new { project.Code, project.Name, Week = await views.WeekFactsAsync(project.Id, cancellationToken), Dashboard = await insight.DashboardAsync(project.Id, cancellationToken) });
        }
        catch (DomainRuleException refused)
        {
            return AiToolOutput.Problem(refused.Message);
        }
    }
}

/// <summary>What in a project's data needs attention, as counted facts for the assistant to explain.</summary>
internal sealed class ProjectRiskSignalsTool(ProjectService projects, TaskViewsService views) : IAiTool
{
    public string Name => "project_risk_signals";

    public string Description => "Checks a project's data for things that need attention: late tasks and milestones, schedule slip, SLA breaches, broken dependencies, people over capacity, unassigned and unestimated work. These are counted facts, not predictions. Explain them and suggest what to look at; a person decides.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("project", "string", "Project code or name", true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Project.TaskView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        try
        {
            ProjectItem project = await ToolProjects.FindAsync(projects, AiJson.Text(input, "project"), cancellationToken);
            return AiToolOutput.Data(new { project.Code, Signals = await views.RiskSignalsAsync(project.Id, cancellationToken) });
        }
        catch (DomainRuleException refused)
        {
            return AiToolOutput.Problem(refused.Message);
        }
    }
}

internal sealed record DraftedTask(string Title, TaskType Type, string? Description, decimal? EstimatedHours, int? StoryPoints);

internal sealed record TaskBreakdownPayload(Guid ProjectId, string ProjectCode, Guid? ParentTaskId, Guid? RequirementId, IReadOnlyList<DraftedTask> Tasks);

/// <summary>Breaks a requirement into tasks. It only proposes them: nothing exists until a person approves the list.</summary>
internal sealed class DraftTaskBreakdownTool(ProjectService projects, TaskService tasks, GovernanceProjectLookup lookup) : IAiActionTool
{
    private const int MostTasks = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "draft_task_breakdown";

    public string Description => "Drafts tasks for a project from a requirement or a description of the work, for the person to approve. Give the project code, optionally the parent task code or requirement code, and up to 30 tasks. Task type is Task, Story, Bug, Experiment or Subtask. Estimates are your suggestion: say they need checking. Nothing is created until the person approves it.";

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["project"] = new { type = "string", description = "Project code or name" },
            ["parentCode"] = new { type = "string", description = "Code of the task these sit under, if any" },
            ["requirementCode"] = new { type = "string", description = "Code of the requirement these come from, if any" },
            ["tasks"] = new
            {
                type = "array",
                description = "The tasks to create",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new { type = "string", description = "A short title" },
                        ["type"] = new { type = "string", description = "Task, Story, Bug, Experiment or Subtask" },
                        ["description"] = new { type = "string", description = "What is to be done, and how to tell it is done" },
                        ["estimatedHours"] = new { type = "number", description = "A suggested estimate in hours" },
                        ["storyPoints"] = new { type = "integer", description = "Suggested story points" },
                    },
                    required = new[] { "title" },
                    additionalProperties = false,
                },
            },
        },
        required = new[] { "project", "tasks" },
        additionalProperties = false,
    });

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Project.TaskManage;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("draft_task_breakdown is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        ProjectItem project = await ToolProjects.FindAsync(projects, AiJson.Text(input, "project"), cancellationToken);
        List<DraftedTask> drafted = [];
        if (input.TryGetProperty("tasks", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                string title = (AiJson.Text(item, "title") ?? string.Empty).Trim();
                if (title.Length == 0)
                {
                    throw new DomainRuleException("AI_TASKS", "Every task needs a title.");
                }

                TaskType type = Enum.TryParse(AiJson.Text(item, "type"), ignoreCase: true, out TaskType parsed) && parsed is TaskType.Task or TaskType.Story or TaskType.Bug or TaskType.Experiment or TaskType.Subtask ? parsed : TaskType.Task;
                decimal? hours = item.TryGetProperty("estimatedHours", out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
                drafted.Add(new DraftedTask(title.Length > 300 ? title[..300] : title, type, AiJson.Text(item, "description"), hours, AiJson.Number(item, "storyPoints")));
            }
        }

        if (drafted.Count is 0 or > MostTasks)
        {
            throw new DomainRuleException("AI_TASKS", $"Draft between 1 and {MostTasks} tasks at a time.");
        }

        Guid? parent = await lookup.TaskByCodeAsync(project.Id, AiJson.Text(input, "parentCode"), cancellationToken);
        Guid? requirement = await lookup.RequirementByCodeAsync(project.Id, AiJson.Text(input, "requirementCode"), cancellationToken);
        string names = string.Join("; ", drafted.Take(8).Select(task => task.EstimatedHours is { } hours ? $"{task.Title} ({hours.ToString("0.#", CultureInfo.InvariantCulture)} h)" : task.Title));
        string rest = drafted.Count > 8 ? $"; and {drafted.Count - 8} more" : string.Empty;
        return new AiProposal(
            $"Add {drafted.Count} tasks to {project.Code}: {names}{rest}.",
            JsonSerializer.Serialize(new TaskBreakdownPayload(project.Id, project.Code, parent, requirement, drafted), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        TaskBreakdownPayload payload = JsonSerializer.Deserialize<TaskBreakdownPayload>(payloadJson, Json) ?? throw new DomainRuleException("AI_TASKS", "That proposal could not be read.");
        IReadOnlyList<TaskItem> created = await tasks.CreateManyAsync(
            payload.ProjectId,
            [.. payload.Tasks.Select(task => new TaskBody(
                task.Type, payload.ParentTaskId, null, null, null, payload.RequirementId, null, task.Title, task.Description, null, WorkPriority.Medium, null, null, task.EstimatedHours, null, task.StoryPoints, 0, null))],
            cancellationToken);
        return $"Added {created.Count} tasks to {payload.ProjectCode}: {string.Join(", ", created.Select(task => task.Code))}.";
    }
}

internal sealed record RiskPayload(Guid ProjectId, string ProjectCode, string Title, string? Description, int Probability, int Impact, string? Mitigation);

/// <summary>Puts a risk the assistant spotted on the project's RAID log, once a person agrees it is one.</summary>
internal sealed class RaiseProjectRiskTool(ProjectService projects, GovernanceService governance) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "raise_project_risk";

    public string Description => "Drafts a risk for a project's RAID log, for the person to approve: title, what could go wrong, probability and impact from 1 to 5, and a suggested mitigation. Nothing is logged until the person approves it.";

    public JsonElement InputSchema { get; } = AiJson.Schema(
        ("project", "string", "Project code or name", true),
        ("title", "string", "A short name for the risk", true),
        ("description", "string", "What could go wrong, and the evidence for it", false),
        ("probability", "integer", "How likely, 1 to 5", false),
        ("impact", "integer", "How bad, 1 to 5", false),
        ("mitigation", "string", "What could be done about it", false));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Project.RaidManage;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("raise_project_risk is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        ProjectItem project = await ToolProjects.FindAsync(projects, AiJson.Text(input, "project"), cancellationToken);
        string title = (AiJson.Text(input, "title") ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new DomainRuleException("AI_RISK", "Give the risk a title.");
        }

        int probability = Math.Clamp(AiJson.Number(input, "probability") ?? 3, 1, 5);
        int impact = Math.Clamp(AiJson.Number(input, "impact") ?? 3, 1, 5);
        return new AiProposal(
            $"Log a risk on {project.Code}: {title} (probability {probability}, impact {impact}).",
            JsonSerializer.Serialize(new RiskPayload(project.Id, project.Code, title, AiJson.Text(input, "description"), probability, impact, AiJson.Text(input, "mitigation")), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        RiskPayload payload = JsonSerializer.Deserialize<RiskPayload>(payloadJson, Json) ?? throw new DomainRuleException("AI_RISK", "That proposal could not be read.");
        RaidItemView item = await governance.SaveRaidAsync(
            payload.ProjectId,
            null,
            new RaidBody(RaidKind.Risk, payload.Title, payload.Description, null, payload.Probability, payload.Impact, null, WorkPriority.Medium, null, null, null, payload.Mitigation, null, null, null, null),
            cancellationToken);
        return $"Logged risk {item.Code} on {payload.ProjectCode}.";
    }
}
