using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Gggs.Modules.Platform.Endpoints;

/// <summary>
/// The build and health of the running deployment, for the About Nuts and Bolts page.
/// <para>
/// Gated on <see cref="Permissions.Admin.SettingView"/> like the rest of the Settings group: the
/// commit, environment and operating system are of no use to an employee and of some use to an
/// attacker, so they stay behind an admin permission rather than going anonymous like the probes.
/// </para>
/// </summary>
public static class AboutEndpoints
{
    // Read once: the assembly and the process do not change while the host runs.
    private static readonly (string Version, string? Commit) Build = ReadBuild();
    private static readonly DateTimeOffset StartedAt = new(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);

    public static IEndpointRouteBuilder MapAboutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/about", GetAsync)
            .WithTags("About")
            .WithName("GetAbout")
            .RequirePermission(Permissions.Admin.SettingView);

        return endpoints;
    }

    private static async Task<Ok<AboutResponse>> GetAsync(
        AppDbContext database,
        IHostEnvironment environment,
        TimeProvider clock,
        [FromServices] IReadOnlyList<IModule> modules,
        CancellationToken cancellationToken)
    {
        bool databaseReachable;
        try
        {
            databaseReachable = await database.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The page reports the database as down rather than failing: that is the answer asked for.
            databaseReachable = false;
        }

        return TypedResults.Ok(new AboutResponse(
            Product: "Nuts and Bolts",
            Version: Build.Version,
            Commit: Build.Commit,
            Environment: environment.EnvironmentName,
            Runtime: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            StartedAt: StartedAt,
            ServerTime: clock.GetUtcNow(),
            DatabaseReachable: databaseReachable,
            Modules: [.. modules.Select(module => module.Name)]));
    }

    /// <summary>
    /// The SDK writes the source revision after a <c>+</c> in the informational version
    /// (<c>1.0.0+01e0f7c…</c>); split it so the page can show a short commit.
    /// </summary>
    internal static (string Version, string? Commit) ParseInformationalVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return ("unknown", null);
        }

        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0
            ? (informational, null)
            : (informational[..plus], informational[(plus + 1)..]);
    }

    private static (string Version, string? Commit) ReadBuild()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AboutEndpoints).Assembly;
        return ParseInformationalVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }
}
