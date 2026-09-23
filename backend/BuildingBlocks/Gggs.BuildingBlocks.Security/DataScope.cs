namespace Gggs.BuildingBlocks.Security;

/// <summary>
/// How wide a granted permission reaches. A permission answers "may they approve leave?"; the data
/// scope answers "whose leave?". Both are needed: a team lead and an HR admin hold the same
/// permission code and must see very different rows.
/// </summary>
public enum DataScope
{
    /// <summary>Only the acting user's own records.</summary>
    Own = 1,

    /// <summary>Employees reporting directly to them.</summary>
    DirectReports = 2,

    /// <summary>The whole reporting subtree beneath them.</summary>
    AllReports = 3,

    /// <summary>Their department, including sub-departments.</summary>
    Department = 4,

    /// <summary>Everyone at their branch.</summary>
    Branch = 5,

    /// <summary>Everyone in the legal entity (the payroll boundary).</summary>
    LegalEntity = 6,

    /// <summary>Everyone in the business unit.</summary>
    BusinessUnit = 7,

    /// <summary>Every employee in the tenant.</summary>
    Tenant = 8,
}

/// <summary>
/// One permission as actually granted to a user: the code, how far it reaches, and any org unit the
/// grant is anchored to (for example "HR Manager, but only for the Pune branch").
/// </summary>
public sealed record PermissionGrant
{
    public required string Code { get; init; }

    public required DataScope Scope { get; init; }

    /// <summary>The org unit a role assignment was anchored to, when it was anchored at all.</summary>
    public Guid? ScopeId { get; init; }
}

/// <summary>The permission set a user holds in the current tenant, cached per permission version.</summary>
public sealed class PermissionSet
{
    private readonly Dictionary<string, PermissionGrant> _permissions;

    public PermissionSet(IEnumerable<PermissionGrant> permissions, int permissionVersion)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        // A user can hold the same permission through several roles; the widest scope wins.
        _permissions = permissions
            .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.Scope).First(),
                StringComparer.OrdinalIgnoreCase);

        PermissionVersion = permissionVersion;
    }

    /// <summary>Matches the <c>pv</c> claim; a mismatch means the cached set is stale.</summary>
    public int PermissionVersion { get; }

    public int Count => _permissions.Count;

    public IReadOnlyCollection<PermissionGrant> All => _permissions.Values;

    public bool Has(string permissionCode) => _permissions.ContainsKey(permissionCode);

    /// <summary>The widest scope held for this permission, or null when it is not held at all.</summary>
    public DataScope? ScopeFor(string permissionCode)
        => _permissions.TryGetValue(permissionCode, out PermissionGrant? permission) ? permission.Scope : null;

    public PermissionGrant? Find(string permissionCode)
        => _permissions.GetValueOrDefault(permissionCode);

    public static PermissionSet Empty { get; } = new([], 0);
}
