using Gggs.BuildingBlocks.Security;

namespace Gggs.Modules.Identity.Application.PlatformStaff;

/// <summary>The permissions and name for a role every environment starts with.</summary>
public sealed record PlatformRoleTemplate(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>
/// The roles every environment is seeded with: staff administrator, organisation manager, billing and support.
/// <para>
/// Unlike <c>RoleTemplates</c> for tenants, these are kept in sync with the code on every startup
/// rather than seeded once and then left to drift. A tenant's copy of a role is theirs to edit, and
/// topping it back up would silently override that decision; there is no tenant here to have made
/// one - these roles are the platform's own definition of what its own staff can do, and that
/// definition lives in this file, not in something an operator might have narrowed in the database
/// for a reason nobody now remembers.
/// </para>
/// <para>
/// <c>PLATFORM_STAFF_ADMINISTRATOR</c> is deliberately the only role holding
/// <see cref="Permissions.Platform.StaffManage"/> and <see cref="Permissions.Platform.RoleManage"/>
/// among the seeded set - whoever can change what a platform role means, or who holds one, should
/// be a short, reviewable list.
/// </para>
/// </summary>
public static class PlatformRoleTemplates
{
    public const string StaffAdministratorCode = "PLATFORM_STAFF_ADMINISTRATOR";
    public const string OrganisationManagerCode = "PLATFORM_ORGANISATION_MANAGER";
    public const string BillingCode = "PLATFORM_BILLING";
    public const string SupportCode = "PLATFORM_SUPPORT";

    public static IReadOnlyList<PlatformRoleTemplate> Definitions { get; } =
    [
        new(
            StaffAdministratorCode,
            "Platform Staff Administrator",
            "Full control of tenants, subscriptions, features and our own staff.",
            [
                Permissions.Platform.TenantView,
                Permissions.Platform.TenantManage,
                Permissions.Platform.TenantSuspend,
                Permissions.Platform.SubscriptionManage,
                Permissions.Platform.FeatureManage,
                Permissions.Platform.HealthView,
                Permissions.Platform.SupportAccessRequest,
                Permissions.Platform.StaffView,
                Permissions.Platform.StaffInvite,
                Permissions.Platform.StaffManage,
                Permissions.Platform.RoleManage,
                Permissions.Platform.DatabaseManage,
            ]),

        new(
            OrganisationManagerCode,
            "Organisation Manager",
            "Creates organisations, changes their plans and modules, invites their administrators and offboards them.",
            [
                Permissions.Platform.TenantView,
                Permissions.Platform.TenantManage,
                Permissions.Platform.SubscriptionManage,
                Permissions.Platform.FeatureManage,
                Permissions.Platform.HealthView,
                Permissions.Platform.StaffView,
            ]),

        new(
            BillingCode,
            "Billing",
            "Looks after subscriptions and trials, and suspends or reactivates an organisation over payment.",
            [
                Permissions.Platform.TenantView,
                Permissions.Platform.SubscriptionManage,
                Permissions.Platform.TenantSuspend,
            ]),

        new(
            SupportCode,
            "Platform Support",
            "Looks at tenant status and platform health, and can request time-boxed access into a tenant to help.",
            [
                Permissions.Platform.TenantView,
                Permissions.Platform.HealthView,
                Permissions.Platform.SupportAccessRequest,
            ]),
    ];
}
