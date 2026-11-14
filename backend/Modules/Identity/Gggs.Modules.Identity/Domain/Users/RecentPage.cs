using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Domain.Abstractions;

namespace Gggs.Modules.Identity.Domain.Users;

/// <summary>
/// A menu page someone opened lately, for the Recent links on their home screen. One row per user
/// and page; a repeat visit only moves the time on.
/// <para>
/// Holds a menu address and nothing about what was on the page: opening a payroll run records
/// <c>/payroll/runs</c>, never which run. It is a convenience that replaces itself as people work, so
/// unlike business data it carries no audit columns: an audit row for every page opened would bury
/// the audit log, and nothing here needs answering for.
/// </para>
/// </summary>
public sealed partial class RecentPage : AggregateRoot, ITenantOwned
{
    public const int MaxRouteLength = 200;

    /// <summary>
    /// More than the home screen shows, so someone who loses access to a few pages still has a full row.
    /// </summary>
    public const int KeptPerUser = 12;

    private RecentPage()
    {
        Route = null!;
    }

    public RecentPage(Guid userId, string route, DateTime visitedAt)
    {
        UserId = userId;
        Route = route;
        LastVisitedAt = visitedAt;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string Route { get; private set; }

    public DateTime LastVisitedAt { get; private set; }

    /// <summary>A plain menu path. Anything that could carry a query, a fragment or free text is refused.</summary>
    public static bool IsValidRoute(string? route)
        => route is { Length: > 0 and <= MaxRouteLength } && PlainPath().IsMatch(route);

    public void VisitedAgain(DateTime visitedAt) => LastVisitedAt = visitedAt;

    [GeneratedRegex("^/[A-Za-z0-9_/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainPath();
}
