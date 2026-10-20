using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;

namespace Gggs.Modules.Project.Application;

/// <summary>A project's health, schedule, milestones, risks and team, for projects the caller may see.</summary>
internal sealed class ProjectStatusTool(ProjectService projects, InsightService insight) : IAiTool
{
    public string Name => "project_status";

    public string Description => "Finds a project by name or code and returns its status, stage, health, dates, milestones, open risks and issues, and team - or, without a query, lists the active projects with their health.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("query", "string", "Project name or code (optional)", false));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Project.View;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string? query = AiJson.Text(input, "query")?.Trim();
        var page = await projects.ListAsync(new ProjectListQuery { Search = string.IsNullOrEmpty(query) ? null : query, PageSize = 20 }, cancellationToken);
        if (page.Items.Count == 0)
        {
            return AiToolOutput.Problem(string.IsNullOrEmpty(query) ? "There are no projects you can see." : $"No project you can see matches '{query}'.");
        }

        if (string.IsNullOrEmpty(query) || page.Items.Count > 1)
        {
            return AiToolOutput.Data(page.Items.Select(item => new { item.Code, item.Name, item.Status, item.Stage, item.HealthStatus, item.EndDate }));
        }

        ProjectItem project = page.Items[0];
        return AiToolOutput.Data(new { Project = project, Dashboard = await insight.DashboardAsync(project.Id, cancellationToken) });
    }
}
