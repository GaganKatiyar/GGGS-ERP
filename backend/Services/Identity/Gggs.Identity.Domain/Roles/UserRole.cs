using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Security;

namespace Gggs.Identity.Domain.Roles;

/// <summary>What an anchored role assignment is scoped to.</summary>
public enum RoleScopeType
{
    /// <summary>The whole tenant.</summary>
    Tenant = 1,

    LegalEntity = 2,
    BusinessUnit = 3,
    Branch = 4,
    Department = 5,
}

/// <summary>
/// A role granted to a user, optionally anchored to part of the organization and optionally
/// time-limited.
/// <para>
/// Anchoring is what makes one "HR Manager" role serve a company with twelve branches: the same
/// permissions, each holder seeing only their own branch. Without it every branch needs its own
/// role, and the role list becomes unmanageable.
/// </para>
/// <para>
/// Time limits exist because temporary access is real - an auditor during an audit, a manager
/// covering maternity leave - and access granted "just for a few weeks" is otherwise still there
/// years later. Expiry makes the cleanup automatic rather than someone's good intention.
/// </para>
/// </summary>
public sealed class UserRole : TenantAggregateRoot
{
    private UserRole()
    {
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    /// <summary>Null means the grant covers the whole tenant.</summary>
    public RoleScopeType? ScopeType { get; private set; }

    public Guid? ScopeId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    /// <summary>Null for an open-ended grant.</summary>
    public DateOnly? ValidTo { get; private set; }

    /// <summary>Why it was granted. Required for a time-limited grant, which is always an exception.</summary>
    public string? Reason { get; private set; }

    public static UserRole Grant(
        Guid userId,
        Guid roleId,
        DateOnly validFrom,
        DateOnly? validTo = null,
        RoleScopeType? scopeType = null,
        Guid? scopeId = null,
        string? reason = null)
    {
        Guard.NotEmpty(userId);
        Guard.NotEmpty(roleId);

        Guard.Against(
            validTo is not null && validTo < validFrom,
            "USER_ROLE_DATES_INVALID",
            "A role grant cannot end before it begins.");

        // An anchored grant without its anchor would silently widen to the whole tenant, which is
        // the opposite of what anchoring is for.
        Guard.Against(
            scopeType is not null and not RoleScopeType.Tenant && scopeId is null,
            "USER_ROLE_SCOPE_MISSING",
            "A role anchored to part of the organization must name which part.");

        Guard.Against(
            validTo is not null && string.IsNullOrWhiteSpace(reason),
            "USER_ROLE_REASON_REQUIRED",
            "A time-limited role grant must record why it was given.");

        return new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            ScopeType = scopeType,
            ScopeId = scopeId,
            Reason = reason is null ? null : Guard.MaxLength(reason.Trim(), 500),
        };
    }

    /// <summary>Whether this grant is in force on a given day.</summary>
    public bool IsActiveOn(DateOnly date)
        => ValidFrom <= date && (ValidTo is null || ValidTo >= date);

    /// <summary>Ends a grant today rather than deleting it, so the history survives an audit.</summary>
    public void EndOn(DateOnly date)
    {
        Guard.Against(
            date < ValidFrom,
            "USER_ROLE_DATES_INVALID",
            "A role grant cannot be ended before it began.");

        ValidTo = date;
    }

    /// <summary>
    /// Widens a permission's reach when this grant is anchored.
    /// <para>
    /// An anchored grant never <em>widens</em> what the role allows - it narrows it. A tenant-wide
    /// permission held through a branch-anchored role reaches that branch only.
    /// </para>
    /// </summary>
    public DataScope NarrowScope(DataScope roleScope)
    {
        if (ScopeType is null or RoleScopeType.Tenant)
        {
            return roleScope;
        }

        DataScope anchorCeiling = ScopeType switch
        {
            RoleScopeType.LegalEntity => DataScope.LegalEntity,
            RoleScopeType.BusinessUnit => DataScope.BusinessUnit,
            RoleScopeType.Branch => DataScope.Branch,
            RoleScopeType.Department => DataScope.Department,
            _ => DataScope.Tenant,
        };

        // The narrower of the two wins, so neither the role nor the anchor can be escaped.
        return roleScope < anchorCeiling ? roleScope : anchorCeiling;
    }
}
