namespace Gggs.Modules.Platform.Endpoints;

/// <summary>
/// What the running deployment is: its build, where it runs and whether its database answers.
/// Shown on the Settings > Platform > About Nuts and Bolts page.
/// </summary>
public sealed record AboutResponse(
    string Product,
    string Version,
    string? Commit,
    string Environment,
    string Runtime,
    string OperatingSystem,
    DateTimeOffset StartedAt,
    DateTimeOffset ServerTime,
    bool DatabaseReachable,
    IReadOnlyList<string> Modules);
