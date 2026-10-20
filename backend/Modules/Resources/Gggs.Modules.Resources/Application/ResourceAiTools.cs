using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;

namespace Gggs.Modules.Resources.Application;

/// <summary>People with free capacity now - the bench and the partly available - optionally with a skill.</summary>
internal sealed class AvailablePeopleTool(ResourceService resources) : IAiTool
{
    public string Name => "available_people";

    public string Description => "People with free capacity now (the bench and partly allocated people): designation, location, skills, how much of them is free, since when and their cost rate. Optionally filter by a skill name.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("skill", "string", "A skill name to filter by, e.g. Angular (optional)", false));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Resource.AllocationView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string? skill = AiJson.Text(input, "skill")?.Trim();
        IReadOnlyList<BenchItem> bench = await resources.BenchAsync(cancellationToken);
        IEnumerable<BenchItem> matches = string.IsNullOrEmpty(skill)
            ? bench
            : bench.Where(item => item.Skills.Any(tag => tag.Name.Contains(skill, StringComparison.OrdinalIgnoreCase)));
        return AiToolOutput.Data(matches.Take(25).Select(item => new
        {
            item.EmployeeName,
            item.Designation,
            item.LocationName,
            Skills = item.Skills.Select(tag => tag.Name),
            item.AvailablePercent,
            item.BenchSince,
            item.BenchDays,
            item.PreviousProject,
            item.CostRate,
        }));
    }
}
