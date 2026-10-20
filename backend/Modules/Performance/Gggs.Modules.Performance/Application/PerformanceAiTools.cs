using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;

namespace Gggs.Modules.Performance.Application;

/// <summary>The caller's own goals and how they are going.</summary>
internal sealed class MyGoalsTool(GoalService goals) : IAiTool
{
    public string Name => "my_goals";

    public string Description => "This person's goals: title, level, due date, weight, progress, confidence and last check-in.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Performance.GoalManageOwn;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GoalItem> mine = await goals.MineAsync(cancellationToken);
        return AiToolOutput.Data(mine.Select(goal => new { goal.Title, goal.Level, goal.Status, goal.DueDate, goal.Weight, goal.Progress, goal.Confidence, goal.LastCheckInOn, goal.ParentTitle }));
    }
}
