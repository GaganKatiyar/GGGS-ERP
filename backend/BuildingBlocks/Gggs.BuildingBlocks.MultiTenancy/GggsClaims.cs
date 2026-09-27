namespace Gggs.BuildingBlocks.MultiTenancy;

/// <summary>
/// Claim types issued by the Identity service. The tenant is taken from <see cref="TenantId"/> and
/// nowhere else: never from a header, route value or request body (§3.3 of the architecture).
/// </summary>
public static class GggsClaims
{
    /// <summary>Tenant the token was issued for.</summary>
    public const string TenantId = "tid";

    /// <summary>Employee record linked to the user, when there is one.</summary>
    public const string EmployeeId = "emp";

    /// <summary>Identity session, used to revoke access without waiting for token expiry.</summary>
    public const string SessionId = "sid";

    /// <summary>Permission version; bumping it invalidates the cached permission set.</summary>
    public const string PermissionVersion = "pv";

    /// <summary>User type: tenant user, platform user or API client.</summary>
    public const string UserType = "utyp";

    /// <summary>Set when a platform support user is acting inside a tenant under a support grant.</summary>
    public const string Impersonator = "imp";

    /// <summary>The user's own language preference, which wins over the tenant default. OIDC name.</summary>
    public const string Locale = "locale";

    /// <summary>The user's own IANA time zone, which wins over the tenant default. OIDC name.</summary>
    public const string TimeZone = "zoneinfo";
}
