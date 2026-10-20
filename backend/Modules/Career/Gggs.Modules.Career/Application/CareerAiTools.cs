using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Contracts;

namespace Gggs.Modules.Career.Application;

/// <summary>The caller's career position: where they are on each path, the next step and the skills it needs.</summary>
internal sealed class MyCareerTool(CareerService career) : IAiTool
{
    public string Name => "my_career";

    public string Description => "This person's career picture: current role and grade, latest rating, their position on each career path with the next step and the skills it needs, and promotions.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.CareerView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => AiToolOutput.Data(await career.MineAsync(cancellationToken));
}
