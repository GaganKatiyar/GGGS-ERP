using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Platform.Domain.Subscriptions;

namespace Gggs.Modules.Platform.Application.Provisioning;

/// <summary>One plan as the product sells it.</summary>
public sealed record PlanDefinition(
    string Code,
    string Name,
    PlanEdition Edition,
    int? MaxEmployees,
    IReadOnlyList<string> Features);

/// <summary>
/// The plans PeopleOS is sold as, in code so the commercial packaging is reviewable in a diff.
/// <para>
/// Seeded into <c>platform.Plan</c> at startup and kept in step: a feature added to a plan here
/// reaches every tenant on that plan, because a subscription's features are resolved from its plan
/// on every lookup (per-tenant overrides still apply on top).
/// </para>
/// </summary>
public static class PlanCatalog
{
    public const string Core = "CORE";
    public const string Professional = "PROFESSIONAL";
    public const string Enterprise = "ENTERPRISE";

    public static IReadOnlyList<PlanDefinition> Definitions { get; } =
    [
        new(Core, "PeopleOS Core", PlanEdition.Core, MaxEmployees: 250,
        [
            Features.CoreHr, Features.Time, Features.Documents, Features.Assets, Features.Helpdesk,
        ]),

        new(Professional, "PeopleOS Professional", PlanEdition.Professional, MaxEmployees: 2500,
        [
            Features.CoreHr, Features.Time, Features.Documents, Features.Assets, Features.Helpdesk,
            Features.Recruitment, Features.Onboarding, Features.Payroll, Features.Expenses,
            Features.Performance, Features.Learning, Features.Rewards, Features.Engagement,
            Features.Separation, Features.Compliance,
            Features.Projects, Features.Resources, Features.Timesheets,
        ]),

        // Everything, including the delivery financials, workforce planning, analytics and AI.
        new(Enterprise, "PeopleOS Enterprise", PlanEdition.Enterprise, MaxEmployees: null, Features.All),
    ];
}
