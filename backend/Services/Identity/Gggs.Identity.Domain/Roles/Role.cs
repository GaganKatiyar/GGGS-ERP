using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Security;

namespace Gggs.Identity.Domain.Roles;

/// <summary>
/// A named set of permissions inside one tenant.
/// <para>
/// Roles are per tenant even when they come from a shared template. A customer who edits "HR
/// Manager" is editing their own copy, and our changing the template later never silently widens
/// access inside a live customer - which is the failure mode that makes shared role definitions a
/// bad idea in a multi-tenant system.
/// </para>
/// </summary>
public sealed class Role : TenantAggregateRoot
{
    private readonly List<PermissionAssignment> _permissions = [];

    private Role()
    {
        Name = null!;
        NormalizedName = null!;
    }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Seeded from a template at provisioning. System roles can be edited but not deleted: a
    /// tenant that deletes "Employee" has no way to grant anyone self-service again.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>Template this role was created from, so later template changes can be offered.</summary>
    public string? TemplateCode { get; private set; }

    /// <summary>
    /// Optional parent whose permissions are inherited. Used sparingly: deep hierarchies make
    /// "why can this person do that" genuinely hard to answer.
    /// </summary>
    public Guid? ParentRoleId { get; private set; }

    public IReadOnlyCollection<PermissionAssignment> Permissions => _permissions;

    public static Role Create(string name, string? description, bool isSystem = false, string? templateCode = null)
    {
        return new Role
        {
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            NormalizedName = Guard.NotNullOrWhiteSpace(name).Trim().ToUpperInvariant(),
            Description = description is null ? null : Guard.MaxLength(description.Trim(), 500),
            IsSystem = isSystem,
            TemplateCode = templateCode,
        };
    }

    /// <summary>
    /// Grants a permission at a given reach.
    /// <para>
    /// The permission must exist in the platform catalog. A role naming a code no endpoint checks
    /// looks like access and grants nothing, which is worse than being refused outright.
    /// </para>
    /// </summary>
    public void Grant(string permissionCode, DataScope scope)
    {
        Guard.NotNullOrWhiteSpace(permissionCode);

        Guard.Against(
            !PermissionCatalog.Exists(permissionCode),
            "PERMISSION_UNKNOWN",
            $"'{permissionCode}' is not a permission this platform defines.");

        // Platform permissions belong to our own staff and are never offered to tenant roles.
        Guard.Against(
            PermissionCatalog.Find(permissionCode)!.IsPlatformOnly,
            "PERMISSION_PLATFORM_ONLY",
            $"'{permissionCode}' is a platform permission and cannot be granted inside a tenant.");

        _permissions.RemoveAll(existing =>
            string.Equals(existing.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase));

        _permissions.Add(new PermissionAssignment(permissionCode, scope));
    }

    public void Revoke(string permissionCode)
        => _permissions.RemoveAll(existing =>
            string.Equals(existing.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase));

    public void Rename(string name, string? description)
    {
        Guard.Against(
            IsSystem,
            "ROLE_IS_SYSTEM",
            "A system role cannot be renamed; its permissions can still be changed.");

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        NormalizedName = Name.ToUpperInvariant();
        Description = description is null ? null : Guard.MaxLength(description.Trim(), 500);
    }

    public void EnsureCanBeDeleted()
        => Guard.Against(
            IsSystem,
            "ROLE_IS_SYSTEM",
            "A system role cannot be deleted; disable the users who hold it instead.");
}

/// <summary>A permission granted to a role, and how far it reaches. Stored in the RolePermission table.</summary>
public sealed record PermissionAssignment(string PermissionCode, DataScope Scope);
