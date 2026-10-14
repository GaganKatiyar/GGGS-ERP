using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Security;

namespace Gggs.Modules.Identity.Domain.PlatformStaff;

/// <summary>
/// A named set of permissions our own staff can be given.
/// <para>
/// The counterpart to <c>Role</c>, and simpler in one specific way: platform permissions
/// (<c>platform.*</c>) are never anchored to a branch, department or reporting line - there is no
/// organization to anchor them to, because they operate above every tenant, not inside one. A grant
/// is held or it is not; there is no <see cref="DataScope"/> to narrow.
/// </para>
/// <para>
/// Not per anything, unlike tenant roles which are per-tenant copies of a template. There is one
/// platform, so there is one copy of each role.
/// </para>
/// </summary>
public sealed class PlatformRole : AggregateRoot, IPlatformOwned, IAuditable, IConcurrencyAware
{
    private readonly List<string> _permissionCodes = [];

    private PlatformRole()
    {
        Name = null!;
        NormalizedName = null!;
    }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Seeded at startup and not deletable, for the same reason a tenant's "Employee" role is not:
    /// deleting the one role that can grant others would leave nobody able to grant anything back.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>The seed template this came from, so a rename does not lose track of it. Null for a custom role.</summary>
    public string? TemplateCode { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public byte[]? RowVersion { get; private set; }

    public IReadOnlyCollection<string> PermissionCodes => _permissionCodes;

    public static PlatformRole Create(string name, string? description, bool isSystem = false, string? templateCode = null)
    {
        return new PlatformRole
        {
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            NormalizedName = Guard.NotNullOrWhiteSpace(name).Trim().ToUpperInvariant(),
            Description = description is null ? null : Guard.MaxLength(description.Trim(), 500),
            IsSystem = isSystem,
            TemplateCode = templateCode,
        };
    }

    /// <summary>
    /// Grants a permission.
    /// <para>
    /// Refuses anything that is not <see cref="PermissionDefinition.IsPlatformOnly"/> - the mirror
    /// image of a tenant role refusing a platform-only code. A tenant permission granted here would
    /// mean a platform account could act as if it worked for whichever tenant is asking, which is
    /// exactly the boundary the isolation model exists to hold.
    /// </para>
    /// </summary>
    public void Grant(string permissionCode)
    {
        Guard.NotNullOrWhiteSpace(permissionCode);

        Guard.Against(
            !PermissionCatalog.Exists(permissionCode),
            "PERMISSION_UNKNOWN",
            $"'{permissionCode}' is not a permission this platform defines.");

        Guard.Against(
            !PermissionCatalog.Find(permissionCode)!.IsPlatformOnly,
            "PERMISSION_NOT_PLATFORM_ONLY",
            $"'{permissionCode}' is a tenant permission and cannot be granted to a platform role.");

        if (!_permissionCodes.Contains(permissionCode, StringComparer.OrdinalIgnoreCase))
        {
            _permissionCodes.Add(permissionCode);
        }
    }

    public void Revoke(string permissionCode)
        => _permissionCodes.RemoveAll(existing => string.Equals(existing, permissionCode, StringComparison.OrdinalIgnoreCase));

    public void Rename(string name, string? description)
    {
        Guard.Against(IsSystem, "PLATFORM_ROLE_IS_SYSTEM", "A system role cannot be renamed; its permissions can still be changed.");

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        NormalizedName = Name.ToUpperInvariant();
        Description = description is null ? null : Guard.MaxLength(description.Trim(), 500);
    }

    public void EnsureCanBeDeleted()
        => Guard.Against(IsSystem, "PLATFORM_ROLE_IS_SYSTEM", "A system role cannot be deleted; disable the staff who hold it instead.");
}

/// <summary>A role granted to a platform staff member. No anchor and no expiry: platform access is not partial.</summary>
public sealed class PlatformRoleAssignment : AggregateRoot, IPlatformOwned, IAuditable
{
    private PlatformRoleAssignment()
    {
    }

    public Guid PlatformStaffId { get; private set; }

    public Guid PlatformRoleId { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public static PlatformRoleAssignment Grant(Guid platformStaffId, Guid platformRoleId)
    {
        Guard.NotEmpty(platformStaffId);
        Guard.NotEmpty(platformRoleId);

        return new PlatformRoleAssignment
        {
            PlatformStaffId = platformStaffId,
            PlatformRoleId = platformRoleId,
        };
    }
}
